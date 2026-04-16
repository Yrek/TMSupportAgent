using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Configurations;

internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs");

        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, v => JobId.From(v));

        builder.Property(j => j.OrgId)
            .HasColumnName("org_id")
            .HasConversion(id => id.Value, v => OrgId.From(v))
            .IsRequired();

        builder.Property(j => j.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, v => UserId.From(v))
            .IsRequired();

        builder.Property(j => j.Title)
            .HasColumnName("title")
            .HasMaxLength(255);

        builder.Property(j => j.Status)
            .HasColumnName("status")
            .HasConversion(
                s => s.ToString().ToLowerInvariant(),
                v => Enum.Parse<JobStatus>(v, true))
            .IsRequired();

        builder.Property(j => j.ErrorCode)
            .HasColumnName("error_code")
            .HasMaxLength(100);

        builder.Property(j => j.ArtifactBlobPath)
            .HasColumnName("artifact_blob_path")
            .HasMaxLength(2000);

        builder.Property(j => j.ArtifactType)
            .HasColumnName("artifact_type")
            .HasMaxLength(50);

        builder.Property(j => j.ApplicationDescription)
            .HasColumnName("application_description")
            .HasMaxLength(2000);

        builder.Property(j => j.ArchitectureDescription)
            .HasColumnName("architecture_description")
            .HasMaxLength(4000);

        builder.Property(j => j.LlmTokenUsageJson)
            .HasColumnName("llm_token_usage")
            .HasColumnType("jsonb");

        builder.Property(j => j.CreatedAt).HasColumnName("created_at");
        builder.Property(j => j.UpdatedAt).HasColumnName("updated_at");
        builder.Property(j => j.CompletedAt).HasColumnName("completed_at");

        builder.HasIndex(j => new { j.OrgId, j.CreatedAt });
        builder.HasIndex(j => j.CreatedBy);
    }
}
