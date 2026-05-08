namespace ThreatModelingAgent.Worker.Llm;

/// <summary>
/// Abstraction over LLM providers (Azure OpenAI, Anthropic).
/// All LLM output is returned as raw text for schema validation by the caller —
/// callers MUST validate before using output (CLAUDE.md §16.5, 05-llm-workflow §2).
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Sends a prompt and returns the raw model response text.
    /// Temperature, max tokens, and model are specified per-call.
    ///
    /// IMPORTANT: The caller is responsible for:
    /// - Validating the response against the expected schema before use
    /// - Never using the response as SQL, shell commands, file paths, or policy input
    /// - Logging only token counts, never prompt content or response content
    /// </summary>
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct);
}

public sealed record LlmRequest(
    string SystemPrompt,
    string UserPrompt,
    string Model,
    float Temperature = 0f,
    int? MaxTokens = null,
    // Vision support — image/jpeg or image/png; null for text-only requests
    string? ImageBase64 = null,
    string? ImageMediaType = null);

public sealed record LlmResponse(
    string Content,
    int InputTokens,
    int OutputTokens,
    string Model);
