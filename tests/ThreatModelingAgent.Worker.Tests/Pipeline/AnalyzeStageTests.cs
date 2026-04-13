using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ThreatModelingAgent.Worker.Llm;
using ThreatModelingAgent.Worker.Pipeline.Contracts;
using ThreatModelingAgent.Worker.Pipeline.Stages;

namespace ThreatModelingAgent.Worker.Tests.Pipeline;

/// <summary>
/// Unit tests for AnalyzeStage — Stage 5 of the pipeline.
///
/// AnalyzeStage runs one LLM sub-stage per selected threat modeling method.
/// Security invariants under test:
///   1. Security-critical methods (stride, tenant_isolation, etc.) use the strong model.
///   2. Pattern-driven methods (abuse_case, supply_chain) use the low-cost model.
///   3. Threats referencing unknown element labels are moved to rejectedCandidates
///      (EnforceTraceability — spec §5.1 Validation point 2).
///   4. LLM output is schema-validated — bad output retries then fails.
///   5. RunAllMethodsAsync runs all selected methods (one per method, parallel).
///   6. Token budget is enforced before the LLM call.
/// </summary>
public sealed class AnalyzeStageTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (AnalyzeStage Stage, ILlmClientFactory Factory, ILlmClient Client)
        BuildStage(string strongModel = "gpt-4o", string lowCostModel = "gpt-4o-mini")
    {
        var client  = Substitute.For<ILlmClient>();
        var factory = Substitute.For<ILlmClientFactory>();

        factory.GetStrongModel().Returns(strongModel);
        factory.GetLowCostModel().Returns(lowCostModel);
        factory.GetForModel(Arg.Any<string>()).Returns(client);

        var stage = new AnalyzeStage(factory, NullLogger<AnalyzeStage>.Instance);
        return (stage, factory, client);
    }

    private static CanonicalModel MinimalCanonical(params string[] componentLabels) => new(
        SystemPurpose: null,
        Components: componentLabels.Select(l => new CanonicalComponent(l, "service", null, [])).ToArray(),
        Actors: [],
        ExternalSystems: [],
        DataStores: [],
        DataFlows: [],
        TrustBoundaries: [],
        NetworkExposure: "internet_facing",
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

    private static ClassificationResult MinimalClassification() => new(
        Categories: ["standard_web_app"],
        SelectedMethods: [new("stride", "required", true, ["analyze"])],
        ModelRoutingPlan: new("gpt-4o", "gpt-4o-mini", "gpt-4o"));

    private static ThreatCandidateSet GoodCandidateSet(string method, string[] elementLabels) =>
        new(
            Method: method,
            Candidates: [new ThreatCandidate(
                Title:                "Spoofing — API",
                MethodCategory:       "stride_spoofing",
                AffectedElementLabels: elementLabels,
                Description:          "An attacker could spoof the API identity.",
                AttackScenario:       "MITM attack targeting the API.",
                Preconditions:        null,
                ImpactedAssets:       ["user_data"],
                SecurityImpact:       "high",
                PrivacyImpact:        null,
                ExistingControls:     null,
                ControlGaps:          null,
                Confidence:           "high",
                EvidenceBasis:        ["architecture"],
                EvidenceStrength:     "direct",
                Assumptions:          null,
                FindingType:          "confirmed")],
            RejectedCandidates: []);

    private static LlmResponse ResponseFor(ThreatCandidateSet set) =>
        new(JsonSerializer.Serialize(set, CamelCase), 1000, 500, "gpt-4o");

    private static AnalyzeInput InputFor(string method, CanonicalModel? model = null) =>
        new(Method: method,
            CanonicalModel: model ?? MinimalCanonical("API"),
            ClassificationResult: MinimalClassification());

    // ── Model selection ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("stride")]
    [InlineData("tenant_isolation")]
    [InlineData("identity_session_delegation")]
    [InlineData("ai_llm_threat")]
    [InlineData("linddun")]
    public async Task SecurityCriticalMethod_UsesStrongModel(string method)
    {
        var (stage, factory, client) = BuildStage(strongModel: "gpt-4o");
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(GoodCandidateSet(method, ["API"])));

        await stage.ExecuteAsync(InputFor(method), None);

        factory.Received(1).GetStrongModel();
        factory.Received(1).GetForModel("gpt-4o");
        factory.DidNotReceive().GetLowCostModel();
    }

    [Theory]
    [InlineData("abuse_case")]
    [InlineData("supply_chain")]
    [InlineData("availability_resilience")]
    public async Task PatternDrivenMethod_UsesLowCostModel(string method)
    {
        var (stage, factory, client) = BuildStage(lowCostModel: "gpt-4o-mini");
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(GoodCandidateSet(method, ["API"])));

        await stage.ExecuteAsync(InputFor(method), None);

        factory.Received(1).GetLowCostModel();
        factory.Received(1).GetForModel("gpt-4o-mini");
        factory.DidNotReceive().GetStrongModel();
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidInput_ReturnsThreatCandidateSet()
    {
        var (stage, _, client) = BuildStage();
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(GoodCandidateSet("stride", ["API"])));

        var result = await stage.ExecuteAsync(InputFor("stride"), None);

        result.Method.Should().Be("stride");
        result.Candidates.Should().HaveCount(1);
        result.RejectedCandidates.Should().BeEmpty();
    }

    // ── EnforceTraceability ───────────────────────────────────────────────────

    [Fact]
    public async Task ThreatWithUnknownElementLabel_MovedToRejectedCandidates()
    {
        var (stage, _, client) = BuildStage();
        // Canonical model only has "API" but threat references "UnknownComponent"
        var setWithUnknownLabel = new ThreatCandidateSet(
            Method: "stride",
            Candidates: [new ThreatCandidate(
                Title: "Spoofing",
                MethodCategory: "stride_spoofing",
                AffectedElementLabels: ["UnknownComponent"],  // not in canonical model
                Description: "desc",
                AttackScenario: "scenario",
                Preconditions: null,
                ImpactedAssets: [],
                SecurityImpact: null,
                PrivacyImpact: null,
                ExistingControls: null,
                ControlGaps: null,
                Confidence: "high",
                EvidenceBasis: [],
                EvidenceStrength: "direct",
                Assumptions: null,
                FindingType: "confirmed")],
            RejectedCandidates: []);

        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse(JsonSerializer.Serialize(setWithUnknownLabel, CamelCase), 1000, 500, "gpt-4o"));

        var result = await stage.ExecuteAsync(InputFor("stride", MinimalCanonical("API")), None);

        result.Candidates.Should().BeEmpty(
            because: "the threat referenced an element label not present in the canonical model");
        result.RejectedCandidates.Should().HaveCount(1);
        result.RejectedCandidates[0].RejectionReason.Should().Be("out_of_scope");
    }

    [Fact]
    public async Task ThreatWithAllKnownLabels_StaysInCandidates()
    {
        var (stage, _, client) = BuildStage();
        var model = MinimalCanonical("API", "Database");
        var set = GoodCandidateSet("stride", ["API", "Database"]);

        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(set));

        var result = await stage.ExecuteAsync(InputFor("stride", model), None);

        result.Candidates.Should().HaveCount(1);
        result.RejectedCandidates.Should().BeEmpty();
    }

    [Fact]
    public async Task ThreatWithPartiallyUnknownLabels_MovedToRejected()
    {
        var (stage, _, client) = BuildStage();
        var model = MinimalCanonical("API");
        var mixedSet = new ThreatCandidateSet(
            Method: "stride",
            Candidates: [
                new ThreatCandidate("Known", "stride_s", ["API"], "d", "s", null, [], null, null, null, null, "high", [], "direct", null, "confirmed"),
                new ThreatCandidate("Unknown", "stride_t", ["API", "GhostService"], "d", "s", null, [], null, null, null, null, "high", [], "direct", null, "confirmed")
            ],
            RejectedCandidates: []);

        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse(JsonSerializer.Serialize(mixedSet, CamelCase), 1000, 500, "gpt-4o"));

        var result = await stage.ExecuteAsync(InputFor("stride", model), None);

        result.Candidates.Should().HaveCount(1);
        result.Candidates[0].Title.Should().Be("Known");
        result.RejectedCandidates.Should().HaveCount(1);
        result.RejectedCandidates[0].Title.Should().Be("Unknown");
    }

    // ── Schema validation ─────────────────────────────────────────────────────

    [Fact]
    public async Task LlmReturnsMissingMethodField_RetriesAndThrows_AnalyzeFailed()
    {
        var (stage, _, client) = BuildStage();
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("{\"candidates\": [], \"rejectedCandidates\": []}", 500, 200, "gpt-4o"));

        var act = async () => await stage.ExecuteAsync(InputFor("stride"), None);

        await act.Should().ThrowAsync<PipelineStageException>()
            .Where(ex => ex.ErrorCode == "ANALYZE_FAILED");
        await client.Received(3).CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CandidateMissingAffectedElements_RetriesAndThrows()
    {
        var (stage, _, client) = BuildStage();
        var badSet = new
        {
            method = "stride",
            candidates = new[]
            {
                new { title = "Bad Threat", methodCategory = "stride_s", affectedElementLabels = Array.Empty<string>(),
                      description = "d", attackScenario = "s", confidence = "high",
                      evidenceBasis = Array.Empty<string>(), evidenceStrength = "direct",
                      findingType = "confirmed" }
            },
            rejectedCandidates = Array.Empty<object>()
        };

        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse(JsonSerializer.Serialize(badSet), 500, 200, "gpt-4o"));

        var act = async () => await stage.ExecuteAsync(InputFor("stride"), None);

        await act.Should().ThrowAsync<PipelineStageException>()
            .Where(ex => ex.ErrorCode == "ANALYZE_FAILED");
    }

    // ── RunAllMethodsAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task RunAllMethodsAsync_TwoMethods_ReturnsTwoSets()
    {
        var (stage, _, client) = BuildStage();
        var strideSet     = GoodCandidateSet("stride",     ["API"]);
        var abuseCaseSet  = GoodCandidateSet("abuse_case", ["API"]);

        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                ResponseFor(strideSet),
                ResponseFor(abuseCaseSet));

        var classification = new ClassificationResult(
            Categories: ["standard_web_app"],
            SelectedMethods:
            [
                new("stride",     "required", true,  ["analyze"]),
                new("abuse_case", "required", true,  ["analyze"])
            ],
            ModelRoutingPlan: new("gpt-4o", "gpt-4o-mini", "gpt-4o"));

        var results = await stage.RunAllMethodsAsync(MinimalCanonical("API"), classification, None);

        results.Should().HaveCount(2);
        results.Select(r => r.Method).Should().BeEquivalentTo(["stride", "abuse_case"]);
    }

    [Fact]
    public async Task RunAllMethodsAsync_SingleMethod_ReturnsOneSet()
    {
        var (stage, _, client) = BuildStage();
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(GoodCandidateSet("stride", ["API"])));

        var classification = new ClassificationResult(
            Categories: ["standard_web_app"],
            SelectedMethods: [new("stride", "required", true, ["analyze"])],
            ModelRoutingPlan: new("gpt-4o", "gpt-4o-mini", "gpt-4o"));

        var results = await stage.RunAllMethodsAsync(MinimalCanonical("API"), classification, None);

        results.Should().HaveCount(1);
    }

    // ── Token budget ──────────────────────────────────────────────────────────

    [Fact]
    public async Task VeryLargeInput_ExceedsTokenBudget_ThrowsBeforeLlmCall()
    {
        var (stage, _, client) = BuildStage();

        // Build a canonical model so large it exceeds the 12,288 token budget
        var hugeComponents = Enumerable.Range(0, 3000)
            .Select(i => new CanonicalComponent($"Component{i}", "service", "A very long description that adds tokens", []))
            .ToArray();

        var bigModel = new CanonicalModel(
            SystemPurpose: null,
            Components: hugeComponents,
            Actors: [], ExternalSystems: [], DataStores: [], DataFlows: [],
            TrustBoundaries: [], NetworkExposure: "internet_facing",
            AuthenticationMethods: [], AuthorizationModel: null, SessionModel: null,
            MachineIdentities: [], PrivilegedPaths: [], TenantModel: null,
            SensitiveDataTypes: [], SecretsUsage: [], AsyncFlows: [], BackgroundJobs: [],
            HasLoggingMonitoring: false, AiLlmBoundaries: [], Assumptions: [], Gaps: [],
            ClarificationQuestions: []);

        var act = async () => await stage.ExecuteAsync(
            new AnalyzeInput("stride", bigModel, MinimalClassification()), None);

        await act.Should().ThrowAsync<PipelineStageException>();

        await client.DidNotReceive().CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }
}
