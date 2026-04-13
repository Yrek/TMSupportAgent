using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Worker.Llm;
using ThreatModelingAgent.Worker.Pipeline.Contracts;
using ThreatModelingAgent.Worker.Pipeline.Stages;

namespace ThreatModelingAgent.Worker.Tests.Pipeline;

/// <summary>
/// Unit tests for ParseStage — Stage 2 of the threat modeling pipeline.
///
/// ParseStage reads an artifact from blob storage and calls an LLM to extract its
/// raw structure. Security invariants under test:
///   1. Oversized text artifacts are rejected before reaching the LLM (INPUT_TOO_LARGE).
///   2. LLM output is always schema-validated — bad output retries then fails (PARSE_FAILED).
///   3. Image artifacts use the strong model; text artifacts use the low-cost model.
///   4. Image magic bytes are correctly detected so the right media type is sent to the LLM.
///   5. Blob content is never logged (validated at the component level — no log assertions here,
///      but the stage delegates logging to the LLM client which is mocked).
/// </summary>
public sealed class ParseStageTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (ParseStage Stage, ILlmClientFactory Factory, ILlmClient Client, IBlobStorage Blob)
        BuildStage(string strongModel = "gpt-4o", string lowCostModel = "gpt-4o-mini")
    {
        var client  = Substitute.For<ILlmClient>();
        var factory = Substitute.For<ILlmClientFactory>();
        var blob    = Substitute.For<IBlobStorage>();

        factory.GetStrongModel().Returns(strongModel);
        factory.GetLowCostModel().Returns(lowCostModel);
        factory.GetForModel(Arg.Any<string>()).Returns(client);

        var stage = new ParseStage(blob, factory, NullLogger<ParseStage>.Instance);
        return (stage, factory, client, blob);
    }

    private static void SetupBlobBytes(IBlobStorage blob, byte[] bytes) =>
        blob.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(bytes)));

    private static void SetupBlobText(IBlobStorage blob, string text) =>
        SetupBlobBytes(blob, Encoding.UTF8.GetBytes(text));

    private static LlmResponse GoodParseResponse() => new(
        JsonSerializer.Serialize(new
        {
            rawElements   = Array.Empty<object>(),
            rawFlows      = Array.Empty<object>(),
            rawBoundaries = Array.Empty<object>(),
            rawDescription    = "A simple system",
            parserNotes       = "",
            extractionConfidence = "high"
        }),
        InputTokens: 500, OutputTokens: 200, Model: "gpt-4o-mini");

    private static ParseInput TextInput(string blobPath = "org1/uploads/job1/arch.txt") =>
        new(ArtifactType: "text", BlobPath: blobPath, LowConfidenceArtifactType: false);

    private static ParseInput ImageInput(string blobPath = "org1/uploads/job1/arch.png") =>
        new(ArtifactType: "image", BlobPath: blobPath, LowConfidenceArtifactType: false);

    // ── Text artifact — happy path ────────────────────────────────────────────

    [Fact]
    public async Task TextArtifact_UnderSizeCap_ReturnsValidOutput()
    {
        var (stage, factory, client, blob) = BuildStage();
        SetupBlobText(blob, "@startuml\nactor User\n@enduml");
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(GoodParseResponse());

        var result = await stage.ExecuteAsync(TextInput(), None);

        result.ExtractionConfidence.Should().Be("high");
        result.RawElements.Should().NotBeNull();
        result.RawFlows.Should().NotBeNull();
        result.RawBoundaries.Should().NotBeNull();
    }

    [Fact]
    public async Task TextArtifact_UsesLowCostModel()
    {
        var (stage, factory, client, blob) = BuildStage(lowCostModel: "gpt-4o-mini");
        SetupBlobText(blob, "some diagram text");
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(GoodParseResponse());

        await stage.ExecuteAsync(TextInput(), None);

        factory.Received(1).GetLowCostModel();
        factory.Received(1).GetForModel("gpt-4o-mini");
        factory.DidNotReceive().GetStrongModel();
    }

    // ── Text artifact — size cap ──────────────────────────────────────────────

    [Fact]
    public async Task TextArtifact_OverSizeCap_ThrowsInputTooLarge()
    {
        var (stage, _, _, blob) = BuildStage();
        // 81,000 bytes > 80,000 cap
        SetupBlobBytes(blob, new byte[81_000]);

        var act = async () => await stage.ExecuteAsync(TextInput(), None);

        await act.Should().ThrowAsync<PipelineStageException>()
            .Where(ex => ex.ErrorCode == "INPUT_TOO_LARGE");
    }

    [Fact]
    public async Task TextArtifact_ExactlySizeCap_DoesNotThrow()
    {
        var (stage, _, client, blob) = BuildStage();
        // Exactly 80,000 bytes — at the limit, not over
        SetupBlobBytes(blob, new byte[80_000]);
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(GoodParseResponse());

        var act = async () => await stage.ExecuteAsync(TextInput(), None);

        await act.Should().NotThrowAsync();
    }

    // ── Image artifact ────────────────────────────────────────────────────────

    [Fact]
    public async Task ImageArtifact_UsesStrongModel()
    {
        var (stage, factory, client, blob) = BuildStage(strongModel: "gpt-4o");
        SetupBlobBytes(blob, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(GoodParseResponse());

        await stage.ExecuteAsync(ImageInput(), None);

        factory.Received(1).GetStrongModel();
        factory.Received(1).GetForModel("gpt-4o");
        factory.DidNotReceive().GetLowCostModel();
    }

    [Fact]
    public async Task ImageArtifact_RequestIncludesBase64AndMediaType()
    {
        var (stage, _, client, blob) = BuildStage();
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };
        SetupBlobBytes(blob, pngBytes);

        LlmRequest? capturedRequest = null;
        client.CompleteAsync(Arg.Do<LlmRequest>(r => capturedRequest = r), Arg.Any<CancellationToken>())
            .Returns(GoodParseResponse());

        await stage.ExecuteAsync(ImageInput(), None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.ImageBase64.Should().Be(Convert.ToBase64String(pngBytes));
        capturedRequest.ImageMediaType.Should().Be("image/png");
    }

    // ── Image media type detection ────────────────────────────────────────────

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38 }, "image/gif")]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00 }, "image/png")] // unknown → safe default
    public async Task ImageArtifact_MagicBytes_DetectsCorrectMediaType(byte[] header, string expectedMediaType)
    {
        var (stage, _, client, blob) = BuildStage();
        // Pad to ensure we have enough bytes for reading
        var bytes = new byte[16];
        Array.Copy(header, bytes, header.Length);
        SetupBlobBytes(blob, bytes);

        LlmRequest? capturedRequest = null;
        client.CompleteAsync(Arg.Do<LlmRequest>(r => capturedRequest = r), Arg.Any<CancellationToken>())
            .Returns(GoodParseResponse());

        await stage.ExecuteAsync(ImageInput(), None);

        capturedRequest!.ImageMediaType.Should().Be(expectedMediaType);
    }

    // ── Schema validation and retries ─────────────────────────────────────────

    [Fact]
    public async Task LlmReturnsMissingFields_RetriesAndEventuallyThrows_ParseFailed()
    {
        var (stage, _, client, blob) = BuildStage();
        SetupBlobText(blob, "diagram content");

        // Always returns JSON missing required fields
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("{\"rawDescription\": \"partial\"}", 100, 50, "gpt-4o-mini"));

        var act = async () => await stage.ExecuteAsync(TextInput(), None);

        await act.Should().ThrowAsync<PipelineStageException>()
            .Where(ex => ex.ErrorCode == "PARSE_FAILED");

        // 3 retry attempts
        await client.Received(3).CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LlmReturnsInvalidConfidence_RetriesAndThrows()
    {
        var (stage, _, client, blob) = BuildStage();
        SetupBlobText(blob, "diagram");

        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse(
                JsonSerializer.Serialize(new
                {
                    rawElements = Array.Empty<object>(),
                    rawFlows = Array.Empty<object>(),
                    rawBoundaries = Array.Empty<object>(),
                    rawDescription = "desc",
                    parserNotes = "",
                    extractionConfidence = "very_high"  // invalid value
                }),
                100, 50, "gpt-4o-mini"));

        var act = async () => await stage.ExecuteAsync(TextInput(), None);

        await act.Should().ThrowAsync<PipelineStageException>()
            .Where(ex => ex.ErrorCode == "PARSE_FAILED");
    }

    [Fact]
    public async Task LlmFailsFirstAttempt_SucceedsOnSecond_ReturnsOutput()
    {
        var (stage, _, client, blob) = BuildStage();
        SetupBlobText(blob, "diagram");

        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new LlmResponse("{}", 100, 50, "gpt-4o-mini"),  // attempt 1: fails validation
                GoodParseResponse());                            // attempt 2: succeeds

        var result = await stage.ExecuteAsync(TextInput(), None);

        result.ExtractionConfidence.Should().Be("high");
        await client.Received(2).CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Low-confidence artifact type flag ─────────────────────────────────────

    [Fact]
    public async Task LowConfidenceArtifactType_IncludesNoteInUserPrompt()
    {
        var (stage, _, client, blob) = BuildStage();
        SetupBlobText(blob, "some content");
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(GoodParseResponse());

        var input = new ParseInput("mermaid", "org/uploads/job/arch.md", LowConfidenceArtifactType: true);

        LlmRequest? captured = null;
        client.CompleteAsync(Arg.Do<LlmRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(GoodParseResponse());

        await stage.ExecuteAsync(input, None);

        captured!.UserPrompt.Should().Contain("low confidence");
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CancellationRequested_ThrowsOperationCancelled()
    {
        var (stage, _, _, blob) = BuildStage();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        blob.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromCanceled<Stream>(call.Arg<CancellationToken>()));

        var act = async () => await stage.ExecuteAsync(TextInput(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
