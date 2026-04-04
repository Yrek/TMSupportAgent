using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Configurations;

internal sealed class ThreatNoteConfiguration : IEntityTypeConfiguration<ThreatNote>
{
    public void Configure(EntityTypeBuilder<ThreatNote> builder)
    {
        builder.ToTable("threat_notes");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).HasColumnName("id");
        builder.Property(n => n.ThreatId).HasColumnName("threat_id").IsRequired();

        builder.Property(n => n.OrgId)
            .HasColumnName("org_id")
            .HasConversion(id => id.Value, v => OrgId.From(v))
            .IsRequired();

        builder.Property(n => n.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, v => UserId.From(v))
            .IsRequired();

        builder.Property(n => n.Body).HasColumnName("body").IsRequired();
        builder.Property(n => n.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(n => n.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
