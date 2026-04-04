using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Entities;

/// <summary>
/// A recommended mitigation linked to a threat.
/// Spec: data-model §4.11.
/// </summary>
public class Mitigation
{
    public Guid Id { get; private set; }
    public Guid ThreatId { get; private set; }
    public OrgId OrgId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Priority { get; private set; } = string.Empty;  // critical | high | medium | low
    public string? Category { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private static readonly HashSet<string> AllowedPriorities =
        ["critical", "high", "medium", "low"];

    private Mitigation() { }

    public static Mitigation Create(
        Guid threatId,
        OrgId orgId,
        string title,
        string description,
        string priority,
        string? category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (!AllowedPriorities.Contains(priority))
            throw new ArgumentException($"Priority must be one of: {string.Join(", ", AllowedPriorities)}.", nameof(priority));

        var now = DateTimeOffset.UtcNow;
        return new Mitigation
        {
            Id = Guid.NewGuid(),
            ThreatId = threatId,
            OrgId = orgId,
            Title = title,
            Description = description,
            Priority = priority,
            Category = category,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
