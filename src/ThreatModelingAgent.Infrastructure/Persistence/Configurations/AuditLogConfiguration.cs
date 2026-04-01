using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_log");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.OrgId)
            .HasColumnName("org_id")
            .HasConversion(
                id => id.HasValue ? (Guid?)id.Value.Value : null,
                v => v.HasValue ? OrgId.From(v.Value) : (OrgId?)null);

        builder.Property(a => a.UserId)
            .HasColumnName("user_id")
            .HasConversion(
                id => id.HasValue ? (Guid?)id.Value.Value : null,
                v => v.HasValue ? UserId.From(v.Value) : (UserId?)null);

        builder.Property(a => a.CorrelationId).HasColumnName("correlation_id");

        builder.Property(a => a.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.ResourceType)
            .HasColumnName("resource_type")
            .HasMaxLength(100);

        builder.Property(a => a.ResourceId).HasColumnName("resource_id");

        builder.Property(a => a.Details)
            .HasColumnName("details")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(a => a.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(45);

        builder.Property(a => a.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(a => new { a.OrgId, a.CreatedAt });
        builder.HasIndex(a => new { a.UserId, a.CreatedAt });
        builder.HasIndex(a => new { a.EventType, a.CreatedAt });
    }
}
