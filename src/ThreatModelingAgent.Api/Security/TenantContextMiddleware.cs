using System.Security.Claims;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Api.Security;

/// <summary>
/// Extracts org_id from the validated JWT and sets it on TenantContext.
/// Runs after authentication middleware — JWT has already been validated by this point.
///
/// Security invariants:
/// - platform:admin tokens are allowed through to /v1/admin/* routes only; rejected everywhere else.
/// - Org-scoped routes reject requests where the org is suspended (ORG_SUSPENDED).
/// - If an authenticated, non-admin request is missing the org_id claim, the request fails with 403.
/// - Unauthenticated requests pass through (auth enforcement is on the endpoint).
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

            if (orgIdClaim is null || !Guid.TryParse(orgIdClaim, out var orgId))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "MISSING_ORG_CONTEXT",
                    message = "Authenticated requests must include a valid org_id claim."
                });
                return;
            }

            // Check org suspension — fail closed (CLAUDE.md §4.3)
            var org = await orgs.GetByIdAsync(OrgId.From(orgId), context.RequestAborted);
            if (org is not null && org.IsSuspended)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "ORG_SUSPENDED",
                    message = "This organization has been suspended. Contact support."
                });
                return;
            }

            tenantContext.SetFromClaim(orgId);
        }

        await next(context);
    }
}
