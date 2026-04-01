using FluentAssertions;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Tests.Entities;

/// <summary>
/// Tests for the Job state machine invariants (data-model spec §6, §9).
/// Every allowed and disallowed transition is exercised explicitly.
/// </summary>
public sealed class JobTests
{
    private static Job CreateJob() =>
        Job.Create(OrgId.New(), UserId.New(), "Test Job");

    [Fact]
    public void Create_SetsStatusToPending()
    {
        var job = CreateJob();
        job.Status.Should().Be(JobStatus.Pending);
        job.IsTerminal.Should().BeFalse();
        job.IsInProgress.Should().BeTrue();
    }

    [Fact]
    public void Transition_PendingToParsing_Succeeds()
    {
        var job = CreateJob();
        job.Transition(JobStatus.Parsing);
        job.Status.Should().Be(JobStatus.Parsing);
    }

    [Fact]
    public void Transition_SkippingStage_Throws()
    {
        var job = CreateJob();
        // Cannot jump from Pending straight to Normalizing
        var act = () => job.Transition(JobStatus.Normalizing);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot transition*");
    }

    [Fact]
    public void Transition_ToFailed_SetsCompletedAt()
    {
        var job = CreateJob();
        job.Transition(JobStatus.Parsing);
        job.Transition(JobStatus.Failed, errorCode: "PARSE_FAILED");

        job.Status.Should().Be(JobStatus.Failed);
        job.ErrorCode.Should().Be("PARSE_FAILED");
        job.CompletedAt.Should().NotBeNull();
        job.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void Transition_FromTerminalState_Throws()
    {
        var job = CreateJob();
        job.Transition(JobStatus.Parsing);
        job.Transition(JobStatus.Failed);

        // Cannot transition out of a terminal state
        var act = () => job.Transition(JobStatus.Parsing);
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(JobStatus.Complete)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Partial)]
    public void FullPipeline_ToTerminal_SetsCompletedAt(JobStatus terminal)
    {
        var job = CreateJob();
        job.Transition(JobStatus.Parsing);
        job.Transition(JobStatus.Normalizing);
        job.Transition(JobStatus.AwaitingReview);
        job.Transition(JobStatus.Classifying);
        job.Transition(JobStatus.Analyzing);
        job.Transition(JobStatus.Synthesizing);
        job.Transition(terminal);

        job.Status.Should().Be(terminal);
        job.IsTerminal.Should().BeTrue();
        job.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Create_TitleTooLong_Throws()
    {
        var longTitle = new string('a', 256);
        var act = () => Job.Create(OrgId.New(), UserId.New(), longTitle);
        act.Should().Throw<ArgumentException>().WithMessage("*Title exceeds*");
    }
}
