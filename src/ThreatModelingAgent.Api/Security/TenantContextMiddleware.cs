using System.Security.Claims;

namespace ThreatModelingAgent.Api.Security;

/// <summary>
/// Extracts org_id from the validated JWT and sets it on TenantContext.
/// Runs after authentication middleware — JWT has already been validated by this point.
///
/// If an authenticated request is missing the org_id claim, the request fails with 403.
/// Unauthenticated requests pass through (auth enforcement is on the endpoint).
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
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
