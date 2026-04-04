using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Configurations;

internal sealed class ArchitectureCorrectionConfiguration : IEntityTypeConfiguration<ArchitectureCorrection>
{
    public void Configure(EntityTypeBuilder<ArchitectureCorrection> builder)
    {
        builder.ToTable("architecture_corrections");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.ElementId).HasColumnName("element_id");
        builder.Property(c => c.ArchitectureId).HasColumnName("architecture_id").IsRequired();

        builder.Property(c => c.OrgId)
            .HasColumnName("org_id")
            .HasConversion(id => id.Value, v => OrgId.From(v))
            .IsRequired();

        builder.Property(c => c.CorrectedBy)
            .HasColumnName("corrected_by")
            .HasConversion(id => id.Value, v => UserId.From(v))
            .IsRequired();

        builder.Property(c => c.CorrectionType)
            .HasColumnName("correction_type")
            .HasConversion(
                t => t.ToString().ToLowerInvariant(),
                v => Enum.Parse<CorrectionType>(v, true))
            .IsRequired();

        builder.Property(c => c.FieldName).HasColumnName("field_name").HasMaxLength(100);
        builder.Property(c => c.OriginalValue).HasColumnName("original_value");
        builder.Property(c => c.CorrectedValue).HasColumnName("corrected_value");
        builder.Property(c => c.Note).HasColumnName("note");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();

        // Corrections are immutable — no UpdatedAt column
        builder.HasIndex(c => c.ArchitectureId);
    }
}
