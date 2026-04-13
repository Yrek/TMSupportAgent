using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;

namespace ThreatModelingAgent.Api.Dtos;

// ── Response DTOs (CLAUDE.md §6.6 — purpose-specific, no domain model exposed) ──

public record JobSummaryDto(
    Guid Id,
    string? Title,
    string Status,
    string? ArtifactType,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public static JobSummaryDto From(Job job)
        => new(job.Id.Value, job.Title, job.Status.ToString(), job.ArtifactType,
               job.CreatedAt, job.CompletedAt);
}

public record JobDetailDto(
    Guid Id,
    string? Title,
    string Status,
    string? ArtifactType,
    string? ErrorCode,
    bool IsManual,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public static JobDetailDto From(Job job)
        => new(job.Id.Value, job.Title, job.Status.ToString(), job.ArtifactType,
               job.ErrorCode, job.ArtifactType is null, job.CreatedAt, job.CompletedAt);
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

public class SubmitJobRequest
{
    public IFormFile Artifact { get; set; } = null!;
    public string? Title { get; set; }
}

/// <summary>
/// Creates a manual job with an empty architecture — no file upload required.
/// The job starts in AwaitingReview status and is ready for elements to be added
/// via POST /elements before confirming to trigger analysis.
/// </summary>
public class CreateManualJobRequest
{
    public string? Title { get; set; }
    public string? SystemPurpose { get; set; }
}
