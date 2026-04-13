using FluentAssertions;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Tests.Entities;

/// <summary>
/// Tests for Threat domain entity invariants (data-model spec §9).
/// </summary>
public sealed class ThreatTests
{
    private static readonly JobId SomeJob = JobId.New();
    private static readonly OrgId SomeOrg = OrgId.New();

    private static Threat CreateConfirmed(
        ConfidenceLevel confidence = ConfidenceLevel.High,
        FindingType findingType = FindingType.Confirmed,
        string identifier = "T-001") =>
        Threat.CreateFromPipeline(
            jobId: SomeJob,
            orgId: SomeOrg,
            identifier: identifier,
            title: "Test threat",
            methodCategory: "Spoofing",
            affectedElementIds: [],
            description: "desc",
            attackScenario: "scenario",
            preconditions: null,
            impactedAssets: [],
            securityImpact: null,
            privacyImpact: null,
            existingControls: null,
            controlGaps: null,
            confidence: confidence,
            evidenceBasis: ["extracted_architecture_fact"],
            evidenceStrength: EvidenceStrength.Direct,
            assumptions: null,
            findingType: findingType);

    // ── Invariant: High confidence must not be set on Conditional findings ────

    [Fact]
    public void CreateFromPipeline_HighConfidenceConditional_Throws()
    {
        var act = () => CreateConfirmed(
            confidence: ConfidenceLevel.High,
            findingType: FindingType.Conditional);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*High*Conditional*");
    }

    [Theory]
    [InlineData(ConfidenceLevel.High,   FindingType.Confirmed)]
    [InlineData(ConfidenceLevel.Medium, FindingType.Conditional)]
    [InlineData(ConfidenceLevel.Low,    FindingType.Conditional)]
    [InlineData(ConfidenceLevel.Medium, FindingType.Confirmed)]
    public void CreateFromPipeline_ValidConfidenceFindingTypeCombinations_Succeed(
        ConfidenceLevel confidence, FindingType findingType)
    {
        var act = () => CreateConfirmed(confidence, findingType);
        act.Should().NotThrow();
    }

    // ── Invariant: Identifier format must be T-NNN ────────────────────────────

    [Theory]
    [InlineData("T-001")]
    [InlineData("T-999")]
    [InlineData("T-1000")]
    public void CreateFromPipeline_ValidIdentifier_Succeeds(string identifier)
    {
        var act = () => CreateConfirmed(identifier: identifier);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("T001")]
    [InlineData("t-001")]
    [InlineData("T-1")]
    [InlineData("T-12")]
    [InlineData("THREAT-001")]
    public void CreateFromPipeline_InvalidIdentifier_Throws(string identifier)
    {
        var act = () => CreateConfirmed(identifier: identifier);
        act.Should().Throw<ArgumentException>().WithMessage("*T-NNN*");
    }

    // ── Source = "system" for pipeline threats ────────────────────────────────

    [Fact]
    public void CreateFromPipeline_SetsSourceToSystem()
    {
        var threat = CreateConfirmed();
        threat.Source.Should().Be("system");
    }

    // ── Source = "user" for user-added threats ────────────────────────────────

    [Fact]
    public void CreateUserAdded_SetsSourceToUser()
    {
        var threat = Threat.CreateUserAdded(
            SomeJob, SomeOrg, "T-001", "title", "STRIDE",
            affectedElementIds: [Guid.NewGuid()], "desc", "scenario");

        threat.Source.Should().Be("user");
        threat.Status.Should().Be(ThreatStatus.Open);
    }

    // ── UpdateStatus ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ThreatStatus.Accepted)]
    [InlineData(ThreatStatus.Mitigated)]
    [InlineData(ThreatStatus.Rejected)]
    public void UpdateStatus_ValidStatus_Changes(ThreatStatus newStatus)
    {
        var threat = CreateConfirmed();
        threat.UpdateStatus(newStatus);
        threat.Status.Should().Be(newStatus);
    }
}
