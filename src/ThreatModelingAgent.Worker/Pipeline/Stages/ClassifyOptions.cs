namespace ThreatModelingAgent.Worker.Pipeline.Stages;

/// <summary>
/// Tuning options for the CLASSIFY stage. Bound to config section "Classify".
/// </summary>
public sealed class ClassifyOptions
{
    /// <summary>
    /// Maximum number of threat modeling methods the CLASSIFY stage may select.
    /// Set higher for complex architectures (multi-tenant SaaS, identity-heavy, AI-enabled).
    /// Default: 10 — high enough to avoid silent drops in practice.
    /// </summary>
    public int MaxSelectedMethods { get; init; } = 10;
}
