using FluentAssertions;
using ThreatModelingAgent.Worker.Pipeline;

namespace ThreatModelingAgent.Worker.Tests.Pipeline;

/// <summary>
/// Tests for the shared FrameworkNormalizer used by SynthesizeStage and PipelineDbPersistence.
/// Ensures the allow-list is consistent and all expected aliases normalize correctly.
/// </summary>
public sealed class FrameworkNormalizerTests
{
    [Theory]
    [InlineData("owasp_top10",    "owasp_top10")]
    [InlineData("OWASP",          "owasp_top10")]
    [InlineData("owasp",          "owasp_top10")]
    [InlineData("owasp top10",    "owasp_top10")]
    [InlineData("owasp_top_10",   "owasp_top10")]
    [InlineData("owasp_api",            "owasp_api_top10")]
    [InlineData("owasp_api_top10",      "owasp_api_top10")]
    [InlineData("owasp_api_security",   "owasp_api_top10")]
    [InlineData("owasp_api_top_10",     "owasp_api_top10")]
    [InlineData("asvs",           "asvs")]
    [InlineData("cis",            "cis_controls")]
    [InlineData("cis_controls",   "cis_controls")]
    [InlineData("cis_benchmarks", "cis_controls")]
    [InlineData("ncsc",           "ncsc")]
    [InlineData("twelve_factor",  "twelve_factor")]
    [InlineData("12_factor",      "twelve_factor")]
    [InlineData("12factor",       "twelve_factor")]
    [InlineData("12-factor",      "twelve_factor")]
    public void Normalize_KnownAliases_ReturnCanonicalValue(string input, string expected)
    {
        FrameworkNormalizer.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("nist")]
    [InlineData("iso27001")]
    [InlineData("pci_dss")]
    [InlineData("unknown")]
    [InlineData("")]
    public void Normalize_UnknownFramework_ReturnsNull(string input)
    {
        FrameworkNormalizer.Normalize(input).Should().BeNull();
    }

    [Fact]
    public void Normalize_Null_ReturnsNull()
    {
        FrameworkNormalizer.Normalize(null).Should().BeNull();
    }

    [Fact]
    public void Normalize_WhitespaceOnly_ReturnsNull()
    {
        FrameworkNormalizer.Normalize("   ").Should().BeNull();
    }
}
