using System.Text.Json;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Worker.Llm;
using ThreatModelingAgent.Worker.Pipeline.Contracts;
using ThreatModelingAgent.Worker.Pipeline.Prompts;

namespace ThreatModelingAgent.Worker.Pipeline.Stages;

/// <summary>
/// Stage 3 — NORMALIZE.
///
/// Transforms the raw parsed representation (ParseOutput) into the typed CanonicalModel
/// using a strong reasoning model.
///
/// Model: strong (gpt-4o / claude-sonnet-4-6) — MUST per spec §4 Stage 3.
/// Retry: up to 3 attempts on schema validation failure.
/// Fails with NORMALIZE_FAILED after max retries.
///
/// After completion, the CanonicalModel is persisted to blob storage so it survives
/// across the AWAITING_REVIEW pause and is available for Phase 2 (CLASSIFY onward).
///
/// SECURITY:
/// - Parsed architecture content injected as delimited data (prompt injection prevention)
/// - LLM output validated against CanonicalModel schema before use (CLAUDE.md §16.5)
/// - No org_id or tenant context in prompts (CLAUDE.md §16.3)
/// - Content not logged; only token counts (CLAUDE.md §16.6)
/// </summary>
public sealed class NormalizeStage(
    LlmClientFactory llmFactory,
    IBlobStorage blobStorage,
    ILogger<NormalizeStage> logger) : IPipelineStage<NormalizeInput, CanonicalModel>
{
    private const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<CanonicalModel> ExecuteAsync(NormalizeInput input, CancellationToken ct)
    {
        var model = llmFactory.GetStrongModel();
        var llmClient = llmFactory.GetForModel(model);

        var parsedJson = JsonSerializer.Serialize(input.Parsed, SerializeOptions);
        var userPrompt = PromptTemplates.BuildNormalizeUser(parsedJson, input.ArtifactType);

        var request = new LlmRequest(
            SystemPrompt: PromptTemplates.NormalizeSystem,
            UserPrompt: userPrompt,
            Model: model,
            Temperature: 0.2f,
            MaxTokens: 8192);

        var (output, inputTokens, outputTokens) = await StageRetryHelper.ExecuteWithRetryAsync<CanonicalModel>(
            llmClient, request, Validate, "NORMALIZE_FAILED", MaxAttempts, logger, ct);

        logger.LogInformation(
            "NORMALIZE complete. Components={Components} DataFlows={DataFlows} Gaps={Gaps} " +
            "InputTokens={InputTokens} OutputTokens={OutputTokens}",
            output.Components.Length, output.DataFlows.Length, output.Gaps.Length,
            inputTokens, outputTokens);

        return output;
    }

    /// <summary>Persists the canonical model to blob for cross-phase availability.</summary>
    public static async Task PersistAsync(
        CanonicalModel model, Guid orgId, Guid jobId,
        IBlobStorage blobStorage, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(model, SerializeOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        using var stream = new MemoryStream(bytes);
        var path = $"{orgId}/intermediate/{jobId}/canonical.json";
        await blobStorage.UploadAsync(path, stream, "application/json", ct);
    }

    /// <summary>Reads the canonical model back from blob for Phase 2.</summary>
    public static async Task<CanonicalModel> LoadAsync(
        Guid orgId, Guid jobId,
        IBlobStorage blobStorage, CancellationToken ct)
    {
        var path = $"{orgId}/intermediate/{jobId}/canonical.json";
        await using var stream = await blobStorage.DownloadAsync(path, ct);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        var json = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        return JsonSerializer.Deserialize<CanonicalModel>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new PipelineStageException("NORMALIZE_FAILED", "Canonical model blob was empty.");
    }

    private static string? Validate(CanonicalModel o)
    {
        if (o.Components is null)   return "components is null";
        if (o.DataFlows is null)    return "dataFlows is null";
        if (o.TrustBoundaries is null) return "trustBoundaries is null";
        if (o.Gaps is null)         return "gaps is null";
        if (o.Assumptions is null)  return "assumptions is null";
        if (string.IsNullOrWhiteSpace(o.NetworkExposure)) return "networkExposure is missing";
        return null;
    }
}
