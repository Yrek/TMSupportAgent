using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
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
    ILogger<SynthesizeStage> logger,
    IOptions<SynthesisOptions> synthesisOptions) : IPipelineStage<SynthesizeInput, FinalOutput>
{
    private const int MaxAttempts = 5;

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Group keys are defined in GroupKeyRegistry — single source of truth.
    private static readonly HashSet<string> AllowedGroupKeys = GroupKeyRegistry.AllowedKeys;

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

        // Strip fields from the canonical JSON that synthesis does not need:
        // - ArchitectureDescription / ApplicationDescription / CorrectionsContext: in [SYSTEM_CONTEXT]
        // - Gaps: EnforcePartialStatus reads these from the C# object, not the LLM prompt
        // - ClarificationQuestions / Assumptions: not synthesis-relevant
        // - PrivilegedPaths: already encoded in candidate groupKeys and [MERGE_GROUPS]
        // - BackgroundJobs: low synthesis relevance
        // Stripping these reduces canonical JSON by ~1,000–2,000 tokens, making room to keep
        // full candidate payload without hitting the 30K TPM ceiling.
        var modelForJson = modelForPrompt with
        {
            ArchitectureDescription   = null,
            ApplicationDescription    = null,
            CorrectionsContext        = null,
            Gaps                      = [],
            ClarificationQuestions    = [],
            Assumptions               = [],
            PrivilegedPaths           = [],
            BackgroundJobs            = [],
        };

        // Drop confirmed+direct candidates that carry no evidenceBasis — they violate Rule 18 and
        // would reach synthesis as evidence-free "confirmed" claims, corrupting risk ratings and
        // group key coverage accounting. Filtering here avoids spurious GROUP_KEY_COVERAGE failures.
        var filteredSets = FilterEmptyEvidenceCandidates(input.AllCandidateSets);

        // Flatten all candidates, sort by importance, serialize for synthesis.
        // statedFact (explicit_user_provided_fact) candidates are always prioritized.
        // RiskRating justification text is dropped — synthesis only needs severity/likelihood/impact
        // for prioritization decisions; justifications are regenerated in the final output.
        // All other fields are sent in full so synthesis can produce high-fidelity output.
        var allCandidates = filteredSets
            .SelectMany(set => set.Candidates.Select(c => new
            {
                sourceMethod = set.Method,
                c.Title,
                c.GroupKey,
                c.AffectedElementLabels,
                c.Description,
                c.AttackScenario,
                c.SecurityImpact,
                c.ExistingControls,
                c.ControlGaps,
                c.Preconditions,
                c.ImpactedAssets,
                c.PrivacyImpact,
                riskRating = c.RiskRating is null ? null : new
                {
                    c.RiskRating.Severity,
                    c.RiskRating.Likelihood,
                    c.RiskRating.Impact
                },
                c.EvidenceBasis,
                c.Confidence,
                c.EvidenceStrength,
                c.FindingType,
                c.CoversGapArea,
                statedFact = string.Equals(c.FindingType, "confirmed", StringComparison.OrdinalIgnoreCase)
                          && string.Equals(c.EvidenceStrength, "direct", StringComparison.OrdinalIgnoreCase)
            }))
            .OrderByDescending(c => c.statedFact)
            .ThenByDescending(c => SeverityOrder(c.riskRating?.Severity))
            .ThenByDescending(c => ConfidenceOrder(c.Confidence))
            .ToList();

        var allCandidatesJson = JsonSerializer.Serialize(allCandidates, SerializeOptions);
        var canonicalJson = JsonSerializer.Serialize(modelForJson, SerializeOptions);
        var classificationJson = JsonSerializer.Serialize(input.ClassificationResult, SerializeOptions);
        var hotspotSummary = ComputeHotspots(filteredSets);
        var mergeGroupsSummary = ComputeMergeGroups(filteredSets);

        logger.LogInformation(
            "SYNTHESIZE payload. TotalCandidates={TotalCandidates} CandidateChars={CandidateChars} CanonicalChars={CanonicalChars}",
            allCandidates.Count, allCandidatesJson.Length, canonicalJson.Length);

        var userPrompt = PromptTemplates.BuildSynthesizeUser(
            allCandidatesJson, canonicalJson, classificationJson, modelRoutingSummary,
            modelForPrompt.ApplicationDescription,
            modelForPrompt.ArchitectureDescription,
            modelForPrompt.CorrectionsContext,
            hotspotSummary,
            mergeGroupsSummary);

        // Token budget: driven by config so the ceiling can be raised when using models with larger
        // context windows (e.g. claude-sonnet-4-6 at 200K or gpt-4.1 at 1M).
        // Default matches OpenAI gpt-4o tier-1 TPM ceiling (30K). Set Synthesis:TokenCeiling in
        // appsettings to a higher value when switching to a large-context model.
        var opts = synthesisOptions.Value;
        var synthesisInputBudget = opts.TokenCeiling - opts.MaxOutputTokens;
        TokenEstimator.AssertWithinBudget(PromptTemplates.SynthesizeSystem, userPrompt, synthesisInputBudget, "SYNTHESIZE");

        var request = new LlmRequest(
            SystemPrompt: PromptTemplates.SynthesizeSystem,
            UserPrompt: userPrompt,
            Model: model,
            Temperature: 0.0f,
            MaxTokens: opts.MaxOutputTokens.ToMaxTokens());

        // Group key coverage is a hard synthesis constraint: every confirmed+direct-evidence group key
        // in the candidate pool must appear on at least one confirmed threat in the output.
        // Embedding this in the validator means a coverage failure triggers an automatic retry
        // (up to MaxAttempts) rather than silently passing through to post-hoc patching.
        // coverageGaps is updated on every attempt; its final value (empty on success) is passed to
        // the adversarial review so it can focus on any remaining blind spots.
        var coverageGaps = new List<string>();
        string? ValidateOutput(FinalOutput o)
        {
            var baseError = Validate(o);
            if (baseError is not null) return baseError;

            var uncovered = ComputeUncoveredGroupKeys(filteredSets, o);
            coverageGaps = uncovered;

            if (uncovered.Count == 0)
            {
                logger.LogInformation("SYNTHESIZE: group key coverage check passed.");
                return null;
            }

            logger.LogWarning(
                "SYNTHESIZE: GROUP_KEY_COVERAGE failure — {Count} direct-evidence group key(s) have no confirmed threat. " +
                "Keys: [{Keys}]. Retrying synthesis.",
                uncovered.Count, string.Join(", ", uncovered));
            return $"GROUP_KEY_COVERAGE: {uncovered.Count} direct-evidence group key(s) produced no confirmed threat — " +
                   $"[{string.Join(", ", uncovered)}]. Each must appear as a standalone confirmed threat with that groupKey.";
        }

        var (output, inputTokens, outputTokens) = await StageRetryHelper.ExecuteWithRetryAsync<FinalOutput>(
            llmClient, request, ValidateOutput, "SYNTHESIZE_FAILED", MaxAttempts, logger, ct);

        // Enforce: analysisStatus=partial if any critical gap was unresolved (spec §6 Stage 6 Rule 5)
        output = EnforcePartialStatus(output, input.CanonicalModel);

        // Deterministically fix any findingType mismatches the LLM may have introduced.
        output = EnforceFindingTypeConsistency(output);

        // Warn if any confirmed threat merged candidates from different group keys (should not happen).
        WarnIfCrossGroupKeyMerge(filteredSets, output);

        // Warn if critical/high gaps have no matching threat.
        CheckGapCoverage(input.CanonicalModel, output);

        // Soft check: warn if confirmed group keys outnumber confirmed threats (possible over-merge signal)
        WarnIfOverMerged(filteredSets, output);

        // Soft check: warn if severity distribution is heavily skewed toward Critical.
        WarnSeverityDistribution(output);

        // Adversarial review sub-step: ask model what was missed — guided by coverage gaps so it
        // focuses on attack vectors that had direct evidence but produced no confirmed threat.
        // coverageGaps is set by ValidateOutput during the retry loop — empty if synthesis passed cleanly.
        // Runs before framework mapping so new conditional threats also receive framework references.
        output = await RunAdversarialReviewSubStepAsync(output, input.CanonicalModel, coverageGaps, ct);

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
        output = output with
        {
            UserAddedThreats = [],
            PromptVersions = ExtractPromptVersions()
        };

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

        // Normalize framework mappings produced inline by the synthesis model.
        // The framework mapping sub-step handles its own output; this fixes synthesis-inline ones
        // (e.g. "ATT&CK" → "mitre_attack") before they reach the sub-step merge.
        var normalizedMappings = (threat.FrameworkMappings ?? [])
            .Select(fm => new { fm, normalized = FrameworkNormalizer.Normalize(fm.Framework) })
            .Where(x => x.normalized is not null)
            .Select(x => x.fm with { Framework = x.normalized! })
            .GroupBy(fm => $"{fm.Framework}:{fm.Reference}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();

        return threat with
        {
            SourceMethods = normalizedMethods,
            RiskRating = normalizedRiskRating,
            FrameworkMappings = normalizedMappings
        };
    }

    // Moves threats that landed in the wrong array due to LLM error.
    // Confirmed threats with findingType != "confirmed" are demoted to conditional; conditional
    // threats with findingType == "confirmed" are promoted. Logs each correction as a warning.
    private FinalOutput EnforceFindingTypeConsistency(FinalOutput output)
    {
        var demoted = output.ConfirmedThreats
            .Where(t => !string.Equals(t.FindingType, "confirmed", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var promoted = output.ConditionalThreats
            .Where(t => string.Equals(t.FindingType, "confirmed", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (demoted.Length == 0 && promoted.Length == 0) return output;

        foreach (var t in demoted)
            logger.LogWarning("SYNTHESIZE: demoting {Id} ({Title}) from confirmed — findingType={FindingType}",
                t.Identifier, t.Title, t.FindingType);
        foreach (var t in promoted)
            logger.LogWarning("SYNTHESIZE: promoting {Id} ({Title}) to confirmed — findingType={FindingType}",
                t.Identifier, t.Title, t.FindingType);

        var confirmedIds  = demoted.Select(t => t.Identifier).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conditionalIds = promoted.Select(t => t.Identifier).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return output with
        {
            ConfirmedThreats = output.ConfirmedThreats
                .Where(t => !confirmedIds.Contains(t.Identifier))
                .Concat(promoted)
                .ToArray(),
            ConditionalThreats = output.ConditionalThreats
                .Where(t => !conditionalIds.Contains(t.Identifier))
                .Concat(demoted)
                .ToArray()
        };
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

        // Token budget: skip sub-step rather than fail the job
        var estimated = TokenEstimator.EstimatePrompt(PromptTemplates.FrameworkMappingSystem, userPrompt);
        var fwInputBudget = synthesisOptions.Value.FrameworkMappingInputBudget;
        if (estimated > (int)(fwInputBudget * 0.9))
        {
            logger.LogWarning(
                "Framework mapping sub-step skipped — estimated tokens ({Estimated}) exceed budget ({Budget}). " +
                "Threats will have empty frameworkMappings.", estimated, fwInputBudget);
            return output;
        }

        var request = new LlmRequest(
            SystemPrompt: PromptTemplates.FrameworkMappingSystem,
            UserPrompt: userPrompt,
            Model: cheapModel,
            Temperature: 0f,
            MaxTokens: synthesisOptions.Value.FrameworkMappingMaxOutputTokens.ToMaxTokens());

        List<FrameworkMappingItem>? mappings = null;
        try
        {
            var response = await llmClient.CompleteAsync(request, ct);
            var cleaned = response.Content.Trim();
            if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) cleaned = cleaned[7..];
            else if (cleaned.StartsWith("```")) cleaned = cleaned[3..];
            if (cleaned.EndsWith("```")) cleaned = cleaned[..^3];

            var trimmed = cleaned.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                logger.LogInformation(
                    "Framework mapping returned empty content (possible MaxTokens hit at {OutputTokens}). Mappings skipped.",
                    response.OutputTokens);
                return output;
            }

            mappings = JsonSerializer.Deserialize<List<FrameworkMappingItem>>(trimmed, DeserializeOptions);

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
            var combined = (threat.FrameworkMappings ?? [])
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

    // Warns when a confirmed threat traces back to candidates with 2–5 distinct group keys
    // whose elements are fully contained in the threat's element set — a signal that MERGE_GROUPS
    // was violated for a specific pair of attack vectors. Threats with > 5 distinct keys indicate
    // massive over-merging already captured by WarnIfOverMerged; those are suppressed here to
    // keep this signal actionable rather than noisy. Emits a single summary log line.
    private void WarnIfCrossGroupKeyMerge(ThreatCandidateSet[] sets, FinalOutput output)
    {
        var candidatesByMethod = sets.ToDictionary(
            s => s.Method,
            s => s.Candidates,
            StringComparer.OrdinalIgnoreCase);

        var violations = new List<string>();

        foreach (var threat in output.ConfirmedThreats)
        {
            if (threat.SourceMethods is null or []) continue;

            var affectedSet = threat.AffectedElementLabels.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var groupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var method in threat.SourceMethods)
            {
                if (!candidatesByMethod.TryGetValue(method, out var candidates)) continue;
                foreach (var c in candidates)
                {
                    if (c.GroupKey is null || !AllowedGroupKeys.Contains(c.GroupKey)) continue;
                    // Require ALL candidate elements to sit within the threat's element set.
                    // Using All (not Any) avoids false positives from incidental one-element overlaps.
                    if (c.AffectedElementLabels.Length > 0 &&
                        c.AffectedElementLabels.All(l => affectedSet.Contains(l)))
                        groupKeys.Add(c.GroupKey);
                }
            }

            // 1–2 keys → fine (two-vector threats are legitimate compound findings).
            // 3–5     → specific accidental merge; worth flagging.
            // > 5     → massive over-merge; already surfaced by WarnIfOverMerged.
            if (groupKeys.Count is >= 3 and <= 5)
            {
                var label = threat.Title.Length > 50 ? threat.Title[..50] + "…" : threat.Title;
                violations.Add($"{threat.Identifier} ({label}): [{string.Join(", ", groupKeys.OrderBy(k => k))}]");
            }
        }

        if (violations.Count > 0 && logger.IsEnabled(LogLevel.Warning))
            logger.LogWarning(
                "SYNTHESIZE: {Count} confirmed threat(s) show possible cross-group-key merge (3–5 distinct keys). {Threats}",
                violations.Count, string.Join(" | ", violations));
    }

    // Checks whether each critical/high canonical gap is referenced by at least one
    // confirmed or conditional threat. Uses AffectedElementLabels linkage when available;
    // falls back to keyword matching on gap.Area for legacy gaps without element linkage.
    private void CheckGapCoverage(CanonicalModel model, FinalOutput output)
    {
        var gaps = model.Gaps
            .Where(g => string.Equals(g.SecurityRelevance, "critical", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(g.SecurityRelevance, "high", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (gaps.Length == 0) return;

        var allThreats = output.ConfirmedThreats.Concat(output.ConditionalThreats).ToArray();

        foreach (var gap in gaps)
        {
            bool covered;

            // Prefer deterministic element-label matching when the gap carries element refs.
            if (gap.AffectedElementLabels is { Length: > 0 })
            {
                var gapLabels = gap.AffectedElementLabels.ToHashSet(StringComparer.OrdinalIgnoreCase);
                covered = allThreats.Any(t =>
                    t.AffectedElementLabels.Any(l => gapLabels.Contains(l)));
            }
            else
            {
                // Fallback: keyword matching on gap area string.
                var gapWords = gap.Area
                    .Split([' ', '_', '-', '.'], StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 3)
                    .ToArray();

                covered = gapWords.Length > 0 && allThreats.Any(t =>
                {
                    var threatText = $"{t.Title} {t.Description} {t.ControlGaps}";
                    return gapWords.Any(w => threatText.Contains(w, StringComparison.OrdinalIgnoreCase));
                });
            }

            if (!covered)
                logger.LogWarning(
                    "SYNTHESIZE: {Relevance} gap [{Area}] has no matching confirmed or conditional threat. Gap: {Description}",
                    gap.SecurityRelevance, gap.Area, gap.Description);
        }
    }

    // ── Group key coverage enforcement ───────────────────────────────────────

    /// <summary>
    /// Pure computation: returns every confirmed+direct-evidence group key in the candidate pool
    /// that has no matching confirmed threat in the output.  No side effects — callers log as needed.
    /// Used by the ValidateOutput closure (retry path) and can be called post-retry for diagnostics.
    /// </summary>
    private static List<string> ComputeUncoveredGroupKeys(ThreatCandidateSet[] sets, FinalOutput output)
    {
        var directGroupKeys = sets
            .SelectMany(s => s.Candidates)
            .Where(c => c.GroupKey is not null
                     && AllowedGroupKeys.Contains(c.GroupKey!)
                     && string.Equals(c.FindingType, "confirmed", StringComparison.OrdinalIgnoreCase)
                     && string.Equals(c.EvidenceStrength, "direct", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.GroupKey!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var coveredGroupKeys = output.ConfirmedThreats
            .Where(t => t.GroupKey is not null)
            .Select(t => t.GroupKey!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return directGroupKeys
            .Where(k => !coveredGroupKeys.Contains(k))
            .OrderBy(k => k)
            .ToList();
    }

    // ── Evidence basis pre-filter ─────────────────────────────────────────────

    /// <summary>
    /// Removes confirmed+direct candidates whose evidenceBasis is null or empty.
    /// These violate analyze Rule 18 ("evidenceBasis MUST be populated for every candidate").
    /// Filtering before synthesis prevents evidence-free claims from entering the confirmed pool,
    /// corrupting risk ratings, and causing spurious GROUP_KEY_COVERAGE failures.
    /// </summary>
    private ThreatCandidateSet[] FilterEmptyEvidenceCandidates(ThreatCandidateSet[] sets)
    {
        return sets.Select(set =>
        {
            var valid   = new List<ThreatCandidate>();
            var dropped = new List<ThreatCandidate>();

            foreach (var c in set.Candidates)
            {
                if (string.Equals(c.FindingType, "confirmed", StringComparison.OrdinalIgnoreCase)
                 && string.Equals(c.EvidenceStrength, "direct", StringComparison.OrdinalIgnoreCase)
                 && (c.EvidenceBasis is null or { Length: 0 }))
                    dropped.Add(c);
                else
                    valid.Add(c);
            }

            if (dropped.Count == 0) return set;

            foreach (var d in dropped)
                logger.LogWarning(
                    "SYNTHESIZE: dropping confirmed/direct candidate '{Title}' [{Method}] — empty evidenceBasis (Rule 18 violation). GroupKey={GroupKey}",
                    d.Title, set.Method, d.GroupKey ?? "null");

            return set with { Candidates = [.. valid] };
        }).ToArray();
    }

    // Soft check: warns when the confirmed threat distribution is heavily skewed toward Critical.
    private void WarnSeverityDistribution(FinalOutput output)
    {
        var total = output.ConfirmedThreats.Length;
        if (total < 5) return;

        var criticalCount = output.ConfirmedThreats
            .Count(t => string.Equals(t.RiskRating?.Severity, "critical", StringComparison.OrdinalIgnoreCase));

        if (criticalCount * 100 / total > 60)
            logger.LogWarning(
                "SYNTHESIZE: severity distribution skewed — {Critical}/{Total} confirmed threats are Critical. " +
                "Possible likelihood inflation. Review OWASP Risk Rating justifications.",
                criticalCount, total);
    }

    // ── Adversarial review sub-step ───────────────────────────────────────────

    private sealed record ReviewMissedThreat(
        string Title,
        string[] AffectedElementLabels,
        string Description,
        string AttackScenario,
        string? Preconditions,
        string? SecurityImpact,
        string? PrivacyImpact,
        string? ControlGaps,
        string? Likelihood,          // high | medium | low
        string? Impact,              // high | medium | low
        string[]? EvidenceBasis,
        string[]? MitigationHints);  // 1-2 short mitigation titles

    /// <summary>
    /// Asks the strong (or cheap, if configured) model what attack paths the primary
    /// analysis may have missed.  Guided by coverage gaps — group keys with direct evidence
    /// but no confirmed threat — so the review focuses on the most important blind spots.
    /// Appends findings as low-confidence conditional threats (T-NNN, continuing the sequence).
    /// Runs before framework mapping so adversarial threats also receive references.
    /// Non-fatal: if the call fails or produces nothing valid, the output is unchanged.
    /// </summary>
    private async Task<FinalOutput> RunAdversarialReviewSubStepAsync(
        FinalOutput output, CanonicalModel canonicalModel, List<string> coverageGaps, CancellationToken ct)
    {
        var allThreats = output.ConfirmedThreats.Concat(output.ConditionalThreats).ToArray();

        var threatSummaries = allThreats.Select(t => new
        {
            identifier = t.Identifier,
            title = t.Title,
            description = t.Description,
            affectedElementLabels = t.AffectedElementLabels
        });
        var threatsJson = JsonSerializer.Serialize(threatSummaries, SerializeOptions);

        // Send a stripped canonical model — structure and gaps only, no large text blobs.
        var canonicalSummary = new
        {
            systemPurpose = canonicalModel.SystemPurpose,
            components = canonicalModel.Components.Select(c => new { c.Label, c.Type }),
            actors = canonicalModel.Actors.Select(a => new { a.Label, a.Type }),
            externalSystems = canonicalModel.ExternalSystems.Select(e => new { e.Label }),
            dataStores = canonicalModel.DataStores.Select(d => new { d.Label, d.StoreType }),
            trustBoundaries = canonicalModel.TrustBoundaries.Select(b => new { b.Label, b.ContainedComponentLabels }),
            gaps = canonicalModel.Gaps.Select(g => new { g.Area, g.SecurityRelevance }),
            untrustedContentProcessors = canonicalModel.UntrustedContentProcessors,
            outboundInternetComponents = canonicalModel.OutboundInternetComponents,
            federatedIdentityProviders = canonicalModel.FederatedIdentityProviders
        };
        var canonicalJson = JsonSerializer.Serialize(canonicalSummary, SerializeOptions);

        // Build coverage gap summary to guide the reviewer toward known blind spots.
        string? coverageGapsSummary = coverageGaps.Count == 0 ? null : BuildCoverageGapSummary(coverageGaps);

        var opts = synthesisOptions.Value;
        var reviewModel = opts.UseStrongModelForAdversarialReview
            ? llmFactory.GetStrongModel()
            : llmFactory.GetLowCostModel();
        var llmClient = llmFactory.GetForModel(reviewModel);
        var userPrompt = PromptTemplates.BuildReviewUser(canonicalJson, threatsJson, coverageGapsSummary);

        var estimated = TokenEstimator.EstimatePrompt(PromptTemplates.ReviewSystem, userPrompt);
        if (estimated > (int)(opts.ReviewInputBudget * 0.9))
        {
            logger.LogWarning(
                "Adversarial review sub-step skipped — estimated tokens ({Estimated}) exceed budget ({Budget}).",
                estimated, opts.ReviewInputBudget);
            return output;
        }

        var request = new LlmRequest(
            SystemPrompt: PromptTemplates.ReviewSystem,
            UserPrompt: userPrompt,
            Model: reviewModel,
            Temperature: 0f,
            MaxTokens: opts.ReviewMaxOutputTokens.ToMaxTokens());

        List<ReviewMissedThreat>? missed = null;
        try
        {
            var response = await llmClient.CompleteAsync(request, ct);
            var cleaned = response.Content.Trim();
            if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) cleaned = cleaned[7..];
            else if (cleaned.StartsWith("```")) cleaned = cleaned[3..];
            if (cleaned.EndsWith("```")) cleaned = cleaned[..^3];

            var trimmed = cleaned.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                logger.LogInformation(
                    "Adversarial review returned empty content (possible MaxTokens hit at {OutputTokens}). No missed threats added.",
                    response.OutputTokens);
                return output;
            }

            missed = JsonSerializer.Deserialize<List<ReviewMissedThreat>>(trimmed, DeserializeOptions);

            logger.LogInformation(
                "Adversarial review sub-step complete. Model={Model} MissedThreats={Count} CoverageGapsProvided={Gaps} " +
                "InputTokens={InputTokens} OutputTokens={OutputTokens}",
                reviewModel, missed?.Count ?? 0, coverageGaps.Count, response.InputTokens, response.OutputTokens);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Adversarial review sub-step failed; synthesis output is unaffected.");
            return output;
        }

        if (missed is null or []) return output;

        // Only accept threats with labels that exist in the canonical model.
        var knownLabels = canonicalModel.Components.Select(c => c.Label)
            .Concat(canonicalModel.Actors.Select(a => a.Label))
            .Concat(canonicalModel.ExternalSystems.Select(e => e.Label))
            .Concat(canonicalModel.DataStores.Select(d => d.Label))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Continue the T-NNN sequence from the highest existing identifier so adversarial
        // threats use the same format as synthesis threats and pass Threat.ValidateIdentifier.
        var existingMax = output.ConfirmedThreats.Concat(output.ConditionalThreats)
            .Select(t => Regex.Match(t.Identifier, @"^T-(\d+)$"))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value))
            .DefaultIfEmpty(0)
            .Max();

        var newConditional = new List<FinalThreat>();
        var idx = existingMax + 1;
        foreach (var m in missed.Take(5))
        {
            if (string.IsNullOrWhiteSpace(m.Title) || string.IsNullOrWhiteSpace(m.Description)) continue;

            var validLabels = (m.AffectedElementLabels ?? [])
                .Where(l => knownLabels.Contains(l))
                .ToArray();

            // Skip if labels were provided but none are recognized (prevents hallucinated elements).
            if ((m.AffectedElementLabels?.Length ?? 0) > 0 && validLabels.Length == 0) continue;

            // Derive risk rating from the structured likelihood/impact provided by the reviewer.
            var riskRating = (m.Likelihood, m.Impact) switch
            {
                (not null, not null) => NormalizeRiskRating(new OwaspRiskRating(
                    m.Likelihood!, m.Impact!, "medium", null, null)),
                _ => null
            };

            // Convert MitigationHints to stub Mitigation objects.
            var mitigations = (m.MitigationHints ?? [])
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Select(h => new Mitigation(h.Trim(), h.Trim(), "medium", []))
                .ToArray();

            newConditional.Add(new FinalThreat(
                Identifier: $"T-{idx:D3}",
                Title: m.Title.Trim(),
                MethodCategory: "AdversarialReview",
                AffectedElementLabels: validLabels,
                Description: m.Description.Trim(),
                AttackScenario: (m.AttackScenario ?? string.Empty).Trim(),
                Preconditions: m.Preconditions,
                ImpactedAssets: [],
                SecurityImpact: m.SecurityImpact,
                PrivacyImpact: m.PrivacyImpact,
                ExistingControls: null,
                ControlGaps: m.ControlGaps,
                Confidence: "low",
                EvidenceStrength: "inferred",
                FindingType: "conditional",
                Mitigations: mitigations,
                FrameworkMappings: [],
                SourceMethods: ["adversarial_review"],
                RiskRating: riskRating,
                EvidenceBasis: m.EvidenceBasis));
            idx++;
        }

        if (newConditional.Count == 0) return output;

        logger.LogInformation("SYNTHESIZE: adversarial review added {Count} conditional threat(s).", newConditional.Count);

        return output with
        {
            ConditionalThreats = output.ConditionalThreats.Concat(newConditional).ToArray()
        };
    }

    private static string BuildCoverageGapSummary(List<string> gaps)
    {
        var lines = new List<string>
        {
            "The following attack-vector group keys had direct architecture evidence in the candidate pool",
            "but produced NO confirmed threat (likely merged away). Prioritize finding missed threats in these areas:"
        };
        foreach (var key in gaps)
        {
            var def = GroupKeyRegistry.All.FirstOrDefault(d =>
                string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
            lines.Add(def is not null
                ? $"- {key}: {def.Description}"
                : $"- {key}");
        }
        return string.Join("\n", lines);
    }

    // Computes severity-weighted hotspots: elements flagged by ≥2 methods, ordered by weighted score.
    // Weight: critical=3, high=2, medium=1, low/unknown=0. Score reflects true risk concentration,
    // not just method count, so synthesis prioritizes genuinely dangerous elements.
    private static string? ComputeHotspots(ThreatCandidateSet[] sets)
    {
        var byElement = new Dictionary<string, (HashSet<string> Methods, int Score)>(StringComparer.OrdinalIgnoreCase);

        foreach (var set in sets)
        {
            foreach (var candidate in set.Candidates)
            {
                var weight = candidate.RiskRating?.Severity?.ToLowerInvariant() switch
                {
                    "critical" => 3,
                    "high"     => 2,
                    "medium"   => 1,
                    _          => 0
                };

                foreach (var label in candidate.AffectedElementLabels)
                {
                    if (!byElement.TryGetValue(label, out var entry))
                        entry = (new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);

                    entry.Methods.Add(set.Method);
                    byElement[label] = (entry.Methods, entry.Score + weight);
                }
            }
        }

        var hotspots = byElement
            .Where(kv => kv.Value.Methods.Count >= 2)
            .OrderByDescending(kv => kv.Value.Score)
            .ThenByDescending(kv => kv.Value.Methods.Count)
            .ToArray();

        if (hotspots.Length == 0) return null;

        return string.Join("\n", hotspots.Select(kv =>
            $"- {kv.Key}: flagged by {kv.Value.Methods.Count} methods, " +
            $"severity score {kv.Value.Score} ({string.Join(", ", kv.Value.Methods.OrderBy(m => m))})"));
    }

    private static CanonicalModel TruncateArchDesc(CanonicalModel model, int maxChars)
    {
        var desc = model.ArchitectureDescription;
        return desc is not null && desc.Length > maxChars
            ? model with { ArchitectureDescription = desc[..maxChars] + " [truncated]" }
            : model;
    }

    private FinalOutput EnforcePartialStatus(FinalOutput output, CanonicalModel model)
    {
        var criticalGaps = model.Gaps
            .Where(g => string.Equals(g.SecurityRelevance, "critical", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (criticalGaps.Length > 0)
        {
            var areas = string.Join(", ", criticalGaps.Select(g => g.Area));
            if (output.AnalysisStatus != "partial")
            {
                logger.LogInformation(
                    "SYNTHESIZE: forcing partial status due to {Count} unresolved critical gap(s): {Areas}",
                    criticalGaps.Length, areas);

                return output with
                {
                    AnalysisStatus = "partial",
                    PartialReason = output.PartialReason
                        ?? "One or more critical architectural gaps were unresolved before analysis."
                };
            }

            logger.LogInformation(
                "SYNTHESIZE: status already partial (LLM-declared). Critical gap(s) also present: {Areas}", areas);
        }

        return output;
    }

    private static int SeverityOrder(string? severity) => severity?.ToLowerInvariant() switch
    {
        "critical" => 4,
        "high"     => 3,
        "medium"   => 2,
        "low"      => 1,
        _          => 0
    };

    private static int ConfidenceOrder(string? confidence) => confidence?.ToLowerInvariant() switch
    {
        "high"   => 2,
        "medium" => 1,
        _        => 0
    };

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

    // Extracts "prompt-version: X" strings from all known prompt templates.
    private static Dictionary<string, string> ExtractPromptVersions()
    {
        static string Extract(string promptText)
        {
            var m = Regex.Match(promptText, @"prompt-version:\s*(.+)");
            return m.Success ? m.Groups[1].Value.Trim() : "unknown";
        }

        return new Dictionary<string, string>
        {
            ["parse"]            = Extract(PromptTemplates.ParseSystem),
            ["normalize"]        = Extract(PromptTemplates.NormalizeSystem),
            ["normalizeEnrich"]  = Extract(PromptTemplates.NormalizeEnrichSystem),
            ["classify"]         = Extract(PromptTemplates.ClassifySystem),
            ["analyze"]          = Extract(PromptTemplates.BuildAnalyzeSystem("stride")),
            ["synthesize"]       = Extract(PromptTemplates.SynthesizeSystem),
            ["review"]           = Extract(PromptTemplates.ReviewSystem),
            ["frameworkMapping"] = Extract(PromptTemplates.FrameworkMappingSystem),
        };
    }
}

/// <summary>
/// Controls token budget for the SYNTHESIZE stage.
/// Registered via Configure&lt;SynthesisOptions&gt; and bound to "Synthesis" config section.
///
/// When switching to a large-context model (claude-sonnet-4-6, gpt-4.1, gemini-2.5-pro),
/// increase TokenCeiling to match the model's practical per-request limit and increase
/// MaxOutputTokens for richer synthesis output. The input budget is TokenCeiling − MaxOutputTokens.
/// </summary>
public sealed class SynthesisOptions
{
    /// <summary>
    /// Total tokens (input + output) allowed per synthesis request.
    /// Default: 30,000 — matches OpenAI gpt-4o tier-1 TPM ceiling.
    /// For claude-sonnet-4-6 or high-tier models, set to 100,000–200,000.
    /// </summary>
    public int TokenCeiling { get; init; } = 30_000;

    /// <summary>
    /// Maximum tokens reserved for synthesis output (max_tokens on the LLM request).
    /// Default: 12,000 — sufficient for 15–20 threats with full mitigations.
    /// With a large-context model, 24,000–30,000 produces more detailed output.
    /// Set to 0 to omit the ceiling and let the model use its own default.
    /// </summary>
    public int MaxOutputTokens { get; init; } = 12_000;

    /// <summary>
    /// Maximum estimated input tokens for the framework-mapping sub-step.
    /// Sub-step is skipped (non-fatal) when the prompt exceeds this. Raise for models
    /// with larger context windows (GPT-5+).
    /// </summary>
    public int FrameworkMappingInputBudget { get; init; } = 8_192;

    /// <summary>
    /// max_completion_tokens for the framework-mapping sub-step.
    /// Reasoning models consume tokens internally before output; set higher than the
    /// expected output size to leave headroom for the reasoning phase.
    /// Set to 0 to omit the ceiling and let the model use its own default.
    /// </summary>
    public int FrameworkMappingMaxOutputTokens { get; init; } = 8_192;

    /// <summary>
    /// Maximum estimated input tokens for the adversarial review sub-step.
    /// Sub-step is skipped (non-fatal) when the prompt exceeds this.
    /// </summary>
    public int ReviewInputBudget { get; init; } = 20_000;

    /// <summary>
    /// max_completion_tokens for the adversarial review sub-step.
    /// Set to 0 to omit the ceiling and let the model use its own default.
    /// </summary>
    public int ReviewMaxOutputTokens { get; init; } = 16_000;

    /// <summary>
    /// When true (default), the adversarial review sub-step uses the strong model instead of the
    /// cheap model.  Security review requires judgment; the cheap model is reserved for pattern-matching
    /// tasks like framework mapping.  Set to false to reduce cost at the expense of review quality.
    /// </summary>
    public bool UseStrongModelForAdversarialReview { get; init; } = true;
}
