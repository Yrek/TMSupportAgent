using System.Text.Json;
using Microsoft.Extensions.Options;
using ThreatModelingAgent.Worker.Llm;
using ThreatModelingAgent.Worker.Pipeline.Contracts;
using ThreatModelingAgent.Worker.Pipeline.Prompts;

namespace ThreatModelingAgent.Worker.Pipeline.Stages;

/// <summary>
/// Stage 4 — CLASSIFY.
///
/// Classifies the confirmed architecture into categories and selects the appropriate
/// threat modeling methods. Uses a low-cost model (pattern-matching task).
///
/// Post-LLM deterministic enforcement: required methods per category are verified and
/// added if omitted (spec §4 Stage 4 Validation). Omissions are logged as quality signals
/// but do not fail the job.
///
/// Model: low-cost (gpt-4o-mini / claude-haiku-4-5).
/// Retry: up to 3 attempts on schema validation failure.
/// </summary>
public sealed class ClassifyStage(
    ILlmClientFactory llmFactory,
    ILogger<ClassifyStage> logger,
    IOptions<StageMaxOutputTokensOptions> stageTokenOpts,
    IOptions<ClassifyOptions> classifyOpts) : IPipelineStage<ClassifyInput, ClassificationResult>
{
    private const int MaxAttempts = 3;
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "stride",
        "linddun",
        "abuse_case",
        "tenant_isolation",
        "identity_session_delegation",
        "ai_llm_threat",
        "vast",
        "pasta",
        "octave",
        "trike",
        "mitre_attack",
        "owasp_cumulus",
        "owasp_cornucopia",
        "maestro",
        "supply_chain",
        "availability_resilience"
    };

    // Required methods per architecture category (spec §4 Stage 4)
    private static readonly Dictionary<string, string[]> RequiredMethods = new()
    {
        ["standard_web_app"]          = ["stride", "abuse_case"],
        ["api_centric"]               = ["stride", "abuse_case"],
        ["multi_tenant_saas"]         = ["stride", "abuse_case", "tenant_isolation"],
        ["identity_complex"]          = ["stride", "abuse_case", "identity_session_delegation"],
        ["privacy_heavy"]             = ["stride", "abuse_case", "linddun"],
        // llm_enabled: LLM used for text generation/RAG but not necessarily agentic.
        // ai_llm_threat covers prompt injection and model-output trust abuse.
        // maestro and mitre_attack are optional extras the classifier may add — not mandatory here.
        ["llm_enabled"]               = ["stride", "abuse_case", "ai_llm_threat"],
        // agentic_mcp_enabled: full agentic system with MCP tools and multi-step planning.
        // maestro (agent orchestration red-team) and mitre_attack (TTP coverage) are mandatory.
        ["agentic_mcp_enabled"]       = ["stride", "abuse_case", "ai_llm_threat", "maestro", "mitre_attack"],
        ["microservice_distributed"]  = ["stride", "abuse_case"],
        ["event_driven"]              = ["stride", "abuse_case"],
        ["integration_heavy"]         = ["stride", "abuse_case", "supply_chain"],
        ["cloud_native"]              = ["stride", "abuse_case", "supply_chain"],
    };

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<ClassificationResult> ExecuteAsync(ClassifyInput input, CancellationToken ct)
    {
        var model = llmFactory.GetLowCostModel();
        var llmClient = llmFactory.GetForModel(model);

        // ArchitectureDescription appears in both [SYSTEM_CONTEXT] and inside the serialized
        // [CANONICAL_MODEL] JSON — sending it twice wastes tokens. Null it out in the JSON copy
        // so it only travels via [SYSTEM_CONTEXT], allowing a higher limit without blowing the budget.
        // Also strip enrichment-only fields (PrivilegedPaths, Gaps, BackgroundJobs, Assumptions,
        // ClarificationQuestions) — classify only needs architecture structure to select methods.
        // This prevents large enrichment output from GPT-5-class models overflowing the budget.
        const int MaxArchDescChars = 4_000;
        var modelForPrompt = TruncateArchDesc(input.ConfirmedModel, MaxArchDescChars);
        var modelForJson = modelForPrompt with
        {
            ArchitectureDescription   = null,
            PrivilegedPaths           = [],
            Gaps                      = [],
            BackgroundJobs            = [],
            Assumptions               = [],
            ClarificationQuestions    = [],
        };

        var canonicalJson = JsonSerializer.Serialize(modelForJson, SerializeOptions);
        var correctionsJson = JsonSerializer.Serialize(input.UserCorrections, SerializeOptions);
        var userPrompt = PromptTemplates.BuildClassifyUser(
            canonicalJson, correctionsJson,
            modelForPrompt.ApplicationDescription,
            modelForPrompt.ArchitectureDescription,
            modelForPrompt.CorrectionsContext);

        // Token budget: raised to 30,000 to accommodate large-context models (GPT-5+).
        // The 8,192 original limit was calibrated for gpt-4o tier-1.
        TokenEstimator.AssertWithinBudget(PromptTemplates.ClassifySystem, userPrompt, 30_000, "CLASSIFY");

        var request = new LlmRequest(
            SystemPrompt: PromptTemplates.ClassifySystem,
            UserPrompt: userPrompt,
            Model: model,
            Temperature: 0f,
            MaxTokens: stageTokenOpts.Value.Classify.ToMaxTokens());

        var (output, inputTokens, outputTokens) = await StageRetryHelper.ExecuteWithRetryAsync<ClassificationResult>(
            llmClient, request, Validate, "CLASSIFY_FAILED", MaxAttempts, logger, ct);

        // Deterministic post-validation: enforce required methods, add user selections,
        // limit total count, then strip any methods the user explicitly rejected.
        // Rejection is applied last — user decision overrides spec requirements.
        output = EnforceRequiredMethods(output, model, input.UserRejectedMethods);
        output = EnforceUserSelectedMethods(output, input.UserSelectedMethods);
        output = LimitSelectedMethods(output, input.UserSelectedMethods);
        output = StripRejectedMethods(output, input.UserRejectedMethods);

        logger.LogInformation(
            "CLASSIFY complete. Categories={Categories} Methods={Methods} " +
            "InputTokens={InputTokens} OutputTokens={OutputTokens}",
            string.Join(",", output.Categories), output.SelectedMethods.Length,
            inputTokens, outputTokens);

        return output;
    }

    private ClassificationResult EnforceRequiredMethods(
        ClassificationResult result, string model, string[] userRejected)
    {
        var rejectedSet = userRejected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existing = result.SelectedMethods.Select(m => m.Method).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toAdd = new List<SelectedMethod>();

        foreach (var category in result.Categories)
        {
            if (!RequiredMethods.TryGetValue(category, out var required)) continue;

            foreach (var method in required)
            {
                if (existing.Contains(method)) continue;

                if (rejectedSet.Contains(method))
                {
                    logger.LogInformation(
                        "CLASSIFY: required method skipped — user explicitly rejected it. Category={Category} Method={Method}",
                        category, method);
                    continue;
                }

                logger.LogWarning(
                    "CLASSIFY: required method omitted by model — adding. Category={Category} Method={Method}",
                    category, method);

                toAdd.Add(new SelectedMethod(
                    Method: method,
                    Rationale: $"Required by specification for {category} architecture.",
                    RequiredBySpec: true,
                    Stages: ["analyze"]));

                existing.Add(method);
            }
        }

        if (toAdd.Count == 0) return result;

        return result with { SelectedMethods = [.. result.SelectedMethods, .. toAdd] };
    }

    private ClassificationResult StripRejectedMethods(
        ClassificationResult result, string[] userRejected)
    {
        if (userRejected.Length == 0) return result;

        var rejectedSet = userRejected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stripped = result.SelectedMethods.Where(m => !rejectedSet.Contains(m.Method)).ToArray();

        var dropped = result.SelectedMethods.Length - stripped.Length;
        if (dropped > 0)
            logger.LogInformation(
                "CLASSIFY: stripped {Dropped} method(s) per user rejection. Rejected={Rejected}",
                dropped, string.Join(", ", userRejected));

        return result with { SelectedMethods = stripped };
    }

    private ClassificationResult EnforceUserSelectedMethods(
        ClassificationResult result,
        IReadOnlyCollection<string> userSelectedMethods)
    {
        if (userSelectedMethods.Count == 0)
            return result;

        var existing = result.SelectedMethods.Select(m => m.Method).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additions = new List<SelectedMethod>();
        foreach (var method in userSelectedMethods)
        {
            if (!AllowedMethods.Contains(method))
                continue;

            if (existing.Contains(method))
                continue;

            additions.Add(new SelectedMethod(
                Method: method,
                Rationale: "Explicitly selected by reviewer at architecture confirmation.",
                RequiredBySpec: false,
                Stages: ["analyze"]));
            existing.Add(method);
        }

        return additions.Count == 0
            ? result
            : result with { SelectedMethods = [.. result.SelectedMethods, .. additions] };
    }

    private ClassificationResult LimitSelectedMethods(
        ClassificationResult result,
        IReadOnlyCollection<string> userSelectedMethods)
    {
        if (result.SelectedMethods.Length <= classifyOpts.Value.MaxSelectedMethods)
            return result;

        var userSelectedSet = userSelectedMethods.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var required = result.SelectedMethods
            .Where(m => m.RequiredBySpec || userSelectedSet.Contains(m.Method))
            .ToList();

        var optionalSlots = Math.Max(0, classifyOpts.Value.MaxSelectedMethods - required.Count);
        var optional = result.SelectedMethods
            .Where(m => !m.RequiredBySpec)
            .Take(optionalSlots)
            .ToList();

        var limited = required
            .Concat(optional)
            .DistinctBy(m => m.Method, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var limitedSet = limited.Select(m => m.Method).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dropped = result.SelectedMethods
            .Where(m => !limitedSet.Contains(m.Method))
            .Select(m => m.Method)
            .ToArray();

        logger.LogWarning(
            "CLASSIFY selected too many methods; limiting for runtime control. Selected={Selected} LimitedTo={Limited} RequiredKept={RequiredKept} Dropped={Dropped}",
            result.SelectedMethods.Length, limited.Length, required.Count, string.Join(", ", dropped));

        return result with { SelectedMethods = limited };
    }

    private static CanonicalModel TruncateArchDesc(CanonicalModel model, int maxChars)
    {
        var desc = model.ArchitectureDescription;
        return desc is not null && desc.Length > maxChars
            ? model with { ArchitectureDescription = desc[..maxChars] + " [truncated]" }
            : model;
    }

    private static string? Validate(ClassificationResult o)
    {
        if (o.Categories is null || o.Categories.Length == 0) return "categories is empty";
        if (o.SelectedMethods is null || o.SelectedMethods.Length == 0) return "selectedMethods is empty";
        if (o.ModelRoutingPlan is null) return "modelRoutingPlan is null";
        return null;
    }
}
