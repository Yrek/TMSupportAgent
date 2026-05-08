namespace ThreatModelingAgent.Worker.Llm;

/// <summary>
/// Scoped service that accumulates LLM token usage across all calls in a single pipeline job.
/// One instance per DI scope (one per job message). Thread-safe for parallel ANALYZE stage.
/// </summary>
public sealed class TokenUsageTracker
{
    private readonly object _lock = new();
    private readonly Dictionary<string, (long Input, long Output)> _perModel =
        new(StringComparer.OrdinalIgnoreCase);

    public void Record(string model, int inputTokens, int outputTokens)
    {
        lock (_lock)
        {
            if (_perModel.TryGetValue(model, out var existing))
                _perModel[model] = (existing.Input + inputTokens, existing.Output + outputTokens);
            else
                _perModel[model] = (inputTokens, outputTokens);
        }
    }

    public IReadOnlyDictionary<string, (long Input, long Output)> PerModel
    {
        get
        {
            lock (_lock)
                return new Dictionary<string, (long Input, long Output)>(_perModel, StringComparer.OrdinalIgnoreCase);
        }
    }

    public long TotalInputTokens
    {
        get { lock (_lock) { return _perModel.Values.Sum(v => v.Input); } }
    }

    public long TotalOutputTokens
    {
        get { lock (_lock) { return _perModel.Values.Sum(v => v.Output); } }
    }
}
