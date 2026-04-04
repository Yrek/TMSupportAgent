using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Entities;

/// <summary>User annotation on a threat. Spec: data-model §4.10.</summary>
public class ThreatNote
{
    public Guid Id { get; private set; }
    public Guid ThreatId { get; private set; }
    public OrgId OrgId { get; private set; }
    public UserId CreatedBy { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ThreatNote() { }

    public static ThreatNote Create(Guid threatId, OrgId orgId, UserId createdBy, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        var now = DateTimeOffset.UtcNow;
        return new ThreatNote
        {
            Id = Guid.NewGuid(),
            ThreatId = threatId,
            OrgId = orgId,
            CreatedBy = createdBy,
            Body = body,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
