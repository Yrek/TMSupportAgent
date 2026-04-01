using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Entities;

public class OrgMembership
{
    public Guid Id { get; private set; }
    public OrgId OrgId { get; private set; }
    public UserId UserId { get; private set; }
    public OrgMemberRole Role { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private OrgMembership() { }

    public static OrgMembership Create(OrgId orgId, UserId userId, OrgMemberRole role)
    {
        var now = DateTimeOffset.UtcNow;
        return new OrgMembership
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            UserId = userId,
            Role = role,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void UpdateRole(OrgMemberRole role)
    {
        Role = role;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
