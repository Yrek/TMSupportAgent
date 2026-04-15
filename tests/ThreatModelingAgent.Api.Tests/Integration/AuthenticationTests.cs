using System.Net;
using System.Text;
using FluentAssertions;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Api.Tests.Integration;

/// <summary>
/// Verifies that unauthenticated and incorrectly-formed requests are rejected
/// before reaching any controller logic.
/// </summary>
[Collection("Integration")]
public sealed class AuthenticationTests
{
    private readonly ApiWebApplicationFactory _factory;

    public AuthenticationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task NoToken_Returns401()
    {
        var client = _factory.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MissingOrgIdClaim_AuthenticatedUser_Returns403()
    {
        // Authenticated (sub claim present) but no org_id — TenantContextMiddleware should deny
        var claims = new Dictionary<string, string>
        {
            [System.Security.Claims.ClaimTypes.NameIdentifier] = Guid.NewGuid().ToString()
        };
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.ClaimsHeader,
            System.Text.Json.JsonSerializer.Serialize(claims));

        // Any org-scoped endpoint triggers the tenant context check
        var orgId = Guid.NewGuid();
        var response = await client.GetAsync($"/v1/orgs/{orgId}/jobs");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PlatformAdminRole_Returns403()
    {
        // platform:admin tokens must be rejected by TenantContextMiddleware
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync();

        var claims = new Dictionary<string, string>
        {
            [System.Security.Claims.ClaimTypes.NameIdentifier] = userId.Value.ToString(),
            [System.Security.Claims.ClaimTypes.Role] = "platform:admin",
            ["org_id"] = orgId.Value.ToString()
        };
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.ClaimsHeader,
            System.Text.Json.JsonSerializer.Serialize(claims));

        var response = await client.GetAsync($"/v1/orgs/{orgId.Value}/jobs");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuthenticatedWithValidClaims_CanReachEndpoint()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync();

        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        // Should get 200 (empty list), not 401 or 403
        var response = await client.GetAsync($"/v1/orgs/{orgId.Value}/jobs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OrgCreation_OnUserPlane_Returns403()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync();
        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var payload = """
                      { "name": "Blocked", "slug": "blocked-org" }
                      """;

        var response = await client.PostAsync(
            "/v1/orgs",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetMe_UnauthenticatedRequest_Returns401()
    {
        var client = _factory.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
