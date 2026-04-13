using System.Text.Json;
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
    ILogger<ClassifyStage> logger) : IPipelineStage<ClassifyInput, ClassificationResult>
{
    private const int MaxAttempts = 3;

    // Required methods per architecture category (spec §4 Stage 4)
    private static readonly Dictionary<string, string[]> RequiredMethods = new()
    {
        ["standard_web_app"]          = ["stride", "abuse_case"],
        ["api_centric"]               = ["stride", "abuse_case"],
        ["multi_tenant_saas"]         = ["stride", "abuse_case", "tenant_isolation"],
        ["identity_complex"]          = ["stride", "abuse_case", "identity_session_delegation"],
        ["privacy_heavy"]             = ["stride", "abuse_case", "linddun"],
        ["llm_enabled"]               = ["stride", "abuse_case", "ai_llm_threat"],
        ["agentic_mcp_enabled"]       = ["stride", "abuse_case", "ai_llm_threat"],
        ["microservice_distributed"]  = ["stride", "abuse_case"],
        ["event_driven"]              = ["stride", "abuse_case"],
        ["integration_heavy"]         = ["stride", "abuse_case", "supply_chain"],
        ["cloud_native"]              = ["stride", "abuse_case"],
    };

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<ClassificationResult> ExecuteAsync(ClassifyInput input, CancellationToken ct)
    {
        var model = llmFactory.GetLowCostModel();
        var llmClient = llmFactory.GetForModel(model);

        var canonicalJson = JsonSerializer.Serialize(input.ConfirmedModel, SerializeOptions);
        var correctionsJson = JsonSerializer.Serialize(input.UserCorrections, SerializeOptions);
        var userPrompt = PromptTemplates.BuildClassifyUser(canonicalJson, correctionsJson);

        // Token budget: 8,192 input (spec §7) — fail closed rather than truncate
        TokenEstimator.AssertWithinBudget(PromptTemplates.ClassifySystem, userPrompt, 8_192, "CLASSIFY");

        var request = new LlmRequest(
            SystemPrompt: PromptTemplates.ClassifySystem,
            UserPrompt: userPrompt,
            Model: model,
            Temperature: 0f,
            MaxTokens: 2048);

        var (output, inputTokens, outputTokens) = await StageRetryHelper.ExecuteWithRetryAsync<ClassificationResult>(
            llmClient, request, Validate, "CLASSIFY_FAILED", MaxAttempts, logger, ct);

        // Deterministic post-validation: enforce required methods
        output = EnforceRequiredMethods(output, model);

        logger.LogInformation(
            "CLASSIFY complete. Categories={Categories} Methods={Methods} " +
            "InputTokens={InputTokens} OutputTokens={OutputTokens}",
            string.Join(",", output.Categories), output.SelectedMethods.Length,
            inputTokens, outputTokens);

        return output;
    }

    private ClassificationResult EnforceRequiredMethods(ClassificationResult result, string model)
    {
        var existing = result.SelectedMethods.Select(m => m.Method).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toAdd = new List<SelectedMethod>();

        foreach (var category in result.Categories)
        {
            if (!RequiredMethods.TryGetValue(category, out var required)) continue;

            foreach (var method in required)
            {
                if (existing.Contains(method)) continue;

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

    private static string? Validate(ClassificationResult o)
    {
        if (o.Categories is null || o.Categories.Length == 0) return "categories is empty";
        if (o.SelectedMethods is null || o.SelectedMethods.Length == 0) return "selectedMethods is empty";
        if (o.ModelRoutingPlan is null) return "modelRoutingPlan is null";
        return null;
    }
}
