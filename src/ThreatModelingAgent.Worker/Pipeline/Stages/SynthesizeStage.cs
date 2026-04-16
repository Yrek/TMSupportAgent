using System.Text.Json;
using System.Text.Json.Serialization;
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
    ILlmClientFactory llmFactory,
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
            ["analyzeBaseline"] = input.ClassificationResult.ModelRoutingPlan.AnalyzeStageSecurity,
            ["analyzeStrong"] = input.ClassificationResult.ModelRoutingPlan.AnalyzeStageSecurity,
            ["analyzeLight"] = input.ClassificationResult.ModelRoutingPlan.AnalyzeStageLight
        };

        var allCandidatesJson = JsonSerializer.Serialize(input.AllCandidateSets, SerializeOptions);
        var canonicalJson = JsonSerializer.Serialize(input.CanonicalModel, SerializeOptions);
        var classificationJson = JsonSerializer.Serialize(input.ClassificationResult, SerializeOptions);

        var userPrompt = PromptTemplates.BuildSynthesizeUser(
            allCandidatesJson, canonicalJson, classificationJson, modelRoutingSummary);

        // Token budget: 16,384 input (spec §7) — fail closed rather than truncate
        TokenEstimator.AssertWithinBudget(PromptTemplates.SynthesizeSystem, userPrompt, 16_384, "SYNTHESIZE");

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

        // Framework mapping sub-step: cheap model call after synthesis (spec §4 Stage 6, §7)
        output = await RunFrameworkMappingSubStepAsync(output, model, ct);

        // Ensure UserAddedThreats is always an empty array at synthesis time (spec §4 Stage 6)
        // Populated later via POST /threats API — never by the LLM.
        // Clear any LLM-produced value, whether null or non-empty.
        output = output with { UserAddedThreats = [] };

        logger.LogInformation(
            "SYNTHESIZE complete. Confirmed={Confirmed} Conditional={Conditional} Status={Status} " +
            "InputTokens={InputTokens} OutputTokens={OutputTokens}",
            output.ConfirmedThreats.Length, output.ConditionalThreats.Length,
            output.AnalysisStatus, inputTokens, outputTokens);

        return output;
    }

    // ── Framework mapping sub-step ────────────────────────────────────────────

    private sealed record FrameworkMappingItem(
        [property: JsonPropertyName("threatIdentifier")] string ThreatIdentifier,
        [property: JsonPropertyName("framework")] string Framework,
        [property: JsonPropertyName("reference")] string Reference,
        [property: JsonPropertyName("mappingType")] string? MappingType);

    /// <summary>
    /// Runs a separate cheap-model call to map final threats to framework references.
    /// Unknown framework values are discarded. Pipeline does not fail if this sub-step fails —
    /// it is supplementary; the main synthesis output is already validated.
    /// Spec reference: 05-llm-workflow §4 Stage 6, §7 (framework-mapping token budget).
    /// </summary>
    private async Task<FinalOutput> RunFrameworkMappingSubStepAsync(
        FinalOutput output, string _synthesisModel, CancellationToken ct)
    {
        var allThreats = output.ConfirmedThreats.Concat(output.ConditionalThreats).ToArray();
        if (allThreats.Length == 0) return output;

        var cheapModel = llmFactory.GetLowCostModel();
        var llmClient = llmFactory.GetForModel(cheapModel);

        // Serialize only the fields the framework mapper needs (identifier + title + description)
        var threatSummaries = allThreats.Select(t => new
        {
            identifier = t.Identifier,
            title = t.Title,
            methodCategory = t.MethodCategory,
            description = t.Description
        });
        var threatsJson = JsonSerializer.Serialize(threatSummaries, SerializeOptions);
        var userPrompt = PromptTemplates.BuildFrameworkMappingUser(threatsJson);

        // Token budget: 8,192 input (spec §7) — skip sub-step rather than fail the job
        var estimated = TokenEstimator.EstimatePrompt(PromptTemplates.FrameworkMappingSystem, userPrompt);
        if (estimated > (int)(8_192 * 0.9))
        {
            logger.LogWarning(
                "Framework mapping sub-step skipped — estimated tokens ({Estimated}) exceed budget. " +
                "Threats will have empty frameworkMappings.", estimated);
            return output;
        }

        var request = new LlmRequest(
            SystemPrompt: PromptTemplates.FrameworkMappingSystem,
            UserPrompt: userPrompt,
            Model: cheapModel,
            Temperature: 0f,
            MaxTokens: 4096);

        List<FrameworkMappingItem>? mappings = null;
        try
        {
            var response = await llmClient.CompleteAsync(request, ct);
            var cleaned = response.Content.Trim();
            if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) cleaned = cleaned[7..];
            else if (cleaned.StartsWith("```")) cleaned = cleaned[3..];
            if (cleaned.EndsWith("```")) cleaned = cleaned[..^3];

            mappings = JsonSerializer.Deserialize<List<FrameworkMappingItem>>(cleaned.Trim(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            logger.LogInformation(
                "Framework mapping sub-step complete. Model={Model} Mappings={Count} " +
                "InputTokens={InputTokens} OutputTokens={OutputTokens}",
                cheapModel, mappings?.Count ?? 0, response.InputTokens, response.OutputTokens);
        }
        catch (Exception ex)
        {
            // Framework mapping is supplementary — log and continue without it
            logger.LogWarning(ex, "Framework mapping sub-step failed; synthesis output is unaffected.");
            return output;
        }

        if (mappings is null or []) return output;

        // Build a lookup: threatIdentifier → list of valid normalized mappings
        var mappingsByIdentifier = new Dictionary<string, List<FrameworkMapping>>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mappings)
        {
            var normalizedFramework = FrameworkNormalizer.Normalize(m.Framework);
            if (normalizedFramework is null) continue;  // discard unknown frameworks

            if (!mappingsByIdentifier.TryGetValue(m.ThreatIdentifier, out var list))
            {
                list = [];
                mappingsByIdentifier[m.ThreatIdentifier] = list;
            }
            list.Add(new FrameworkMapping(normalizedFramework, m.Reference, m.MappingType ?? "direct"));
        }

        // Merge into final output threats
        FinalThreat MergeMappings(FinalThreat threat)
        {
            if (!mappingsByIdentifier.TryGetValue(threat.Identifier, out var newMappings)) return threat;
            // Combine with any mappings the synthesis model already produced, deduplicated by framework+reference
            var combined = threat.FrameworkMappings
                .Concat(newMappings)
                .GroupBy(fm => $"{fm.Framework}:{fm.Reference}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToArray();
            return threat with { FrameworkMappings = combined };
        }

        return output with
        {
            ConfirmedThreats  = output.ConfirmedThreats.Select(MergeMappings).ToArray(),
            ConditionalThreats = output.ConditionalThreats.Select(MergeMappings).ToArray()
        };
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
        // UserAddedThreats may be null from LLM (it is not in the schema); normalised to [] after validation
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
