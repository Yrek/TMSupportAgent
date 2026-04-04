using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ThreatModelingAgent.Api.Dtos;
using ThreatModelingAgent.Api.Security;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Api.Controllers;

/// <summary>
/// Org member management.
///
/// GET    /v1/orgs/{orgId}/members                  — list members
/// POST   /v1/orgs/{orgId}/members                  — invite by email (owner only)
/// PATCH  /v1/orgs/{orgId}/members/{userId}         — update role (owner only)
/// DELETE /v1/orgs/{orgId}/members/{userId}         — remove member (owner only)
///
/// Security invariants:
/// - All write endpoints require Owner role (checked via shared helper — CLAUDE.md §14).
/// - Cannot demote or remove yourself if you are the last owner.
/// - Invite response is identical whether the email is already a member or not (no enumeration).
/// - Member list returns userId + role + joinedAt only — no PII (CLAUDE.md §10.4).
/// </summary>
[ApiController]
[Authorize]
[Route("v1/orgs/{orgId:guid}/members")]
[EnableRateLimiting("api")]
public sealed class MembersController(
    IOrganizationRepository orgs,
    IMembershipRepository memberships,
    IJobRepository jobs,
    IWorkOsClient workOs,
    IAuditLogger audit,
    ILogger<MembersController> logger) : ControllerBase
{
    private static readonly HashSet<string> AllowedRoles = ["owner", "member"];

    // GET /v1/orgs/{orgId}/members
    [HttpGet]
    public async Task<IActionResult> ListMembers(Guid orgId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        var all = await memberships.ListByOrgAsync(orgIdValue, ct);

        return Ok(new
        {
            data = all.Select(m => new
            {
                userId = m.UserId.Value,
                role = m.Role.ToString().ToLower(),
                joinedAt = m.CreatedAt
            })
        });
    }

    // POST /v1/orgs/{orgId}/members — invite a user by email
    [HttpPost]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> InviteMember(
        Guid orgId,
        [FromBody] InviteMemberRequest request,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, OrgMemberRole.Owner, ct))
            return Forbid();

        // Validate email format (allow-list style — CLAUDE.md §6.3)
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 255
            || !request.Email.Contains('@'))
            return BadRequest(new { code = "INVALID_EMAIL", message = "A valid email address is required." });

        var org = await orgs.GetByIdAsync(orgIdValue, ct);
        if (org is null) return NotFound();

        if (string.IsNullOrWhiteSpace(org.WorkOsOrgId))
            return UnprocessableEntity(new
            {
                code = "ORG_NOT_SYNCED",
                message = "This organisation is not yet linked to an identity provider. Contact support."
            });

        try
        {
            await workOs.SendInvitationAsync(request.Email, org.WorkOsOrgId, ct);
        }
        catch (WorkOsException ex) when (ex.StatusCode == 422)
        {
            // User may already be a member — return 202 to prevent enumeration (CLAUDE.md §7.6)
            logger.LogInformation("WorkOS invitation 422 (possible duplicate). OrgId={OrgId}", orgIdValue);
        }
        catch (WorkOsException ex)
        {
            logger.LogError("WorkOS invitation failed. OrgId={OrgId} StatusCode={StatusCode}",
                orgIdValue, ex.StatusCode);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { code = "INVITE_FAILED", message = "Failed to send invitation. Please try again." });
        }

        await audit.LogAsync("member.invited",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "org_membership",
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct: ct);

        // Always 202 — identical response whether email exists or not (no enumeration oracle)
        return Accepted(new { message = "Invitation sent if the address is not already a member." });
    }

    // PATCH /v1/orgs/{orgId}/members/{memberId} — update role
    [HttpPatch("{memberId:guid}")]
    public async Task<IActionResult> UpdateRole(
        Guid orgId,
        Guid memberId,
        [FromBody] UpdateMemberRoleRequest request,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, OrgMemberRole.Owner, ct))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.Role) || !AllowedRoles.Contains(request.Role))
            return BadRequest(new { code = "INVALID_ROLE", message = "Role must be 'owner' or 'member'." });

        var newRole = Enum.Parse<OrgMemberRole>(request.Role, ignoreCase: true);

        var target = await memberships.GetAsync(orgIdValue, UserId.From(memberId), ct);
        if (target is null) return NotFound();

        // Prevent demoting yourself if you are the last owner
        if (target.UserId == userId && newRole != OrgMemberRole.Owner)
        {
            var ownerCount = (await memberships.ListByOrgAsync(orgIdValue, ct))
                .Count(m => m.Role == OrgMemberRole.Owner);
            if (ownerCount <= 1)
                return Conflict(new
                {
                    code = "LAST_OWNER",
                    message = "Cannot demote the last owner of an organisation."
                });
        }

        target.UpdateRole(newRole);
        await memberships.SaveChangesAsync(ct);

        await audit.LogAsync("member.role_updated",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "org_membership",
            resourceId: memberId,
            details: new { newRole = request.Role },
            ct: ct);

        return Ok(new
        {
            userId = target.UserId.Value,
            role = target.Role.ToString().ToLower(),
            joinedAt = target.CreatedAt
        });
    }

    // DELETE /v1/orgs/{orgId}/members/{memberId} — remove member
    [HttpDelete("{memberId:guid}")]
    public async Task<IActionResult> RemoveMember(
        Guid orgId,
        Guid memberId,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, OrgMemberRole.Owner, ct))
            return Forbid();

        var target = await memberships.GetAsync(orgIdValue, UserId.From(memberId), ct);
        if (target is null) return NotFound();

        // Prevent removing yourself if you are the last owner
        if (target.UserId == userId && target.Role == OrgMemberRole.Owner)
        {
            var ownerCount = (await memberships.ListByOrgAsync(orgIdValue, ct))
                .Count(m => m.Role == OrgMemberRole.Owner);
            if (ownerCount <= 1)
                return Conflict(new
                {
                    code = "LAST_OWNER",
                    message = "Cannot remove the last owner of an organisation."
                });
        }

        await memberships.RemoveAsync(target, ct);
        await memberships.SaveChangesAsync(ct);

        await audit.LogAsync("member.removed",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "org_membership",
            resourceId: memberId,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct: ct);

        return NoContent();
    }

    // GET /v1/orgs/{orgId}/members/{memberId}/data — GDPR right of access (06-security §6.2)
    [HttpGet("{memberId:guid}/data")]
    public async Task<IActionResult> GetMemberData(
        Guid orgId,
        Guid memberId,
        CancellationToken ct)
    {
        var callerId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);
        var targetUserId = UserId.From(memberId);

        // Caller must be the target user OR an org owner
        var callerMembership = await memberships.GetAsync(orgIdValue, callerId, ct);
        if (callerMembership is null) return Forbid();

        var isSelf = callerId == targetUserId;
        var isOwner = callerMembership.Role == OrgMemberRole.Owner;

        if (!isSelf && !isOwner) return Forbid();

        var targetMembership = await memberships.GetAsync(orgIdValue, targetUserId, ct);
        if (targetMembership is null) return NotFound();

        // Count jobs submitted by this user's org (not personal — jobs are org-scoped)
        var (orgJobs, _) = await jobs.ListAsync(orgIdValue, status: null, pageSize: 1, afterId: null, ct);

        // Return only personal data held for this user — no architecture content (CLAUDE.md §10.4)
        return Ok(new
        {
            userId = targetMembership.UserId.Value,
            role = targetMembership.Role.ToString().ToLower(),
            joinedAt = targetMembership.CreatedAt,
            orgJobCount = orgJobs.Count  // org-level stat; not personal data but useful for portability
        });
    }
}
