namespace ThreatModelingAgent.Worker.Llm;

/// <summary>
/// Selects the appropriate LLM client for a given model name.
/// Model routing rules are defined in 05-llm-workflow §4 and architecture §8.2.
/// </summary>
public sealed class LlmClientFactory(
    IEnumerable<ILlmClient> clients,
    IConfiguration configuration)
{
    // Strong models for security-critical reasoning (architecture §8.2)
    public static readonly HashSet<string> StrongModels =
        ["gpt-4o", "claude-sonnet-4-6"];

    // Low-cost models for classification, formatting, deduplication
    public static readonly HashSet<string> LowCostModels =
        ["gpt-4o-mini", "claude-haiku-4-5"];

    public ILlmClient GetForModel(string model)
    {
        // Route to Azure OpenAI for gpt-* models, Anthropic for claude-* models
        if (model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase))
            return clients.OfType<AzureOpenAiClient>().First();

        if (model.StartsWith("claude-", StringComparison.OrdinalIgnoreCase))
            return clients.OfType<AnthropicClient>().First();

        throw new InvalidOperationException($"No LLM client configured for model: {model}");
    }

    /// <summary>
    /// Selects the strong model based on configuration preference.
    /// Defaults to gpt-4o if not configured.
    /// </summary>
    public string GetStrongModel()
        => configuration["LlmRouting:StrongModel"] ?? "gpt-4o";

    /// <summary>
    /// Selects the low-cost model based on configuration preference.
    /// Defaults to gpt-4o-mini if not configured.
    /// </summary>
    public string GetLowCostModel()
        => configuration["LlmRouting:LowCostModel"] ?? "gpt-4o-mini";
}
