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

    private static readonly HashSet<string> AllowedGroupKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "storage_shared_key",
        "sas_token_access",
        "cicd_platform_permissions",
        "cicd_external_api_token",
        "bola_request_parameter",
        "no_database_rls",
        "break_glass_no_ca",
        "standing_operational_access",
        "managed_identity_overpriv",
        "api_bypass_edge",
        "sensitive_data_in_logs",
        "cross_tenant_isolation_flaw",
        "supply_chain_ci_cd"
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

        // Arch desc sent once via [SYSTEM_CONTEXT]; null in JSON copy to avoid duplication.
        const int MaxArchDescChars = 12_000;
        var modelForPrompt = TruncateArchDesc(input.CanonicalModel, MaxArchDescChars);
        var modelForJson = modelForPrompt with { ArchitectureDescription = null };

        // Strip only RejectedCandidates (already discarded) and EvidenceBasis/Assumptions
        // (raw analysis notes not useful for synthesis). All other candidate fields are preserved
        // so synthesis has full evidence for deduplication, merging, and mitigation generation.
        var slimCandidateSets = input.AllCandidateSets.Select(set => new
        {
            method = set.Method,
            candidates = set.Candidates.Select(c => new
            {
                c.Title,
                c.MethodCategory,
                c.AffectedElementLabels,
                c.Description,
                c.AttackScenario,
                c.Preconditions,
                c.ImpactedAssets,
                c.SecurityImpact,
                c.PrivacyImpact,
                c.ExistingControls,
                c.ControlGaps,
                c.Confidence,
                c.EvidenceStrength,
                c.FindingType,
                c.GroupKey,
                c.RiskRating
            })
        });
        var allCandidatesJson = JsonSerializer.Serialize(slimCandidateSets, SerializeOptions);
        var canonicalJson = JsonSerializer.Serialize(modelForJson, SerializeOptions);
        var classificationJson = JsonSerializer.Serialize(input.ClassificationResult, SerializeOptions);
        var hotspotSummary = ComputeHotspots(input.AllCandidateSets);
        var mergeGroupsSummary = ComputeMergeGroups(input.AllCandidateSets);

        var userPrompt = PromptTemplates.BuildSynthesizeUser(
            allCandidatesJson, canonicalJson, classificationJson, modelRoutingSummary,
            modelForPrompt.ApplicationDescription,
            modelForPrompt.ArchitectureDescription,
            modelForPrompt.CorrectionsContext,
            hotspotSummary,
            mergeGroupsSummary);

        // Token budget: 65,536 — synthesis receives all candidate sets; model context supports it.
        TokenEstimator.AssertWithinBudget(PromptTemplates.SynthesizeSystem, userPrompt, 65_536, "SYNTHESIZE");

        var request = new LlmRequest(
            SystemPrompt: PromptTemplates.SynthesizeSystem,
            UserPrompt: userPrompt,
            Model: model,
            Temperature: 0.2f,
            MaxTokens: 16384);

        var (output, inputTokens, outputTokens) = await StageRetryHelper.ExecuteWithRetryAsync<FinalOutput>(
            llmClient, request, Validate, "SYNTHESIZE_FAILED", MaxAttempts, logger, ct);

        // Enforce: analysisStatus=partial if any critical gap was unresolved (spec §6 Stage 6 Rule 5)
        output = EnforcePartialStatus(output, input.CanonicalModel);

        // Soft check: warn if confirmed group keys outnumber confirmed threats (possible over-merge signal)
        WarnIfOverMerged(input.AllCandidateSets, output);

        // Framework mapping sub-step: cheap model call after synthesis (spec §4 Stage 6, §7)
        output = await RunFrameworkMappingSubStepAsync(output, model, ct);

        output = output with
        {
            ConfirmedThreats = output.ConfirmedThreats.Select(NormalizeThreat).ToArray(),
            ConditionalThreats = output.ConditionalThreats.Select(NormalizeThreat).ToArray()
        };

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

    private static FinalThreat NormalizeThreat(FinalThreat threat)
    {
        var normalizedMethods = (threat.SourceMethods ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var normalizedRiskRating = NormalizeRiskRating(threat.RiskRating);

        return threat with { SourceMethods = normalizedMethods, RiskRating = normalizedRiskRating };
    }

    private static OwaspRiskRating? NormalizeRiskRating(OwaspRiskRating? rating)
    {
        if (rating is null) return null;

        var likelihood = rating.Likelihood?.ToLowerInvariant() switch
        {
            "high" or "medium" or "low" => rating.Likelihood.ToLowerInvariant(),
            _ => "medium"
        };
        var impact = rating.Impact?.ToLowerInvariant() switch
        {
            "high" or "medium" or "low" => rating.Impact.ToLowerInvariant(),
            _ => "medium"
        };

        // Deterministically derive severity from likelihood × impact (OWASP Risk Rating matrix)
        var severity = (likelihood, impact) switch
        {
            ("high",   "high")   => "critical",
            ("high",   "medium") => "high",
            ("medium", "high")   => "high",
            ("high",   "low")    => "medium",
            ("medium", "medium") => "medium",
            ("low",    "high")   => "medium",
            ("medium", "low")    => "low",
            ("low",    "medium") => "low",
            ("low",    "low")    => "note",
            _                    => "medium"
        };

        return new OwaspRiskRating(
            Likelihood: likelihood,
            Impact: impact,
            Severity: severity,
            LikelihoodJustification: rating.LikelihoodJustification,
            ImpactJustification: rating.ImpactJustification);
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

    // Groups candidates by their allow-listed groupKey.
    // Returns a [MERGE_GROUPS] summary string for the synthesis prompt, or null if fewer than 2 groups exist.
    private static string? ComputeMergeGroups(ThreatCandidateSet[] sets)
    {
        var groups = sets
            .SelectMany(set => set.Candidates
                .Where(c => c.GroupKey is not null && AllowedGroupKeys.Contains(c.GroupKey))
                .Select(c => new { Key = c.GroupKey!, c.AffectedElementLabels, set.Method }))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                groupKey = g.Key,
                elements = g.SelectMany(x => x.AffectedElementLabels)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(e => e)
                            .ToArray(),
                methods = g.Select(x => x.Method)
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .OrderBy(m => m)
                           .ToArray()
            })
            .Where(g => g.elements.Length > 0)
            .OrderBy(g => g.groupKey)
            .ToArray();

        if (groups.Length < 2) return null;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Candidates were grouped by attack-vector key during analysis.");
        sb.AppendLine("Candidates in DIFFERENT groups MUST produce SEPARATE final threats — do not merge across groups.");
        foreach (var g in groups)
            sb.AppendLine($"- {g.groupKey}: affects [{string.Join(", ", g.elements)}], seen by [{string.Join(", ", g.methods)}]");
        return sb.ToString().TrimEnd();
    }

    // Soft diagnostic: warns in logs if distinct confirmed group keys outnumber confirmed threats.
    // This is a signal of over-merging; it does not fail the pipeline.
    private void WarnIfOverMerged(ThreatCandidateSet[] sets, FinalOutput output)
    {
        var confirmedGroupKeyCount = sets
            .SelectMany(s => s.Candidates)
            .Where(c => c.GroupKey is not null
                     && AllowedGroupKeys.Contains(c.GroupKey!)
                     && string.Equals(c.FindingType, "confirmed", StringComparison.OrdinalIgnoreCase)
                     && string.Equals(c.EvidenceStrength, "direct", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.GroupKey!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (confirmedGroupKeyCount > output.ConfirmedThreats.Length)
            logger.LogWarning(
                "SYNTHESIZE: possible over-merge detected. DistinctConfirmedGroupKeys={Keys} ConfirmedThreats={Threats}. " +
                "Review [MERGE_GROUPS] constraints in the next run.",
                confirmedGroupKeyCount, output.ConfirmedThreats.Length);
    }

    // Counts how many distinct analysis methods independently flagged each element label.
    // Elements flagged by ≥2 methods are surfaced as hotspots for the synthesis model.
    private static string? ComputeHotspots(ThreatCandidateSet[] sets)
    {
        var methodsByElement = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var set in sets)
        {
            foreach (var candidate in set.Candidates)
            {
                foreach (var label in candidate.AffectedElementLabels)
                {
                    if (!methodsByElement.TryGetValue(label, out var methods))
                    {
                        methods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        methodsByElement[label] = methods;
                    }
                    methods.Add(set.Method);
                }
            }
        }

        var hotspots = methodsByElement
            .Where(kv => kv.Value.Count >= 2)
            .OrderByDescending(kv => kv.Value.Count)
            .ToArray();

        if (hotspots.Length == 0) return null;

        return string.Join("\n", hotspots.Select(kv =>
            $"- {kv.Key}: flagged by {kv.Value.Count} methods ({string.Join(", ", kv.Value.OrderBy(m => m))})"));
    }

    private static CanonicalModel TruncateArchDesc(CanonicalModel model, int maxChars)
    {
        var desc = model.ArchitectureDescription;
        return desc is not null && desc.Length > maxChars
            ? model with { ArchitectureDescription = desc[..maxChars] + " [truncated]" }
            : model;
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
