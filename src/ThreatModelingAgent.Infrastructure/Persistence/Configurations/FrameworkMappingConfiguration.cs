using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Configurations;

internal sealed class FrameworkMappingConfiguration : IEntityTypeConfiguration<FrameworkMapping>
{
    public void Configure(EntityTypeBuilder<FrameworkMapping> builder)
    {
        builder.ToTable("framework_mappings");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id");
        builder.Property(f => f.ThreatId).HasColumnName("threat_id").IsRequired();

        builder.Property(f => f.OrgId)
            .HasColumnName("org_id")
            .HasConversion(id => id.Value, v => OrgId.From(v))
            .IsRequired();

        builder.Property(f => f.Framework).HasColumnName("framework").HasMaxLength(100).IsRequired();
        builder.Property(f => f.Reference).HasColumnName("reference").HasMaxLength(500).IsRequired();
        builder.Property(f => f.MappingType).HasColumnName("mapping_type").HasMaxLength(20).IsRequired();
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(f => f.ThreatId);
    }
}
