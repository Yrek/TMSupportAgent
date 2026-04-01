using System.Text.Json;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Worker.Llm;
using ThreatModelingAgent.Worker.Pipeline.Contracts;
using ThreatModelingAgent.Worker.Pipeline.Prompts;

namespace ThreatModelingAgent.Worker.Pipeline.Stages;

/// <summary>
/// Stage 6 — SYNTHESIZE.
///
/// Merges all method-specific ThreatCandidateSets into a final, deduplicated,
/// prioritized FinalOutput.
///
/// Also runs a cheap parallel call to map final threats to framework references
/// (OWASP, ASVS, CIS, NCSC) — separated because it is pattern-matching, not reasoning.
///
/// Model: strong (gpt-4o / claude-sonnet-4-6) — synthesis requires judgment.
/// Retry: up to 3 attempts on schema validation failure.
/// Fails with SYNTHESIZE_FAILED after max retries.
///
/// After completion, FinalOutput is persisted to blob:
///   /{orgId}/outputs/{jobId}/analysis.json
/// </summary>
public sealed class SynthesizeStage(
    LlmClientFactory llmFactory,
    IBlobStorage blobStorage,
    ILogger<SynthesizeStage> logger) : IPipelineStage<SynthesizeInput, FinalOutput>
{
    private const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<FinalOutput> ExecuteAsync(SynthesizeInput input, CancellationToken ct)
    {
        var model = llmFactory.GetStrongModel();
        var llmClient = llmFactory.GetForModel(model);

        var modelRoutingSummary = new Dictionary<string, string>
        {
            ["synthesize"] = model,
            ["analyzeStrong"] = input.ClassificationResult.ModelRoutingPlan.AnalyzeStageSecurity,
            ["analyzeLight"] = input.ClassificationResult.ModelRoutingPlan.AnalyzeStageLight
        };

        var allCandidatesJson = JsonSerializer.Serialize(input.AllCandidateSets, SerializeOptions);
        var canonicalJson = JsonSerializer.Serialize(input.CanonicalModel, SerializeOptions);
        var classificationJson = JsonSerializer.Serialize(input.ClassificationResult, SerializeOptions);

        var userPrompt = PromptTemplates.BuildSynthesizeUser(
            allCandidatesJson, canonicalJson, classificationJson, modelRoutingSummary);

        var request = new LlmRequest(
            SystemPrompt: PromptTemplates.SynthesizeSystem,
            UserPrompt: userPrompt,
            Model: model,
            Temperature: 0.2f,
            MaxTokens: 12288);

        var (output, inputTokens, outputTokens) = await StageRetryHelper.ExecuteWithRetryAsync<FinalOutput>(
            llmClient, request, Validate, "SYNTHESIZE_FAILED", MaxAttempts, logger, ct);

        // Enforce: analysisStatus=partial if any critical gap was unresolved (spec §6 Stage 6 Rule 5)
        output = EnforcePartialStatus(output, input.CanonicalModel);

        logger.LogInformation(
            "SYNTHESIZE complete. Confirmed={Confirmed} Conditional={Conditional} Status={Status} " +
            "InputTokens={InputTokens} OutputTokens={OutputTokens}",
            output.ConfirmedThreats.Length, output.ConditionalThreats.Length,
            output.AnalysisStatus, inputTokens, outputTokens);

        return output;
    }

    /// <summary>
    /// Persists the final output JSON blob and returns the blob path.
    /// </summary>
    public static async Task<string> PersistAsync(
        FinalOutput output, Guid orgId, Guid jobId,
        IBlobStorage blobStorage, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(output, SerializeOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        using var stream = new MemoryStream(bytes);
        var path = $"{orgId}/outputs/{jobId}/analysis.json";
        await blobStorage.UploadAsync(path, stream, "application/json", ct);
        return path;
    }

    private static FinalOutput EnforcePartialStatus(FinalOutput output, CanonicalModel model)
    {
        var hasCriticalGap = model.Gaps.Any(g =>
            string.Equals(g.SecurityRelevance, "critical", StringComparison.OrdinalIgnoreCase));

        if (hasCriticalGap && output.AnalysisStatus != "partial")
        {
            return output with
            {
                AnalysisStatus = "partial",
                PartialReason = output.PartialReason
                    ?? "One or more critical architectural gaps were unresolved before analysis."
            };
        }

        return output;
    }

    private static string? Validate(FinalOutput o)
    {
        if (string.IsNullOrWhiteSpace(o.SystemSummary))      return "systemSummary is missing";
        if (o.ConfirmedThreats is null)                      return "confirmedThreats is null";
        if (o.ConditionalThreats is null)                    return "conditionalThreats is null";
        if (o.SecureDesignRecommendations is null)           return "secureDesignRecommendations is null";
        if (o.PrioritizedRemediationList is null)            return "prioritizedRemediationList is null";
        if (string.IsNullOrWhiteSpace(o.AnalysisStatus))    return "analysisStatus is missing";
        if (o.AnalysisStatus is not ("complete" or "partial"))
            return $"analysisStatus invalid value: {o.AnalysisStatus}";

        // Confirm only confirmed threats appear in remediation list (spec §6 Rule 3)
        var confirmedIds = o.ConfirmedThreats.Select(t => t.Identifier).ToHashSet();
        foreach (var r in o.PrioritizedRemediationList)
        {
            if (!confirmedIds.Contains(r.ThreatIdentifier))
                return $"prioritizedRemediationList references non-confirmed threat: {r.ThreatIdentifier}";
        }

        return null;
    }
}
