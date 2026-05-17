using System.Text.Json;
using ThreatModelingAgent.Worker.Pipeline.Contracts;

namespace ThreatModelingAgent.QualityTests;

public sealed record BenchmarkScore(
    string BenchmarkId,
    double MustFindRecall,
    int MustFindHits,
    int MustFindTotal,
    List<string> MustNotClaimViolations,
    bool PassesRecall,
    bool PassesHallucination)
{
    public bool Passes => PassesRecall && PassesHallucination;

    public string Summary()
    {
        var lines = new List<string>
        {
            $"Benchmark: {BenchmarkId}",
            $"  must_find recall:       {MustFindHits}/{MustFindTotal} = {MustFindRecall:P0}  [{(PassesRecall ? "PASS" : "FAIL")}]",
            $"  must_not_claim violations: {MustNotClaimViolations.Count}  [{(PassesHallucination ? "PASS" : "FAIL")}]"
        };
        if (MustNotClaimViolations.Count > 0)
            lines.Add($"  violations: {string.Join(", ", MustNotClaimViolations)}");
        return string.Join(Environment.NewLine, lines);
    }
}

public static class BenchmarkRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static FinalOutput LoadResult(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        return JsonSerializer.Deserialize<FinalOutput>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialise FinalOutput from {jsonPath}");
    }

    public static BenchmarkScore Score(FinalOutput output, BenchmarkExpected expected)
    {
        var allThreats = output.ConfirmedThreats.Concat(output.ConditionalThreats);

        var foundGroupKeys = allThreats
            .Where(t => t.GroupKey != null)
            .Select(t => t.GroupKey!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var mustFindHits = expected.MustFindGroupKeys
            .Count(k => foundGroupKeys.Contains(k));

        var recall = expected.MustFindGroupKeys.Count == 0
            ? 1.0
            : (double)mustFindHits / expected.MustFindGroupKeys.Count;

        var violations = expected.MustNotClaimGroupKeys
            .Where(k => foundGroupKeys.Contains(k))
            .ToList();

        return new BenchmarkScore(
            BenchmarkId: expected.Id,
            MustFindRecall: recall,
            MustFindHits: mustFindHits,
            MustFindTotal: expected.MustFindGroupKeys.Count,
            MustNotClaimViolations: violations,
            PassesRecall: recall >= expected.Scoring.MinMustFindRecall,
            PassesHallucination: violations.Count <= expected.Scoring.MaxMustNotClaimViolations);
    }
}
