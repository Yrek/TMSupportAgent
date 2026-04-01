using System.Text.Json;
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
    LlmClientFactory llmFactory,
    ILogger<AnalyzeStage> logger) : IPipelineStage<AnalyzeInput, ThreatCandidateSet>
{
    private const int MaxAttempts = 3;

    private static readonly HashSet<string> SecurityCriticalMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "stride", "tenant_isolation", "identity_session_delegation", "ai_llm_threat", "linddun"
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

        var canonicalJson = JsonSerializer.Serialize(input.CanonicalModel, SerializeOptions);
        var classificationJson = JsonSerializer.Serialize(input.ClassificationResult, SerializeOptions);
        var userPrompt = PromptTemplates.BuildAnalyzeUser(canonicalJson, classificationJson);

        var request = new LlmRequest(
            SystemPrompt: PromptTemplates.BuildAnalyzeSystem(input.Method),
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
        var tasks = classification.SelectedMethods
            .Select(m => ExecuteAsync(
                new AnalyzeInput(m.Method, canonicalModel, classification), ct))
            .ToArray();

        return await Task.WhenAll(tasks);
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
