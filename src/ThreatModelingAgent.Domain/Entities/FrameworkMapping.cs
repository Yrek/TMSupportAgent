using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Entities;

/// <summary>
/// Maps a threat to a security framework control reference.
/// Spec: data-model §4.12.
/// </summary>
public class FrameworkMapping
{
    public Guid Id { get; private set; }
    public Guid ThreatId { get; private set; }
    public OrgId OrgId { get; private set; }
    public string Framework { get; private set; } = string.Empty;
    public string Reference { get; private set; } = string.Empty;
    public string MappingType { get; private set; } = string.Empty;  // direct | approximate
    public DateTimeOffset CreatedAt { get; private set; }

    // Must stay in sync with FrameworkNormalizer.Normalize() canonical output values.
    private static readonly HashSet<string> AllowedFrameworks =
    [
        "stride", "octave", "trike",
        "mitre_attack", "owasp_cumulus", "owasp_cornucopia",
        "owasp_top10", "owasp_api_top10", "owasp_llm_top10", "owasp_agentic_top10",
        "asvs", "cis_controls", "ncsc", "twelve_factor", "cwe"
    ];

    private static readonly HashSet<string> AllowedMappingTypes = ["direct", "approximate"];

    private FrameworkMapping() { }

    public static FrameworkMapping Create(
        Guid threatId,
        OrgId orgId,
        string framework,
        string reference,
        string mappingType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        if (!AllowedFrameworks.Contains(framework))
            throw new ArgumentException($"Unknown framework: {framework}.", nameof(framework));
        if (!AllowedMappingTypes.Contains(mappingType))
            throw new ArgumentException($"MappingType must be direct or approximate.", nameof(mappingType));

        return new FrameworkMapping
        {
            Id = Guid.NewGuid(),
            ThreatId = threatId,
            OrgId = orgId,
            Framework = framework,
            Reference = reference,
            MappingType = mappingType,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
