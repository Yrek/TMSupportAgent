using System.Text.Json;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;
using ThreatModelingAgent.Worker.Pipeline.Contracts;
using ThreatModelingAgent.Worker.Pipeline.Stages;

namespace ThreatModelingAgent.Worker.Pipeline;

/// <summary>
/// Orchestrates the two-phase analysis pipeline for a single job.
///
/// Phase 1 (Parse):  DETECT → PARSE → NORMALIZE → AWAITING_REVIEW
/// Phase 2 (Analyze): CLASSIFY → ANALYZE (all methods, parallel) → SYNTHESIZE → COMPLETE
///
/// Security invariants:
/// - org_id from the message is validated against the DB job record before processing
/// - LLM output is never used directly — each stage validates its output schema
/// - No tenant architecture content is logged (CLAUDE.md §16.6)
/// - No secrets appear in prompts (CLAUDE.md §16.3)
/// - All failures transition the job to Failed and fail closed (CLAUDE.md §4.3)
/// </summary>
public sealed class JobOrchestrator(
    IJobRepository jobs,
    IAuditLogger audit,
    IBlobStorage blobStorage,
    DetectStage detectStage,
    ParseStage parseStage,
    NormalizeStage normalizeStage,
    ClassifyStage classifyStage,
    AnalyzeStage analyzeStage,
    SynthesizeStage synthesizeStage,
    ILogger<JobOrchestrator> logger)
{
    private static readonly JsonSerializerOptions TokenJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task RunAsync(AnalysisJobMessage message, CancellationToken ct)
    {
        var jobId = JobId.From(message.JobId);
        var orgId = OrgId.From(message.OrgId);

        // Validate org_id from message matches the DB record — prevent cross-tenant processing
        var job = await jobs.GetByIdAsync(jobId, orgId, ct);
        if (job is null)
        {
            logger.LogWarning(
                "Job not found or org mismatch — discarding. JobId={JobId} OrgId={OrgId}",
                jobId, orgId);
            return;
        }

        logger.LogInformation(
            "Pipeline starting. JobId={JobId} Phase={Phase} Status={Status}",
            jobId, message.Phase, job.Status);

        try
        {
            if (message.Phase == PipelinePhase.Parse)
                await RunParsePhaseAsync(message, job, orgId, ct);
            else
                await RunAnalyzePhaseAsync(message, job, orgId, ct);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Pipeline cancelled. JobId={JobId}", jobId);
            throw;
        }
        catch (PipelineStageException ex)
        {
            logger.LogError(
                "Pipeline stage failed. JobId={JobId} ErrorCode={ErrorCode}",
                jobId, ex.ErrorCode); // no Detail — may contain model output fragments

            job.Transition(JobStatus.Failed, errorCode: ex.ErrorCode);
            await jobs.SaveChangesAsync(ct);

            await audit.LogAsync("job.failed",
                orgId: orgId,
                resourceType: "job",
                resourceId: job.Id.Value,
                details: new { errorCode = ex.ErrorCode },
                ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Pipeline unexpected failure. JobId={JobId} Stage={Status}",
                jobId, job.Status);

            job.Transition(JobStatus.Failed, errorCode: "PIPELINE_ERROR");
            await jobs.SaveChangesAsync(ct);

            await audit.LogAsync("job.failed",
                orgId: orgId,
                resourceType: "job",
                resourceId: job.Id.Value,
                details: new { errorCode = "PIPELINE_ERROR" },
                ct: ct);
        }
    }

    // ── Phase 1: DETECT → PARSE → NORMALIZE → AWAITING_REVIEW ────────────────

    private async Task RunParsePhaseAsync(
        AnalysisJobMessage message,
        Domain.Entities.Job job,
        OrgId orgId,
        CancellationToken ct)
    {
        // DETECT
        await TransitionAsync(job, JobStatus.Parsing, ct);
        var detectOutput = await detectStage.ExecuteAsync(message, ct);

        // PARSE
        var parseInput = new ParseInput(
            ArtifactType: detectOutput.ArtifactType,
            BlobPath: message.ArtifactBlobPath,
            LowConfidenceArtifactType: detectOutput.LowConfidence);

        var parseOutput = await parseStage.ExecuteAsync(parseInput, ct);

        // NORMALIZE
        await TransitionAsync(job, JobStatus.Normalizing, ct);
        var normalizeInput = new NormalizeInput(parseOutput, detectOutput.ArtifactType);
        var canonicalModel = await normalizeStage.ExecuteAsync(normalizeInput, ct);

        // Persist canonical model to blob — survives AWAITING_REVIEW pause
        await NormalizeStage.PersistAsync(canonicalModel, orgId.Value, job.Id.Value, blobStorage, ct);

        // Transition to AWAITING_REVIEW — pipeline pauses until user confirms via API
        await TransitionAsync(job, JobStatus.AwaitingReview, ct);

        logger.LogInformation("Pipeline paused for review. JobId={JobId}", job.Id);

        await audit.LogAsync("job.awaiting_review",
            orgId: orgId,
            resourceType: "job",
            resourceId: job.Id.Value,
            ct: ct);
    }

    // ── Phase 2: CLASSIFY → ANALYZE → SYNTHESIZE → COMPLETE ──────────────────

    private async Task RunAnalyzePhaseAsync(
        AnalysisJobMessage message,
        Domain.Entities.Job job,
        OrgId orgId,
        CancellationToken ct)
    {
        // Load the canonical model persisted by Phase 1
        var canonicalModel = await NormalizeStage.LoadAsync(
            orgId.Value, job.Id.Value, blobStorage, ct);

        // CLASSIFY
        await TransitionAsync(job, JobStatus.Classifying, ct);
        var classifyInput = new ClassifyInput(canonicalModel);
        var classification = await classifyStage.ExecuteAsync(classifyInput, ct);

        // ANALYZE — all methods in parallel
        await TransitionAsync(job, JobStatus.Analyzing, ct);
        var allCandidateSets = await analyzeStage.RunAllMethodsAsync(canonicalModel, classification, ct);

        // SYNTHESIZE
        await TransitionAsync(job, JobStatus.Synthesizing, ct);
        var synthesizeInput = new SynthesizeInput(allCandidateSets, canonicalModel, classification);
        var finalOutput = await synthesizeStage.ExecuteAsync(synthesizeInput, ct);

        // Persist final output to blob
        var outputBlobPath = await SynthesizeStage.PersistAsync(
            finalOutput, orgId.Value, job.Id.Value, blobStorage, ct);

        // Store token usage summary in job record for cost tracking
        job.RecordTokenUsage(JsonSerializer.Serialize(
            new { outputBlobPath, modelRoutingSummary = finalOutput.ModelRoutingSummary },
            TokenJsonOptions));

        // Transition to final status
        var finalStatus = finalOutput.AnalysisStatus == "partial"
            ? JobStatus.Partial
            : JobStatus.Complete;

        await TransitionAsync(job, finalStatus, ct);

        await audit.LogAsync("job.completed",
            orgId: orgId,
            resourceType: "job",
            resourceId: job.Id.Value,
            details: new { status = finalStatus.ToString(), outputBlobPath },
            ct: ct);

        logger.LogInformation(
            "Pipeline complete. JobId={JobId} Status={Status} Threats={Threats}",
            job.Id, finalStatus, finalOutput.ConfirmedThreats.Length);
    }

    private async Task TransitionAsync(Domain.Entities.Job job, JobStatus newStatus, CancellationToken ct)
    {
        job.Transition(newStatus);
        await jobs.SaveChangesAsync(ct);
        logger.LogInformation("Job transitioned. JobId={JobId} Status={Status}", job.Id, newStatus);
    }
}
