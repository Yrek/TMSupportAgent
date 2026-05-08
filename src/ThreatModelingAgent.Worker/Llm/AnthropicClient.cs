using System.Text;
using System.Text.Json;
using System.Net.Http;

namespace ThreatModelingAgent.Worker.Llm;

/// <summary>
/// Anthropic Claude client. API key sourced from Key Vault — never hardcoded.
/// Explicit HTTP timeouts applied (CLAUDE.md §9.8).
/// Supports vision (multimodal) requests via optional ImageBase64 / ImageMediaType on LlmRequest.
/// Token counts logged; prompt/response content NEVER logged (CLAUDE.md §16.6).
/// </summary>
public sealed class AnthropicClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<AnthropicClient> logger,
    TokenUsageTracker tokenUsage) : ILlmClient
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var apiKey = configuration["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException("Anthropic:ApiKey is required.");

        // Build user message content — text-only or multimodal (vision)
        object userContent = request.ImageBase64 is not null
            ? BuildVisionUserContent(request.UserPrompt, request.ImageBase64, request.ImageMediaType ?? "image/png")
            : BuildTextUserContent(request.UserPrompt);

        var payload = new
        {
            model = request.Model,
            max_tokens = request.MaxTokens ?? 100_000,
            temperature = request.Temperature,
            system = request.SystemPrompt,
            messages = new[] { new { role = "user", content = userContent } }
        };

        using var client = httpClientFactory.CreateClient("Anthropic");
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(ApiUrl, content, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorSummary = TryExtractErrorSummary(responseJson);
            logger.LogWarning(
                "Anthropic request failed. Status={Status} Model={Model} Error={Error}",
                (int)response.StatusCode, request.Model, errorSummary);
            throw new HttpRequestException(
                $"Anthropic API {(int)response.StatusCode} for model '{request.Model}': {errorSummary}",
                null,
                response.StatusCode);
        }

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var text = root
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;

        var usage = root.GetProperty("usage");
        var inputTokens = usage.GetProperty("input_tokens").GetInt32();
        var outputTokens = usage.GetProperty("output_tokens").GetInt32();

        tokenUsage.Record(request.Model, inputTokens, outputTokens);

        // Log token counts only — no content (CLAUDE.md §16.6)
        logger.LogInformation(
            "LLM call complete. Model={Model} InputTokens={InputTokens} OutputTokens={OutputTokens} Vision={IsVision}",
            request.Model, inputTokens, outputTokens, request.ImageBase64 is not null);

        return new LlmResponse(text, inputTokens, outputTokens, request.Model);
    }

    /// <summary>
    /// Builds Anthropic's multimodal content block format for vision requests.
    /// Spec: https://docs.anthropic.com/en/api/messages (content block types).
    /// </summary>
    private static object[] BuildVisionUserContent(string text, string imageBase64, string mediaType)
        =>
        [
            new
            {
                type = "image",
                source = new
                {
                    type = "base64",
                    media_type = mediaType,
                    data = imageBase64
                }
            },
            new { type = "text", text }
        ];

    private static object[] BuildTextUserContent(string text)
        =>
        [
            new { type = "text", text }
        ];

    private static string TryExtractErrorSummary(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
            {
                var type = err.TryGetProperty("type", out var t) ? t.GetString() : null;
                var message = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                return $"{type ?? "unknown"}: {message ?? "no message"}";
            }
        }
        catch
        {
            // ignore parse failure and fall back to raw truncated payload
        }

        var compact = responseJson.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return compact.Length > 300 ? $"{compact[..300]}..." : compact;
    }
}
