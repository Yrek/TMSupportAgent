namespace ThreatModelingAgent.Worker.Pipeline;

/// <summary>
/// Contract for each pipeline stage. Each stage is a pure function:
/// typed input → typed output. No shared mutable state between stages.
/// Stages communicate only through their declared inputs and outputs (05-llm-workflow §4).
/// </summary>
public interface IPipelineStage<TInput, TOutput>
{
    Task<TOutput> ExecuteAsync(TInput input, CancellationToken ct);
}
