using System.Net;
using FluentAssertions;

namespace ThreatModelingAgent.Api.Tests.Integration;

/// <summary>
/// Verifies that the rate limiter tiers are wired up and return 429 when exceeded.
///
/// The "strict" tier (10 req/min) is easier to exhaust than the "api" tier (60/min),
/// so we target strict-tier endpoints. We send 11 requests and expect the 11th to be
/// throttled.
///
/// Note: the rate limiter is per-IP; WebApplicationFactory test clients present the
/// same loopback IP, so the counter accumulates across all requests in a test.
/// Each test should use its own factory-scoped client to avoid cross-test pollution
/// — or accept that limits may already be partially consumed if tests run sequentially.
///
/// Since rate limit windows reset per-instance and tests run in-process, we use a
/// dedicated factory per test class via IClassFixture to get a fresh rate limiter.
/// </summary>
[Collection("Integration")]
public sealed class RateLimitingTests
{
    private readonly ApiWebApplicationFactory _factory;

    public RateLimitingTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task StrictTierEndpoint_ExceedsLimit_Returns429()
    {
        // POST /v1/orgs/{orgId}/architecture/confirm is "strict" (10/min)
        // We use a non-existent org, so we'll get 401/403/404 — but the rate
        // limiter fires BEFORE the handler, so after 10 requests the 11th → 429.
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("RateLimit Org");
        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var jobId = Guid.NewGuid();

        // POST confirm is strict-tier; send 10 legitimate requests (they'll 404/409 on job)
        for (var i = 0; i < 10; i++)
        {
            await client.PostAsync(
                $"/v1/orgs/{orgId.Value}/jobs/{jobId}/architecture/confirm",
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        }

        // 11th request should be rate-limited
        var response = await client.PostAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId}/architecture/confirm",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task RateLimitResponse_ContainsRetryAfterHeader()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("RateLimit Header Org");
        // Use a fresh client to avoid pollution from other tests
        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var jobId = Guid.NewGuid();
        var url = $"/v1/orgs/{orgId.Value}/jobs/{jobId}/architecture/confirm";
        var body = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        // Exhaust the strict limit (10 req/min)
        for (var i = 0; i < 10; i++)
            await client.PostAsync(url, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        var throttled = await client.PostAsync(url, body);

        throttled.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        throttled.Headers.Should().ContainKey("Retry-After");
    }

    [Fact]
    public async Task RateLimitResponse_ContainsRateLimitExceededCode()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("RateLimit Code Org");
        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var jobId = Guid.NewGuid();
        var url = $"/v1/orgs/{orgId.Value}/jobs/{jobId}/architecture/confirm";

        for (var i = 0; i < 10; i++)
            await client.PostAsync(url, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        var throttled = await client.PostAsync(url, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        var body = await throttled.Content.ReadAsStringAsync();
        body.Should().Contain("RATE_LIMIT_EXCEEDED");
    }
}
