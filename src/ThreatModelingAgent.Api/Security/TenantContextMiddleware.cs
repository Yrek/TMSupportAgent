using System.Security.Claims;

namespace ThreatModelingAgent.Api.Security;

/// <summary>
/// Extracts org_id from the validated JWT and sets it on TenantContext.
/// Runs after authentication middleware — JWT has already been validated by this point.
///
/// Security invariants:
/// - If an authenticated request is missing the org_id claim, the request fails with 403.
/// - Tokens bearing the 'platform:admin' role are rejected on org-scoped routes —
///   the admin API does not exist in this service; using an admin token here is an error
///   (02-architecture §5.4 OD-4 resolved: platform:admin is out of MVP scope).
/// - Unauthenticated requests pass through (auth enforcement is on the endpoint).
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            // Reject platform:admin tokens on org-scoped routes — service identity separation
            // (CLAUDE.md §8.2, 02-architecture §6.3). Admin service is a separate deployment.
            var role = context.User.FindFirstValue(ClaimTypes.Role)
                       ?? context.User.FindFirstValue("role");
            if (string.Equals(role, "platform:admin", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "ADMIN_TOKEN_NOT_ACCEPTED",
                    message = "Platform admin tokens are not accepted by this service."
                });
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

            tenantContext.SetFromClaim(orgId);
        }

        await next(context);
    }
}
