using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Configurations;

internal sealed class ArchitectureElementConfiguration : IEntityTypeConfiguration<ArchitectureElement>
{
    public void Configure(EntityTypeBuilder<ArchitectureElement> builder)
    {
        builder.ToTable("architecture_elements");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.ArchitectureId).HasColumnName("architecture_id").IsRequired();

        builder.Property(e => e.OrgId)
            .HasColumnName("org_id")
            .HasConversion(id => id.Value, v => OrgId.From(v))
            .IsRequired();

        builder.Property(e => e.ElementType)
            .HasColumnName("element_type")
            .HasConversion(
                t => t.ToString().ToLowerInvariant(),
                v => Enum.Parse<ElementType>(v, true))
            .IsRequired();

        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(e => e.Description).HasColumnName("description");

        builder.Property(e => e.PropertiesJson)
            .HasColumnName("properties")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.Source).HasColumnName("source").HasMaxLength(20).IsRequired();

        builder.Property(e => e.ExtractionConfidence)
            .HasColumnName("extraction_confidence")
            .HasConversion(
                c => c.HasValue ? c.Value.ToString().ToLowerInvariant() : null,
                v => v != null ? Enum.Parse<ConfidenceLevel>(v, true) : (ConfidenceLevel?)null);

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(e => e.ArchitectureId);
        builder.HasIndex(e => new { e.ArchitectureId, e.ElementType });

        builder.HasMany(e => e.Corrections)
            .WithOne()
            .HasForeignKey("ElementId")
            .OnDelete(DeleteBehavior.SetNull);
    }
}
