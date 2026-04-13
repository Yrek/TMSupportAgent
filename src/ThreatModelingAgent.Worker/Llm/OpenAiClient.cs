using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThreatModelingAgent.Worker.Llm;

/// <summary>
/// Plain OpenAI client (api.openai.com). API key sourced from configuration — never hardcoded.
/// Outbound HTTP uses explicit timeouts (CLAUDE.md §9.8).
/// Token counts are logged; prompt/response content is NEVER logged (CLAUDE.md §16.6).
/// Supports vision (multimodal) requests via optional ImageBase64 / ImageMediaType on LlmRequest.
/// </summary>
public sealed class OpenAiClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<OpenAiClient> logger) : ILlmClient
{
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var apiKey = configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey is required.");

        // Build the user message — text-only or multimodal (vision)
        object userContent = request.ImageBase64 is not null
            ? BuildVisionUserContent(request.UserPrompt, request.ImageBase64, request.ImageMediaType ?? "image/png")
            : (object)request.UserPrompt;

        // o-series models (o1, o3, o4-mini, etc.) use max_completion_tokens and do not support temperature
        var isOSeries = request.Model.Length > 1
            && request.Model[0] is 'o' or 'O'
            && char.IsDigit(request.Model[1]);

        object payload = isOSeries
            ? new
            {
                model = request.Model,
                messages = new object[]
                {
                    new { role = "system", content = request.SystemPrompt },
                    new { role = "user",   content = userContent }
                },
                max_completion_tokens = request.MaxTokens
            }
            : new
            {
                model = request.Model,
                messages = new object[]
                {
                    new { role = "system", content = request.SystemPrompt },
                    new { role = "user",   content = userContent }
                },
                max_tokens = request.MaxTokens,
                temperature = request.Temperature
            };

        using var client = httpClientFactory.CreateClient("OpenAI");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var json = JsonSerializer.Serialize(payload, SerializeOptions);
        using var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", httpContent, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var text = root
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        var usage = root.GetProperty("usage");
        var inputTokens = usage.GetProperty("prompt_tokens").GetInt32();
        var outputTokens = usage.GetProperty("completion_tokens").GetInt32();

        // Log token counts only — no content (CLAUDE.md §16.6)
        logger.LogInformation(
            "LLM call complete. Model={Model} InputTokens={InputTokens} OutputTokens={OutputTokens} Vision={IsVision}",
            request.Model, inputTokens, outputTokens, request.ImageBase64 is not null);

        return new LlmResponse(text, inputTokens, outputTokens, request.Model);
    }

    private static object[] BuildVisionUserContent(string text, string imageBase64, string mediaType)
        =>
        [
            new { type = "text",      text },
            new { type = "image_url", image_url = new { url = $"data:{mediaType};base64,{imageBase64}" } }
        ];
}
