using System.Text.Json;
using System.Diagnostics;
using ThreatModelingAgent.Worker.Llm;
using ThreatModelingAgent.Worker.Pipeline.Contracts;
using ThreatModelingAgent.Worker.Pipeline.Prompts;

namespace ThreatModelingAgent.Worker.Pipeline.Stages;

/// <summary>
/// Stage 5 — ANALYZE.
///
/// Runs one LLM sub-stage per selected threat modeling method. Sub-stages run
/// in parallel where resources allow (spec §5), capped by Task.WhenAll concurrency.
///
/// Model selection per method (spec §5.1):
///   Security-critical methods (stride, tenant_isolation, identity_session_delegation,
///   ai_llm_threat, linddun) → strong model
///   Pattern-driven methods (abuse_case, supply_chain, availability_resilience) → low-cost model
///
/// Post-LLM deterministic validation:
///   - All affectedElementLabels MUST exist in the canonical model
///   - Threats referencing unknown elements are moved to rejectedCandidates (spec §5.1 Validation)
///
/// Retry: up to 3 attempts per method on schema validation failure.
/// Fails method with ANALYZE_FAILED after max retries.
/// </summary>
public sealed class AnalyzeStage(
    ILlmClientFactory llmFactory,
    ILogger<AnalyzeStage> logger) : IPipelineStage<AnalyzeInput, ThreatCandidateSet>
{
    public const string SecurityExpertBaselineMethod = "security_expert_baseline";
    private const int MaxAttempts = 3;

    private static readonly HashSet<string> SecurityCriticalMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "stride",
        "tenant_isolation",
        "identity_session_delegation",
        "ai_llm_threat",
        "linddun",
        "maestro",
        "mitre_attack",
        "abuse_case",       // multi-step attacker reasoning requires strong model
        "owasp_cumulus",    // cloud trust-boundary analysis requires strong model
        "owasp_cornucopia", // user-selected by reviewer — treat as security-critical
        SecurityExpertBaselineMethod
    };

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<ThreatCandidateSet> ExecuteAsync(AnalyzeInput input, CancellationToken ct)
    {
        var model = SecurityCriticalMethods.Contains(input.Method)
            ? llmFactory.GetStrongModel()
            : llmFactory.GetLowCostModel();

        var llmClient = llmFactory.GetForModel(model);

        // Null out ArchitectureDescription in the JSON copy — it is sent once via [SYSTEM_CONTEXT]
        // to avoid double-counting tokens. Limit raised to 12,000 chars as a result.
        const int MaxArchDescChars = 12_000;
        var modelForPrompt = TruncateArchDesc(input.CanonicalModel, MaxArchDescChars);
        var modelForJson = modelForPrompt with { ArchitectureDescription = null };

        var canonicalJson = JsonSerializer.Serialize(modelForJson, SerializeOptions);
        var classificationJson = JsonSerializer.Serialize(input.ClassificationResult, SerializeOptions);
        var authGapSummary = ComputeAuthGapSummary(modelForPrompt);
        var userPrompt = PromptTemplates.BuildAnalyzeUser(
            canonicalJson, classificationJson,
            modelForPrompt.ApplicationDescription,
            modelForPrompt.ArchitectureDescription,
            modelForPrompt.CorrectionsContext,
            authGapSummary);

        var systemPrompt = PromptTemplates.BuildAnalyzeSystem(input.Method);

        // Token budget: 12,288 input per method (spec §7) — fail closed rather than truncate
        TokenEstimator.AssertWithinBudget(systemPrompt, userPrompt, 12_288, $"ANALYZE:{input.Method}");

        var request = new LlmRequest(
            SystemPrompt: systemPrompt,
            UserPrompt: userPrompt,
            Model: model,
            Temperature: 0.3f,
            MaxTokens: 8192);

        var (output, inputTokens, outputTokens) = await StageRetryHelper.ExecuteWithRetryAsync<ThreatCandidateSet>(
            llmClient, request, Validate, "ANALYZE_FAILED", MaxAttempts, logger, ct);

        // Deterministic traceability check — move unmatched threats to rejected (spec §5.1 Validation point 2)
        output = EnforceTraceability(output, input.CanonicalModel);

        logger.LogInformation(
            "ANALYZE complete. Method={Method} Model={Model} Threats={ThreatCount} Rejected={RejectedCount} " +
            "InputTokens={InputTokens} OutputTokens={OutputTokens}",
            input.Method, model, output.Candidates.Length, output.RejectedCandidates.Length,
            inputTokens, outputTokens);

        return output;
    }

    /// <summary>
    /// Runs all selected methods in parallel and returns all candidate sets.
    /// </summary>
    public async Task<ThreatCandidateSet[]> RunAllMethodsAsync(
        CanonicalModel canonicalModel,
        ClassificationResult classification,
        CancellationToken ct)
    {
        var methodsToRun = classification.SelectedMethods
            .Select(m => m.Method)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Always run a baseline expert-security pass, regardless of user-selected methods.
        // Selected methods are additive targeted lenses.
        if (!methodsToRun.Contains(SecurityExpertBaselineMethod, StringComparer.OrdinalIgnoreCase))
            methodsToRun.Insert(0, SecurityExpertBaselineMethod);

        logger.LogInformation(
            "ANALYZE starting. MethodCount={MethodCount} Methods={Methods}",
            methodsToRun.Count,
            string.Join(",", methodsToRun));

        var tasks = methodsToRun
            .Select(method => ExecuteWithProgressAsync(
                new AnalyzeInput(method, canonicalModel, classification), ct))
            .ToArray();

        return await Task.WhenAll(tasks);
    }

    private async Task<ThreatCandidateSet> ExecuteWithProgressAsync(AnalyzeInput input, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ANALYZE method started. Method={Method}", input.Method);
        var result = await ExecuteAsync(input, ct);
        logger.LogInformation(
            "ANALYZE method completed. Method={Method} Threats={Threats} Rejected={Rejected} ElapsedMs={ElapsedMs}",
            input.Method,
            result.Candidates.Length,
            result.RejectedCandidates.Length,
            sw.ElapsedMilliseconds);
        return result;
    }

    private static ThreatCandidateSet EnforceTraceability(ThreatCandidateSet set, CanonicalModel model)
    {
        // Build the set of all known element labels from the canonical model
        var knownLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in model.Components)      knownLabels.Add(c.Label);
        foreach (var a in model.Actors)          knownLabels.Add(a.Label);
        foreach (var e in model.ExternalSystems) knownLabels.Add(e.Label);
        foreach (var d in model.DataStores)      knownLabels.Add(d.Label);
        foreach (var b in model.TrustBoundaries) knownLabels.Add(b.Label);

        var valid   = new List<ThreatCandidate>();
        var invalid = new List<RejectedCandidate>(set.RejectedCandidates);

        foreach (var threat in set.Candidates)
        {
            var unknownLabels = threat.AffectedElementLabels
                .Where(l => !knownLabels.Contains(l))
                .ToArray();

            if (unknownLabels.Length == 0)
            {
                valid.Add(threat);
            }
            else
            {
                invalid.Add(new RejectedCandidate(
                    Title: threat.Title,
                    RejectionReason: "out_of_scope",
                    RejectionNote: $"AffectedElementLabels not found in canonical model: {string.Join(", ", unknownLabels)}"));
            }
        }

        return set with
        {
            Candidates = [.. valid],
            RejectedCandidates = [.. invalid]
        };
    }

    // Deterministically surfaces auth/authz gaps before the LLM runs, so every method's prompt
    // explicitly sees unauthenticated sensitive flows and missing access control declarations.
    private static CanonicalModel TruncateArchDesc(CanonicalModel model, int maxChars)
    {
        var desc = model.ArchitectureDescription;
        return desc is not null && desc.Length > maxChars
            ? model with { ArchitectureDescription = desc[..maxChars] + " [truncated]" }
            : model;
    }

    private static string? ComputeAuthGapSummary(CanonicalModel model)
    {
        var gaps = new List<string>();

        var unauthSensitiveFlows = model.DataFlows
            .Where(f => f.ContainsSensitiveData && !f.Authenticated)
            .Select(f => f.Label ?? $"{f.From}→{f.To}")
            .ToArray();
        if (unauthSensitiveFlows.Length > 0)
            gaps.Add($"Unauthenticated flows carrying sensitive data: {string.Join(", ", unauthSensitiveFlows)}");

        if (model.AuthenticationMethods.Length == 0)
            gaps.Add("No authentication methods declared in the model");

        if (model.AuthorizationModel is null or "none" or "unknown")
            gaps.Add($"Authorization model is '{model.AuthorizationModel ?? "not set"}' — access controls may be absent");

        var untrustedExternal = model.ExternalSystems
            .Where(e => e.TrustLevel is null or "unknown")
            .Select(e => e.Label)
            .ToArray();
        if (untrustedExternal.Length > 0)
            gaps.Add($"External systems with unknown trust level: {string.Join(", ", untrustedExternal)}");

        if (model.TrustBoundaries.Length == 0)
            gaps.Add("No trust boundaries defined — boundary-crossing threats may be underspecified");

        if (gaps.Count == 0) return null;
        return string.Join("\n", gaps.Select(g => $"- {g}"));
    }

    private static string? Validate(ThreatCandidateSet o)
    {
        if (string.IsNullOrWhiteSpace(o.Method)) return "method is missing";
        if (o.Candidates is null)                return "candidates is null";
        if (o.RejectedCandidates is null)        return "rejectedCandidates is null";

        foreach (var c in o.Candidates)
        {
            if (string.IsNullOrWhiteSpace(c.Title))       return "candidate missing title";
            if (c.AffectedElementLabels is null or [])    return "candidate missing affectedElementLabels";
            if (string.IsNullOrWhiteSpace(c.FindingType)) return "candidate missing findingType";
        }

        return null;
    }
}
