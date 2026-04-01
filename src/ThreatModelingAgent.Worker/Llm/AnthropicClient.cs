using System.Text;
using System.Text.Json;

namespace ThreatModelingAgent.Worker.Llm;

/// <summary>
/// Anthropic Claude client. API key sourced from Key Vault — never hardcoded.
/// Explicit HTTP timeouts applied (CLAUDE.md §9.8).
/// Token counts logged; prompt/response content NEVER logged (CLAUDE.md §16.6).
/// </summary>
public sealed class AnthropicClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<AnthropicClient> logger) : ILlmClient
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var apiKey = configuration["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException("Anthropic:ApiKey is required.");

        var payload = new
        {
            model = request.Model,
            max_tokens = request.MaxTokens,
            temperature = request.Temperature,
            system = request.SystemPrompt,
            messages = new[] { new { role = "user", content = request.UserPrompt } }
        };

        using var client = httpClientFactory.CreateClient("Anthropic");
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(ApiUrl, content, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var text = root
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;

        var usage = root.GetProperty("usage");
        var inputTokens = usage.GetProperty("input_tokens").GetInt32();
        var outputTokens = usage.GetProperty("output_tokens").GetInt32();

        // Log token counts only — no content (CLAUDE.md §16.6)
        logger.LogInformation(
            "LLM call complete. Model={Model} InputTokens={InputTokens} OutputTokens={OutputTokens}",
            request.Model, inputTokens, outputTokens);

        return new LlmResponse(text, inputTokens, outputTokens, request.Model);
    }
}
