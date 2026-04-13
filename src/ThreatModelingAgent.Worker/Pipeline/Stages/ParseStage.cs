using System.Text;
using System.Text.Json;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Worker.Llm;
using ThreatModelingAgent.Worker.Pipeline.Contracts;
using ThreatModelingAgent.Worker.Pipeline.Prompts;

namespace ThreatModelingAgent.Worker.Pipeline.Stages;

/// <summary>
/// Stage 2 — PARSE.
///
/// Reads the artifact from blob storage and uses an LLM to extract its raw structure
/// into a typed ParseOutput.
///
/// Model selection (spec §4 Stage 2):
///   image    → gpt-4o with vision (multimodal)
///   all else → low-cost model (gpt-4o-mini / claude-haiku-4-5)
///
/// Retry: up to 3 attempts on schema validation failure (spec §6).
/// Fails with PARSE_FAILED after max retries.
///
/// SECURITY:
/// - Architecture content injected as delimited data in user message (prompt injection prevention)
/// - LLM output validated against ParseOutput schema before use (CLAUDE.md §16.5)
/// - Blob content NEVER logged (CLAUDE.md §16.6)
/// - No org_id or tenant context in prompts (CLAUDE.md §16.3)
/// </summary>
public sealed class ParseStage(
    IBlobStorage blobStorage,
    ILlmClientFactory llmFactory,
    ILogger<ParseStage> logger) : IPipelineStage<ParseInput, ParseOutput>
{
    private const int MaxAttempts = 3;
    private const int MaxTextBytes = 80_000; // ~20k tokens; hard cap before INPUT_TOO_LARGE

    public async Task<ParseOutput> ExecuteAsync(ParseInput input, CancellationToken ct)
    {
        await using var stream = await blobStorage.DownloadAsync(input.BlobPath, ct);

        string? imageBase64 = null;
        string? imageMediaType = null;
        string artifactContent;

        if (input.ArtifactType == "image")
        {
            // Read image for vision call
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            imageBase64 = Convert.ToBase64String(bytes);
            imageMediaType = DetectImageMediaType(bytes);
            artifactContent = "[image attached as base64]"; // placeholder in text; actual image in content parts
        }
        else
        {
            // Read text artifact — enforce size cap before sending to LLM (spec §7 token budget)
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            if (ms.Length > MaxTextBytes)
                throw new PipelineStageException("INPUT_TOO_LARGE",
                    $"Artifact size {ms.Length} bytes exceeds PARSE stage limit of {MaxTextBytes} bytes.");

            artifactContent = Encoding.UTF8.GetString(ms.ToArray());
        }

        var model = input.ArtifactType == "image"
            ? llmFactory.GetStrongModel()  // vision requires gpt-4o or equivalent multimodal model
            : llmFactory.GetLowCostModel();

        var llmClient = llmFactory.GetForModel(model);
        var userPrompt = PromptTemplates.BuildParseUser(input.ArtifactType, artifactContent, input.LowConfidenceArtifactType);

        var request = new LlmRequest(
            SystemPrompt: PromptTemplates.ParseSystem,
            UserPrompt: userPrompt,
            Model: model,
            Temperature: 0f,
            MaxTokens: 4096,
            ImageBase64: imageBase64,
            ImageMediaType: imageMediaType);

        var (output, inputTokens, outputTokens) = await StageRetryHelper.ExecuteWithRetryAsync<ParseOutput>(
            llmClient, request, Validate, "PARSE_FAILED", MaxAttempts, logger, ct);

        logger.LogInformation(
            "PARSE complete. ArtifactType={ArtifactType} Elements={ElementCount} Confidence={Confidence} " +
            "InputTokens={InputTokens} OutputTokens={OutputTokens}",
            input.ArtifactType, output.RawElements.Length, output.ExtractionConfidence,
            inputTokens, outputTokens);

        return output;
    }

    private static string? Validate(ParseOutput o)
    {
        if (o.RawElements is null) return "rawElements is null";
        if (o.RawFlows is null)    return "rawFlows is null";
        if (o.RawBoundaries is null) return "rawBoundaries is null";
        if (string.IsNullOrWhiteSpace(o.ExtractionConfidence)) return "extractionConfidence is missing";
        if (o.ExtractionConfidence is not ("high" or "medium" or "low"))
            return $"extractionConfidence has invalid value: {o.ExtractionConfidence}";
        return null;
    }

    private static string DetectImageMediaType(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8) return "image/jpeg";
        if (bytes.Length >= 3 && bytes[0] == 0x47 && bytes[1] == 0x49) return "image/gif";
        return "image/png"; // safe default
    }
}
