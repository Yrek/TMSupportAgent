using FluentAssertions;

namespace ThreatModelingAgent.QualityTests;

/// <summary>
/// Offline benchmark suite. Tests are skipped when no result file is present.
///
/// To produce a result file for a benchmark:
///   1. Submit the content of Benchmarks/{id}/input.md to the API as a new job
///      (use applicationDescription field or as a text-based architecture input).
///   2. Wait for the job to reach Complete or Partial status.
///   3. Download the analysis JSON: GET /v1/orgs/{orgId}/jobs/{jobId}/export
///   4. Save the downloaded file to: tests/ThreatModelingAgent.QualityTests/Results/{id}.json
///   5. Run: dotnet test tests/ThreatModelingAgent.QualityTests
/// </summary>
public sealed class QualityTests
{
    private static readonly string ProjectDir = FindProjectDir();
    private static readonly string BenchmarksDir = Path.Combine(ProjectDir, "Benchmarks");
    private static readonly string ResultsDir = Path.Combine(ProjectDir, "Results");

    public static TheoryData<string> BenchmarkIds()
    {
        var data = new TheoryData<string>();
        if (Directory.Exists(BenchmarksDir))
            foreach (var dir in Directory.GetDirectories(BenchmarksDir))
                data.Add(Path.GetFileName(dir));
        return data;
    }

    [Theory]
    [MemberData(nameof(BenchmarkIds))]
    public void Benchmark_MeetsQualityThresholds(string benchmarkId)
    {
        var resultPath = Path.Combine(ResultsDir, $"{benchmarkId}.json");
        if (!File.Exists(resultPath))
        {
            // No result yet — see class-level doc comment for how to generate one.
            return;
        }

        var expectedPath = Path.Combine(BenchmarksDir, benchmarkId, "expected.yaml");
        var expected = BenchmarkExpected.Load(expectedPath);
        var output = BenchmarkRunner.LoadResult(resultPath);
        var score = BenchmarkRunner.Score(output, expected);

        // Print summary so it appears in test output regardless of pass/fail
        Console.WriteLine(score.Summary());

        score.PassesRecall.Should().BeTrue(
            $"must_find recall {score.MustFindRecall:P0} is below threshold " +
            $"{expected.Scoring.MinMustFindRecall:P0} for benchmark '{benchmarkId}'. " +
            $"Missing group keys: {string.Join(", ", expected.MustFindGroupKeys.Where(k => !score.MustNotClaimViolations.Contains(k)))}");

        score.PassesHallucination.Should().BeTrue(
            $"benchmark '{benchmarkId}' produced {score.MustNotClaimViolations.Count} must_not_claim violation(s): " +
            string.Join(", ", score.MustNotClaimViolations));
    }

    private static string FindProjectDir()
    {
        // Walk up from the test assembly location to find the project directory
        // (works both in bin/Debug/... and when running via dotnet test from repo root)
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "ThreatModelingAgent.QualityTests.csproj")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        // Fallback: assume we're in the project dir
        return AppContext.BaseDirectory;
    }
}
