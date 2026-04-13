using FluentAssertions;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Tests.Entities;

/// <summary>
/// Tests for Architecture domain entity invariants.
/// </summary>
public sealed class ArchitectureTests
{
    private static Architecture CreateArch() => Architecture.Create(
        jobId: JobId.New(),
        orgId: OrgId.New(),
        systemPurpose: "Test system",
        classification: [],
        assumptionsJson: "[]",
        gapsJson: "[]",
        clarificationQuestionsJson: "[]");

    [Fact]
    public void Create_StartsAtVersionOne()
    {
        var arch = CreateArch();
        arch.Version.Should().Be(1);
        arch.IsConfirmed.Should().BeFalse();
    }

    [Fact]
    public void Confirm_SetsConfirmedAt()
    {
        var arch = CreateArch();
        var userId = UserId.New();
        arch.Confirm(userId);

        arch.IsConfirmed.Should().BeTrue();
        arch.ConfirmedAt.Should().NotBeNull();
        arch.ConfirmedBy.Should().Be(userId);
    }

    [Fact]
    public void Confirm_AlreadyConfirmed_Throws()
    {
        var arch = CreateArch();
        arch.Confirm(UserId.New());

        var act = () => arch.Confirm(UserId.New());
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already confirmed*");
    }

    [Fact]
    public void IncrementVersion_IncrementsMonotonically()
    {
        var arch = CreateArch();
        arch.Version.Should().Be(1);
        arch.IncrementVersion();
        arch.Version.Should().Be(2);
        arch.IncrementVersion();
        arch.Version.Should().Be(3);
    }

    [Fact]
    public void UpdateClassification_SetsCategories()
    {
        var arch = CreateArch();
        arch.Classification.Should().BeEmpty();

        var categories = new[] { "standard_web_app", "api_centric" };
        arch.UpdateClassification(categories);

        arch.Classification.Should().BeEquivalentTo(categories);
    }
}
