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
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public static JobDetailDto From(Job job)
        => new(job.Id.Value, job.Title, job.Status.ToString(), job.ArtifactType,
               job.ErrorCode, job.CreatedAt, job.CompletedAt);
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

public class SubmitJobRequest
{
    public IFormFile Artifact { get; set; } = null!;
    public string? Title { get; set; }
}
