using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ThreatModelingAgent.Infrastructure.Persistence;

/// <summary>
/// Used by EF Core tooling (dotnet ef migrations) at design time only.
/// This factory is NEVER invoked at runtime.
/// Connection string here is intentionally a local placeholder — real credentials
/// come from environment configuration / Key Vault at runtime (CLAUDE.md §10.1).
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=threatmodeling_dev;Username=postgres;Password=design_time_only",
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options);
    }
}