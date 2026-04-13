using System.Net;
using FluentAssertions;

namespace ThreatModelingAgent.Api.Tests.Integration;

/// <summary>
/// Verifies that suspended organizations cannot be accessed by their members.
///
/// TenantContextMiddleware returns 403 (ORG_SUSPENDED) for any org-scoped request
/// when the org's IsSuspended flag is true — regardless of the endpoint or the
/// caller's role within the org.
///
/// Coverage:
///  - Suspended org member: all org-scoped routes return 403 with ORG_SUSPENDED code
///  - Admin token: still allowed to reach /v1/admin/* even when org is suspended
///  - Unsuspended org: member access restored
///  - Error code in body: includes "ORG_SUSPENDED" for clients to distinguish
/// </summary>
[Collection("Integration")]
public sealed class OrgSuspensionTests
{
    private readonly ApiWebApplicationFactory _factory;

    public OrgSuspensionTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task SuspendOrgAsync(Guid orgId)
    {
        await _factory.SeedAsync(async db =>
        {
            var org = await db.Organizations.FindAsync(orgId);
            org!.Suspend();
            await db.SaveChangesAsync();
        });
    }

    private async Task UnsuspendOrgAsync(Guid orgId)
    {
        await _factory.SeedAsync(async db =>
        {
            var org = await db.Organizations.FindAsync(orgId);
            org!.Unsuspend();
            await db.SaveChangesAsync();
        });
    }

    // ── Suspended org blocks member access ───────────────────────────────────

    [Fact]
    public async Task SuspendedOrg_JobsEndpoint_Returns403()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Suspended Jobs");
        await SuspendOrgAsync(orgId.Value);

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.GetAsync($"/v1/orgs/{orgId.Value}/jobs");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SuspendedOrg_ResponseBody_ContainsOrgSuspendedCode()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Suspended Code Check");
        await SuspendOrgAsync(orgId.Value);

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.GetAsync($"/v1/orgs/{orgId.Value}/jobs");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("ORG_SUSPENDED");
    }

    [Fact]
    public async Task SuspendedOrg_ArchitectureEndpoint_Returns403()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Suspended Architecture");
        await SuspendOrgAsync(orgId.Value);

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.GetAsync($"/v1/orgs/{orgId.Value}/jobs/{Guid.NewGuid()}/architecture");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SuspendedOrg_ThreatsEndpoint_Returns403()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Suspended Threats");
        await SuspendOrgAsync(orgId.Value);

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.GetAsync($"/v1/orgs/{orgId.Value}/threats");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Admin token unaffected by org suspension ─────────────────────────────

    [Fact]
    public async Task SuspendedOrg_AdminToken_CanStillReachAdminRoutes()
    {
        var (orgId, _) = await _factory.SeedOrgAndOwnerAsync("Suspended Admin Still Works");
        await SuspendOrgAsync(orgId.Value);

        var client = _factory.CreateAdminClient();
        var response = await client.GetAsync("/v1/admin/stats");

        // Admin routes must not be affected by any org's suspension state
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task SuspendedOrg_AdminToken_CanUnsuspendOrg()
    {
        var (orgId, _) = await _factory.SeedOrgAndOwnerAsync("Suspended Can Unsuspend");
        await SuspendOrgAsync(orgId.Value);

        var client = _factory.CreateAdminClient();
        var response = await client.PostAsync($"/v1/admin/orgs/{orgId.Value}/unsuspend", null);

        response.EnsureSuccessStatusCode();
    }

    // ── Unsuspend restores access ─────────────────────────────────────────────

    [Fact]
    public async Task UnsuspendedOrg_MemberAccessRestored()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Restored Access");

        // Suspend
        await SuspendOrgAsync(orgId.Value);
        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var blockedResponse = await client.GetAsync($"/v1/orgs/{orgId.Value}/jobs");
        blockedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Unsuspend
        await UnsuspendOrgAsync(orgId.Value);
        var restoredResponse = await client.GetAsync($"/v1/orgs/{orgId.Value}/jobs");
        restoredResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Cross-org: suspension of one org does not affect another ─────────────

    [Fact]
    public async Task SuspendedOrgA_DoesNotAffectOrgB()
    {
        var (orgAId, _) = await _factory.SeedOrgAndOwnerAsync("Cross Suspended A");
        var (orgBId, userBId) = await _factory.SeedOrgAndOwnerAsync("Cross Active B");

        await SuspendOrgAsync(orgAId.Value);

        // Org B member should still have full access
        var clientB = _factory.CreateAuthenticatedClient(userBId, orgBId);
        var response = await clientB.GetAsync($"/v1/orgs/{orgBId.Value}/jobs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
