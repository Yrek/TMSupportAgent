namespace ThreatModelingAgent.Worker.Pipeline;

/// <summary>
/// Conservative token count estimator for pre-flight budget checks.
///
/// Uses the ~4 characters-per-token heuristic, which is accurate enough for the
/// rough budget enforcement required by spec §7. A 10% safety margin is applied so
/// we reject slightly below the hard limit rather than at it.
///
/// IMPORTANT: This is not a tiktoken-exact count. Its purpose is to prevent
/// obviously oversized inputs from reaching the LLM. Do NOT use it for billing.
///
/// Spec reference: 05-llm-workflow §6, §7 — INPUT_TOO_LARGE error code.
/// </summary>
internal static class TokenEstimator
{
    private const double CharsPerToken = 4.0;
    private const double SafetyMargin = 0.9;  // treat 90% of the limit as the effective ceiling

    /// <summary>
    /// Estimates the token count for a string of text.
    /// </summary>
    public static int Estimate(string text)
        => (int)Math.Ceiling(text.Length / CharsPerToken);

    /// <summary>
    /// Estimates combined prompt token count (system + user messages).
    /// </summary>
    public static int EstimatePrompt(string systemPrompt, string userPrompt)
        => Estimate(systemPrompt) + Estimate(userPrompt);

    /// <summary>
    /// Throws a PipelineStageException with INPUT_TOO_LARGE if the estimated token count
    /// exceeds the stage's configured budget.
    /// </summary>
    public static void AssertWithinBudget(string systemPrompt, string userPrompt, int maxInputTokens, string stageName)
    {
        var estimated = EstimatePrompt(systemPrompt, userPrompt);
        var effectiveLimit = (int)(maxInputTokens * SafetyMargin);

        if (estimated > effectiveLimit)
            throw new Stages.PipelineStageException(
                "INPUT_TOO_LARGE",
                $"Stage {stageName} estimated input tokens ({estimated}) exceeds budget ({effectiveLimit}). " +
                "Truncation is not permitted — job must fail. Spec: 05-llm-workflow §7.");
    }
}
