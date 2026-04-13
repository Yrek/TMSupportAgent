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
    public void NormalizeSystem_VersionIs_normalize_1_0_0()
    {
        PromptTemplates.NormalizeSystem.Should().Contain("prompt-version: normalize-1.0.0");
    }

    [Fact]
    public void ClassifySystem_ContainsVersionString()
    {
        PromptTemplates.ClassifySystem.Should().Contain(VersionPrefix,
            because: "CLASSIFY system prompt must carry a prompt-version");
    }

    [Fact]
    public void ClassifySystem_VersionIs_classify_1_0_0()
    {
        PromptTemplates.ClassifySystem.Should().Contain("prompt-version: classify-1.0.0");
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
        system.Should().Contain("prompt-version: analyze-1.0.0");
    }

    [Fact]
    public void SynthesizeSystem_ContainsVersionString()
    {
        PromptTemplates.SynthesizeSystem.Should().Contain(VersionPrefix,
            because: "SYNTHESIZE system prompt must carry a prompt-version");
    }

    [Fact]
    public void SynthesizeSystem_VersionIs_synthesize_1_0_0()
    {
        PromptTemplates.SynthesizeSystem.Should().Contain("prompt-version: synthesize-1.0.0");
    }

    [Fact]
    public void FrameworkMappingSystem_ContainsVersionString()
    {
        PromptTemplates.FrameworkMappingSystem.Should().Contain(VersionPrefix,
            because: "Framework mapping sub-step system prompt must carry a prompt-version");
    }

    [Fact]
    public void FrameworkMappingSystem_VersionIs_framework_mapping_1_0_0()
    {
        PromptTemplates.FrameworkMappingSystem.Should().Contain("prompt-version: framework-mapping-1.0.0");
    }

    // ── No secrets or credentials in any template ─────────────────────────────
    // CLAUDE.md §16.3: secrets MUST NOT appear in prompts.

    [Theory]
    [InlineData(nameof(PromptTemplates.ParseSystem))]
    [InlineData(nameof(PromptTemplates.NormalizeSystem))]
    [InlineData(nameof(PromptTemplates.ClassifySystem))]
    [InlineData(nameof(PromptTemplates.SynthesizeSystem))]
    [InlineData(nameof(PromptTemplates.FrameworkMappingSystem))]
    public void SystemPrompts_DoNotContainSecretPatterns(string templateName)
    {
        var template = templateName switch
        {
            nameof(PromptTemplates.ParseSystem)            => PromptTemplates.ParseSystem,
            nameof(PromptTemplates.NormalizeSystem)        => PromptTemplates.NormalizeSystem,
            nameof(PromptTemplates.ClassifySystem)         => PromptTemplates.ClassifySystem,
            nameof(PromptTemplates.SynthesizeSystem)       => PromptTemplates.SynthesizeSystem,
            nameof(PromptTemplates.FrameworkMappingSystem) => PromptTemplates.FrameworkMappingSystem,
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
}
