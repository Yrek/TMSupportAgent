using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThreatModelingAgent.Worker.Llm;

/// <summary>
/// Azure OpenAI client. API key sourced from Key Vault at startup — never hardcoded.
/// Outbound HTTP uses explicit timeouts (CLAUDE.md §9.8).
/// Token counts are logged; prompt/response content is NEVER logged (CLAUDE.md §16.6).
/// Supports vision (multimodal) requests via optional ImageBase64 / ImageMediaType on LlmRequest.
/// </summary>
public sealed class AzureOpenAiClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<AzureOpenAiClient> logger) : ILlmClient
{
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var endpoint = configuration["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("AzureOpenAI:Endpoint is required.");
        var apiKey = configuration["AzureOpenAI:ApiKey"]
            ?? throw new InvalidOperationException("AzureOpenAI:ApiKey is required.");

        var url = $"{endpoint.TrimEnd('/')}/openai/deployments/{request.Model}/chat/completions?api-version=2024-02-01";

        // Build the user message — text-only or multimodal (vision)
        object userContent = request.ImageBase64 is not null
            ? BuildVisionUserContent(request.UserPrompt, request.ImageBase64, request.ImageMediaType ?? "image/png")
            : (object)request.UserPrompt;

        var payload = new
        {
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user",   content = userContent }
            },
            max_tokens = request.MaxTokens,
            temperature = request.Temperature
        };

        using var client = httpClientFactory.CreateClient("AzureOpenAI");
        client.DefaultRequestHeaders.Add("api-key", apiKey);

        var json = JsonSerializer.Serialize(payload, SerializeOptions);
        using var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(url, httpContent, ct);
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
