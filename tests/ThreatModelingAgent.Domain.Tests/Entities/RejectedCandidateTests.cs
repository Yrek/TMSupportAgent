using FluentAssertions;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Tests.Entities;

public sealed class RejectedCandidateTests
{
    private static readonly JobId SomeJob = JobId.New();
    private static readonly OrgId SomeOrg = OrgId.New();

    [Theory]
    [InlineData("insufficient_evidence")]
    [InlineData("duplicate_root_cause")]
    [InlineData("out_of_scope")]
    [InlineData("mitigation_confirmed")]
    [InlineData("too_speculative")]
    public void Create_AllowedReason_Succeeds(string reason)
    {
        var act = () => RejectedCandidate.Create(SomeJob, SomeOrg, "title", "stride", reason, null);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("not_relevant")]
    [InlineData("false_positive")]
    [InlineData("")]
    [InlineData("INSUFFICIENT_EVIDENCE")]  // case-sensitive
    public void Create_UnknownReason_Throws(string reason)
    {
        var act = () => RejectedCandidate.Create(SomeJob, SomeOrg, "title", "stride", reason, null);
        act.Should().Throw<ArgumentException>().WithMessage("*rejection reason*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_EmptyTitle_Throws(string title)
    {
        var act = () => RejectedCandidate.Create(SomeJob, SomeOrg, title, "stride", "out_of_scope", null);
        act.Should().Throw<ArgumentException>();
    }
}
