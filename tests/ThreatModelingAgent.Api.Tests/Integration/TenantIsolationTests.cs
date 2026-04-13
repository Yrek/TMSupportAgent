using System.Net;
using System.Text.Json;
using FluentAssertions;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.ValueObjects;
using ThreatModelingAgent.Infrastructure.Persistence;

namespace ThreatModelingAgent.Api.Tests.Integration;

/// <summary>
/// Verifies that cross-tenant data access is impossible at the controller layer.
///
/// Key behaviour under test:
/// - A job ID that exists in Org A is not visible to Org B — returns 404, not 403.
///   (403 would leak existence; 404 is the safe response — CLAUDE.md §7.6.)
/// - Querying jobs for an org the caller is not a member of returns Forbid (403).
/// - Membership checks prevent org A members from accessing org B resources.
/// </summary>
public sealed class TenantIsolationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public TenantIsolationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CrossOrgJobId_Returns404_NotLeakingExistence()
    {
        // Arrange: two separate orgs, each with an owner
        var (orgAId, userAId) = await _factory.SeedOrgAndOwnerAsync("Org A");
        var (orgBId, userBId) = await _factory.SeedOrgAndOwnerAsync("Org B");

        // Create a job in Org A
        var jobId = JobId.New();
        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgAId, userAId, "Org A job");
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        // Act: Org B owner tries to access Org A's job via Org B's route
        var clientB = _factory.CreateAuthenticatedClient(userBId, orgBId);

        var response = await clientB.GetAsync($"/v1/orgs/{orgBId.Value}/jobs/{jobId.Value}");

        // Assert: 404 — must not leak that the job exists in another org
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListJobs_ForOrgWithNoMembership_Returns403()
    {
        // Arrange
        var (orgAId, _) = await _factory.SeedOrgAndOwnerAsync("Isolation Org A2");
        var (orgBId, userBId) = await _factory.SeedOrgAndOwnerAsync("Isolation Org B2");

        // Act: User B (member of Org B) tries to list Org A's jobs
        var clientB = _factory.CreateAuthenticatedClient(userBId, orgBId);

        var response = await clientB.GetAsync($"/v1/orgs/{orgAId.Value}/jobs");

        // Assert: 403 because the JWT org_id doesn't match the route org_id
        // TenantContextMiddleware sets CurrentOrgId from JWT; the membership check uses that.
        // However, note: the route orgId and the JWT org_id are different here — the controller
        // checks membership of the ROUTE org using the JWT user. Since userB has no membership
        // in orgA, the membership check returns false → Forbid.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListJobs_ForOwnOrg_ReturnsOnlyOwnOrgJobs()
    {
        // Arrange: two orgs each with a job
        var (orgAId, userAId) = await _factory.SeedOrgAndOwnerAsync("Isolation List Org A");
        var (orgBId, userBId) = await _factory.SeedOrgAndOwnerAsync("Isolation List Org B");

        await _factory.SeedAsync(async db =>
        {
            db.Jobs.Add(Job.Create(orgAId, userAId, "Org A Job"));
            db.Jobs.Add(Job.Create(orgBId, userBId, "Org B Job"));
            await db.SaveChangesAsync();
        });

        var clientA = _factory.CreateAuthenticatedClient(userAId, orgAId);

        var response = await clientA.GetAsync($"/v1/orgs/{orgAId.Value}/jobs");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data").EnumerateArray().ToList();

        // Org A should see exactly 1 job (its own)
        data.Should().HaveCount(1);
        data[0].GetProperty("title").GetString().Should().Be("Org A Job");
    }

    [Fact]
    public async Task GetJob_WithCorrectOrgButWrongJobId_Returns404()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Isolation Org GetJob");

        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        // Totally random job ID that doesn't exist
        var response = await client.GetAsync($"/v1/orgs/{orgId.Value}/jobs/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListMembers_ForOrgWithNoMembership_Returns403()
    {
        var (orgAId, _) = await _factory.SeedOrgAndOwnerAsync("Members Isolation Org A");
        var (orgBId, userBId) = await _factory.SeedOrgAndOwnerAsync("Members Isolation Org B");

        var clientB = _factory.CreateAuthenticatedClient(userBId, orgBId);

        var response = await clientB.GetAsync($"/v1/orgs/{orgAId.Value}/members");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
