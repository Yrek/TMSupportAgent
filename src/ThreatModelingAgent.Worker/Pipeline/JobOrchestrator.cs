using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;
using ThreatModelingAgent.Worker.Llm;
using ThreatModelingAgent.Worker.Pipeline.Contracts;
using ThreatModelingAgent.Worker.Pipeline.Stages;

namespace ThreatModelingAgent.Worker.Pipeline;

/// <summary>
/// Orchestrates the two-phase analysis pipeline for a single job.
///
/// Phase 1 (Parse):   DETECT → PARSE → NORMALIZE → AWAITING_REVIEW
/// Phase 2 (Analyze): CLASSIFY → ANALYZE (all methods, parallel) → SYNTHESIZE → COMPLETE
///
/// Security invariants:
/// - org_id from the message is validated against the DB job record before processing
/// - LLM output is never used directly — each stage validates its output schema
/// - No tenant architecture content is logged (CLAUDE.md §16.6)
/// - No secrets appear in prompts (CLAUDE.md §16.3)
/// - All failures transition the job to Failed and fail closed (CLAUDE.md §4.3)
/// </summary>
internal sealed class JobOrchestrator(
    IJobRepository jobs,
    IAuditLogger audit,
    IBlobStorage blobStorage,
    PipelineDbPersistence dbPersistence,
    DetectStage detectStage,
    ParseStage parseStage,
    NormalizeStage normalizeStage,
    ClassifyStage classifyStage,
    AnalyzeStage analyzeStage,
    SynthesizeStage synthesizeStage,
    IOptions<AnalyzeThrottlingOptions> throttlingOptions,
    TokenUsageTracker tokenUsage,
    IOptions<ModelPricingOptions> pricingOpts,
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

        var sw = Stopwatch.StartNew();

        try
        {
            if (message.Phase == PipelinePhase.Parse)
                await RunParsePhaseAsync(message, job, orgId, sw, ct);
            else
                await RunAnalyzePhaseAsync(message, job, orgId, sw, ct);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Pipeline cancelled. JobId={JobId}", jobId);
            // Persist Failed to DB using CancellationToken.None so the save completes even during
            // graceful shutdown. Without this, the job is left in an intermediate state and the
            // redelivered message will fail the state-machine transition on the next run.
            await TryFailJobAsync(job, "PIPELINE_CANCELLED", orgId, CancellationToken.None);
            throw;
        }
        catch (PipelineStageException ex)
        {
            logger.LogError(
                "Pipeline stage failed. JobId={JobId} ErrorCode={ErrorCode}",
                jobId, ex.ErrorCode); // no Detail — may contain model output fragments

            await TryFailJobAsync(job, ex.ErrorCode, orgId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Pipeline unexpected failure. JobId={JobId} Stage={Status}",
                jobId, job.Status);

            await TryFailJobAsync(job, "PIPELINE_ERROR", orgId, ct);
        }
    }

    // ── Phase 1: DETECT → PARSE → NORMALIZE → AWAITING_REVIEW ────────────────

    private async Task RunParsePhaseAsync(
        AnalysisJobMessage message,
        Domain.Entities.Job job,
        OrgId orgId,
        Stopwatch sw,
        CancellationToken ct)
    {
        // DETECT
        await TransitionAsync(job, JobStatus.Parsing, ct);
        var detectOutput = await detectStage.ExecuteAsync(message, ct);

        // PARSE
        var parseInput = new ParseInput(
            ArtifactType: detectOutput.ArtifactType,
            BlobPath: message.ArtifactBlobPath,
            LowConfidenceArtifactType: detectOutput.LowConfidence,
            ApplicationDescription: message.ApplicationDescription,
            ArchitectureDescription: message.ArchitectureDescription);

        var parseOutput = await parseStage.ExecuteAsync(parseInput, ct);

        // NORMALIZE
        await TransitionAsync(job, JobStatus.Normalizing, ct);
        var normalizeInput = new NormalizeInput(
            parseOutput,
            detectOutput.ArtifactType,
            message.ApplicationDescription,
            message.ArchitectureDescription);
        var canonicalModel = await normalizeStage.ExecuteAsync(normalizeInput, ct);
        var sampleFlows = canonicalModel.DataFlows
            .Take(3)
            .Select(f => $"{f.From}->{f.To}")
            .ToArray();

        logger.LogInformation(
            "Canonical model summary. JobId={JobId} Components={Components} Actors={Actors} DataStores={DataStores} DataFlows={DataFlows} SampleFlows={SampleFlows}",
            job.Id,
            canonicalModel.Components.Length,
            canonicalModel.Actors.Length,
            canonicalModel.DataStores.Length,
            canonicalModel.DataFlows.Length,
            sampleFlows.Length == 0 ? "none" : string.Join(", ", sampleFlows));

        // Persist canonical model to blob — survives AWAITING_REVIEW pause and feeds Phase 2
        await NormalizeStage.PersistAsync(canonicalModel, orgId.Value, job.Id.Value, blobStorage, ct);

        // Persist canonical model to DB — makes architecture available via API
        await dbPersistence.PersistArchitectureAsync(job.Id, orgId, canonicalModel, ct);

        // Transition to AWAITING_REVIEW — pipeline pauses until user confirms via API
        await TransitionAsync(job, JobStatus.AwaitingReview, ct);

        LogUsageSummary(job.Id, "Parse", sw);
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
        Stopwatch sw,
        CancellationToken ct)
    {
        // Load the canonical model.
        // Manual jobs have no Phase 1 blob — build from user-defined elements in DB instead.
        CanonicalModel canonicalModel;
        if (message.ArtifactType == "manual")
        {
            canonicalModel = await dbPersistence.BuildCanonicalModelFromElementsAsync(job.Id, orgId, ct);
            // Inject user-supplied context stored on the job entity
            canonicalModel = canonicalModel with
            {
                ApplicationDescription = job.ApplicationDescription,
                ArchitectureDescription = job.ArchitectureDescription,
            };
            // Persist so CorrectionApplicator and any re-analysis paths can load normally
            await NormalizeStage.PersistAsync(canonicalModel, orgId.Value, job.Id.Value, blobStorage, ct);

            logger.LogInformation(
                "Manual job: canonical model built from DB elements. JobId={JobId}", job.Id);
        }
        else
        {
            canonicalModel = await NormalizeStage.LoadAsync(orgId.Value, job.Id.Value, blobStorage, ct);
            // Ensure descriptions are always present — the canonical blob may pre-date this feature,
            // or Phase 1 may not have injected them. Fall back to the job entity (authoritative source)
            // then to the Phase 2 message as a last resort.
            if (canonicalModel.ApplicationDescription is null || canonicalModel.ArchitectureDescription is null)
            {
                canonicalModel = canonicalModel with
                {
                    ApplicationDescription = canonicalModel.ApplicationDescription
                        ?? job.ApplicationDescription
                        ?? message.ApplicationDescription,
                    ArchitectureDescription = canonicalModel.ArchitectureDescription
                        ?? job.ArchitectureDescription
                        ?? message.ArchitectureDescription,
                };
            }
        }

        // Apply user corrections from DB before CLASSIFY (re-analysis support)
        var arch = await dbPersistence.TryGetArchitectureWithCorrectionsAsync(job.Id, orgId, ct);
        if (arch is not null && arch.Value.corrections.Count > 0)
        {
            canonicalModel = CorrectionApplicator.Apply(
                canonicalModel, arch.Value.elements, arch.Value.corrections, logger);

            // Inject a human-readable corrections summary so downstream stages can reason about
            // what changed since the previous analysis run (re-analysis corrections context)
            canonicalModel = canonicalModel with
            {
                CorrectionsContext = BuildCorrectionsSummary(arch.Value.corrections)
            };

            // Re-persist corrected canonical model to blob so all downstream stages use it
            await NormalizeStage.PersistAsync(canonicalModel, orgId.Value, job.Id.Value, blobStorage, ct);

            // Increment architecture version and delete previous system-generated threats
            arch.Value.architecture.IncrementVersion();
            await dbPersistence.DeleteSystemThreatsAndSaveAsync(job.Id, orgId, ct);

            logger.LogInformation(
                "Corrections applied. JobId={JobId} Count={Count} ArchVersion={Version}",
                job.Id, arch.Value.corrections.Count, arch.Value.architecture.Version);
        }

        // CLASSIFY
        await TransitionAsync(job, JobStatus.Classifying, ct);
        var userCorrections = arch is not null
            ? arch.Value.corrections
                .Select(c => new UserCorrection(
                    ElementId: c.ElementId?.ToString() ?? string.Empty,
                    Field: c.FieldName ?? string.Empty,
                    OldValue: c.OriginalValue,
                    NewValue: c.CorrectedValue ?? string.Empty,
                    CorrectionType: c.CorrectionType.ToString()))
                .ToArray()
            : [];
        var selectedMethods = (message.SelectedMethods ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var classifyInput = new ClassifyInput(canonicalModel, userCorrections, selectedMethods);
        var classification = await classifyStage.ExecuteAsync(classifyInput, ct);

        // Update architecture classification in DB now that CLASSIFY has run
        await dbPersistence.UpdateArchitectureClassificationAsync(
            job.Id, orgId, classification.Categories, ct);

        // ANALYZE — all methods in parallel
        await TransitionAsync(job, JobStatus.Analyzing, ct);
        var allCandidateSets = await analyzeStage.RunAllMethodsAsync(canonicalModel, classification, ct);

        // Brief pause before synthesis so the sliding TPM window can partially clear.
        // Analyze consumes the last batch's tokens right before synthesis fires its large prompt.
        var preSynthesisDelay = throttlingOptions.Value.PreSynthesisDelayMs;
        if (preSynthesisDelay > 0)
        {
            logger.LogInformation("Pre-synthesis delay. DelayMs={DelayMs}", preSynthesisDelay);
            await Task.Delay(preSynthesisDelay, ct);
        }

        // SYNTHESIZE
        await TransitionAsync(job, JobStatus.Synthesizing, ct);
        var synthesizeInput = new SynthesizeInput(allCandidateSets, canonicalModel, classification);
        var finalOutput = await synthesizeStage.ExecuteAsync(synthesizeInput, ct);

        // Persist final output to blob
        var outputBlobPath = await SynthesizeStage.PersistAsync(
            finalOutput, orgId.Value, job.Id.Value, blobStorage, ct);

        // Persist threats, mitigations, framework mappings, and rejected candidates to DB
        await dbPersistence.PersistFinalOutputAsync(
            job.Id, orgId, finalOutput, allCandidateSets, ct);

        // Capture runtime usage before persisting — stop the stopwatch here so elapsed is accurate
        sw.Stop();
        var totalCost = pricingOpts.Value.EstimateTotalCostUsd(tokenUsage.PerModel);

        // Store runtime usage + blob path in job record for UI display and cost tracking
        job.RecordTokenUsage(JsonSerializer.Serialize(new
        {
            outputBlobPath,
            modelRoutingSummary = finalOutput.ModelRoutingSummary,
            elapsedMs          = sw.ElapsedMilliseconds,
            totalInputTokens   = tokenUsage.TotalInputTokens,
            totalOutputTokens  = tokenUsage.TotalOutputTokens,
            estimatedCostUsd   = totalCost > 0m ? (decimal?)totalCost : null,
        }, TokenJsonOptions));

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

        LogUsageSummary(job.Id, "Analyze", sw);

        logger.LogInformation(
            "Pipeline complete. JobId={JobId} Status={Status} Threats={Threats}",
            job.Id, finalStatus, finalOutput.ConfirmedThreats.Length);
    }

    private async Task TryFailJobAsync(Domain.Entities.Job job, string errorCode, OrgId orgId, CancellationToken ct)
    {
        // Guard against double-transition when a save failure in one catch block causes us to
        // re-enter another catch block with the job already in Failed (in-memory).
        if (job.Status != JobStatus.Failed)
            job.Transition(JobStatus.Failed, errorCode: errorCode);

        try
        {
            await jobs.SaveChangesAsync(ct);
        }
        catch (Exception saveEx)
        {
            // Log and swallow — we cannot do better here. The message will be abandoned
            // and redelivered, but we've at least logged the root cause.
            logger.LogError(saveEx,
                "Failed to persist job failure to DB. JobId={JobId} ErrorCode={ErrorCode}",
                job.Id, errorCode);
            return;
        }

        try
        {
            await audit.LogAsync("job.failed",
                orgId: orgId,
                resourceType: "job",
                resourceId: job.Id.Value,
                details: new { errorCode },
                ct: ct);
        }
        catch (Exception auditEx)
        {
            logger.LogError(auditEx,
                "Audit log failed after job failure. JobId={JobId}", job.Id);
        }
    }

    private void LogUsageSummary(JobId jobId, string phase, Stopwatch sw)
    {
        sw.Stop();
        var perModel = tokenUsage.PerModel;
        var pricing = pricingOpts.Value;
        var totalCost = pricing.EstimateTotalCostUsd(perModel);

        var modelLines = new StringBuilder();
        foreach (var (model, (input, output)) in perModel.OrderBy(kv => kv.Key))
        {
            var cost = pricing.EstimateCostUsd(model, input, output);
            modelLines.Append($" | {model}: in={input:N0} out={output:N0}");
            if (cost > 0m) modelLines.Append($" cost=${cost:F4}");
        }

        logger.LogInformation(
            "Pipeline phase usage. JobId={JobId} Phase={Phase} ElapsedMs={ElapsedMs} TotalIn={TotalIn} TotalOut={TotalOut} EstCostUsd={EstCostUsd}{ModelBreakdown}",
            jobId, phase, sw.ElapsedMilliseconds,
            tokenUsage.TotalInputTokens, tokenUsage.TotalOutputTokens,
            totalCost > 0m ? $"{totalCost:F4}" : "n/a",
            modelLines.ToString());
    }

    private static string BuildCorrectionsSummary(IReadOnlyList<ArchitectureCorrection> corrections)
    {
        var lines = new List<string>
        {
            $"{corrections.Count} user correction(s) applied since last analysis:"
        };

        foreach (var c in corrections.Take(20))
        {
            var scope = c.ElementId.HasValue ? $"element" : "architecture";
            var change = c.CorrectionType switch
            {
                CorrectionType.Update        => $"Updated {c.FieldName} from '{c.OriginalValue}' to '{c.CorrectedValue}'",
                CorrectionType.MarkIncorrect => $"Marked {c.FieldName} as incorrect (was '{c.OriginalValue}')",
                CorrectionType.MarkAssumed   => $"Marked {c.FieldName} as an assumption",
                CorrectionType.MarkConfirmed => $"Confirmed {c.FieldName} = '{c.OriginalValue}'",
                CorrectionType.AddNote       => $"Added note: {c.Note}",
                _                            => $"Correction to {c.FieldName}"
            };
            lines.Add($"- [{scope}] {change}");
        }

        if (corrections.Count > 20)
            lines.Add($"... and {corrections.Count - 20} more corrections.");

        return string.Join("\n", lines);
    }

    private async Task TransitionAsync(Domain.Entities.Job job, JobStatus newStatus, CancellationToken ct)
    {
        if (job.Status == newStatus)
        {
            logger.LogDebug(
                "Skipping no-op transition. JobId={JobId} Status={Status}",
                job.Id, newStatus);
            return;
        }

        job.Transition(newStatus);
        await jobs.SaveChangesAsync(ct);
        logger.LogInformation("Job transitioned. JobId={JobId} Status={Status}", job.Id, newStatus);
    }
}
