using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Entities;

public class Job
{
    // Canonical state machine from spec §6 / data-model §6 — forward-only transitions
    private static readonly Dictionary<JobStatus, IReadOnlySet<JobStatus>> AllowedTransitions = new()
    {
        [JobStatus.Pending]        = new HashSet<JobStatus> { JobStatus.Parsing, JobStatus.AwaitingReview, JobStatus.Failed },
        [JobStatus.Parsing]        = new HashSet<JobStatus> { JobStatus.Normalizing, JobStatus.Failed },
        [JobStatus.Normalizing]    = new HashSet<JobStatus> { JobStatus.AwaitingReview, JobStatus.Failed },
        [JobStatus.AwaitingReview] = new HashSet<JobStatus> { JobStatus.Classifying, JobStatus.Failed },
        [JobStatus.Classifying]    = new HashSet<JobStatus> { JobStatus.Analyzing,   JobStatus.Failed },
        [JobStatus.Analyzing]      = new HashSet<JobStatus> { JobStatus.Synthesizing, JobStatus.Failed },
        [JobStatus.Synthesizing]   = new HashSet<JobStatus> { JobStatus.Complete, JobStatus.Partial, JobStatus.Failed },
        [JobStatus.Complete]       = new HashSet<JobStatus> { JobStatus.AwaitingReview },  // re-analysis
        [JobStatus.Failed]         = new HashSet<JobStatus>(),
        [JobStatus.Partial]        = new HashSet<JobStatus> { JobStatus.AwaitingReview },  // re-analysis
    };

    public JobId Id { get; private set; }
    public OrgId OrgId { get; private set; }
    public UserId CreatedBy { get; private set; }
    public string? Title { get; private set; }
    public JobStatus Status { get; private set; }
    public string? ErrorCode { get; private set; }      // minimal code only — no stack traces (CLAUDE.md §7.6)
    public string? ArtifactBlobPath { get; private set; }
    public string? ArtifactType { get; private set; }
    public string? LlmTokenUsageJson { get; private set; }  // {input_tokens, output_tokens} — no content
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private Job() { }

    public static Job Create(OrgId orgId, UserId createdBy, string? title)
    {
        if (title?.Length > 255)
            throw new ArgumentException("Title exceeds maximum length.", nameof(title));

        var now = DateTimeOffset.UtcNow;
        return new Job
        {
            Id = JobId.New(),
            OrgId = orgId,
            CreatedBy = createdBy,
            Title = title,
            Status = JobStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Transition(JobStatus newStatus, string? errorCode = null)
    {
        if (!AllowedTransitions[Status].Contains(newStatus))
            throw new InvalidOperationException(
                $"Cannot transition job from {Status} to {newStatus}.");

        Status = newStatus;
        ErrorCode = errorCode;
        UpdatedAt = DateTimeOffset.UtcNow;

        if (newStatus is JobStatus.Complete or JobStatus.Failed or JobStatus.Partial)
            CompletedAt = DateTimeOffset.UtcNow;
        else if (newStatus is JobStatus.AwaitingReview)
            CompletedAt = null;  // reset when re-queued for review
    }

    public void SetArtifact(string blobPath, string artifactType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactType);
        ArtifactBlobPath = blobPath;
        ArtifactType = artifactType;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordTokenUsage(string tokenUsageJson)
    {
        LlmTokenUsageJson = tokenUsageJson;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsTerminal => Status is JobStatus.Complete or JobStatus.Failed or JobStatus.Partial;
    public bool IsInProgress => !IsTerminal;
}
