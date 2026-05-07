using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Configurations;

internal sealed class ThreatConfiguration : IEntityTypeConfiguration<Threat>
{
    public void Configure(EntityTypeBuilder<Threat> builder)
    {
        builder.ToTable("threats");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.JobId)
            .HasColumnName("job_id")
            .HasConversion(id => id.Value, v => JobId.From(v))
            .IsRequired();

        builder.Property(t => t.OrgId)
            .HasColumnName("org_id")
            .HasConversion(id => id.Value, v => OrgId.From(v))
            .IsRequired();

        builder.Property(t => t.Identifier).HasColumnName("identifier").HasMaxLength(20).IsRequired();
        builder.Property(t => t.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
        builder.Property(t => t.MethodCategory).HasColumnName("method_category").HasMaxLength(100).IsRequired();

        builder.Property(t => t.AffectedElementIds)
            .HasColumnName("affected_element_ids")
            .HasColumnType("uuid[]")
            .IsRequired();

        builder.Property(t => t.Description).HasColumnName("description").IsRequired();
        builder.Property(t => t.AttackScenario).HasColumnName("attack_scenario").IsRequired();
        builder.Property(t => t.Preconditions).HasColumnName("preconditions");

        builder.Property(t => t.ImpactedAssets)
            .HasColumnName("impacted_assets")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(t => t.SecurityImpact).HasColumnName("security_impact");
        builder.Property(t => t.PrivacyImpact).HasColumnName("privacy_impact");
        builder.Property(t => t.ExistingControls).HasColumnName("existing_controls");
        builder.Property(t => t.ControlGaps).HasColumnName("control_gaps");

        builder.Property(t => t.Confidence)
            .HasColumnName("confidence")
            .HasConversion(
                c => c.ToString().ToLowerInvariant(),
                v => Enum.Parse<ConfidenceLevel>(v, true))
            .IsRequired();

        builder.Property(t => t.EvidenceBasis)
            .HasColumnName("evidence_basis")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(t => t.EvidenceStrength)
            .HasColumnName("evidence_strength")
            .HasConversion(
                e => e.ToString().ToLowerInvariant(),
                v => Enum.Parse<EvidenceStrength>(v, true))
            .IsRequired();

        builder.Property(t => t.Assumptions).HasColumnName("assumptions");

        builder.Property(t => t.FindingType)
            .HasColumnName("finding_type")
            .HasConversion(
                f => f.ToString().ToLowerInvariant(),
                v => Enum.Parse<FindingType>(v, true))
            .IsRequired();

        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasConversion(
                s => s.ToString().ToLowerInvariant(),
                v => Enum.Parse<ThreatStatus>(v, true))
            .IsRequired();

        builder.Property(t => t.Source).HasColumnName("source").HasMaxLength(20).IsRequired();

        builder.Property(t => t.RiskRatingJson)
            .HasColumnName("risk_rating")
            .HasColumnType("jsonb");

        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(t => new { t.JobId, t.Identifier }).IsUnique();
        builder.HasIndex(t => t.JobId);
        builder.HasIndex(t => new { t.JobId, t.FindingType });

        builder.HasMany(t => t.Notes)
            .WithOne()
            .HasForeignKey("ThreatId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Mitigations)
            .WithOne()
            .HasForeignKey("ThreatId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.FrameworkMappings)
            .WithOne()
            .HasForeignKey("ThreatId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
