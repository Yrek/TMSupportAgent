using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Configurations;

internal sealed class RejectedCandidateConfiguration : IEntityTypeConfiguration<RejectedCandidate>
{
    public void Configure(EntityTypeBuilder<RejectedCandidate> builder)
    {
        builder.ToTable("rejected_candidates");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");

        builder.Property(r => r.JobId)
            .HasColumnName("job_id")
            .HasConversion(id => id.Value, v => JobId.From(v))
            .IsRequired();

        builder.Property(r => r.OrgId)
            .HasColumnName("org_id")
            .HasConversion(id => id.Value, v => OrgId.From(v))
            .IsRequired();

        builder.Property(r => r.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
        builder.Property(r => r.MethodCategory).HasColumnName("method_category").HasMaxLength(100);
        builder.Property(r => r.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(100).IsRequired();
        builder.Property(r => r.RejectionNote).HasColumnName("rejection_note");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(r => r.JobId);
    }
}
