using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ThreatModelingAgent.Api.Errors;

/// <summary>
/// Global exception handler. Returns RFC 7807 Problem Details with no internal detail.
/// Stack traces, internal paths, and DB errors MUST NOT be returned to clients (CLAUDE.md §7.6).
/// Full diagnostics are written to structured logs only.
/// </summary>
public sealed class ProblemDetailsErrorHandler(ILogger<ProblemDetailsErrorHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken ct)
    {
        var correlationId = context.Items["CorrelationId"] as Guid? ?? Guid.NewGuid();

        // Log full detail server-side; expose nothing to client
        logger.LogError(exception,
            "Unhandled exception. CorrelationId={CorrelationId} Path={Path}",
            correlationId,
            context.Request.Path);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = null,              // no internal detail (CLAUDE.md §7.6)
            Extensions = { ["correlationId"] = correlationId }
        };

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(problem, ct);

        return true;
    }
}
