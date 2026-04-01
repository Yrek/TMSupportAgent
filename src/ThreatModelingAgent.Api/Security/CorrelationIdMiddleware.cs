namespace ThreatModelingAgent.Api.Security;

/// <summary>
/// Creates or adopts a correlation ID for every request (CLAUDE.md §10.5).
///
/// Client-supplied correlation IDs are adopted only after length validation.
/// The validated ID is set on the response header and stored in HttpContext.Items
/// for use by logging and downstream services.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Correlation-Id";
    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context);
        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers[HeaderName] = correlationId.ToString();

        await next(context);
    }

    private static Guid GetOrCreateCorrelationId(HttpContext context)
    {
        // Adopt client-supplied ID only after validation (CLAUDE.md §10.5)
        var clientHeader = context.Request.Headers[HeaderName].FirstOrDefault();
        if (clientHeader is { Length: > 0 and <= MaxLength }
            && Guid.TryParse(clientHeader, out var clientId))
        {
            return clientId;
        }

        return Guid.NewGuid();
    }
}
