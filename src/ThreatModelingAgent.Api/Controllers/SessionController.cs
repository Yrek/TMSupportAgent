using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ThreatModelingAgent.Api.Security;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.Interfaces;

namespace ThreatModelingAgent.Api.Controllers;

/// <summary>
/// Authentication session endpoints.
///
/// GET  /v1/auth/session  — confirm auth state and return current user's org memberships
/// DELETE /v1/auth/session — client-side sign-out hint; server returns 204 (JWT is stateless)
///
/// No database writes occur here. All data is derived from the validated JWT and the
/// membership table, which is already in scope for the authenticated user.
/// </summary>
[ApiController]
[Authorize]
[Route("v1/auth/session")]
[EnableRateLimiting("api")]
public sealed class SessionController(
    IOrganizationRepository orgs,
    IMembershipRepository memberships) : ControllerBase
{
    // GET /v1/auth/session
    [HttpGet]
    public async Task<IActionResult> GetSession(CancellationToken ct)
    {
        var userId = User.GetUserId();

        // Load orgs the user belongs to — includes role per org
        var userOrgs = await orgs.ListByUserAsync(userId, ct);

        var orgList = new List<object>();
        foreach (var org in userOrgs)
        {
            var membership = await memberships.GetAsync(org.Id, userId, ct);
            if (membership is null) continue;

            orgList.Add(new
            {
                orgId = org.Id.Value,
                name = org.Name,
                slug = org.Slug,
                role = membership.Role.ToString().ToLower()
            });
        }

        // sub claim is the authoritative user identifier (CLAUDE.md §8.1)
        return Ok(new
        {
            userId = userId.Value,
            orgs = orgList
        });
    }

    // DELETE /v1/auth/session — stateless sign-out hint
    // JWT cannot be server-side revoked without a token denylist (out of MVP scope).
    // Clients MUST discard the token on receiving 204.
    [HttpDelete]
    public IActionResult SignOut()
    {
        // 204 is sufficient — no server state to clear for a stateless JWT
        return NoContent();
    }
}
