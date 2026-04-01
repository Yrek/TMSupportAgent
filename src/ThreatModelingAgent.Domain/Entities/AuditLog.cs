using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Entities;

/// <summary>
/// Append-only audit record. Application code MUST NOT call UPDATE or DELETE on this table.
/// DB role is granted INSERT only. See data-model spec §4.14.
/// </summary>
public class AuditLog
{
    public Guid Id { get; private set; }
    public OrgId? OrgId { get; private set; }
    public UserId? UserId { get; private set; }
    public Guid CorrelationId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string? ResourceType { get; private set; }
    public Guid? ResourceId { get; private set; }
    public string Details { get; private set; } = "{}";  // JSON; non-PII IDs only (CLAUDE.md §10.4)
    public string? IpAddress { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AuditLog() { }

    public static AuditLog Create(
        Guid correlationId,
        string eventType,
        OrgId? orgId = null,
        UserId? userId = null,
        string? resourceType = null,
        Guid? resourceId = null,
        string? details = null,
        string? ipAddress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        return new AuditLog
        {
            Id = Guid.NewGuid(),
            CorrelationId = correlationId,
            EventType = eventType,
            OrgId = orgId,
            UserId = userId,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details ?? "{}",
            IpAddress = ipAddress,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
