using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Configurations;

internal sealed class OrgIdpConfigConfiguration : IEntityTypeConfiguration<OrgIdpConfig>
{
    public void Configure(EntityTypeBuilder<OrgIdpConfig> builder)
    {
        builder.ToTable("org_idp_configs");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.OrgId)
            .HasColumnName("org_id")
            .HasConversion(id => id.Value, v => OrgId.From(v))
            .IsRequired();

        builder.HasIndex(c => c.OrgId).IsUnique();

        builder.Property(c => c.WorkOsConnectionId)
            .HasColumnName("workos_connection_id")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(c => c.ProviderType)
            .HasColumnName("provider_type")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.DomainHints)
            .HasColumnName("domain_hints")
            .HasColumnType("text[]")
            .HasConversion(
                hints => hints.ToArray(),
                arr => (IReadOnlyList<string>)arr.ToList().AsReadOnly());

        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
    }
}
