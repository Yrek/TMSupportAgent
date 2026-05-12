using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Entities;

public class Organization
{
    public OrgId Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? WorkOsOrgId { get; private set; }
    // Nullable — only set when this org is linked to an Entra ID tenant.
    // Used for per-org Entra lookups (SaaS path). Null in WorkOS-managed orgs.
    public string? EntraTenantId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsSuspended { get; private set; }
    public DateTimeOffset? SuspendedAt { get; private set; }

    private Organization() { } // EF Core

    public static Organization Create(string name, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        if (name.Length > 255) throw new ArgumentException("Name exceeds maximum length.", nameof(name));
        if (slug.Length > 63) throw new ArgumentException("Slug exceeds maximum length.", nameof(slug));
        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, @"^[a-z0-9][a-z0-9\-]*[a-z0-9]$"))
            throw new ArgumentException("Slug must be lowercase alphanumeric with hyphens.", nameof(slug));

        var now = DateTimeOffset.UtcNow;
        return new Organization
        {
            Id = OrgId.New(),
            Name = name,
            Slug = slug,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void UpdateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 255) throw new ArgumentException("Name exceeds maximum length.", nameof(name));
        Name = name;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetEntraTenantId(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        EntraTenantId = tenantId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetWorkOsOrgId(string workOsOrgId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workOsOrgId);
        WorkOsOrgId = workOsOrgId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SoftDelete()
    {
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsDeleted => DeletedAt.HasValue;

    public void Suspend()
    {
        IsSuspended = true;
        SuspendedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Unsuspend()
    {
        IsSuspended = false;
        SuspendedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
