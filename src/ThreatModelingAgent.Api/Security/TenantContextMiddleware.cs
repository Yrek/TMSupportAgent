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
/// - Org-scoped routes reject requests where the org is suspended (ORG_SUSPENDED).
/// - If org_id is present but the org is not found or suspended, context is NOT set — RLS denies
///   all tenant-scoped data (fail-secure, CLAUDE.md §4.3).
/// - If org_id is absent (e.g. bootstrap call to /v1/auth/session), context is not set and the
///   request proceeds — individual endpoints enforce their own auth requirements, and RLS
///   blocks tenant-scoped data without context (fail-secure).
/// - Unauthenticated requests pass through (auth enforcement is on the endpoint).
///
/// org_id resolution (dual-path for test compatibility):
/// - Production: WorkOS puts "org_01XXXXX" in the JWT → looked up via GetByWorkOsOrgIdAsync.
/// - Tests: TestAuthHandler injects the internal GUID directly → looked up via GetByIdAsync.
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

            if (orgIdClaim is not null)
            {
                // Dual-path lookup:
                //   Production path: WorkOS JWT contains WorkOS org ID (e.g. "org_01XXXXX")
                //   Test path: TestAuthHandler injects the internal GUID directly
                Organization? org;
                if (Guid.TryParse(orgIdClaim, out var internalGuid))
                    org = await orgs.GetByIdAsync(OrgId.From(internalGuid), context.RequestAborted);
                else
                    org = await orgs.GetByWorkOsOrgIdAsync(orgIdClaim, context.RequestAborted);

                if (org is not null)
                {
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

                    tenantContext.SetFromClaim(org.Id.Value);
                }
                // org not found for given org_id — context not set, RLS denies all data (fail-secure)
            }
            // No org_id claim — context not set; endpoint-level auth and RLS enforce access
        }

        await next(context);
    }
}
