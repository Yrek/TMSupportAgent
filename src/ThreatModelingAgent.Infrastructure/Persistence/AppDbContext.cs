using Microsoft.EntityFrameworkCore;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext. Row-Level Security is enforced at the PostgreSQL layer via
/// the 'app.current_org_id' session variable set by RlsSessionInterceptor before
/// every query. Never bypass this by querying without setting the session variable.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<OrgMembership> OrgMemberships => Set<OrgMembership>();
    public DbSet<OrgIdpConfig> OrgIdpConfigs => Set<OrgIdpConfig>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
