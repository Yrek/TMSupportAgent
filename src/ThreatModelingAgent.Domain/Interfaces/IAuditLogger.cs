using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Interfaces;

public interface IAuditLogger
{
    Task LogAsync(
        string eventType,
        OrgId? orgId = null,
        UserId? userId = null,
        string? resourceType = null,
        Guid? resourceId = null,
        object? details = null,
        string? ipAddress = null,
        CancellationToken ct = default);
}
