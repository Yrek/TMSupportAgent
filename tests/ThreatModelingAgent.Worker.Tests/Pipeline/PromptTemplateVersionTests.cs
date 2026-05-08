using FluentAssertions;
using ThreatModelingAgent.Worker.Pipeline.Prompts;

namespace ThreatModelingAgent.Worker.Tests.Pipeline;

/// <summary>
/// Verifies that every system prompt template includes an embedded prompt-version string.
///
/// Spec requirement (05-llm-workflow §8):
///   Each template has a version string embedded in the system message:
///   "prompt-version: {stage}-{semver}"
///
/// This test acts as a regression guard — any prompt template change that removes
/// the version string will cause a CI failure, prompting the developer to bump the version.
/// Prompt version strings are used to detect when re-analysis results may differ from
/// previous runs (evaluation regression, CLAUDE.md §16.6).
/// </summary>
public sealed class PromptTemplateVersionTests
{
    private const string VersionPrefix = "prompt-version:";

    [Fact]
    public void ParseSystem_ContainsVersionString()
    {
        PromptTemplates.ParseSystem.Should().Contain(VersionPrefix,
            because: "PARSE system prompt must carry a prompt-version for regression detection");
    }

    [Fact]
    public void ParseSystem_VersionIs_parse_1_0_0()
    {
        PromptTemplates.ParseSystem.Should().Contain("prompt-version: parse-1.0.0");
    }

    [Fact]
    public void NormalizeSystem_ContainsVersionString()
    {
        PromptTemplates.NormalizeSystem.Should().Contain(VersionPrefix,
            because: "NORMALIZE system prompt must carry a prompt-version");
    }

    [Fact]
    public void NormalizeSystem_VersionIs_normalize_2_0_0()
    {
        PromptTemplates.NormalizeSystem.Should().Contain("prompt-version: normalize-2.0.0");
    }

    [Fact]
    public void NormalizeEnrichSystem_ContainsVersionString()
    {
        PromptTemplates.NormalizeEnrichSystem.Should().Contain(VersionPrefix,
            because: "NORMALIZE_ENRICH system prompt must carry a prompt-version");
    }

    [Fact]
    public void NormalizeEnrichSystem_VersionIs_normalize_enrich_4_0_0()
    {
        PromptTemplates.NormalizeEnrichSystem.Should().Contain("prompt-version: normalize-enrich-4.0.0");
    }

    [Fact]
    public void ClassifySystem_ContainsVersionString()
    {
        PromptTemplates.ClassifySystem.Should().Contain(VersionPrefix,
            because: "CLASSIFY system prompt must carry a prompt-version");
    }

    [Fact]
    public void ClassifySystem_VersionIs_classify_2_1_0()
    {
        PromptTemplates.ClassifySystem.Should().Contain("prompt-version: classify-2.1.0");
    }

    [Theory]
    [InlineData("STRIDE")]
    [InlineData("PASTA")]
    [InlineData("LINDDUN")]
    [InlineData("ATTACK_TREE")]
    public void BuildAnalyzeSystem_ContainsVersionString(string method)
    {
        var system = PromptTemplates.BuildAnalyzeSystem(method);
        system.Should().Contain(VersionPrefix,
            because: $"ANALYZE system prompt for method {method} must carry a prompt-version");
        system.Should().Contain("prompt-version: analyze-6.0.0");
    }

    [Fact]
    public void SynthesizeSystem_ContainsVersionString()
    {
        PromptTemplates.SynthesizeSystem.Should().Contain(VersionPrefix,
            because: "SYNTHESIZE system prompt must carry a prompt-version");
    }

    [Fact]
    public void SynthesizeSystem_VersionIs_synthesize_3_1_0()
    {
        PromptTemplates.SynthesizeSystem.Should().Contain("prompt-version: synthesize-3.1.0");
    }

    [Fact]
    public void FrameworkMappingSystem_ContainsVersionString()
    {
        PromptTemplates.FrameworkMappingSystem.Should().Contain(VersionPrefix,
            because: "Framework mapping sub-step system prompt must carry a prompt-version");
    }

    [Fact]
    public void FrameworkMappingSystem_VersionIs_framework_mapping_1_1_0()
    {
        PromptTemplates.FrameworkMappingSystem.Should().Contain("prompt-version: framework-mapping-1.1.0");
    }

    [Fact]
    public void ReviewSystem_ContainsVersionString()
    {
        PromptTemplates.ReviewSystem.Should().Contain(VersionPrefix,
            because: "Adversarial review sub-step system prompt must carry a prompt-version");
    }

    [Fact]
    public void ReviewSystem_VersionIs_review_1_0_0()
    {
        PromptTemplates.ReviewSystem.Should().Contain("prompt-version: review-1.0.0");
    }

    // ── No secrets or credentials in any template ─────────────────────────────
    // CLAUDE.md §16.3: secrets MUST NOT appear in prompts.

    [Theory]
    [InlineData(nameof(PromptTemplates.ParseSystem))]
    [InlineData(nameof(PromptTemplates.NormalizeSystem))]
    [InlineData(nameof(PromptTemplates.NormalizeEnrichSystem))]
    [InlineData(nameof(PromptTemplates.ClassifySystem))]
    [InlineData(nameof(PromptTemplates.SynthesizeSystem))]
    [InlineData(nameof(PromptTemplates.FrameworkMappingSystem))]
    [InlineData(nameof(PromptTemplates.ReviewSystem))]
    public void SystemPrompts_DoNotContainSecretPatterns(string templateName)
    {
        var template = templateName switch
        {
            nameof(PromptTemplates.ParseSystem)            => PromptTemplates.ParseSystem,
            nameof(PromptTemplates.NormalizeSystem)        => PromptTemplates.NormalizeSystem,
            nameof(PromptTemplates.NormalizeEnrichSystem)  => PromptTemplates.NormalizeEnrichSystem,
            nameof(PromptTemplates.ClassifySystem)         => PromptTemplates.ClassifySystem,
            nameof(PromptTemplates.SynthesizeSystem)       => PromptTemplates.SynthesizeSystem,
            nameof(PromptTemplates.FrameworkMappingSystem) => PromptTemplates.FrameworkMappingSystem,
            nameof(PromptTemplates.ReviewSystem)           => PromptTemplates.ReviewSystem,
            _ => throw new InvalidOperationException($"Unknown template: {templateName}")
        };

        template.Should().NotContainAny(
            ["sk-", "Bearer ", "api_key", "connection_string", "password="],
            because: "CLAUDE.md §16.3 — secrets must not appear in prompts");
    }

    // ── All system prompt builders include delimited data blocks ─────────────
    // CLAUDE.md §16.3: user-controlled content must be injected as delimited data

    [Fact]
    public void BuildNormalizeUser_WrapsContentInDelimiters()
    {
        var result = PromptTemplates.BuildNormalizeUser("{}", "plantuml");
        result.Should().Contain("[PARSED_ARCHITECTURE]")
            .And.Contain("[/PARSED_ARCHITECTURE]",
                because: "user-supplied architecture content must be delimited to prevent prompt injection");
    }

    [Fact]
    public void BuildClassifyUser_WrapsCorrectionsInDelimiters()
    {
        var result = PromptTemplates.BuildClassifyUser("{}", "[]");
        result.Should().Contain("[USER_CORRECTIONS]")
            .And.Contain("[/USER_CORRECTIONS]",
                because: "user corrections must be delimited to prevent prompt injection");
    }

    [Fact]
    public void BuildFrameworkMappingUser_WrapsThreatsInDelimiters()
    {
        var result = PromptTemplates.BuildFrameworkMappingUser("[]");
        result.Should().Contain("[THREATS]")
            .And.Contain("[/THREATS]",
                because: "threat data passed to framework mapping must be delimited");
    }

    [Fact]
    public void BuildReviewUser_WrapsContentInDelimiters()
    {
        var result = PromptTemplates.BuildReviewUser("{}", "[]");
        result.Should().Contain("[ARCHITECTURE]")
            .And.Contain("[/ARCHITECTURE]",
                because: "canonical model passed to adversarial review must be delimited")
            .And.Contain("[THREATS]")
            .And.Contain("[/THREATS]",
                because: "threat list passed to adversarial review must be delimited");
    }
}
