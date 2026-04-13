using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ThreatModelingAgent.Api.Tests.Integration;

/// <summary>
/// Replaces JWT bearer authentication in integration tests.
/// Claims are passed via the X-Test-Claims header as a JSON object.
/// Absent header → NoResult (unauthenticated). Empty JSON object → authenticated, no claims.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestAuth";
    public const string ClaimsHeader = "X-Test-Claims";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ClaimsHeader, out var headerValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        var json = headerValues.ToString();
        Dictionary<string, string>? claimsMap;
        try
        {
            claimsMap = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid X-Test-Claims JSON."));
        }

        var claims = (claimsMap ?? [])
            .Select(kv => new Claim(kv.Key, kv.Value))
            .ToList();

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
