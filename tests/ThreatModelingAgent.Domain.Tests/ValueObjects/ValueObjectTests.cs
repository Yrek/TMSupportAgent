using FluentAssertions;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Tests.ValueObjects;

/// <summary>
/// Validates that security-relevant value objects enforce construction-time validation
/// and reject empty GUIDs (CLAUDE.md §6.2 — validation at birth).
/// </summary>
public sealed class ValueObjectTests
{
    [Fact]
    public void OrgId_EmptyGuid_Throws()
    {
        var act = () => OrgId.From(Guid.Empty);
        act.Should().Throw<ArgumentException>().WithMessage("*OrgId cannot be empty*");
    }

    [Fact]
    public void UserId_EmptyGuid_Throws()
    {
        var act = () => UserId.From(Guid.Empty);
        act.Should().Throw<ArgumentException>().WithMessage("*UserId cannot be empty*");
    }

    [Fact]
    public void JobId_EmptyGuid_Throws()
    {
        var act = () => JobId.From(Guid.Empty);
        act.Should().Throw<ArgumentException>().WithMessage("*JobId cannot be empty*");
    }

    [Fact]
    public void OrgId_ValidGuid_RoundTrips()
    {
        var id = Guid.NewGuid();
        var orgId = OrgId.From(id);
        orgId.Value.Should().Be(id);
        orgId.ToString().Should().Be(id.ToString());
    }

    [Fact]
    public void OrgId_New_IsNotEmpty()
    {
        var orgId = OrgId.New();
        orgId.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void OrgId_EqualityIsValueBased()
    {
        var id = Guid.NewGuid();
        OrgId.From(id).Should().Be(OrgId.From(id));
        OrgId.From(id).Should().NotBe(OrgId.New());
    }
}
