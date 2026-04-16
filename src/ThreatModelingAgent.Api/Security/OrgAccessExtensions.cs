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
    public const string AppUserIdClaim = "app_user_id";

    public static string? GetSubject(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");

    /// <summary>
    /// Returns the internal UserId from the claim injected by TenantContextMiddleware.
    /// Falls back to sub only for test tokens where sub is already an internal GUID.
    /// Throws if no valid internal user id can be resolved — fail secure (CLAUDE.md §4.3).
    /// </summary>
    public static UserId GetUserId(this ClaimsPrincipal user)
    {
        var internalUserId = user.FindFirstValue(AppUserIdClaim);
        if (Guid.TryParse(internalUserId, out var appUserId))
            return UserId.From(appUserId);

        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? user.FindFirstValue("sub");

        if (!Guid.TryParse(sub, out var userId))
            throw new InvalidOperationException("No valid internal user id is present in claims.");

        return UserId.From(userId);
    }

    /// <summary>
    /// Resolves the internal user id from claims.
    /// Supports both internal GUID test tokens and WorkOS user ids (user_...) by repository lookup.
    /// Returns null when no internal user mapping exists.
    /// </summary>
    public static async Task<UserId?> ResolveUserIdAsync(
        this ClaimsPrincipal user,
        IUserRepository users,
        CancellationToken ct = default)
    {
        var internalUserId = user.FindFirstValue(AppUserIdClaim);
        if (Guid.TryParse(internalUserId, out var appUserId))
            return UserId.From(appUserId);

        var sub = user.GetSubject();
        if (string.IsNullOrWhiteSpace(sub))
            return null;

        if (Guid.TryParse(sub, out var guidSub))
            return UserId.From(guidSub);

        var mapped = await users.GetByWorkOsUserIdAsync(sub, ct);
        return mapped?.Id;
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
