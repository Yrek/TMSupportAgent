using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ThreatModelingAgent.Worker.Llm;
using ThreatModelingAgent.Worker.Pipeline.Contracts;
using ThreatModelingAgent.Worker.Pipeline.Stages;

namespace ThreatModelingAgent.Worker.Tests.Pipeline;

/// <summary>
/// Unit tests for ClassifyStage — Stage 4 of the pipeline.
///
/// ClassifyStage classifies the confirmed architecture and selects threat modeling methods.
/// Security invariants under test:
///   1. Always uses the low-cost model (classification is pattern-matching, not reasoning).
///   2. Required methods are enforced deterministically after the LLM call — the model cannot
///      omit required methods for a given architecture category.
///   3. LLM output is schema-validated — bad output retries then fails.
///   4. Token budget is enforced before the LLM call.
/// </summary>
public sealed class ClassifyStageTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (ClassifyStage Stage, ILlmClientFactory Factory, ILlmClient Client)
        BuildStage(string lowCostModel = "gpt-4o-mini")
    {
        var client  = Substitute.For<ILlmClient>();
        var factory = Substitute.For<ILlmClientFactory>();

        factory.GetLowCostModel().Returns(lowCostModel);
        factory.GetForModel(Arg.Any<string>()).Returns(client);

        var stage = new ClassifyStage(factory, NullLogger<ClassifyStage>.Instance);
        return (stage, factory, client);
    }

    private static ClassificationResult MakeResult(string[] categories, string[] methods) =>
        new(
            Categories: categories,
            SelectedMethods: methods.Select(m => new SelectedMethod(
                Method: m,
                Rationale: "auto",
                RequiredBySpec: false,
                Stages: ["analyze"])).ToArray(),
            ModelRoutingPlan: new ModelRoutingPlan(
                AnalyzeStageSecurity: "gpt-4o",
                AnalyzeStageLight:    "gpt-4o-mini",
                SynthesizeStage:      "gpt-4o"));

    private static LlmResponse ResponseFor(ClassificationResult result) =>
        new(JsonSerializer.Serialize(result, CamelCase), 400, 150, "gpt-4o-mini");

    private static ClassifyInput MinimalInput() => new(
        ConfirmedModel: BuildMinimalCanonical(),
        UserCorrections: []);

    private static CanonicalModel BuildMinimalCanonical() => new(
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
        Gaps:                  [],
        ClarificationQuestions: []);

    // ── Model selection ───────────────────────────────────────────────────────

    [Fact]
    public async Task ClassifyStage_AlwaysUsesLowCostModel()
    {
        var (stage, factory, client) = BuildStage(lowCostModel: "gpt-4o-mini");
        var result = MakeResult(["standard_web_app"], ["stride", "abuse_case"]);
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(result));

        await stage.ExecuteAsync(MinimalInput(), None);

        factory.Received(1).GetLowCostModel();
        factory.Received(1).GetForModel("gpt-4o-mini");
        factory.DidNotReceive().GetStrongModel();
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidInput_ReturnsClassificationResult()
    {
        var (stage, _, client) = BuildStage();
        var result = MakeResult(["standard_web_app"], ["stride", "abuse_case"]);
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(result));

        var output = await stage.ExecuteAsync(MinimalInput(), None);

        output.Categories.Should().Contain("standard_web_app");
        output.SelectedMethods.Should().NotBeEmpty();
        output.ModelRoutingPlan.Should().NotBeNull();
    }

    // ── EnforceRequiredMethods ────────────────────────────────────────────────

    [Fact]
    public async Task MultiTenantSaas_MissingRequiredMethods_AddsThemDeterministically()
    {
        var (stage, _, client) = BuildStage();
        // LLM only returned "stride" — missing "abuse_case" and "tenant_isolation"
        var result = MakeResult(["multi_tenant_saas"], ["stride"]);
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(result));

        var output = await stage.ExecuteAsync(MinimalInput(), None);

        var methodNames = output.SelectedMethods.Select(m => m.Method).ToArray();
        methodNames.Should().Contain("stride");
        methodNames.Should().Contain("abuse_case",
            because: "abuse_case is required for multi_tenant_saas");
        methodNames.Should().Contain("tenant_isolation",
            because: "tenant_isolation is required for multi_tenant_saas");
    }

    [Fact]
    public async Task LlmEnabled_MissingAiLlmThreat_AddsItWithRequiredBySpecTrue()
    {
        var (stage, _, client) = BuildStage();
        var result = MakeResult(["llm_enabled"], ["stride", "abuse_case"]);
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(result));

        var output = await stage.ExecuteAsync(MinimalInput(), None);

        var added = output.SelectedMethods.FirstOrDefault(m => m.Method == "ai_llm_threat");
        added.Should().NotBeNull(because: "ai_llm_threat is required for llm_enabled");
        added!.RequiredBySpec.Should().BeTrue();
    }

    [Fact]
    public async Task PrivacyHeavy_MissingLinddun_AddsIt()
    {
        var (stage, _, client) = BuildStage();
        var result = MakeResult(["privacy_heavy"], ["stride"]);
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(result));

        var output = await stage.ExecuteAsync(MinimalInput(), None);

        output.SelectedMethods.Select(m => m.Method)
            .Should().Contain("linddun", because: "linddun is required for privacy_heavy");
    }

    [Fact]
    public async Task AllRequiredMethodsPresent_NoMethodsAdded()
    {
        var (stage, _, client) = BuildStage();
        var result = MakeResult(["standard_web_app"], ["stride", "abuse_case"]);
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(result));

        var output = await stage.ExecuteAsync(MinimalInput(), None);

        // Should not have grown beyond the 2 methods returned by the LLM
        output.SelectedMethods.Should().HaveCount(2);
    }

    [Fact]
    public async Task MultipleCategories_AllRequiredMethodsEnforced()
    {
        var (stage, _, client) = BuildStage();
        // llm_enabled requires ai_llm_threat; privacy_heavy requires linddun
        var result = MakeResult(["llm_enabled", "privacy_heavy"], ["stride", "abuse_case"]);
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(result));

        var output = await stage.ExecuteAsync(MinimalInput(), None);

        var methods = output.SelectedMethods.Select(m => m.Method).ToArray();
        methods.Should().Contain("ai_llm_threat");
        methods.Should().Contain("linddun");
        // No duplicates — stride/abuse_case only appear once even though both categories require them
        methods.Should().OnlyHaveUniqueItems();
    }

    // ── Schema validation ─────────────────────────────────────────────────────

    [Fact]
    public async Task LlmReturnsEmptyCategories_RetriesAndThrows_ClassifyFailed()
    {
        var (stage, _, client) = BuildStage();
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse(
                "{\"categories\": [], \"selectedMethods\": [], \"modelRoutingPlan\": {}}",
                400, 100, "gpt-4o-mini"));

        var act = async () => await stage.ExecuteAsync(MinimalInput(), None);

        await act.Should().ThrowAsync<PipelineStageException>()
            .Where(ex => ex.ErrorCode == "CLASSIFY_FAILED");
        await client.Received(3).CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LlmReturnsMissingModelRoutingPlan_ThrowsClassifyFailed()
    {
        var (stage, _, client) = BuildStage();
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse(
                "{\"categories\": [\"standard_web_app\"], \"selectedMethods\": [{\"method\":\"stride\",\"rationale\":\"x\",\"requiredBySpec\":false,\"stages\":[\"analyze\"]}]}",
                400, 100, "gpt-4o-mini"));

        var act = async () => await stage.ExecuteAsync(MinimalInput(), None);

        await act.Should().ThrowAsync<PipelineStageException>()
            .Where(ex => ex.ErrorCode == "CLASSIFY_FAILED");
    }

    [Fact]
    public async Task LlmFailsFirstAttempt_SucceedsOnSecond()
    {
        var (stage, _, client) = BuildStage();
        var goodResult = MakeResult(["api_centric"], ["stride", "abuse_case"]);
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new LlmResponse("{}", 400, 50, "gpt-4o-mini"),
                ResponseFor(goodResult));

        var output = await stage.ExecuteAsync(MinimalInput(), None);

        output.Categories.Should().Contain("api_centric");
        await client.Received(2).CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    // ── UserCorrections forwarded in prompt ───────────────────────────────────

    [Fact]
    public async Task UserCorrections_AreIncludedInUserPrompt()
    {
        var (stage, _, client) = BuildStage();
        var result = MakeResult(["standard_web_app"], ["stride", "abuse_case"]);
        client.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(result));

        LlmRequest? captured = null;
        client.CompleteAsync(Arg.Do<LlmRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(ResponseFor(result));

        var inputWithCorrections = new ClassifyInput(
            ConfirmedModel: BuildMinimalCanonical(),
            UserCorrections:
            [
                new UserCorrection("elem-1", "type", "service", "database", "Update")
            ]);

        await stage.ExecuteAsync(inputWithCorrections, None);

        captured!.UserPrompt.Should().Contain("[USER_CORRECTIONS]");
        captured.UserPrompt.Should().Contain("[/USER_CORRECTIONS]");
    }
}
