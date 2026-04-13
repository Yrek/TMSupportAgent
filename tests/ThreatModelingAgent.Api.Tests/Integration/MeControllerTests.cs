using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Api.Tests.Integration;

public sealed class MeControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public MeControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetMe_HappyPath_ReturnsOnlyPlatformIds()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("GetMe Happy");

        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.GetAsync("/v1/me");

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Platform identifiers MUST be present
        doc.RootElement.TryGetProperty("userId", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("workosUserId", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("createdAt", out _).Should().BeTrue();

        // PII MUST NOT be present (CLAUDE.md §10.4)
        doc.RootElement.TryGetProperty("email", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("displayName", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetMe_UnknownUser_Returns404()
    {
        // Authenticated token with a userId that doesn't exist in the DB
        var (orgId, _) = await _factory.SeedOrgAndOwnerAsync("GetMe 404 Org");
        var nonExistentUserId = UserId.New();

        var client = _factory.CreateAuthenticatedClient(nonExistentUserId, orgId);

        var response = await client.GetAsync("/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteMe_HappyPath_CallsWorkOsAndReturns204()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("DeleteMe Happy");

        _factory.WorkOsClient
            .DeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.DeleteAsync("/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _factory.WorkOsClient.Received(1)
            .DeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteMe_WorkOsFails_Returns502AndLeavesDbIntact()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("DeleteMe WorkOsFail");

        _factory.WorkOsClient
            .DeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new Domain.Interfaces.WorkOsException("WorkOS error", statusCode: 500));

        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.DeleteAsync("/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ERASURE_FAILED");

        // User record must still exist in DB (fail-secure — WorkOS failed before DB update)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ThreatModelingAgent.Infrastructure.Persistence.AppDbContext>();
        var user = await db.Users.FindAsync(userId.Value);
        user.Should().NotBeNull();
        user!.IsDeleted.Should().BeFalse();
    }
}
