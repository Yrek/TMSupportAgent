using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Interfaces;

/// <summary>
/// Abstracts the Service Bus queue so the API can enqueue analysis jobs
/// without a direct Azure SDK dependency. Implemented by Infrastructure.
/// </summary>
public interface IJobQueue
{
    /// <summary>Enqueues Phase 1 (DETECT → PARSE → NORMALIZE → AWAITING_REVIEW).</summary>
    Task EnqueueParsePhaseAsync(
        JobId jobId,
        OrgId orgId,
        string artifactBlobPath,
        string artifactType,
        string? applicationDescription = null,
        string? architectureDescription = null,
        CancellationToken ct = default);

    /// <summary>Enqueues Phase 2 (CLASSIFY → ANALYZE → SYNTHESIZE → COMPLETE).</summary>
    Task EnqueueAnalyzePhaseAsync(
        JobId jobId,
        OrgId orgId,
        string artifactBlobPath,
        string artifactType,
        string[]? selectedMethods = null,
        CancellationToken ct = default);
}
