using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Entities;

public class OrgIdpConfig
{
    private static readonly HashSet<string> AllowedProviderTypes =
        ["okta", "google_workspace", "entra_id", "oidc", "saml"];

    public Guid Id { get; private set; }
    public OrgId OrgId { get; private set; }
    public string WorkOsConnectionId { get; private set; } = string.Empty;
    public string ProviderType { get; private set; } = string.Empty;
    public IReadOnlyList<string> DomainHints { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private OrgIdpConfig() { }

    public static OrgIdpConfig Create(
        OrgId orgId,
        string workOsConnectionId,
        string providerType,
        IEnumerable<string> domainHints)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workOsConnectionId);
        if (!AllowedProviderTypes.Contains(providerType))
            throw new ArgumentException($"Invalid provider type: {providerType}", nameof(providerType));

        var hints = domainHints.ToList();
        if (hints.Count == 0)
            throw new ArgumentException("At least one domain hint is required.", nameof(domainHints));

        var now = DateTimeOffset.UtcNow;
        return new OrgIdpConfig
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            WorkOsConnectionId = workOsConnectionId,
            ProviderType = providerType,
            DomainHints = hints.AsReadOnly(),
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
