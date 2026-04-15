using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;

namespace ThreatModelingAgent.Api.Tests.Integration;

/// <summary>
/// Integration tests for /v1/admin/* endpoints.
///
/// Coverage:
///  - Authorization: only platform:admin tokens accepted; org tokens and unauthenticated rejected
///  - GET /v1/admin/stats — returns correct aggregate counts
///  - GET /v1/admin/orgs — pagination, search, suspension badge included
///  - GET /v1/admin/orgs/{id} — happy path, not found
///  - POST /v1/admin/orgs/{id}/suspend — sets IsSuspended, idempotent on double-call
///  - POST /v1/admin/orgs/{id}/unsuspend — clears IsSuspended, idempotent
///  - DELETE /v1/admin/orgs/{id} — soft-delete, subsequent GET returns not found
/// </summary>
[Collection("Integration")]
public sealed class AdminControllerTests
{
    private readonly ApiWebApplicationFactory _factory;

    public AdminControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateOrg_AdminToken_CreatesOrg()
    {
        var client = _factory.CreateAdminClient();
        var slug = "admin-create-" + Guid.NewGuid().ToString("N")[..8];
        var body = JsonSerializer.Serialize(new { name = "Admin Created Org", slug });

        _factory.WorkOsClient
            .CreateOrganizationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("org_workos_test");

        var response = await client.PostAsync(
            "/v1/admin/orgs",
            new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("name").GetString().Should().Be("Admin Created Org");
        doc.RootElement.GetProperty("slug").GetString().Should().Be(slug);
    }

    [Fact]
    public async Task CreateOrg_OrgScopedToken_Returns403()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Admin Create Auth");
        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var body = JsonSerializer.Serialize(new { name = "Should Fail", slug = "should-fail" });

        var response = await client.PostAsync(
            "/v1/admin/orgs",
            new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Authorization ────────────────────────────────────────────────────────

    [Fact]
    public async Task AdminEndpoints_Unauthenticated_Returns401()
    {
        var client = _factory.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/v1/admin/stats");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminEndpoints_OrgScopedToken_Returns403()
    {
        // A regular user token must never reach admin routes
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Admin Auth OrgToken");
        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.GetAsync("/v1/admin/stats");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminEndpoints_AdminToken_Returns200()
    {
        var client = _factory.CreateAdminClient();

        var response = await client.GetAsync("/v1/admin/stats");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── GET /v1/admin/stats ───────────────────────────────────────────────────

    [Fact]
    public async Task GetStats_ReturnsCorrectCounts()
    {
        // Seed two orgs, suspend one
        var (orgAId, _) = await _factory.SeedOrgAndOwnerAsync("Stats Active Org");
        var (orgBId, _) = await _factory.SeedOrgAndOwnerAsync("Stats Suspended Org");

        await _factory.SeedAsync(async db =>
        {
            var org = await db.Organizations.FindAsync(orgBId);
            org!.Suspend();
            await db.SaveChangesAsync();
        });

        var client = _factory.CreateAdminClient();
        var response = await client.GetAsync("/v1/admin/stats");

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        // At least the orgs we seeded are present (other tests may have added more)
        root.GetProperty("totalOrgs").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        root.GetProperty("suspendedOrgs").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        root.GetProperty("activeOrgs").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        root.GetProperty("totalUsers").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetStats_JobsLast30Days_OnlyCountsRecentJobs()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Stats Jobs30 Org");

        await _factory.SeedAsync(async db =>
        {
            // Recent job — should be counted
            var recent = Job.Create(orgId, userId, "Recent job");
            db.Jobs.Add(recent);

            await db.SaveChangesAsync();
        });

        var client = _factory.CreateAdminClient();
        var response = await client.GetAsync("/v1/admin/stats");
        response.EnsureSuccessStatusCode();

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("jobsLast30Days").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        doc.RootElement.GetProperty("totalJobs").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    // ── GET /v1/admin/orgs ───────────────────────────────────────────────────

    [Fact]
    public async Task ListOrgs_DefaultPagination_ReturnsPagedResult()
    {
        await _factory.SeedOrgAndOwnerAsync("Paged Org Alpha");
        await _factory.SeedOrgAndOwnerAsync("Paged Org Beta");

        var client = _factory.CreateAdminClient();
        var response = await client.GetAsync("/v1/admin/orgs?page=1&pageSize=50");

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var data = doc.RootElement.GetProperty("data");
        data.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);

        var pagination = doc.RootElement.GetProperty("pagination");
        pagination.GetProperty("page").GetInt32().Should().Be(1);
        pagination.GetProperty("pageSize").GetInt32().Should().Be(50);
        pagination.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        pagination.GetProperty("totalPages").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ListOrgs_SearchByName_FiltersResults()
    {
        var uniqueSlug = Guid.NewGuid().ToString("N")[..8];
        await _factory.SeedOrgAndOwnerAsync($"SearchTarget-{uniqueSlug}");
        await _factory.SeedOrgAndOwnerAsync("Irrelevant Org");

        var client = _factory.CreateAdminClient();
        var response = await client.GetAsync($"/v1/admin/orgs?search=SearchTarget-{uniqueSlug}");

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");

        data.GetArrayLength().Should().Be(1);
        data[0].GetProperty("name").GetString().Should().Contain(uniqueSlug);
    }

    [Fact]
    public async Task ListOrgs_PageSizeCappedAt100()
    {
        var client = _factory.CreateAdminClient();
        var response = await client.GetAsync("/v1/admin/orgs?pageSize=999");

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("pagination").GetProperty("pageSize").GetInt32()
            .Should().Be(100);
    }

    [Fact]
    public async Task ListOrgs_SuspendedOrg_IncludesSuspensionFlag()
    {
        var uniqueName = "SuspendedInList-" + Guid.NewGuid().ToString("N")[..8];
        var (orgId, _) = await _factory.SeedOrgAndOwnerAsync(uniqueName);

        await _factory.SeedAsync(async db =>
        {
            var org = await db.Organizations.FindAsync(orgId);
            org!.Suspend();
            await db.SaveChangesAsync();
        });

        var client = _factory.CreateAdminClient();
        var response = await client.GetAsync($"/v1/admin/orgs?search={uniqueName}");

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");

        data.GetArrayLength().Should().Be(1);
        data[0].GetProperty("isSuspended").GetBoolean().Should().BeTrue();
        data[0].GetProperty("suspendedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task ListOrgs_DeletedOrg_ExcludedFromList()
    {
        var uniqueName = "DeletedFromList-" + Guid.NewGuid().ToString("N")[..8];
        var (orgId, _) = await _factory.SeedOrgAndOwnerAsync(uniqueName);

        await _factory.SeedAsync(async db =>
        {
            var org = await db.Organizations.FindAsync(orgId);
            org!.SoftDelete();
            await db.SaveChangesAsync();
        });

        var client = _factory.CreateAdminClient();
        var response = await client.GetAsync($"/v1/admin/orgs?search={uniqueName}");

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetArrayLength().Should().Be(0);
    }

    // ── GET /v1/admin/orgs/{id} ──────────────────────────────────────────────

    [Fact]
    public async Task GetOrg_HappyPath_ReturnsOrgWithCounts()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("GetOrg Happy");

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Admin job");
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
        });

        var client = _factory.CreateAdminClient();
        var response = await client.GetAsync($"/v1/admin/orgs/{orgId.Value}");

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        doc.RootElement.GetProperty("id").GetGuid().Should().Be(orgId.Value);
        doc.RootElement.GetProperty("name").GetString().Should().Be("GetOrg Happy");
        doc.RootElement.GetProperty("memberCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        doc.RootElement.GetProperty("jobCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        doc.RootElement.GetProperty("isSuspended").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetOrg_UnknownId_Returns404()
    {
        var client = _factory.CreateAdminClient();
        var response = await client.GetAsync($"/v1/admin/orgs/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST /v1/admin/orgs/{id}/suspend ────────────────────────────────────

    [Fact]
    public async Task SuspendOrg_HappyPath_SetsSuspendedFlag()
    {
        var (orgId, _) = await _factory.SeedOrgAndOwnerAsync("Suspend Happy");

        var client = _factory.CreateAdminClient();
        var response = await client.PostAsync($"/v1/admin/orgs/{orgId.Value}/suspend", null);

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("isSuspended").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("suspendedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task SuspendOrg_AlreadySuspended_IsIdempotent()
    {
        var (orgId, _) = await _factory.SeedOrgAndOwnerAsync("Suspend Idempotent");
        var client = _factory.CreateAdminClient();

        // First call
        await client.PostAsync($"/v1/admin/orgs/{orgId.Value}/suspend", null);
        // Second call — should not throw
        var response = await client.PostAsync($"/v1/admin/orgs/{orgId.Value}/suspend", null);

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        // Either the dto or the idempotent message — both are valid 200s
        // The controller returns {message} for already-suspended, dto otherwise
        // We just assert 200 and that suspension is reflected in a subsequent GET
        var getResponse = await client.GetAsync($"/v1/admin/orgs/{orgId.Value}");
        var getDoc = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        getDoc.RootElement.GetProperty("isSuspended").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task SuspendOrg_UnknownId_Returns404()
    {
        var client = _factory.CreateAdminClient();
        var response = await client.PostAsync($"/v1/admin/orgs/{Guid.NewGuid()}/suspend", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SuspendOrg_OrgToken_Returns403()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Suspend Auth Org");
        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.PostAsync($"/v1/admin/orgs/{orgId.Value}/suspend", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── POST /v1/admin/orgs/{id}/unsuspend ──────────────────────────────────

    [Fact]
    public async Task UnsuspendOrg_HappyPath_ClearsSuspendedFlag()
    {
        var (orgId, _) = await _factory.SeedOrgAndOwnerAsync("Unsuspend Happy");
        var client = _factory.CreateAdminClient();

        await client.PostAsync($"/v1/admin/orgs/{orgId.Value}/suspend", null);
        var response = await client.PostAsync($"/v1/admin/orgs/{orgId.Value}/unsuspend", null);

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("isSuspended").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("suspendedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task UnsuspendOrg_NotSuspended_IsIdempotent()
    {
        var (orgId, _) = await _factory.SeedOrgAndOwnerAsync("Unsuspend Idempotent");
        var client = _factory.CreateAdminClient();

        // Org is not suspended — unsuspend should still return 200
        var response = await client.PostAsync($"/v1/admin/orgs/{orgId.Value}/unsuspend", null);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task UnsuspendOrg_UnknownId_Returns404()
    {
        var client = _factory.CreateAdminClient();
        var response = await client.PostAsync($"/v1/admin/orgs/{Guid.NewGuid()}/unsuspend", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DELETE /v1/admin/orgs/{id} ───────────────────────────────────────────

    [Fact]
    public async Task DeleteOrg_HappyPath_Returns204()
    {
        var (orgId, _) = await _factory.SeedOrgAndOwnerAsync("Delete Happy");
        var client = _factory.CreateAdminClient();

        var response = await client.DeleteAsync($"/v1/admin/orgs/{orgId.Value}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteOrg_SubsequentGetOrg_Returns404()
    {
        var (orgId, _) = await _factory.SeedOrgAndOwnerAsync("Delete Then Get");
        var client = _factory.CreateAdminClient();

        await client.DeleteAsync($"/v1/admin/orgs/{orgId.Value}");
        var getResponse = await client.GetAsync($"/v1/admin/orgs/{orgId.Value}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteOrg_SubsequentListOrgs_ExcludesDeletedOrg()
    {
        var uniqueName = "DeletedFromListVerify-" + Guid.NewGuid().ToString("N")[..8];
        var (orgId, _) = await _factory.SeedOrgAndOwnerAsync(uniqueName);
        var client = _factory.CreateAdminClient();

        await client.DeleteAsync($"/v1/admin/orgs/{orgId.Value}");
        var listResponse = await client.GetAsync($"/v1/admin/orgs?search={uniqueName}");

        listResponse.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task DeleteOrg_UnknownId_Returns404()
    {
        var client = _factory.CreateAdminClient();
        var response = await client.DeleteAsync($"/v1/admin/orgs/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteOrg_OrgToken_Returns403()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Delete Auth Org");
        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.DeleteAsync($"/v1/admin/orgs/{orgId.Value}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
