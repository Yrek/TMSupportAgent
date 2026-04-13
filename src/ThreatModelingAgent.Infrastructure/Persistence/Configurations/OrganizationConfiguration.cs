using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, v => OrgId.From(v));

        builder.Property(o => o.Name)
            .HasColumnName("name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(o => o.Slug)
            .HasColumnName("slug")
            .HasMaxLength(63)
            .IsRequired();

        builder.Property(o => o.WorkOsOrgId)
            .HasColumnName("workos_org_id")
            .HasMaxLength(255);

        builder.Property(o => o.CreatedAt).HasColumnName("created_at");
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at");
        builder.Property(o => o.DeletedAt).HasColumnName("deleted_at");
        builder.Property(o => o.IsSuspended).HasColumnName("is_suspended").HasDefaultValue(false);
        builder.Property(o => o.SuspendedAt).HasColumnName("suspended_at");

        builder.HasIndex(o => o.Slug)
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(o => o.WorkOsOrgId).IsUnique();

        // Global query filter excludes soft-deleted orgs from all queries
        builder.HasQueryFilter(o => o.DeletedAt == null);
    }
}
