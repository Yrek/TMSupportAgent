using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Api.Tests.Integration;

[Collection("Integration")]
public sealed class MembersControllerTests
{
    private readonly ApiWebApplicationFactory _factory;

    public MembersControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── List members ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ListMembers_HappyPath_ReturnsMembers()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("List Members Happy");

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.GetAsync($"/v1/orgs/{orgId.Value}/members");

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data").EnumerateArray().ToList();
        data.Should().HaveCount(1); // just the owner seeded
        data[0].GetProperty("role").GetString().Should().Be("owner");
        // Must NOT expose email or display name (CLAUDE.md §10.4)
        data[0].TryGetProperty("email", out _).Should().BeFalse();
        data[0].TryGetProperty("displayName", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ListMembers_CrossOrg_Returns403()
    {
        var (orgAId, _) = await _factory.SeedOrgAndOwnerAsync("List Members CrossOrg A");
        var (orgBId, userBId) = await _factory.SeedOrgAndOwnerAsync("List Members CrossOrg B");

        var clientB = _factory.CreateAuthenticatedClient(userBId, orgBId);
        var response = await clientB.GetAsync($"/v1/orgs/{orgAId.Value}/members");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Invite member ─────────────────────────────────────────────────────────

    [Fact]
    public async Task InviteMember_HappyPath_Returns202()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Invite Happy Org");

        // Seed the org with a WorkOS org ID so the invite can proceed
        await _factory.SeedAsync(async db =>
        {
            var org = await db.Organizations.FindAsync(orgId.Value);
            org!.SetWorkOsOrgId("workos_org_123");
            await db.SaveChangesAsync();
        });

        _factory.WorkOsClient
            .SendInvitationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("inv_123"));

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var body = JsonSerializer.Serialize(new { email = "newmember@test.invalid" });

        var response = await client.PostAsync(
            $"/v1/orgs/{orgId.Value}/members",
            new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task InviteMember_SameResponseForExistingAndNonExisting_NoEnumerationOracle()
    {
        // WorkOS 422 (existing member) must return 202 — identical to new invite (CLAUDE.md §7.6)
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Invite NoEnum Org");

        await _factory.SeedAsync(async db =>
        {
            var org = await db.Organizations.FindAsync(orgId.Value);
            org!.SetWorkOsOrgId("workos_org_enum");
            await db.SaveChangesAsync();
        });

        _factory.WorkOsClient
            .SendInvitationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new WorkOsException("already a member", statusCode: 422));

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var body = JsonSerializer.Serialize(new { email = "existing@test.invalid" });

        var response = await client.PostAsync(
            $"/v1/orgs/{orgId.Value}/members",
            new StringContent(body, Encoding.UTF8, "application/json"));

        // Must be 202 — same as a new invite — no leak about whether the user exists
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    // ── Update role ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRole_OwnerOnly_MemberCannotUpdateRole()
    {
        // Seed an org with an owner + a regular member
        var (orgId, ownerId) = await _factory.SeedOrgAndOwnerAsync("Role Update Org");
        UserId memberId = default!;

        await _factory.SeedAsync(async db =>
        {
            var memberUser = User.Create("workos_member", "member@test.invalid");
            db.Users.Add(memberUser);
            var membership = OrgMembership.Create(orgId, memberUser.Id, OrgMemberRole.Member);
            db.OrgMemberships.Add(membership);
            await db.SaveChangesAsync();
            memberId = memberUser.Id;
        });

        // Act as the regular member (not owner)
        var memberClient = _factory.CreateAuthenticatedClient(memberId, orgId);
        var body = JsonSerializer.Serialize(new { role = "owner" });

        var response = await memberClient.PatchAsync(
            $"/v1/orgs/{orgId.Value}/members/{ownerId.Value}",
            new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateRole_LastOwner_CannotDemoteSelf()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Last Owner Demote");

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var body = JsonSerializer.Serialize(new { role = "member" });

        var response = await client.PatchAsync(
            $"/v1/orgs/{orgId.Value}/members/{userId.Value}",
            new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("LAST_OWNER");
    }

    // ── Remove member ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveMember_HappyPath_Returns204()
    {
        var (orgId, ownerId) = await _factory.SeedOrgAndOwnerAsync("Remove Member Happy");
        UserId memberToRemoveId = default!;

        await _factory.SeedAsync(async db =>
        {
            var u = User.Create("workos_rm", "remove@test.invalid");
            db.Users.Add(u);
            db.OrgMemberships.Add(OrgMembership.Create(orgId, u.Id, OrgMemberRole.Member));
            await db.SaveChangesAsync();
            memberToRemoveId = u.Id;
        });

        var client = _factory.CreateAuthenticatedClient(ownerId, orgId);
        var response = await client.DeleteAsync($"/v1/orgs/{orgId.Value}/members/{memberToRemoveId.Value}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RemoveMember_LastOwner_Returns409()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Remove Last Owner");

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.DeleteAsync($"/v1/orgs/{orgId.Value}/members/{userId.Value}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("LAST_OWNER");
    }
}
