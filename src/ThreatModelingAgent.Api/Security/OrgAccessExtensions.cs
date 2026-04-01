using System.Security.Claims;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Api.Security;

/// <summary>
/// Shared helpers for server-side authorization checks.
/// Security logic MUST live here — never duplicated per controller (CLAUDE.md §14).
/// </summary>
public static class OrgAccessExtensions
{
    /// <summary>
    /// Returns the UserId from the validated JWT sub claim.
    /// Throws if the claim is missing or malformed — fail secure (CLAUDE.md §4.3).
    /// </summary>
    public static UserId GetUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? user.FindFirstValue("sub")
                  ?? throw new InvalidOperationException("JWT is missing 'sub' claim.");

        if (!Guid.TryParse(sub, out var userId))
            throw new InvalidOperationException("JWT 'sub' claim is not a valid GUID.");

        return UserId.From(userId);
    }

    /// <summary>
    /// Verifies the current user is a member of the requested org with at least the given role.
    /// Returns false if not a member or insufficient role — caller must return 403.
    /// org_id comes from the validated JWT via TenantContext, NOT from the route (CLAUDE.md §8.2).
    /// </summary>
    public static async Task<bool> HasOrgAccessAsync(
        this IMembershipRepository memberships,
        OrgId orgId,
        UserId userId,
        OrgMemberRole minimumRole = OrgMemberRole.Member,
        CancellationToken ct = default)
    {
        var membership = await memberships.GetAsync(orgId, userId, ct);
        if (membership is null) return false;

        return minimumRole == OrgMemberRole.Member
            || membership.Role == OrgMemberRole.Owner;
    }
}
