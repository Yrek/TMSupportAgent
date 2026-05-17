using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ThreatModelingAgent.QualityTests;

/// <summary>
/// Deserialised from expected.yaml in each benchmark folder.
/// Uses snake_case YAML keys mapped to PascalCase C# properties via UnderscoredNamingConvention.
/// </summary>
public sealed class BenchmarkExpected
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> MustFindGroupKeys { get; set; } = [];
    public List<string> MustNotClaimGroupKeys { get; set; } = [];
    public BenchmarkScoring Scoring { get; set; } = new();

    public static BenchmarkExpected Load(string yamlPath)
    {
        var yaml = File.ReadAllText(yamlPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<BenchmarkExpected>(yaml);
    }
}

public sealed class BenchmarkScoring
{
    public double MinMustFindRecall { get; set; } = 1.0;
    public int MaxMustNotClaimViolations { get; set; } = 0;
}
