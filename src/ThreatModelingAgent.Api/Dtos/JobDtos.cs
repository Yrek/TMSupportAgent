using System.Text.Json;
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

public record JobUsageDto(
    long ElapsedMs,
    long TotalInputTokens,
    long TotalOutputTokens,
    decimal? EstimatedCostUsd);

public record JobDetailDto(
    Guid Id,
    string? Title,
    string Status,
    string? ArtifactType,
    string? ErrorCode,
    bool IsManual,
    string? ApplicationDescription,
    string? ArchitectureDescription,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    JobUsageDto? UsageSummary)
{
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNameCaseInsensitive = true };

    public static JobDetailDto From(Job job)
    {
        JobUsageDto? usage = null;
        if (job.LlmTokenUsageJson is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(job.LlmTokenUsageJson);
                var r = doc.RootElement;
                if (r.TryGetProperty("elapsedMs", out var elapsedEl) &&
                    r.TryGetProperty("totalInputTokens", out var inEl) &&
                    r.TryGetProperty("totalOutputTokens", out var outEl))
                {
                    decimal? cost = r.TryGetProperty("estimatedCostUsd", out var costEl) && costEl.ValueKind != JsonValueKind.Null
                        ? costEl.GetDecimal() : null;
                    usage = new JobUsageDto(elapsedEl.GetInt64(), inEl.GetInt64(), outEl.GetInt64(), cost);
                }
            }
            catch { /* malformed JSON — omit usage */ }
        }

        return new(job.Id.Value, job.Title, job.Status.ToString(), job.ArtifactType,
                   job.ErrorCode, job.ArtifactType is null, job.ApplicationDescription,
                   job.ArchitectureDescription, job.CreatedAt, job.CompletedAt, usage);
    }
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

public class SubmitJobRequest
{
    public IFormFile Artifact { get; set; } = null!;
    public string? Title { get; set; }
    public string? ApplicationDescription { get; set; }
    public string? ArchitectureDescription { get; set; }
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
