using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Configurations;

internal sealed class MitigationConfiguration : IEntityTypeConfiguration<Mitigation>
{
    public void Configure(EntityTypeBuilder<Mitigation> builder)
    {
        builder.ToTable("mitigations");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.ThreatId).HasColumnName("threat_id").IsRequired();

        builder.Property(m => m.OrgId)
            .HasColumnName("org_id")
            .HasConversion(id => id.Value, v => OrgId.From(v))
            .IsRequired();

        builder.Property(m => m.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
        builder.Property(m => m.Description).HasColumnName("description").IsRequired();
        builder.Property(m => m.Priority).HasColumnName("priority").HasMaxLength(20).IsRequired();
        builder.Property(m => m.Category).HasColumnName("category").HasMaxLength(100);
        builder.Property(m => m.AcceptanceCriteriaJson).HasColumnName("acceptance_criteria").HasColumnType("text");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(m => m.ThreatId);
    }
}
