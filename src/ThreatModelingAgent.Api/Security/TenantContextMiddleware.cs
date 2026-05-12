using System.Security.Claims;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Api.Security;

/// <summary>
/// Resolves org context from the validated JWT and sets it on TenantContext.
/// Runs after authentication middleware — JWT has already been validated by this point.
///
/// Security invariants:
/// - platform:admin tokens are allowed through to /v1/admin/* and /v1/auth/session;
///   rejected everywhere else.
/// - Authenticated non-admin requests MUST carry a valid org context — missing or
///   unresolvable org returns 403 MISSING_ORG_CONTEXT (fail-secure, CLAUDE.md §4.3).
/// - Org-scoped routes reject requests where the org is suspended (ORG_SUSPENDED).
/// - Unauthenticated requests pass through (auth enforcement is on the endpoint).
///
/// org_id resolution (three paths):
/// - WorkOS: JWT contains "org_id" = WorkOS org ID ("org_01XXXXX") → GetByWorkOsOrgIdAsync.
/// - Tests:  TestAuthHandler injects the internal GUID directly → GetByIdAsync.
/// - Entra:  JWT contains "tid" (Entra tenant ID) with no "org_id" claim.
///           Resolves org via GetByEntraTenantIdAsync("tid") (SaaS per-org future path),
///           or falls back to EntraIdOptions.DefaultOrgId (self-hosted path).
///           Users are JIT-provisioned on first login.
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next, EntraIdOptions entraOptions)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext,
        IOrganizationRepository orgs,
        IMembershipRepository memberships,
        IUserRepository users)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var isPlatformAdmin = context.User.IsPlatformAdmin();
            var isAdminRoute = context.Request.Path.StartsWithSegments("/v1/admin", StringComparison.OrdinalIgnoreCase);
            var isSessionRoute = context.Request.Path.StartsWithSegments("/v1/auth/session", StringComparison.OrdinalIgnoreCase);

            if (isSessionRoute || (isPlatformAdmin && isAdminRoute))
            {
                await next(context);
                return;
            }

            var orgIdClaim = context.User.FindFirstValue("org_id");
            var entraTid = context.User.FindFirstValue("tid"); // Entra tenant ID claim

            // ── Entra ID path ─────────────────────────────────────────────────
            if (orgIdClaim is null && entraTid is not null)
            {
                await HandleEntraAsync(context, tenantContext, orgs, memberships, users, entraTid);
                return;
            }

            // ── WorkOS / test path ────────────────────────────────────────────
            if (orgIdClaim is null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "MISSING_ORG_CONTEXT",
                    message = "This request requires an organization context."
                });
                return;
            }

            Organization? org;
            Guid? resolvedInternalId;

            if (Guid.TryParse(orgIdClaim, out var internalGuid))
            {
                org = await orgs.GetByIdAsync(OrgId.From(internalGuid), context.RequestAborted);
                resolvedInternalId = internalGuid;
            }
            else
            {
                org = await orgs.GetByWorkOsOrgIdAsync(orgIdClaim, context.RequestAborted);
                resolvedInternalId = org?.Id.Value;
            }

            if (org is null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "MISSING_ORG_CONTEXT",
                    message = "This request requires an organization context."
                });
                return;
            }

            var internalUserId = await context.User.ResolveUserIdAsync(users, context.RequestAborted);
            if (internalUserId is null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "MISSING_USER_CONTEXT",
                    message = "This request requires a valid user context."
                });
                return;
            }

            StampUserIdClaim(context, internalUserId.Value);

            var isMappedMember = await memberships.GetAsync(
                org.Id, internalUserId.Value, context.RequestAborted);
            if (isMappedMember is null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "ORG_MEMBERSHIP_REQUIRED",
                    message = "Your account is not mapped to this organization."
                });
                return;
            }

            if (org.IsSuspended)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "ORG_SUSPENDED",
                    message = "This organization has been suspended. Contact support."
                });
                return;
            }

            tenantContext.SetFromClaim(resolvedInternalId!.Value);
        }

        await next(context);
    }

    private async Task HandleEntraAsync(
        HttpContext context,
        TenantContext tenantContext,
        IOrganizationRepository orgs,
        IMembershipRepository memberships,
        IUserRepository users,
        string entraTid)
    {
        var ct = context.RequestAborted;

        // ── Resolve org ───────────────────────────────────────────────────────
        // SaaS path: org has EntraTenantId matching the JWT "tid" claim.
        // Self-hosted path: no per-org Entra config; use configured DefaultOrgId.
        Organization? org = await orgs.GetByEntraTenantIdAsync(entraTid, ct);

        if (org is null && entraOptions.DefaultOrgId.HasValue)
            org = await orgs.GetByIdAsync(OrgId.From(entraOptions.DefaultOrgId.Value), ct);

        if (org is null)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "MISSING_ORG_CONTEXT",
                message = "No organization is configured for this Entra tenant."
            });
            return;
        }

        if (org.IsSuspended)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "ORG_SUSPENDED",
                message = "This organization has been suspended. Contact support."
            });
            return;
        }

        // ── Resolve or JIT-provision user ─────────────────────────────────────
        // Entra stable identifier is "oid" (object ID), not "sub" (which is per-app).
        var oid = context.User.FindFirstValue("oid");
        if (string.IsNullOrWhiteSpace(oid))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "MISSING_USER_CONTEXT",
                message = "Entra ID token is missing the required 'oid' claim."
            });
            return;
        }

        var externalId = $"entra:{oid}";
        var user = await users.GetByWorkOsUserIdAsync(externalId, ct);

        if (user is null)
        {
            // JIT provision: first time this Entra user signs in.
            var email = context.User.FindFirstValue("preferred_username")
                     ?? context.User.FindFirstValue("upn")
                     ?? context.User.FindFirstValue(ClaimTypes.Email)
                     ?? $"{oid}@entra";
            var displayName = context.User.FindFirstValue("name");

            user = User.Create(externalId, email, displayName);
            await users.AddAsync(user, ct);

            var role = entraOptions.AdminOids.Contains(oid) ? OrgMemberRole.Owner : OrgMemberRole.Member;
            var membership = OrgMembership.Create(org.Id, user.Id, role);
            await memberships.AddAsync(membership, ct);

            await users.SaveChangesAsync(ct);
            await memberships.SaveChangesAsync(ct);
        }
        else
        {
            // Existing user — verify membership, provision if missing (e.g., added to a new org).
            var existingMembership = await memberships.GetAsync(org.Id, user.Id, ct);
            if (existingMembership is null)
            {
                var role = entraOptions.AdminOids.Contains(oid) ? OrgMemberRole.Owner : OrgMemberRole.Member;
                var membership = OrgMembership.Create(org.Id, user.Id, role);
                await memberships.AddAsync(membership, ct);
                await memberships.SaveChangesAsync(ct);
            }
        }

        StampUserIdClaim(context, user.Id);
        tenantContext.SetFromClaim(org.Id.Value);

        await next(context);
    }

    private static void StampUserIdClaim(HttpContext context, UserId userId)
    {
        if (context.User.Identity is ClaimsIdentity identity &&
            !context.User.HasClaim(OrgAccessExtensions.AppUserIdClaim, userId.Value.ToString()))
        {
            identity.AddClaim(new Claim(OrgAccessExtensions.AppUserIdClaim, userId.Value.ToString()));
        }
    }
}
