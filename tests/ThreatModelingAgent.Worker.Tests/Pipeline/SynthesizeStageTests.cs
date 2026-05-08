using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Worker.Llm;
using ThreatModelingAgent.Worker.Pipeline.Contracts;
using ThreatModelingAgent.Worker.Pipeline.Stages;

namespace ThreatModelingAgent.Worker.Tests.Pipeline;

/// <summary>
/// Unit tests for SynthesizeStage — Stage 6 of the pipeline.
///
/// SynthesizeStage merges all method-specific ThreatCandidateSets into a final
/// prioritized FinalOutput, then runs a cheap framework-mapping sub-step.
/// Security invariants under test:
///   1. Always uses the strong model for the main synthesis call.
///   2. UserAddedThreats from LLM is always normalised to [] (never populated by LLM).
///   3. EnforcePartialStatus: a critical gap in the canonical model forces partial status.
///   4. Validate: remediation list referencing a non-confirmed threat is rejected.
///   5. Framework mapping sub-step: failures are swallowed (supplementary, never blocks).
///   6. Framework mapping sub-step: token budget exceeded → skipped gracefully.
///   7. PersistAsync writes to the correct tenant-scoped blob path.
///   8. Token budget exceeded (main stage) throws before calling the LLM.
/// </summary>
public sealed class SynthesizeStageTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (SynthesizeStage Stage, ILlmClientFactory Factory, ILlmClient StrongClient, ILlmClient CheapClient, IBlobStorage Blob)
        BuildStage(string strongModel = "gpt-4o", string lowCostModel = "gpt-4o-mini")
    {
        var strongClient = Substitute.For<ILlmClient>();
        var cheapClient  = Substitute.For<ILlmClient>();
        var factory      = Substitute.For<ILlmClientFactory>();
        var blob         = Substitute.For<IBlobStorage>();

        factory.GetStrongModel().Returns(strongModel);
        factory.GetLowCostModel().Returns(lowCostModel);

        // Route by model name: strong → strongClient, cheap → cheapClient
        factory.GetForModel(strongModel).Returns(strongClient);
        factory.GetForModel(lowCostModel).Returns(cheapClient);
        factory.GetForModel(Arg.Is<string>(m => m != strongModel && m != lowCostModel)).Returns(strongClient);

        var stage = new SynthesizeStage(factory, NullLogger<SynthesizeStage>.Instance,
            Microsoft.Extensions.Options.Options.Create(new SynthesisOptions()));
        return (stage, factory, strongClient, cheapClient, blob);
    }

    private static CanonicalModel MinimalCanonical(Gap[]? gaps = null) => new(
        SystemPurpose: "Test system",
        Components:        [new("API", "service", null, [])],
        Actors:            [],
        ExternalSystems:   [],
        DataStores:        [],
        DataFlows:         [],
        TrustBoundaries:   [],
        NetworkExposure:   "internet_facing",
        AuthenticationMethods: [],
        AuthorizationModel:    null,
        SessionModel:          null,
        MachineIdentities:     [],
        PrivilegedPaths:       [],
        TenantModel:           null,
        SensitiveDataTypes:    [],
        SecretsUsage:          [],
        AsyncFlows:            [],
        BackgroundJobs:        [],
        HasLoggingMonitoring:  false,
        AiLlmBoundaries:       [],
        Assumptions:           [],
        Gaps:                  gaps ?? [],
        ClarificationQuestions: []);

    private static ClassificationResult MinimalClassification() => new(
        Categories: ["standard_web_app"],
        SelectedMethods: [new("stride", "required", true, ["analyze"])],
        ModelRoutingPlan: new("gpt-4o", "gpt-4o-mini", "gpt-4o"));

    private static FinalOutput MinimalFinalOutput(
        string analysisStatus = "complete",
        FinalThreat[]? confirmed = null,
        FinalThreat[]? userAdded = null) =>
        new(
            SystemSummary: "System is an internet-facing API.",
            ArchitectureClassification: ["standard_web_app"],
            SelectedMethodsWithRationale: [],
            ModelRoutingSummary: new Dictionary<string, string> { ["synthesize"] = "gpt-4o" },
            ConfirmedThreats:   confirmed ?? [MakeThreat("T-001")],
            ConditionalThreats: [],
            UserAddedThreats:   userAdded ?? [],
            SecureDesignRecommendations: [],
            PrioritizedRemediationList: [new RemediationItem("T-001", "Fix auth", "high", "Add MFA")],
            ReviewQuestions: [],
            AnalysisStatus: analysisStatus,
            PartialReason:  null);

    private static FinalThreat MakeThreat(string id, string[]? frameworkMappings = null) =>
        new(
            Identifier:            id,
            Title:                 $"Threat {id}",
            MethodCategory:        "stride_spoofing",
            AffectedElementLabels: ["API"],
            Description:           "An attacker could spoof.",
            AttackScenario:        "MITM",
            Preconditions:         null,
            ImpactedAssets:        ["user_data"],
            SecurityImpact:        "high",
            PrivacyImpact:         null,
            ExistingControls:      null,
            ControlGaps:           null,
            Confidence:            "high",
            EvidenceStrength:      "direct",
            FindingType:           "confirmed",
            Mitigations:           [],
            FrameworkMappings:     frameworkMappings?.Select(f => new FrameworkMapping(f, "REF-1", null)).ToArray() ?? []);

    private static LlmResponse ResponseFor(FinalOutput output) =>
        new(JsonSerializer.Serialize(output, CamelCase), 2000, 1000, "gpt-4o");

    private static LlmResponse CheapResponse(object mappings) =>
        new(JsonSerializer.Serialize(mappings), 300, 100, "gpt-4o-mini");

    private static SynthesizeInput MinimalInput(CanonicalModel? model = null) => new(
        AllCandidateSets: [new ThreatCandidateSet("stride", [], [])],
        CanonicalModel: model ?? MinimalCanonical(),
        ClassificationResult: MinimalClassification());

    // ── Model selection ───────────────────────────────────────────────────────

    [Fact]
    public async Task SynthesizeStage_AlwaysUsesStrongModel()
    {
        var (stage, factory, strongClient, cheapClient, blob) = BuildStage(strongModel: "gpt-4o");
        strongClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(MinimalFinalOutput()));
        cheapClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(CheapResponse(Array.Empty<object>()));

        await stage.ExecuteAsync(MinimalInput(), None);

        factory.Received(1).GetStrongModel();
        factory.Received(1).GetForModel("gpt-4o");
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidInput_ReturnsFinalOutput_WithAllRequiredFields()
    {
        var (stage, _, strongClient, cheapClient, _) = BuildStage();
        strongClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(MinimalFinalOutput()));
        cheapClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(CheapResponse(Array.Empty<object>()));

        var result = await stage.ExecuteAsync(MinimalInput(), None);

        result.SystemSummary.Should().NotBeNullOrWhiteSpace();
        result.ConfirmedThreats.Should().NotBeNull();
        result.ConditionalThreats.Should().NotBeNull();
        result.SecureDesignRecommendations.Should().NotBeNull();
        result.PrioritizedRemediationList.Should().NotBeNull();
        result.AnalysisStatus.Should().BeOneOf("complete", "partial");
    }

    // ── UserAddedThreats normalization ────────────────────────────────────────

    [Fact]
    public async Task LlmReturnsNullUserAddedThreats_NormalisedToEmptyArray()
    {
        var (stage, _, strongClient, cheapClient, _) = BuildStage();

        // Synthesize output from LLM has null UserAddedThreats (model omits the field)
        var outputWithNullUserAdded = MinimalFinalOutput() with { UserAddedThreats = null! };
        strongClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(outputWithNullUserAdded));
        cheapClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(CheapResponse(Array.Empty<object>()));

        var result = await stage.ExecuteAsync(MinimalInput(), None);

        result.UserAddedThreats.Should().NotBeNull(
            because: "UserAddedThreats MUST be an empty array at synthesis time; populated via API later");
        result.UserAddedThreats.Should().BeEmpty();
    }

    [Fact]
    public async Task LlmReturnsNonEmptyUserAddedThreats_ReplacedWithEmptyArray()
    {
        var (stage, _, strongClient, cheapClient, _) = BuildStage();

        // LLM returned a user-added threat — this is not permitted at synthesis time
        var outputWithIllegalUserAdded = MinimalFinalOutput(
            userAdded: [MakeThreat("U-001")]);

        strongClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(outputWithIllegalUserAdded));
        cheapClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(CheapResponse(Array.Empty<object>()));

        var result = await stage.ExecuteAsync(MinimalInput(), None);

        result.UserAddedThreats.Should().BeEmpty(
            because: "the LLM MUST NOT populate UserAddedThreats; they are set via the API");
    }

    // ── EnforcePartialStatus ──────────────────────────────────────────────────

    [Fact]
    public async Task CriticalGapInModel_ForcesPartialStatus()
    {
        var (stage, _, strongClient, cheapClient, _) = BuildStage();

        var modelWithCriticalGap = MinimalCanonical(
            gaps: [new Gap("Authentication", "Auth mechanism not specified", "critical")]);

        var completeOutput = MinimalFinalOutput(analysisStatus: "complete");
        strongClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(completeOutput));
        cheapClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(CheapResponse(Array.Empty<object>()));

        var result = await stage.ExecuteAsync(MinimalInput(modelWithCriticalGap), None);

        result.AnalysisStatus.Should().Be("partial",
            because: "a critical gap in the canonical model must force partial status");
        result.PartialReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task NoCriticalGaps_CompleteStatusPreserved()
    {
        var (stage, _, strongClient, cheapClient, _) = BuildStage();

        var modelWithHighGap = MinimalCanonical(
            gaps: [new Gap("Logging", "Logging config unknown", "high")]);

        var completeOutput = MinimalFinalOutput(analysisStatus: "complete");
        strongClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(completeOutput));
        cheapClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(CheapResponse(Array.Empty<object>()));

        var result = await stage.ExecuteAsync(MinimalInput(modelWithHighGap), None);

        result.AnalysisStatus.Should().Be("complete",
            because: "only critical gaps force partial status; high gaps do not");
    }

    [Fact]
    public async Task LlmAlreadyReturnsPartialStatus_PreservedWithoutOverride()
    {
        var (stage, _, strongClient, cheapClient, _) = BuildStage();

        var partialOutput = MinimalFinalOutput(analysisStatus: "partial") with
            { PartialReason = "LLM determined analysis was partial due to architecture ambiguity." };

        strongClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(partialOutput));
        cheapClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(CheapResponse(Array.Empty<object>()));

        var result = await stage.ExecuteAsync(MinimalInput(), None);

        result.AnalysisStatus.Should().Be("partial");
        result.PartialReason.Should().Contain("ambiguity");
    }

    // ── Schema validation ─────────────────────────────────────────────────────

    [Fact]
    public async Task RemediationReferencesNonConfirmedThreat_RetriesAndThrows()
    {
        var (stage, _, strongClient, cheapClient, _) = BuildStage();

        // Remediation list references T-999 which is not in ConfirmedThreats
        var badOutput = MinimalFinalOutput(
            confirmed: [MakeThreat("T-001")]) with
        {
            PrioritizedRemediationList = [new RemediationItem("T-999", "Ghost fix", "high", "Fix it")]
        };

        strongClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(badOutput));

        var act = async () => await stage.ExecuteAsync(MinimalInput(), None);

        await act.Should().ThrowAsync<PipelineStageException>()
            .Where(ex => ex.ErrorCode == "SYNTHESIZE_FAILED",
                because: "remediation items must only reference confirmed threats");
        await strongClient.Received(5).CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingSystemSummary_RetriesAndThrows()
    {
        var (stage, _, strongClient, _, _) = BuildStage();

        var badOutput = MinimalFinalOutput() with { SystemSummary = "" };
        strongClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(badOutput));

        var act = async () => await stage.ExecuteAsync(MinimalInput(), None);

        await act.Should().ThrowAsync<PipelineStageException>()
            .Where(ex => ex.ErrorCode == "SYNTHESIZE_FAILED");
    }

    [Fact]
    public async Task InvalidAnalysisStatus_RetriesAndThrows()
    {
        var (stage, _, strongClient, _, _) = BuildStage();

        var badOutput = MinimalFinalOutput() with { AnalysisStatus = "in_progress" };
        strongClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(badOutput));

        var act = async () => await stage.ExecuteAsync(MinimalInput(), None);

        await act.Should().ThrowAsync<PipelineStageException>()
            .Where(ex => ex.ErrorCode == "SYNTHESIZE_FAILED");
    }

    // ── Framework mapping sub-step ────────────────────────────────────────────

    [Fact]
    public async Task FrameworkMapping_ValidMappings_MergedIntoConfirmedThreats()
    {
        var (stage, _, strongClient, cheapClient, _) = BuildStage();

        var output = MinimalFinalOutput(confirmed: [MakeThreat("T-001")]);
        strongClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(output));

        var mappings = new[]
        {
            new { threatIdentifier = "T-001", framework = "OWASP", reference = "A01:2021", mappingType = "direct" }
        };
        cheapClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(CheapResponse(mappings));

        var result = await stage.ExecuteAsync(MinimalInput(), None);

        result.ConfirmedThreats[0].FrameworkMappings.Should().ContainSingle(
            fm => fm.Framework == "owasp_top10" && fm.Reference == "A01:2021",
            because: "framework mappings from the sub-step should be merged into confirmed threats after normalization");
    }

    [Fact]
    public async Task FrameworkMapping_SubStepThrows_SynthesisOutputStillReturned()
    {
        var (stage, _, strongClient, cheapClient, _) = BuildStage();

        strongClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(MinimalFinalOutput()));
        cheapClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("LLM service unavailable"));

        // Framework mapping failure should NOT fail the whole synthesis
        var act = async () => await stage.ExecuteAsync(MinimalInput(), None);
        await act.Should().NotThrowAsync(
            because: "framework mapping sub-step is supplementary and must not block synthesis");

        var result = await stage.ExecuteAsync(MinimalInput(), None);
        result.ConfirmedThreats.Should().HaveCount(1);
    }

    [Fact]
    public async Task FrameworkMapping_InvalidJson_SynthesisOutputStillReturned()
    {
        var (stage, _, strongClient, cheapClient, _) = BuildStage();

        strongClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(MinimalFinalOutput()));
        cheapClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("this is not valid JSON", 100, 50, "gpt-4o-mini"));

        var result = await stage.ExecuteAsync(MinimalInput(), None);

        // Should return the main synthesis output even if framework mapping returned garbage
        result.SystemSummary.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task FrameworkMapping_UnknownFrameworkName_Discarded()
    {
        var (stage, _, strongClient, cheapClient, _) = BuildStage();

        strongClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(MinimalFinalOutput(confirmed: [MakeThreat("T-001")])));

        var mappings = new[]
        {
            new { threatIdentifier = "T-001", framework = "UNKNOWN_FRAMEWORK_XYZ", reference = "REF-1", mappingType = "direct" }
        };
        cheapClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(CheapResponse(mappings));

        var result = await stage.ExecuteAsync(MinimalInput(), None);

        result.ConfirmedThreats[0].FrameworkMappings.Should().BeEmpty(
            because: "unknown framework names must be discarded by FrameworkNormalizer");
    }

    // ── PersistAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PersistAsync_WritesToCorrectBlobPath()
    {
        var blob   = Substitute.For<IBlobStorage>();
        var orgId  = Guid.NewGuid();
        var jobId  = Guid.NewGuid();
        var output = MinimalFinalOutput();

        string? capturedPath = null;
        blob.UploadAsync(
            Arg.Do<string>(p => capturedPath = p),
            Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("uploaded"));

        var path = await SynthesizeStage.PersistAsync(output, orgId, jobId, blob, None);

        path.Should().Be($"{orgId}/outputs/{jobId}/analysis.json");
        capturedPath.Should().Be($"{orgId}/outputs/{jobId}/analysis.json");
        await blob.Received(1).UploadAsync(
            Arg.Any<string>(), Arg.Any<Stream>(), "application/json", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PersistAsync_ReturnsCorrectPath()
    {
        var blob  = Substitute.For<IBlobStorage>();
        var orgId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        blob.UploadAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("uploaded"));

        var returnedPath = await SynthesizeStage.PersistAsync(MinimalFinalOutput(), orgId, jobId, blob, None);

        returnedPath.Should().Be($"{orgId}/outputs/{jobId}/analysis.json");
    }

    // ── Acceptance criteria round-trip ───────────────────────────────────────

    [Fact]
    public async Task MitigationAcceptanceCriteria_PreservedInOutput()
    {
        var (stage, _, strongClient, cheapClient, _) = BuildStage();

        var mitigation = new Mitigation(
            Title: "Enforce MFA",
            Description: "Require multi-factor authentication for all users.",
            Priority: "high",
            AcceptanceCriteria: ["MFA is enforced on all login paths", "No bypass route exists in auth middleware"]);

        var threat = MakeThreat("T-001") with { Mitigations = [mitigation] };
        var output = MinimalFinalOutput(confirmed: [threat]);

        strongClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(output));
        cheapClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(CheapResponse(Array.Empty<object>()));

        var result = await stage.ExecuteAsync(MinimalInput(), None);

        var resultMitigation = result.ConfirmedThreats[0].Mitigations[0];
        resultMitigation.AcceptanceCriteria.Should().HaveCount(2,
            because: "acceptance criteria must survive JSON serialization through the stage");
        resultMitigation.AcceptanceCriteria.Should().Contain("MFA is enforced on all login paths");
        resultMitigation.AcceptanceCriteria.Should().Contain("No bypass route exists in auth middleware");
    }

    [Fact]
    public async Task MitigationWithEmptyAcceptanceCriteria_ReturnsEmptyArray()
    {
        var (stage, _, strongClient, cheapClient, _) = BuildStage();

        var mitigation = new Mitigation(
            Title: "Add logging",
            Description: "Log all auth events.",
            Priority: "medium",
            AcceptanceCriteria: []);

        var threat = MakeThreat("T-001") with { Mitigations = [mitigation] };
        strongClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(MinimalFinalOutput(confirmed: [threat])));
        cheapClient.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(CheapResponse(Array.Empty<object>()));

        var result = await stage.ExecuteAsync(MinimalInput(), None);

        result.ConfirmedThreats[0].Mitigations[0].AcceptanceCriteria.Should().NotBeNull();
        result.ConfirmedThreats[0].Mitigations[0].AcceptanceCriteria.Should().BeEmpty();
    }

    // ── Token budget ──────────────────────────────────────────────────────────

    [Fact]
    public async Task VeryLargeInput_ExceedsTokenBudget_ThrowsBeforeStrongModelCall()
    {
        var (stage, _, strongClient, _, _) = BuildStage();

        // Build a set of candidate sets large enough to exceed 16,384 token budget
        var hugeCandidates = Enumerable.Range(0, 2000)
            .Select(i => new ThreatCandidate(
                $"Threat {i}", "stride_spoofing", ["API"],
                new string('x', 200),  // large description
                "scenario", null, [], null, null, null, null,
                "high", [], "direct", null, "confirmed"))
            .ToArray();

        var bigInput = new SynthesizeInput(
            AllCandidateSets: [new ThreatCandidateSet("stride", hugeCandidates, [])],
            CanonicalModel: MinimalCanonical(),
            ClassificationResult: MinimalClassification());

        var act = async () => await stage.ExecuteAsync(bigInput, None);

        await act.Should().ThrowAsync<PipelineStageException>();

        await strongClient.DidNotReceive().CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }
}
