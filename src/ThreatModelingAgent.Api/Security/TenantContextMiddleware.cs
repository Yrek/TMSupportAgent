using System.Security.Claims;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Api.Security;

/// <summary>
/// Resolves org context from the validated JWT and sets it on TenantContext.
/// Runs after authentication middleware — JWT has already been validated by this point.
///
/// Security invariants:
/// - platform:admin tokens are allowed through to /v1/admin/* routes only; rejected everywhere else.
/// - Authenticated non-admin requests MUST carry a valid org_id — missing or unresolvable org_id
///   returns 403 MISSING_ORG_CONTEXT (fail-secure, CLAUDE.md §4.3).
/// - Org-scoped routes reject requests where the org is suspended (ORG_SUSPENDED).
/// - Unauthenticated requests pass through (auth enforcement is on the endpoint).
///
/// org_id resolution (dual-path for test compatibility):
/// - Production: WorkOS puts "org_01XXXXX" in the JWT → looked up via GetByWorkOsOrgIdAsync;
///   tenant context is set to the org's internal UUID.
/// - Tests: TestAuthHandler injects the internal GUID directly → looked up via GetByIdAsync;
///   tenant context is set to the same GUID that was claimed (verified to exist in DB).
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext,
        IOrganizationRepository orgs)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var role = context.User.FindFirstValue(ClaimTypes.Role)
                       ?? context.User.FindFirstValue("role");
            var isPlatformAdmin = string.Equals(role, "platform:admin", StringComparison.OrdinalIgnoreCase);

            if (isPlatformAdmin)
            {
                // Platform admin tokens are only accepted on /v1/admin/* routes.
                // All other routes reject them — service identity separation (CLAUDE.md §8.2).
                if (!context.Request.Path.StartsWithSegments("/v1/admin", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        code = "ADMIN_TOKEN_NOT_ACCEPTED",
                        message = "Platform admin tokens are not accepted on org-scoped routes."
                    });
                    return;
                }

                // Admin route — no org_id claim required; skip tenant context setup
                await next(context);
                return;
            }

            var orgIdClaim = context.User.FindFirstValue("org_id");

            if (orgIdClaim is null)
            {
                // Authenticated non-admin with no org_id — fail-secure (CLAUDE.md §4.3).
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "MISSING_ORG_CONTEXT",
                    message = "This request requires an organization context."
                });
                return;
            }

            // Dual-path lookup:
            //   Production path: WorkOS JWT contains WorkOS org ID (e.g. "org_01XXXXX")
            //   Test path: TestAuthHandler injects the internal GUID directly
            Organization? org;
            Guid? resolvedInternalId;

            if (Guid.TryParse(orgIdClaim, out var internalGuid))
            {
                org = await orgs.GetByIdAsync(OrgId.From(internalGuid), context.RequestAborted);
                resolvedInternalId = internalGuid; // use the verified claimed GUID directly
            }
            else
            {
                org = await orgs.GetByWorkOsOrgIdAsync(orgIdClaim, context.RequestAborted);
                resolvedInternalId = org?.Id.Value;
            }

            if (org is null)
            {
                // org_id present but not found — fail-secure (CLAUDE.md §4.3).
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "MISSING_ORG_CONTEXT",
                    message = "This request requires an organization context."
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
}
