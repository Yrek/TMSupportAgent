using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using NSubstitute;
using Testcontainers.PostgreSql;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;
using ThreatModelingAgent.Infrastructure.Persistence;

namespace ThreatModelingAgent.Api.Tests.Integration;

/// <summary>
/// Shared WebApplicationFactory for all Group B integration tests.
/// Spins up a real PostgreSQL container, applies migrations, replaces
/// external service dependencies with NSubstitute mocks, and wires up
/// a test authentication scheme so tests can inject arbitrary claims.
/// </summary>
public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly TimeSpan ContainerStartupTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MigrationTimeout = TimeSpan.FromSeconds(60);
    private readonly string _dbName = $"tma_it_{Guid.NewGuid():N}";

    private readonly PostgreSqlContainer _postgres;

    public ApiWebApplicationFactory()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase(_dbName)
            .Build();
    }

    // Exposed mocks — tests configure return values as needed.
    public IBlobStorage BlobStorage { get; } = Substitute.For<IBlobStorage>();
    public IJobQueue JobQueue { get; } = Substitute.For<IJobQueue>();
    public IWorkOsClient WorkOsClient { get; } = Substitute.For<IWorkOsClient>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Inject test config so startup validation passes.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                ["WorkOS:ClientId"]  = "test-client-id",
                ["WorkOS:JwksUri"]   = "https://test.workos.invalid/.well-known/jwks.json",
                ["WorkOS:Issuer"]    = "https://test.workos.invalid",
                // Suppress Application Insights in tests
                ["ApplicationInsights:ConnectionString"] = string.Empty,
            });
        });

        builder.ConfigureServices(services =>
        {
            // Force AppDbContext to the Testcontainers connection string.
            // This prevents any fallback to local appsettings.Development.json (threatmodeling_dev).
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>((sp, options) =>
            {
                options
                    .UseNpgsql(_postgres.GetConnectionString())
                    .AddInterceptors(sp.GetRequiredService<RlsSessionInterceptor>());
            });

            // Replace external singletons with mocks.
            RemoveSingleton<IBlobStorage>(services);
            services.AddSingleton(BlobStorage);

            RemoveSingleton<IJobQueue>(services);
            services.AddSingleton(JobQueue);

            // IWorkOsClient is scoped in production; replace at the scoped level.
            var workOs = services.SingleOrDefault(d => d.ServiceType == typeof(IWorkOsClient));
            if (workOs != null) services.Remove(workOs);
            services.AddScoped<IWorkOsClient>(_ => WorkOsClient);

            // Replace JWT with our test scheme — registering last makes it the default.
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });
        });
    }

    // ── IAsyncLifetime ────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        try
        {
            using var startCts = new CancellationTokenSource(ContainerStartupTimeout);
            await _postgres.StartAsync(startCts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"Testcontainers PostgreSQL startup exceeded {ContainerStartupTimeout.TotalSeconds}s. " +
                "Check Docker Desktop status/permissions.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to start Testcontainers PostgreSQL. " +
                "Check Docker Desktop status and access to the Docker engine.", ex);
        }

        // Apply all EF migrations to the fresh database.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var csb = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        if (string.Equals(csb.Database, "threatmodeling_dev", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Integration tests are pointing at threatmodeling_dev. " +
                "Aborting to avoid contaminating local developer data.");
        }

        if (!string.Equals(csb.Database, _dbName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Integration tests expected database '{_dbName}' but got '{csb.Database}'.");
        }

        try
        {
            using var migrateCts = new CancellationTokenSource(MigrationTimeout);
            await db.Database.MigrateAsync(migrateCts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"EF migrations exceeded {MigrationTimeout.TotalSeconds}s in test setup. " +
                "Check DB container health and migration lock contention.");
        }
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    // ── Seeding helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a scope with the given org ID injected into the tenant context,
    /// then passes the AppDbContext to the seed action.
    /// Testcontainers connects as the postgres superuser — superusers bypass RLS,
    /// so seed inserts work without setting app.current_org_id.
    /// </summary>
    public async Task SeedAsync(Func<AppDbContext, Task> seed)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Disable the RLS interceptor for seeding by using raw SQL before each command.
        // Since the postgres user is a superuser, RLS is bypassed automatically.
        await seed(db);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds a minimal org + user + owner membership and returns the created IDs.
    /// </summary>
    public async Task<(OrgId OrgId, UserId UserId)> SeedOrgAndOwnerAsync(
        string? orgName = null,
        string? userEmail = null)
    {
        var org = Organization.Create(orgName ?? "Test Org", "test-org-" + Guid.NewGuid().ToString("N")[..8]);
        var user = User.Create("workos_" + Guid.NewGuid().ToString("N"), userEmail ?? "owner@test.invalid");
        var membership = OrgMembership.Create(org.Id, user.Id, OrgMemberRole.Owner);

        await SeedAsync(db =>
        {
            db.Organizations.Add(org);
            db.Users.Add(user);
            db.OrgMemberships.Add(membership);
            return Task.CompletedTask;
        });

        return (org.Id, user.Id);
    }

    // ── Client factory ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an HttpClient authenticated as the given user/org.
    /// Both sub (UserId) and org_id claims are set so TenantContextMiddleware
    /// and GetUserId() both resolve correctly.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(UserId userId, OrgId orgId,
        IEnumerable<(string type, string value)>? extraClaims = null)
    {
        var claims = new Dictionary<string, string>
        {
            [ClaimTypes.NameIdentifier] = userId.Value.ToString(),
            ["org_id"] = orgId.Value.ToString(),
        };

        if (extraClaims != null)
            foreach (var (type, value) in extraClaims)
                claims[type] = value;

        var client = CreateClient();
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.ClaimsHeader,
            JsonSerializer.Serialize(claims));
        return client;
    }

    /// <summary>
    /// Creates an HttpClient authenticated as a platform admin (role = "platform:admin").
    /// The token has no org_id — TenantContextMiddleware routes admin tokens to /v1/admin/* only.
    /// </summary>
    public HttpClient CreateAdminClient()
    {
        var claims = new Dictionary<string, string>
        {
            [ClaimTypes.NameIdentifier] = Guid.NewGuid().ToString(),
            [ClaimTypes.Role] = "platform:admin",
        };

        var client = CreateClient();
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.ClaimsHeader,
            JsonSerializer.Serialize(claims));
        return client;
    }

    /// <summary>Creates an unauthenticated HttpClient (no test claims header).</summary>
    public HttpClient CreateUnauthenticatedClient() => CreateClient();

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void RemoveSingleton<T>(IServiceCollection services)
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null) services.Remove(descriptor);
    }
}
