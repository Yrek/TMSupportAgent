using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Entities;

public class OrgIdpConfig
{
    private static readonly HashSet<string> AllowedProviderTypes =
        ["okta", "google_workspace", "entra_id", "oidc", "saml"];
    private static readonly System.Text.RegularExpressions.Regex DomainHintRegex =
        new(@"^(?=.{1,253}$)(?!-)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

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

        var hints = domainHints
            .Select(h => h.Trim().TrimEnd('.').ToLowerInvariant())
            .ToList();
        if (hints.Count == 0)
            throw new ArgumentException("At least one domain hint is required.", nameof(domainHints));
        if (hints.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Domain hints must not be empty.", nameof(domainHints));
        if (hints.Distinct(StringComparer.Ordinal).Count() != hints.Count)
            throw new ArgumentException("Domain hints must be unique.", nameof(domainHints));
        if (hints.Any(h => !DomainHintRegex.IsMatch(h)))
            throw new ArgumentException("One or more domain hints are invalid.", nameof(domainHints));

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
