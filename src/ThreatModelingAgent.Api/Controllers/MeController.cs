using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ThreatModelingAgent.Api.Security;
using ThreatModelingAgent.Domain.Interfaces;

namespace ThreatModelingAgent.Api.Controllers;

/// <summary>
/// Current-user profile and self-erasure (GDPR right to erasure).
///
/// GET    /v1/me  — current user's platform identifiers (no PII)
/// DELETE /v1/me  — soft-erase PII + call WorkOS to delete the user account
///
/// Security invariants:
/// - Returns only platform IDs — no email or display name (CLAUDE.md §10.4, 06-security §6.1).
/// - DELETE is destructive but scoped to the requesting user's own account only.
/// - WorkOS deletion is called before the DB update so a failure there leaves the DB intact.
/// - User-added threats and audit log entries are preserved (IDs only; PII already nulled).
/// </summary>
[ApiController]
[Authorize]
[Route("v1/me")]
[EnableRateLimiting("api")]
public sealed class MeController(
    IUserRepository users,
    IMembershipRepository memberships,
    IWorkOsClient workOs,
    IAuditLogger audit,
    ILogger<MeController> logger) : ControllerBase
{
    // GET /v1/me
    [HttpGet]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var userId = User.GetUserId();

        var user = await users.GetByIdAsync(userId, ct);
        if (user is null || user.IsDeleted) return NotFound();

        // Return only platform identifiers — no PII (CLAUDE.md §10.4)
        return Ok(new
        {
            userId = user.Id.Value,
            workosUserId = user.WorkOsUserId,
            createdAt = user.CreatedAt
        });
    }

    // DELETE /v1/me — GDPR right to erasure (06-security §6.2)
    [HttpDelete]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> DeleteMe(CancellationToken ct)
    {
        var userId = User.GetUserId();

        var user = await users.GetByIdAsync(userId, ct);
        if (user is null || user.IsDeleted) return NotFound();

        // 1. Revoke all org memberships so the user cannot be referenced as active
        var orgMemberships = await memberships.ListByUserAsync(userId, ct);
        foreach (var m in orgMemberships)
            await memberships.RemoveAsync(m, ct);

        // 2. Call WorkOS to delete the identity before we null local PII
        //    If WorkOS fails, the DB is unchanged — fail secure (CLAUDE.md §4.3)
        try
        {
            await workOs.DeleteUserAsync(user.WorkOsUserId, ct);
        }
        catch (WorkOsException ex)
        {
            logger.LogError(
                "WorkOS user deletion failed during self-erasure. UserId={UserId} StatusCode={StatusCode}",
                userId, ex.StatusCode);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { code = "ERASURE_FAILED", message = "Identity provider deletion failed. Please try again." });
        }

        // 3. Soft-delete: null PII, keep ID for audit log FK integrity (06-security §6.2)
        user.Erase();
        await users.SaveChangesAsync(ct);

        await audit.LogAsync("user.erased",
            orgId: null,
            userId: userId,
            resourceType: "user",
            resourceId: user.Id.Value,
            ct: ct);

        logger.LogInformation("User self-erasure complete. UserId={UserId}", userId);

        return NoContent();
    }
}
