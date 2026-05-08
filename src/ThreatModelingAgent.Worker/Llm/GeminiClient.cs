using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThreatModelingAgent.Worker.Llm;

/// <summary>
/// Google Gemini client (generativelanguage.googleapis.com). API key sourced from
/// configuration — never hardcoded (CLAUDE.md §10.1).
/// Outbound HTTP uses explicit timeouts (CLAUDE.md §9.8).
/// Token counts are logged; prompt/response content is NEVER logged (CLAUDE.md §16.6).
/// Supports vision (multimodal) requests via optional ImageBase64 / ImageMediaType on LlmRequest.
/// </summary>
public sealed class GeminiClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<GeminiClient> logger,
    TokenUsageTracker tokenUsage) : ILlmClient
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var apiKey = configuration["Google:ApiKey"]
            ?? throw new InvalidOperationException("Google:ApiKey is required.");

        var url = $"{BaseUrl}/{Uri.EscapeDataString(request.Model)}:generateContent?key={apiKey}";

        // Build user parts — text-only or multimodal (vision)
        object[] userParts = request.ImageBase64 is not null
            ? BuildVisionParts(request.UserPrompt, request.ImageBase64, request.ImageMediaType ?? "image/png")
            : [new { text = request.UserPrompt }];

        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = request.SystemPrompt } } },
            contents = new[]
            {
                new { role = "user", parts = userParts }
            },
            generationConfig = new
            {
                maxOutputTokens = request.MaxTokens,   // null → omitted by WhenWritingNull → model uses its default
                temperature = (double)request.Temperature
            }
        };

        using var client = httpClientFactory.CreateClient("Google");

        var json = JsonSerializer.Serialize(payload, SerializeOptions);
        using var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(url, httpContent, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var text = root
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;

        var usage = root.GetProperty("usageMetadata");
        var inputTokens = usage.GetProperty("promptTokenCount").GetInt32();
        var outputTokens = usage.GetProperty("candidatesTokenCount").GetInt32();

        tokenUsage.Record(request.Model, inputTokens, outputTokens);

        // Log token counts only — no content (CLAUDE.md §16.6)
        logger.LogInformation(
            "LLM call complete. Model={Model} InputTokens={InputTokens} OutputTokens={OutputTokens} Vision={IsVision}",
            request.Model, inputTokens, outputTokens, request.ImageBase64 is not null);

        return new LlmResponse(text, inputTokens, outputTokens, request.Model);
    }

    private static object[] BuildVisionParts(string text, string imageBase64, string mediaType)
        =>
        [
            new { text },
            new { inlineData = new { mimeType = mediaType, data = imageBase64 } }
        ];
}
