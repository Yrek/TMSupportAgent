using FluentAssertions;
using ThreatModelingAgent.Worker.Pipeline;
using ThreatModelingAgent.Worker.Pipeline.Stages;

namespace ThreatModelingAgent.Worker.Tests.Pipeline;

/// <summary>
/// Tests for the token budget estimator.
/// Spec: 05-llm-workflow §6 — INPUT_TOO_LARGE error code required.
/// </summary>
public sealed class TokenEstimatorTests
{
    [Fact]
    public void Estimate_EmptyString_ReturnsZero()
    {
        TokenEstimator.Estimate(string.Empty).Should().Be(0);
    }

    [Fact]
    public void Estimate_FourChars_ReturnsOne()
    {
        // 4 chars / 4 chars-per-token = 1
        TokenEstimator.Estimate("abcd").Should().Be(1);
    }

    [Fact]
    public void EstimatePrompt_SumsSystemAndUser()
    {
        var system = new string('a', 400);  // 100 tokens
        var user   = new string('b', 800);  // 200 tokens
        TokenEstimator.EstimatePrompt(system, user).Should().Be(300);
    }

    [Fact]
    public void AssertWithinBudget_WellBelowLimit_DoesNotThrow()
    {
        // 100-char system + 100-char user = ~50 tokens, well within 8192 budget
        var act = () => TokenEstimator.AssertWithinBudget(
            new string('a', 100), new string('b', 100), 8_192, "TEST");
        act.Should().NotThrow();
    }

    [Fact]
    public void AssertWithinBudget_ExceedsBudget_ThrowsInputTooLarge()
    {
        // 8192 * 4 chars/token = 32768 chars needed to hit the limit
        // 90% effective ceiling = 7372 tokens = 29489 chars
        // Using 30000 chars for each should exceed it
        var hugePart = new string('x', 30_000);
        var act = () => TokenEstimator.AssertWithinBudget(hugePart, hugePart, 8_192, "NORMALIZE");

        act.Should().Throw<PipelineStageException>()
            .Which.ErrorCode.Should().Be("INPUT_TOO_LARGE");
    }

    [Fact]
    public void AssertWithinBudget_ExactlyAtSafetyMargin_DoesNotThrow()
    {
        // Effective limit = 8192 * 0.9 = 7372 tokens = 29488 chars
        // Use 29488 chars total (split between system and user)
        var at_margin = new string('x', 14_744);
        var act = () => TokenEstimator.AssertWithinBudget(at_margin, at_margin, 8_192, "TEST");
        act.Should().NotThrow();
    }
}
