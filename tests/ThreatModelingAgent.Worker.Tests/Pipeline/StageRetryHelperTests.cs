using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ThreatModelingAgent.Worker.Llm;
using ThreatModelingAgent.Worker.Pipeline.Stages;

namespace ThreatModelingAgent.Worker.Tests.Pipeline;

/// <summary>
/// Tests for StageRetryHelper — the shared LLM retry wrapper used by all LLM-backed stages.
///
/// Security invariant tested here: LLM output is NEVER returned to the caller without
/// passing schema validation. If the LLM returns malformed output on every attempt,
/// the stage fails with a PipelineStageException rather than letting the bad output through.
/// This is the core enforcement of CLAUDE.md §16.5 at the infrastructure level.
/// </summary>
public sealed class StageRetryHelperTests
{
    private static readonly ILogger NullLog = NullLogger.Instance;
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed record SimpleOutput(string Value);

    private static LlmResponse GoodResponse(string json) =>
        new(json, InputTokens: 100, OutputTokens: 50, Model: "test-model");

    private static Func<SimpleOutput, string?> AlwaysValid() =>
        _ => null;

    private static Func<SimpleOutput, string?> AlwaysInvalid() =>
        _ => "Value is missing";

    private static Func<SimpleOutput, string?> ValidateNonEmpty() =>
        o => string.IsNullOrEmpty(o.Value) ? "Value must not be empty" : null;

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidResponse_ReturnsDeserializedOutput()
    {
        var client = Substitute.For<ILlmClient>();
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(GoodResponse("""{"value":"hello"}"""));

        var (output, _, _) = await StageRetryHelper.ExecuteWithRetryAsync(
            client, BuildRequest(), AlwaysValid(), "TEST_STAGE", maxAttempts: 3, NullLog, None);

        output.Value.Should().Be("hello");
        await client.Received(1).CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidResponse_ReturnsTotalTokenCounts()
    {
        var client = Substitute.For<ILlmClient>();
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("""{"value":"x"}""", InputTokens: 200, OutputTokens: 80, Model: "m"));

        var (_, inputTokens, outputTokens) = await StageRetryHelper.ExecuteWithRetryAsync(
            client, BuildRequest(), AlwaysValid(), "TEST_STAGE", maxAttempts: 3, NullLog, None);

        inputTokens.Should().Be(200);
        outputTokens.Should().Be(80);
    }

    // ── Markdown fence stripping ──────────────────────────────────────────────

    [Theory]
    [InlineData("```json\n{\"value\":\"stripped\"}\n```")]
    [InlineData("```\n{\"value\":\"stripped\"}\n```")]
    [InlineData("  ```json\n{\"value\":\"stripped\"}\n```  ")]
    public async Task MarkdownFencedJson_StrippedAndDeserialized(string fencedContent)
    {
        var client = Substitute.For<ILlmClient>();
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(GoodResponse(fencedContent));

        var (output, _, _) = await StageRetryHelper.ExecuteWithRetryAsync(
            client, BuildRequest(), AlwaysValid(), "TEST_STAGE", maxAttempts: 1, NullLog, None);

        output.Value.Should().Be("stripped");
    }

    // ── Retry on validation failure ───────────────────────────────────────────

    [Fact]
    public async Task ValidationFailsOnFirstAttempt_RetriesAndSucceeds()
    {
        var client = Substitute.For<ILlmClient>();
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                GoodResponse("""{"value":""}"""),          // attempt 1: fails validation (empty)
                GoodResponse("""{"value":"nonempty"}""")); // attempt 2: passes

        var (output, _, _) = await StageRetryHelper.ExecuteWithRetryAsync(
            client, BuildRequest(), ValidateNonEmpty(), "TEST_STAGE", maxAttempts: 3, NullLog, None);

        output.Value.Should().Be("nonempty");
        await client.Received(2).CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidationFailsAllAttempts_ThrowsPipelineStageException()
    {
        var client = Substitute.For<ILlmClient>();
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(GoodResponse("""{"value":""}"""));  // always fails validation

        var act = async () => await StageRetryHelper.ExecuteWithRetryAsync(
            client, BuildRequest(), ValidateNonEmpty(), "STAGE_ERR", maxAttempts: 3, NullLog, None);

        await act.Should().ThrowAsync<PipelineStageException>()
            .Where(ex => ex.ErrorCode == "STAGE_ERR");

        await client.Received(3).CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Retry on JSON parse failure ───────────────────────────────────────────

    [Fact]
    public async Task InvalidJsonOnFirstAttempt_RetriesAndSucceeds()
    {
        var client = Substitute.For<ILlmClient>();
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                GoodResponse("not valid json at all"),
                GoodResponse("""{"value":"recovered"}"""));

        var (output, _, _) = await StageRetryHelper.ExecuteWithRetryAsync(
            client, BuildRequest(), AlwaysValid(), "TEST_STAGE", maxAttempts: 3, NullLog, None);

        output.Value.Should().Be("recovered");
    }

    [Fact]
    public async Task InvalidJsonAllAttempts_ThrowsWithStageErrorCode()
    {
        var client = Substitute.For<ILlmClient>();
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(GoodResponse("{ this is not json }"));

        var act = async () => await StageRetryHelper.ExecuteWithRetryAsync(
            client, BuildRequest(), AlwaysValid(), "PARSE_FAILED", maxAttempts: 3, NullLog, None);

        await act.Should().ThrowAsync<PipelineStageException>()
            .Where(ex => ex.ErrorCode == "PARSE_FAILED");

        await client.Received(3).CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Token accumulation across retries ─────────────────────────────────────

    [Fact]
    public async Task MultipleRetries_TokenCountsAccumulateAcrossAttempts()
    {
        var client = Substitute.For<ILlmClient>();
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new LlmResponse("""{"value":""}""",     InputTokens: 100, OutputTokens: 50, Model: "m"),  // fails
                new LlmResponse("""{"value":"ok"}""",   InputTokens: 100, OutputTokens: 50, Model: "m")); // succeeds

        var (_, inputTokens, outputTokens) = await StageRetryHelper.ExecuteWithRetryAsync(
            client, BuildRequest(), ValidateNonEmpty(), "TEST_STAGE", maxAttempts: 3, NullLog, None);

        inputTokens.Should().Be(200,  because: "both attempt token counts should be summed");
        outputTokens.Should().Be(100, because: "both attempt token counts should be summed");
    }

    // ── maxAttempts = 1 means no retries ─────────────────────────────────────

    [Fact]
    public async Task MaxAttemptsOne_FailsImmediatelyWithoutRetry()
    {
        var client = Substitute.For<ILlmClient>();
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(GoodResponse("not json"));

        var act = async () => await StageRetryHelper.ExecuteWithRetryAsync(
            client, BuildRequest(), AlwaysValid(), "SINGLE_SHOT", maxAttempts: 1, NullLog, None);

        await act.Should().ThrowAsync<PipelineStageException>()
            .Where(ex => ex.ErrorCode == "SINGLE_SHOT");

        await client.Received(1).CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Case-insensitive deserialization ─────────────────────────────────────

    [Fact]
    public async Task PascalCaseResponse_DeserializedCaseInsensitively()
    {
        var client = Substitute.For<ILlmClient>();
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(GoodResponse("""{"Value":"pascal"}"""));  // Pascal case from LLM

        var (output, _, _) = await StageRetryHelper.ExecuteWithRetryAsync(
            client, BuildRequest(), AlwaysValid(), "TEST_STAGE", maxAttempts: 1, NullLog, None);

        output.Value.Should().Be("pascal");
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CancellationRequested_ThrowsBeforeAttempt()
    {
        var client = Substitute.For<ILlmClient>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await StageRetryHelper.ExecuteWithRetryAsync(
            client, BuildRequest(), AlwaysValid(), "TEST_STAGE", maxAttempts: 3, NullLog, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await client.DidNotReceive().CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static LlmRequest BuildRequest() =>
        new(SystemPrompt: "sys", UserPrompt: "user", Model: "test-model");
}
