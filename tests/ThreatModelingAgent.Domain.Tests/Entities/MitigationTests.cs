using FluentAssertions;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Tests.Entities;

public sealed class MitigationTests
{
    private static readonly OrgId SomeOrg = OrgId.New();

    [Theory]
    [InlineData("critical")]
    [InlineData("high")]
    [InlineData("medium")]
    [InlineData("low")]
    public void Create_ValidPriority_Succeeds(string priority)
    {
        var act = () => Mitigation.Create(Guid.NewGuid(), SomeOrg, "title", "desc", priority, null);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("urgent")]
    [InlineData("HIGH")]
    [InlineData("")]
    [InlineData("none")]
    public void Create_InvalidPriority_Throws(string priority)
    {
        var act = () => Mitigation.Create(Guid.NewGuid(), SomeOrg, "title", "desc", priority, null);
        act.Should().Throw<ArgumentException>().WithMessage("*Priority*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_EmptyTitle_Throws(string title)
    {
        var act = () => Mitigation.Create(Guid.NewGuid(), SomeOrg, title, "desc", "high", null);
        act.Should().Throw<ArgumentException>();
    }
}
