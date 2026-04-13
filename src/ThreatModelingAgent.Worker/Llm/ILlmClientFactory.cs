namespace ThreatModelingAgent.Worker.Llm;

/// <summary>
/// Abstraction over LlmClientFactory — decouples pipeline stages from the concrete factory
/// so they can be unit-tested with a mock factory (CLAUDE.md §15, testability requirement).
/// </summary>
public interface ILlmClientFactory
{
    /// <summary>Returns the configured strong model name (e.g. gpt-4o or claude-sonnet-4-6).</summary>
    string GetStrongModel();

    /// <summary>Returns the configured low-cost model name (e.g. gpt-4o-mini or claude-haiku-4-5).</summary>
    string GetLowCostModel();

    /// <summary>Returns the <see cref="ILlmClient"/> that handles the given model name.</summary>
    ILlmClient GetForModel(string model);
}
