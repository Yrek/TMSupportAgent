using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.ValueObjects;
using ThreatModelingAgent.Worker.Pipeline;
using ThreatModelingAgent.Worker.Pipeline.Contracts;

namespace ThreatModelingAgent.Worker.Tests.Pipeline;

/// <summary>
/// Tests for CorrectionApplicator — the pure function that applies user corrections
/// to the CanonicalModel before Phase 2 analysis.
/// </summary>
public sealed class CorrectionApplicatorTests
{
    private static readonly OrgId SomeOrg = OrgId.New();
    private static readonly ILogger NullLog = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CanonicalModel EmptyModel() => new(
        SystemPurpose: null,
        Components: [],
        Actors: [],
        ExternalSystems: [],
        DataStores: [],
        DataFlows: [],
        TrustBoundaries: [],
        NetworkExposure: "unknown",
        AuthenticationMethods: [],
        AuthorizationModel: null,
        SessionModel: null,
        MachineIdentities: [],
        PrivilegedPaths: [],
        TenantModel: null,
        SensitiveDataTypes: [],
        SecretsUsage: [],
        AsyncFlows: [],
        BackgroundJobs: [],
        HasLoggingMonitoring: false,
        AiLlmBoundaries: [],
        Assumptions: [],
        Gaps: [],
        ClarificationQuestions: []);

    private static CanonicalModel ModelWithComponent(string label) =>
        EmptyModel() with { Components = [new CanonicalComponent(label, "service", null, [])] };

    private static CanonicalModel ModelWithActor(string label) =>
        EmptyModel() with { Actors = [new CanonicalActor(label, "human", false)] };

    private static ArchitectureElement ComponentElement(Guid archId, string name) =>
        ArchitectureElement.CreateExtracted(archId, SomeOrg, ElementType.Component,
            name, null, "{}", ConfidenceLevel.High);

    private static ArchitectureElement ActorElement(Guid archId, string name) =>
        ArchitectureElement.CreateExtracted(archId, SomeOrg, ElementType.Actor,
            name, null, "{}", ConfidenceLevel.High);

    private static ArchitectureCorrection UpdateCorrection(Guid elementId, string field, string newValue) =>
        ArchitectureCorrection.Create(
            elementId: elementId,
            architectureId: Guid.NewGuid(),
            orgId: SomeOrg,
            correctedBy: UserId.New(),
            correctionType: CorrectionType.Update,
            fieldName: field,
            originalValue: null,
            correctedValue: newValue,
            note: null);

    private static ArchitectureCorrection RemovalCorrection(Guid elementId) =>
        ArchitectureCorrection.Create(
            elementId: elementId,
            architectureId: Guid.NewGuid(),
            orgId: SomeOrg,
            correctedBy: UserId.New(),
            correctionType: CorrectionType.MarkIncorrect,
            fieldName: null,
            originalValue: null,
            correctedValue: null,
            note: null);

    // ── No corrections ────────────────────────────────────────────────────────

    [Fact]
    public void Apply_NoCorrectionss_ReturnsOriginalModel()
    {
        var model = ModelWithComponent("API Gateway");
        var result = CorrectionApplicator.Apply(model, [], [], NullLog);
        result.Should().Be(model);
    }

    // ── Rename (Update field=name) ────────────────────────────────────────────

    [Fact]
    public void Apply_RenameComponent_UpdatesLabelInModel()
    {
        var archId = Guid.NewGuid();
        var element = ComponentElement(archId, "Old Name");
        var model = ModelWithComponent("Old Name");

        var correction = UpdateCorrection(element.Id, "name", "New Name");

        var result = CorrectionApplicator.Apply(model, [element], [correction], NullLog);

        result.Components.Should().ContainSingle()
            .Which.Label.Should().Be("New Name");
    }

    [Fact]
    public void Apply_RenameActor_UpdatesLabelInModel()
    {
        var archId = Guid.NewGuid();
        var element = ActorElement(archId, "User");
        var model = ModelWithActor("User");

        var correction = UpdateCorrection(element.Id, "label", "End User");

        var result = CorrectionApplicator.Apply(model, [element], [correction], NullLog);

        result.Actors.Should().ContainSingle()
            .Which.Label.Should().Be("End User");
    }

    // ── Remove (MarkIncorrect) ────────────────────────────────────────────────

    [Fact]
    public void Apply_MarkIncorrect_RemovesComponentFromModel()
    {
        var archId = Guid.NewGuid();
        var element = ComponentElement(archId, "Ghost Service");
        var model = EmptyModel() with
        {
            Components =
            [
                new CanonicalComponent("Ghost Service", "service", null, []),
                new CanonicalComponent("Real Service",  "service", null, [])
            ]
        };

        var correction = RemovalCorrection(element.Id);

        var result = CorrectionApplicator.Apply(model, [element], [correction], NullLog);

        result.Components.Should().ContainSingle()
            .Which.Label.Should().Be("Real Service");
    }

    [Fact]
    public void Apply_MarkIncorrect_RemovesActorFromModel()
    {
        var archId = Guid.NewGuid();
        var element = ActorElement(archId, "Attacker");
        var model = EmptyModel() with
        {
            Actors =
            [
                new CanonicalActor("Attacker", "external", true),
                new CanonicalActor("Admin", "human", false)
            ]
        };

        var correction = RemovalCorrection(element.Id);
        var result = CorrectionApplicator.Apply(model, [element], [correction], NullLog);

        result.Actors.Should().ContainSingle()
            .Which.Label.Should().Be("Admin");
    }

    // ── Unknown element ID is skipped ─────────────────────────────────────────

    [Fact]
    public void Apply_UnknownElementId_SkipsCorrectionSilently()
    {
        var model = ModelWithComponent("API");
        var correction = UpdateCorrection(Guid.NewGuid(), "name", "Something");

        // No elements passed, so element ID won't be found
        var result = CorrectionApplicator.Apply(model, [], [correction], NullLog);

        result.Components.Should().ContainSingle()
            .Which.Label.Should().Be("API");  // unchanged
    }

    // ── MarkAssumed / MarkConfirmed / AddNote are no-ops ─────────────────────

    [Theory]
    [InlineData(CorrectionType.MarkAssumed)]
    [InlineData(CorrectionType.MarkConfirmed)]
    [InlineData(CorrectionType.AddNote)]
    public void Apply_MetadataCorrectionTypes_LeaveModelUnchanged(CorrectionType correctionType)
    {
        var archId = Guid.NewGuid();
        var element = ComponentElement(archId, "Service");
        var model = ModelWithComponent("Service");

        var correction = ArchitectureCorrection.Create(
            element.Id, archId, SomeOrg, UserId.New(),
            correctionType, "note", null, "some note", null);

        var result = CorrectionApplicator.Apply(model, [element], [correction], NullLog);

        result.Components.Should().ContainSingle()
            .Which.Label.Should().Be("Service");
    }

    // ── Rename propagates to subsequent corrections ───────────────────────────

    [Fact]
    public void Apply_RenameFollowedByRemoval_SecondCorrectionUsesNewName()
    {
        var archId = Guid.NewGuid();
        var element = ComponentElement(archId, "OldService");
        var model = ModelWithComponent("OldService");

        var rename  = UpdateCorrection(element.Id, "name", "NewService");
        var removal = RemovalCorrection(element.Id);

        var result = CorrectionApplicator.Apply(model, [element], [rename, removal], NullLog);

        // After rename + remove, should be empty
        result.Components.Should().BeEmpty();
    }
}
