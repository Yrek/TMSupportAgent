using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Entities;

/// <summary>
/// The normalized canonical system model for a job (spec §5, data-model §4.6).
/// One per job; version incremented on re-analysis after user corrections.
/// </summary>
public class Architecture
{
    public Guid Id { get; private set; }
    public JobId JobId { get; private set; }
    public OrgId OrgId { get; private set; }
    public int Version { get; private set; }
    public string[] Classification { get; private set; } = [];
    public string? SystemPurpose { get; private set; }
    public string AssumptionsJson { get; private set; } = "[]";       // jsonb — list of {text, confirmed}
    public string GapsJson { get; private set; } = "[]";              // jsonb — material unknowns
    public string ClarificationQuestionsJson { get; private set; } = "[]"; // jsonb — prioritized questions
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public UserId? ConfirmedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Navigation
    public ICollection<ArchitectureElement> Elements { get; private set; } = [];
    public ICollection<ArchitectureCorrection> Corrections { get; private set; } = [];

    private Architecture() { }

    public static Architecture Create(
        JobId jobId,
        OrgId orgId,
        string? systemPurpose,
        string[] classification,
        string assumptionsJson,
        string gapsJson,
        string clarificationQuestionsJson)
    {
        var now = DateTimeOffset.UtcNow;
        return new Architecture
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            OrgId = orgId,
            Version = 1,
            SystemPurpose = systemPurpose,
            Classification = classification,
            AssumptionsJson = assumptionsJson,
            GapsJson = gapsJson,
            ClarificationQuestionsJson = clarificationQuestionsJson,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Marks the architecture as confirmed by the user. Transitions the job to CLASSIFYING phase.
    /// </summary>
    public void Confirm(UserId confirmedBy)
    {
        if (ConfirmedAt.HasValue)
            throw new InvalidOperationException("Architecture is already confirmed.");

        ConfirmedAt = DateTimeOffset.UtcNow;
        ConfirmedBy = confirmedBy;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Increments version when re-analysis is triggered after user corrections.
    /// </summary>
    public void IncrementVersion()
    {
        Version++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsConfirmed => ConfirmedAt.HasValue;

    /// <summary>
    /// Resets confirmed state so the user can review and re-confirm before the next analysis run.
    /// Also increments version so downstream pipeline stages know this is a new revision.
    /// Called when the user triggers re-analysis on a completed job.
    /// </summary>
    public void ResetForReanalysis()
    {
        ConfirmedAt = null;
        ConfirmedBy = null;
        Version++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Sets the architecture classification after the CLASSIFY pipeline stage completes.
    /// Called in Phase 2 before ANALYZE.
    /// </summary>
    public void UpdateClassification(string[] classification)
    {
        Classification = classification;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
