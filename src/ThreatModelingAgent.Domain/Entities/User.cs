using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Entities;

public class User
{
    public UserId Id { get; private set; }
    public string WorkOsUserId { get; private set; } = string.Empty;
    public string? Email { get; private set; }       // nullable: nulled on GDPR erasure
    public string? DisplayName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private User() { }

    public static User Create(string workOsUserId, string email, string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workOsUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        if (email.Length > 255) throw new ArgumentException("Email exceeds maximum length.", nameof(email));

        var now = DateTimeOffset.UtcNow;
        return new User
        {
            Id = UserId.New(),
            WorkOsUserId = workOsUserId,
            Email = email,
            DisplayName = displayName,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// GDPR right to erasure: nulls PII, retains IDs for audit log integrity.
    /// </summary>
    public void Erase()
    {
        Email = null;
        DisplayName = null;
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsDeleted => DeletedAt.HasValue;
}
