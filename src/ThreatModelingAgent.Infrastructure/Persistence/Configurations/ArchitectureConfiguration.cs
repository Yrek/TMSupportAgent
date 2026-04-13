using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Configurations;

internal sealed class ArchitectureConfiguration : IEntityTypeConfiguration<Architecture>
{
    public void Configure(EntityTypeBuilder<Architecture> builder)
    {
        builder.ToTable("architectures");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.JobId)
            .HasColumnName("job_id")
            .HasConversion(id => id.Value, v => JobId.From(v))
            .IsRequired();

        builder.Property(a => a.OrgId)
            .HasColumnName("org_id")
            .HasConversion(id => id.Value, v => OrgId.From(v))
            .IsRequired();

        builder.Property(a => a.Version).HasColumnName("version").IsRequired();

        builder.Property(a => a.Classification)
            .HasColumnName("classification")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(a => a.SystemPurpose).HasColumnName("system_purpose");

        builder.Property(a => a.AssumptionsJson)
            .HasColumnName("assumptions")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(a => a.GapsJson)
            .HasColumnName("gaps")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(a => a.ClarificationQuestionsJson)
            .HasColumnName("clarification_questions")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(a => a.ConfirmedAt).HasColumnName("confirmed_at");

        builder.Property(a => a.ConfirmedBy)
            .HasColumnName("confirmed_by")
            .HasConversion(new ValueConverter<UserId?, Guid?>(
                id => id.HasValue ? (Guid?)id.Value.Value : null,
                v => v.HasValue ? (UserId?)UserId.From(v.Value) : null));

        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(a => a.JobId).IsUnique();
        builder.HasIndex(a => a.OrgId);

        builder.HasMany(a => a.Elements)
            .WithOne()
            .HasForeignKey("ArchitectureId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.Corrections)
            .WithOne()
            .HasForeignKey("ArchitectureId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
