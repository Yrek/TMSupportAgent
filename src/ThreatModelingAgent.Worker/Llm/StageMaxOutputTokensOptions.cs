namespace ThreatModelingAgent.Worker.Llm;

/// <summary>
/// Per-stage LLM output token ceilings. Reasoning models (gpt-5-mini, o4-mini) consume
/// tokens internally before generating output, so effective output budget is
/// max_completion_tokens minus reasoning tokens. Set these higher than the expected
/// output size to leave headroom for the reasoning phase.
/// Set any value to 0 (or negative) to omit the ceiling and let the model use its own default.
/// Bound to config section "StageMaxOutputTokens".
/// </summary>
public sealed class StageMaxOutputTokensOptions
{
    /// <summary>PARSE stage — raw element/flow JSON extracted from the diagram.</summary>
    public int Parse { get; init; } = 8_192;

    /// <summary>NORMALIZE stage (LLM path) — full canonical model JSON.</summary>
    public int Normalize { get; init; } = 16_000;

    /// <summary>NORMALIZE enrichment sub-step — security context fields only.</summary>
    public int NormalizeEnrich { get; init; } = 16_000;

    /// <summary>CLASSIFY stage — categories, selected methods, routing plan.</summary>
    public int Classify { get; init; } = 8_192;
}
