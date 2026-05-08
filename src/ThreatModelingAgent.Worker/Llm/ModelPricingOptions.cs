namespace ThreatModelingAgent.Worker.Llm;

/// <summary>
/// Per-model pricing for cost estimation. Bound to "ModelPricing" config section.
/// Values are USD per 1 million tokens. Update as provider pricing changes.
/// </summary>
public sealed class ModelPricingOptions
{
    public Dictionary<string, ModelPrice> Prices { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public decimal EstimateCostUsd(string model, long inputTokens, long outputTokens)
    {
        if (!Prices.TryGetValue(model, out var price))
            return 0m;

        return (inputTokens * price.InputPerMToken + outputTokens * price.OutputPerMToken) / 1_000_000m;
    }

    public decimal EstimateTotalCostUsd(IReadOnlyDictionary<string, (long Input, long Output)> perModel)
    {
        var total = 0m;
        foreach (var (model, (input, output)) in perModel)
            total += EstimateCostUsd(model, input, output);
        return total;
    }
}

public sealed class ModelPrice
{
    public decimal InputPerMToken { get; init; }    // USD per 1M input tokens
    public decimal OutputPerMToken { get; init; }   // USD per 1M output tokens
}
