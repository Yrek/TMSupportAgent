using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Configurations;

internal sealed class OrgMembershipConfiguration : IEntityTypeConfiguration<OrgMembership>
{
    public void Configure(EntityTypeBuilder<OrgMembership> builder)
    {
        builder.ToTable("org_memberships");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");

        builder.Property(m => m.OrgId)
            .HasColumnName("org_id")
            .HasConversion(id => id.Value, v => OrgId.From(v))
            .IsRequired();

        builder.Property(m => m.UserId)
            .HasColumnName("user_id")
            .HasConversion(id => id.Value, v => UserId.From(v))
            .IsRequired();

        builder.Property(m => m.Role)
            .HasColumnName("role")
            .HasConversion(r => r.ToString().ToLower(), v => Enum.Parse<OrgMemberRole>(v, true))
            .IsRequired();

        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(m => new { m.OrgId, m.UserId }).IsUnique();
        builder.HasIndex(m => m.UserId);
        builder.HasIndex(m => m.OrgId);
    }
}
