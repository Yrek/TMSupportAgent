namespace ThreatModelingAgent.Api.Tests.Integration;

/// <summary>
/// Defines a single shared ApiWebApplicationFactory for all Group B integration tests.
///
/// Without this, each IClassFixture<ApiWebApplicationFactory> creates its own factory,
/// which in turn creates its own PostgreSqlContainer. On GitHub Actions, Testcontainers
/// reuses a single Docker container across all identical PostgreSqlBuilder configurations,
/// so multiple factories end up pointing at the same database. Concurrent MigrateAsync()
/// calls then race and fail with "duplicate key on PK___EFMigrationsHistory".
///
/// Grouping all tests under one [Collection("Integration")] means xUnit creates exactly
/// one ApiWebApplicationFactory for the entire suite: one container, one migration run,
/// no race conditions. Test classes within the collection still receive the factory by
/// constructor injection just as with IClassFixture.
/// </summary>
[CollectionDefinition("Integration")]
public sealed class IntegrationTestCollection : ICollectionFixture<ApiWebApplicationFactory>
{
    // Marker class only — no code needed.
}
