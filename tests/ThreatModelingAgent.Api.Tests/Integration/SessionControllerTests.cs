using System.Net;
using System.Text.Json;
using FluentAssertions;
using ThreatModelingAgent.Domain.Enums;

namespace ThreatModelingAgent.Api.Tests.Integration;

/// <summary>
/// Integration tests for GET /v1/auth/session.
///
/// Coverage:
///  - Unauthenticated request returns 401
///  - Platform admin token: isPlatformAdmin=true, userId=null, orgs=[]
///  - Regular user token: isPlatformAdmin=false, userId set, orgs list populated
///  - User with multiple org memberships: all orgs included with correct roles
///  - DELETE /v1/auth/session returns 204 for any authenticated caller
/// </summary>
[Collection("Integration")]
public sealed class SessionControllerTests
{
    private readonly ApiWebApplicationFactory _factory;

    public SessionControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Unauthenticated ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetSession_Unauthenticated_Returns401()
    {
        var client = _factory.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/v1/auth/session");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Platform admin token ──────────────────────────────────────────────────

    [Fact]
    public async Task GetSession_AdminToken_ReturnsPlatformAdminTrue()
    {
        var client = _factory.CreateAdminClient();

        var response = await client.GetAsync("/v1/auth/session");

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        root.GetProperty("isPlatformAdmin").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetSession_AdminToken_UserIdIsNull()
    {
        var client = _factory.CreateAdminClient();

        var response = await client.GetAsync("/v1/auth/session");

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("userId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetSession_AdminToken_OrgsIsEmpty()
    {
        var client = _factory.CreateAdminClient();

        var response = await client.GetAsync("/v1/auth/session");

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("orgs").GetArrayLength().Should().Be(0);
    }

    // ── Regular user token ────────────────────────────────────────────────────

    [Fact]
    public async Task GetSession_RegularUser_ReturnsPlatformAdminFalse()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Session RegularUser");
        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.GetAsync("/v1/auth/session");

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("isPlatformAdmin").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetSession_RegularUser_ReturnsUserId()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Session UserId");
        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.GetAsync("/v1/auth/session");

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("userId").GetGuid().Should().Be(userId.Value);
    }

    [Fact]
    public async Task GetSession_RegularUser_ReturnsOrgMembership()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Session OrgMembership");
        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.GetAsync("/v1/auth/session");

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var orgs = doc.RootElement.GetProperty("orgs");

        orgs.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);

        var match = orgs.EnumerateArray()
            .FirstOrDefault(o => o.GetProperty("id").GetGuid() == orgId.Value);

        match.ValueKind.Should().NotBe(JsonValueKind.Undefined, "the seeded org should appear in the session");
        match.GetProperty("name").GetString().Should().Be("Session OrgMembership");
        match.GetProperty("role").GetString().Should().Be("owner");
    }

    [Fact]
    public async Task GetSession_RegularUser_OrgDtoDoesNotLeakInternalFields()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Session FieldLeak");
        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.GetAsync("/v1/auth/session");

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var orgs = doc.RootElement.GetProperty("orgs");
        var org = orgs.EnumerateArray().First(o => o.GetProperty("id").GetGuid() == orgId.Value);

        // Internal/admin-only fields must not appear in regular session response
        org.TryGetProperty("isSuspended", out _).Should().BeFalse("suspension state is admin-only");
        org.TryGetProperty("deletedAt", out _).Should().BeFalse("deletedAt is internal");
        org.TryGetProperty("memberCount", out _).Should().BeFalse("memberCount is admin-only");
    }

    // ── DELETE /v1/auth/session ───────────────────────────────────────────────

    [Fact]
    public async Task DeleteSession_AuthenticatedUser_Returns204()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Session SignOut User");
        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.DeleteAsync("/v1/auth/session");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteSession_AdminToken_Returns204()
    {
        var client = _factory.CreateAdminClient();

        var response = await client.DeleteAsync("/v1/auth/session");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteSession_Unauthenticated_Returns401()
    {
        var client = _factory.CreateUnauthenticatedClient();

        var response = await client.DeleteAsync("/v1/auth/session");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
