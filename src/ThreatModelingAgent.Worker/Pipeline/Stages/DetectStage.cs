using System.Text;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Worker.Pipeline.Contracts;

namespace ThreatModelingAgent.Worker.Pipeline.Stages;

/// <summary>
/// Stage 1 — DETECT (deterministic, no LLM).
///
/// Determines artifact type from the uploaded blob using:
///   1. Magic bytes (takes precedence for images)
///   2. Content sniffing (PlantUML @startuml, Mermaid keywords, Draw.io XML)
///   3. Extension fallback
///
/// If the job already carries an ArtifactType from the API upload validator, that is
/// used as the starting hint but re-validated here.  Files that cannot be classified
/// fail immediately with UNSUPPORTED_ARTIFACT_TYPE (spec §4 Stage 1 Rules).
/// </summary>
public sealed class DetectStage(IBlobStorage blobStorage, ILogger<DetectStage> logger)
    : IPipelineStage<AnalysisJobMessage, DetectOutput>
{
    // All supported artifact types; anything else is UNSUPPORTED_ARTIFACT_TYPE
    private static readonly HashSet<string> SupportedTypes =
        ["image", "plantuml", "mermaid", "drawio", "text"];

    // Image magic bytes (offset 0)
    private static readonly (byte[] Magic, string Ext)[] ImageMagicBytes =
    [
        ([0xFF, 0xD8, 0xFF], "jpg"),
        ([0x89, 0x50, 0x4E, 0x47], "png"),
        ([0x47, 0x49, 0x46], "gif"),
        ([0x42, 0x4D], "bmp"),
        ([0x52, 0x49, 0x46, 0x46], "webp"), // RIFF header; webp has additional bytes at 8-11
    ];

    public async Task<DetectOutput> ExecuteAsync(AnalysisJobMessage message, CancellationToken ct)
    {
        // If ArtifactType is already set from API validation and is a known supported type,
        // use it as the authoritative hint (the API already ran the expensive blob read).
        var hintType = message.ArtifactType?.ToLowerInvariant();
        if (!string.IsNullOrEmpty(hintType) && SupportedTypes.Contains(hintType))
        {
            logger.LogInformation(
                "DETECT using pre-validated artifact type from API. Type={ArtifactType}",
                hintType);
            return new DetectOutput(hintType, "extension", "high", false);
        }

        // Re-detect by reading a prefix of the blob (avoid loading entire file)
        await using var stream = await blobStorage.DownloadAsync(message.ArtifactBlobPath, ct);
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
        var prefix = buffer[..bytesRead];

        // 1. Magic bytes — image detection
        foreach (var (magic, _) in ImageMagicBytes)
        {
            if (StartsWith(prefix, magic))
            {
                logger.LogInformation("DETECT: image (magic bytes)");
                return new DetectOutput("image", "magic_bytes", "high", false);
            }
        }

        // 2. Content sniffing for text-based artifacts
        var text = Encoding.UTF8.GetString(prefix);

        if (text.Contains("@startuml", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("DETECT: plantuml (content sniff)");
            return new DetectOutput("plantuml", "content_sniff", "high", false);
        }

        if (IsMermaid(text))
        {
            logger.LogInformation("DETECT: mermaid (content sniff)");
            return new DetectOutput("mermaid", "content_sniff", "high", false);
        }

        if (text.Contains("<mxfile", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<mxGraph", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("DETECT: drawio (content sniff)");
            return new DetectOutput("drawio", "content_sniff", "high", false);
        }

        // 3. Extension fallback — low confidence
        var ext = Path.GetExtension(message.ArtifactBlobPath).TrimStart('.').ToLowerInvariant();
        var (extType, extConfidence) = ext switch
        {
            "puml" or "pu"          => ("plantuml", "medium"),
            "md"                    => ("mermaid", "low"),
            "xml" or "drawio"       => ("drawio", "medium"),
            "jpg" or "jpeg"
                or "png" or "gif"
                or "bmp" or "webp"  => ("image", "medium"),
            "txt"                   => ("text", "medium"),
            _                       => ("unknown", "low")
        };

        if (extType == "unknown")
        {
            logger.LogWarning(
                "DETECT: unsupported artifact. Path={BlobPath}", message.ArtifactBlobPath);
            throw new PipelineStageException("UNSUPPORTED_ARTIFACT_TYPE",
                $"Cannot detect artifact type from blob path: {Path.GetFileName(message.ArtifactBlobPath)}");
        }

        logger.LogInformation(
            "DETECT: {Type} (extension fallback, confidence={Confidence})", extType, extConfidence);
        return new DetectOutput(extType, "extension", extConfidence, extConfidence == "low");
    }

    private static bool StartsWith(byte[] data, byte[] magic)
        => data.Length >= magic.Length && data.AsSpan(0, magic.Length).SequenceEqual(magic);

    private static bool IsMermaid(string text)
    {
        // Mermaid diagrams start with a diagram-type declaration
        ReadOnlySpan<string> keywords =
        [
            "graph ", "graph\n", "graph\r",
            "flowchart ",
            "sequenceDiagram",
            "classDiagram",
            "stateDiagram",
            "erDiagram",
            "gantt",
            "pie title",
            "gitGraph",
            "journey",
            "mindmap"
        ];

        foreach (var kw in keywords)
            if (text.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }
}
