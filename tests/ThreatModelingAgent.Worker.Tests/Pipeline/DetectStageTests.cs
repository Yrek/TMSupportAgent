using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.Messaging;
using ThreatModelingAgent.Worker.Pipeline.Stages;

namespace ThreatModelingAgent.Worker.Tests.Pipeline;

/// <summary>
/// Tests for DetectStage — the deterministic artifact-type detector (Stage 1).
/// All detection is pure logic; no LLM is involved.
///
/// Security note: DetectStage is the first gate in the pipeline. A wrong type
/// detection would send the wrong model (e.g. vision vs text) and could increase
/// cost or fail later stages with a misleading error code. These tests enforce
/// the detection priority order: magic bytes → content sniff → extension fallback.
/// </summary>
public sealed class DetectStageTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (DetectStage Stage, IBlobStorage BlobStorage) BuildStage()
    {
        var blobStorage = Substitute.For<IBlobStorage>();
        var logger = NullLogger<DetectStage>.Instance;
        return (new DetectStage(blobStorage, logger), blobStorage);
    }

    private static AnalysisJobMessage Msg(string path, string? artifactType = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), path, artifactType ?? string.Empty);

    private static void SetupBlob(IBlobStorage blobStorage, byte[] bytes) =>
        blobStorage.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(bytes)));

    private static void SetupBlob(IBlobStorage blobStorage, string text) =>
        SetupBlob(blobStorage, System.Text.Encoding.UTF8.GetBytes(text));

    // ── Pre-validated artifact type (short-circuit path) ─────────────────────

    [Theory]
    [InlineData("image")]
    [InlineData("plantuml")]
    [InlineData("mermaid")]
    [InlineData("drawio")]
    [InlineData("text")]
    public async Task PreValidatedType_DoesNotReadBlob_ReturnsType(string type)
    {
        var (stage, blob) = BuildStage();
        var msg = Msg("any/path.bin", type);

        var result = await stage.ExecuteAsync(msg, None);

        result.ArtifactType.Should().Be(type);
        result.DetectionMethod.Should().Be("extension");
        result.Confidence.Should().Be("high");
        await blob.DidNotReceive().DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreValidatedType_Unknown_FallsThroughToDetection()
    {
        // "pdf" is not in the supported set — should fall through to content detection
        var (stage, blob) = BuildStage();
        SetupBlob(blob, "@startuml\nactor User\n@enduml");
        var msg = Msg("path/diagram.pu", "pdf");

        var result = await stage.ExecuteAsync(msg, None);

        result.ArtifactType.Should().Be("plantuml");
        await blob.Received(1).DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Magic bytes — image detection ─────────────────────────────────────────

    [Fact]
    public async Task PngMagicBytes_DetectedAsImage()
    {
        var (stage, blob) = BuildStage();
        SetupBlob(blob, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var result = await stage.ExecuteAsync(Msg("upload/arch.png"), None);

        result.ArtifactType.Should().Be("image");
        result.DetectionMethod.Should().Be("magic_bytes");
        result.Confidence.Should().Be("high");
    }

    [Fact]
    public async Task JpegMagicBytes_DetectedAsImage()
    {
        var (stage, blob) = BuildStage();
        SetupBlob(blob, [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]);

        var result = await stage.ExecuteAsync(Msg("upload/arch.jpg"), None);

        result.ArtifactType.Should().Be("image");
        result.DetectionMethod.Should().Be("magic_bytes");
    }

    [Fact]
    public async Task GifMagicBytes_DetectedAsImage()
    {
        var (stage, blob) = BuildStage();
        SetupBlob(blob, [0x47, 0x49, 0x46, 0x38, 0x39, 0x61]);  // GIF89a

        var result = await stage.ExecuteAsync(Msg("upload/arch.gif"), None);

        result.ArtifactType.Should().Be("image");
    }

    // ── Content sniffing ──────────────────────────────────────────────────────

    [Fact]
    public async Task StartumlContent_DetectedAsPlantuml()
    {
        var (stage, blob) = BuildStage();
        SetupBlob(blob, "@startuml\nactor User\nUser -> API : request\n@enduml");

        var result = await stage.ExecuteAsync(Msg("upload/diagram.txt"), None);

        result.ArtifactType.Should().Be("plantuml");
        result.DetectionMethod.Should().Be("content_sniff");
        result.Confidence.Should().Be("high");
    }

    [Fact]
    public async Task StartumlCaseInsensitive_DetectedAsPlantuml()
    {
        var (stage, blob) = BuildStage();
        SetupBlob(blob, "@STARTUML\nactor User\n@ENDUML");

        var result = await stage.ExecuteAsync(Msg("upload/diagram.bin"), None);

        result.ArtifactType.Should().Be("plantuml");
    }

    [Theory]
    [InlineData("flowchart TD\n  A --> B")]
    [InlineData("graph LR\n  A-->B-->C")]
    [InlineData("sequenceDiagram\n  Alice->>Bob: Hello")]
    [InlineData("classDiagram\n  class Animal")]
    [InlineData("stateDiagram\n  [*] --> Active")]
    [InlineData("erDiagram\n  USER ||--o{ ORDER : places")]
    public async Task MermaidKeywords_DetectedAsMermaid(string content)
    {
        var (stage, blob) = BuildStage();
        SetupBlob(blob, content);

        var result = await stage.ExecuteAsync(Msg("upload/diagram.txt"), None);

        result.ArtifactType.Should().Be("mermaid");
        result.DetectionMethod.Should().Be("content_sniff");
    }

    [Theory]
    [InlineData("<mxfile host=\"Electron\" modified=\"2025-01-01\">")]
    [InlineData("<mxGraph><root></root></mxGraph>")]
    public async Task DrawioXml_DetectedAsDrawio(string content)
    {
        var (stage, blob) = BuildStage();
        SetupBlob(blob, content);

        var result = await stage.ExecuteAsync(Msg("upload/diagram.xml"), None);

        result.ArtifactType.Should().Be("drawio");
        result.DetectionMethod.Should().Be("content_sniff");
        result.Confidence.Should().Be("high");
    }

    // ── Extension fallback ────────────────────────────────────────────────────

    [Theory]
    [InlineData("diagram.puml", "plantuml", "medium")]
    [InlineData("diagram.pu",   "plantuml", "medium")]
    [InlineData("diagram.md",   "mermaid",  "low")]
    [InlineData("diagram.xml",  "drawio",   "medium")]
    [InlineData("diagram.drawio","drawio",  "medium")]
    [InlineData("diagram.jpg",  "image",    "medium")]
    [InlineData("diagram.jpeg", "image",    "medium")]
    [InlineData("diagram.png",  "image",    "medium")]
    [InlineData("diagram.txt",  "text",     "medium")]
    public async Task ExtensionFallback_ReturnsExpectedType(string filename, string expectedType, string expectedConfidence)
    {
        var (stage, blob) = BuildStage();
        SetupBlob(blob, "some arbitrary content that does not sniff as any type");

        var result = await stage.ExecuteAsync(Msg($"uploads/{filename}"), None);

        result.ArtifactType.Should().Be(expectedType);
        result.DetectionMethod.Should().Be("extension");
        result.Confidence.Should().Be(expectedConfidence);
    }

    [Fact]
    public async Task LowConfidenceExtension_SetsLowConfidenceFlag()
    {
        var (stage, blob) = BuildStage();
        SetupBlob(blob, "some content");  // does not sniff as mermaid

        var result = await stage.ExecuteAsync(Msg("uploads/diagram.md"), None);

        result.Confidence.Should().Be("low");
        result.LowConfidence.Should().BeTrue();
    }

    [Fact]
    public async Task MediumConfidenceExtension_DoesNotSetLowConfidenceFlag()
    {
        var (stage, blob) = BuildStage();
        SetupBlob(blob, "some content");

        var result = await stage.ExecuteAsync(Msg("uploads/diagram.puml"), None);

        result.Confidence.Should().Be("medium");
        result.LowConfidence.Should().BeFalse();
    }

    // ── Unsupported type ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("uploads/doc.pdf")]
    [InlineData("uploads/file.docx")]
    [InlineData("uploads/archive.zip")]
    [InlineData("uploads/noextension")]
    public async Task UnknownExtension_Throws_UnsupportedArtifactType(string blobPath)
    {
        var (stage, blob) = BuildStage();
        SetupBlob(blob, "arbitrary content with no known markers");

        var act = async () => await stage.ExecuteAsync(Msg(blobPath), None);

        await act.Should().ThrowAsync<PipelineStageException>()
            .Where(ex => ex.ErrorCode == "UNSUPPORTED_ARTIFACT_TYPE");
    }

    // ── Detection priority: magic bytes beats extension ───────────────────────

    [Fact]
    public async Task PngMagicBytesWithTextExtension_DetectedAsImageNotText()
    {
        // PNG magic bytes should take precedence over .txt extension
        var (stage, blob) = BuildStage();
        SetupBlob(blob, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00]);

        var result = await stage.ExecuteAsync(Msg("uploads/mislabeled.txt"), None);

        result.ArtifactType.Should().Be("image");
        result.DetectionMethod.Should().Be("magic_bytes");
    }

    [Fact]
    public async Task StartumlContentWithXmlExtension_DetectedAsPlantumlNotDrawio()
    {
        // Content sniff takes precedence over extension (.xml would otherwise → drawio)
        var (stage, blob) = BuildStage();
        SetupBlob(blob, "@startuml\nactor User\n@enduml");

        var result = await stage.ExecuteAsync(Msg("uploads/diagram.xml"), None);

        result.ArtifactType.Should().Be("plantuml");
        result.DetectionMethod.Should().Be("content_sniff");
    }
}
