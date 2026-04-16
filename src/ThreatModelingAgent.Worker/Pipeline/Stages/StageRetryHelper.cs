using System.Text.Json;
using ThreatModelingAgent.Worker.Llm;

namespace ThreatModelingAgent.Worker.Pipeline.Stages;

/// <summary>
/// Shared retry helper for all LLM-backed pipeline stages.
///
/// On each attempt:
///   1. Call the LLM
///   2. Strip any markdown code fences (model may wrap JSON in ```json ... ```)
///   3. Deserialize into T using case-insensitive camelCase matching
///   4. Run the caller-supplied validator; if it throws, retry
///
/// After maxAttempts failures, throws PipelineStageException with the supplied errorCode.
/// Token usage is accumulated across attempts for audit logging (CLAUDE.md §16.6).
///
/// SECURITY: LLM output is NEVER used without deserialization into a typed record
/// with an additional validation step — never executed as code, SQL, file path, or
/// policy input (CLAUDE.md §16.5).
/// </summary>
public static class StageRetryHelper
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static async Task<(T Output, int TotalInputTokens, int TotalOutputTokens)> ExecuteWithRetryAsync<T>(
        ILlmClient llmClient,
        LlmRequest request,
        Func<T, string?> validator,   // returns null if valid, error message if invalid
        string stageErrorCode,
        int maxAttempts,
        ILogger logger,
        CancellationToken ct)
    {
        int totalInput = 0, totalOutput = 0;
        Exception? lastException = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            LlmResponse? llmResponse = null;
            try
            {
                llmResponse = await llmClient.CompleteAsync(request, ct);
                totalInput += llmResponse.InputTokens;
                totalOutput += llmResponse.OutputTokens;

                var cleaned = StripMarkdownFences(llmResponse.Content);
                var parsed = JsonSerializer.Deserialize<T>(cleaned, DeserializeOptions)
                    ?? throw new PipelineStageException(stageErrorCode, "LLM returned null after deserialization.");

                var validationError = validator(parsed);
                if (validationError is null)
                    return (parsed, totalInput, totalOutput);

                logger.LogWarning(
                    "Stage output validation failed on attempt {Attempt}/{Max}. Error={Error} Stage={StageErrorCode}",
                    attempt, maxAttempts, validationError, stageErrorCode);
                lastException = new PipelineStageException(stageErrorCode, validationError);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(
                    "Stage JSON parse failed on attempt {Attempt}/{Max}. Stage={StageErrorCode}",
                    attempt, maxAttempts, stageErrorCode);
                lastException = new PipelineStageException(stageErrorCode, $"JSON parse error: {ex.Message}");
            }
            catch (HttpRequestException ex) when (IsRetryable(ex) && !IsQuotaExhausted(ex) && attempt < maxAttempts)
            {
                logger.LogWarning(
                    "LLM request transient error on attempt {Attempt}/{Max}. Stage={StageErrorCode}",
                    attempt, maxAttempts, stageErrorCode);
                lastException = ex;
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), ct);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(
                    "LLM request failed on attempt {Attempt}/{Max}. Stage={StageErrorCode} Status={StatusCode}",
                    attempt, maxAttempts, stageErrorCode, (int?)ex.StatusCode);

                // Non-retryable client errors (e.g., invalid API key, insufficient credits)
                // should fail immediately to avoid useless retries and queue churn.
                if (!IsRetryable(ex) || IsQuotaExhausted(ex))
                {
                    throw new PipelineStageException(
                        stageErrorCode,
                        $"LLM HTTP failure: {(int?)ex.StatusCode} {ex.Message}");
                }

                // Retryable error that has exhausted attempts.
                lastException = new PipelineStageException(
                    stageErrorCode,
                    $"LLM transient failure after retries: {(int?)ex.StatusCode} {ex.Message}");
            }
        }

        throw lastException ?? new PipelineStageException(stageErrorCode, "All retry attempts exhausted.");
    }

    private static string StripMarkdownFences(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[7..];
        else if (trimmed.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[3..];

        if (trimmed.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^3];

        return trimmed.Trim();
    }

    private static bool IsRetryable(HttpRequestException ex)
        // Retry transient infrastructure and rate-limit failures.
        => ex.StatusCode is System.Net.HttpStatusCode.InternalServerError
            or System.Net.HttpStatusCode.BadGateway
            or System.Net.HttpStatusCode.ServiceUnavailable
            or System.Net.HttpStatusCode.GatewayTimeout
            or System.Net.HttpStatusCode.RequestTimeout
            or System.Net.HttpStatusCode.TooManyRequests
            or null; // no status = connection-level error

    private static bool IsQuotaExhausted(HttpRequestException ex)
    {
        var msg = ex.Message ?? string.Empty;
        return msg.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("billing", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("credit balance is too low", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class PipelineStageException(string ErrorCode, string Detail)
    : Exception($"Pipeline stage failed: {ErrorCode} — {Detail}")
{
    public string ErrorCode { get; } = ErrorCode;
}
