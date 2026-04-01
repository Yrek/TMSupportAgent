namespace ThreatModelingAgent.Api.Security;

/// <summary>
/// Sets all required security headers centrally on every response (CLAUDE.md §11).
/// Headers are NEVER managed per-route — this is the single enforcement point.
///
/// Also removes identifying headers (Server, X-Powered-By) per CLAUDE.md §11.2.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // Required headers (CLAUDE.md §11.1) — set before next() so they are
        // present regardless of how the response is written (including unit tests
        // using DefaultHttpContext where OnStarting callbacks never fire).
        headers["Content-Security-Policy"] = "default-src 'none'";
        headers["X-Frame-Options"] = "DENY";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Strict-Transport-Security"] = "max-age=0"; // staged: increase before GA
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

        // Cache-Control: no-store on all responses (CLAUDE.md §11.3)
        if (!headers.ContainsKey("Cache-Control"))
            headers["Cache-Control"] = "no-store";

        // Remove identifying headers (CLAUDE.md §11.2)
        headers.Remove("Server");
        headers.Remove("X-Powered-By");
        headers.Remove("X-AspNet-Version");
        headers.Remove("X-AspNetMvc-Version");

        await next(context);
    }
}
