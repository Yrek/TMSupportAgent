namespace ThreatModelingAgent.Worker.Llm;

/// <summary>
/// Selects the appropriate LLM client for a given model name.
/// Model routing rules are defined in 05-llm-workflow §4 and architecture §8.2.
/// </summary>
public sealed class LlmClientFactory(
    IEnumerable<ILlmClient> clients,
    IConfiguration configuration) : ILlmClientFactory
{
    // Strong models for security-critical reasoning (architecture §8.2)
    public static readonly HashSet<string> StrongModels =
        ["gpt-4o", "gpt-4.1", "claude-sonnet-4-6", "gemini-2.5-pro"];

    // Low-cost models for classification, formatting, deduplication
    // o4-mini is an OpenAI reasoning model — uses max_completion_tokens (no temperature)
    public static readonly HashSet<string> LowCostModels =
        ["gpt-4o-mini", "claude-haiku-4-5", "o4-mini", "gemini-2.0-flash"];

    public ILlmClient GetForModel(string model)
    {
        var isOpenAiModel =
            model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) ||
            (model.StartsWith("o", StringComparison.OrdinalIgnoreCase) && model.Length > 1 && char.IsDigit(model[1]));

        if (isOpenAiModel)
        {
            // Prefer plain OpenAI when an API key is configured; fall back to Azure OpenAI.
            if (!string.IsNullOrEmpty(configuration["OpenAI:ApiKey"]))
                return clients.OfType<OpenAiClient>().First();
            return clients.OfType<AzureOpenAiClient>().First();
        }

        if (model.StartsWith("claude-", StringComparison.OrdinalIgnoreCase))
            return clients.OfType<AnthropicClient>().First();

        if (model.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
            return clients.OfType<GeminiClient>().First();

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
