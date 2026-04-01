using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, v => UserId.From(v));

        builder.Property(u => u.WorkOsUserId)
            .HasColumnName("workos_user_id")
            .HasMaxLength(255)
            .IsRequired();

        builder.HasIndex(u => u.WorkOsUserId).IsUnique();

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(255);

        builder.Property(u => u.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(255);

        builder.Property(u => u.CreatedAt).HasColumnName("created_at");
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at");
        builder.Property(u => u.DeletedAt).HasColumnName("deleted_at");

        builder.HasQueryFilter(u => u.DeletedAt == null);
    }
}
