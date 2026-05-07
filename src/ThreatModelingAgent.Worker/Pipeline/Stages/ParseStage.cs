using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Worker.Llm;
using ThreatModelingAgent.Worker.Pipeline.Contracts;
using ThreatModelingAgent.Worker.Pipeline.Prompts;

namespace ThreatModelingAgent.Worker.Pipeline.Stages;

/// <summary>
/// Stage 2 - PARSE.
///
/// Deterministic-first parser for known structured formats (mermaid, drawio, plantuml),
/// with safe fallback to LLM parsing for unsupported/ambiguous cases.
///
/// SECURITY:
/// - Uploaded content is treated as untrusted data
/// - LLM output is schema-validated before use
/// - Prompt content is never logged
/// </summary>
public sealed class ParseStage(
    IBlobStorage blobStorage,
    ILlmClientFactory llmFactory,
    ILogger<ParseStage> logger,
    IOptions<StageMaxOutputTokensOptions> stageTokenOpts) : IPipelineStage<ParseInput, ParseOutput>
{
    private const int MaxAttempts = 3;
    private const int MaxTextBytes = 80_000; // ~20k tokens

    private static readonly HashSet<string> StructuredTypes =
        ["mermaid", "drawio", "plantuml"];

    public async Task<ParseOutput> ExecuteAsync(ParseInput input, CancellationToken ct)
    {
        await using var stream = await blobStorage.DownloadAsync(input.BlobPath, ct);

        string? imageBase64 = null;
        string? imageMediaType = null;
        string artifactContent;

        if (input.ArtifactType == "image")
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            imageBase64 = Convert.ToBase64String(bytes);
            imageMediaType = DetectImageMediaType(bytes);
            artifactContent = "[image attached as base64]";
        }
        else
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            if (ms.Length > MaxTextBytes)
            {
                throw new PipelineStageException(
                    "INPUT_TOO_LARGE",
                    $"Artifact size {ms.Length} bytes exceeds PARSE stage limit of {MaxTextBytes} bytes.");
            }

            artifactContent = Encoding.UTF8.GetString(ms.ToArray());

            if (StructuredTypes.Contains(input.ArtifactType))
            {
                var deterministic = TryDeterministicParse(input.ArtifactType, artifactContent);
                if (deterministic is not null &&
                    deterministic.RawElements.Length > 0 &&
                    deterministic.RawFlows.Length > 0)
                {
                    logger.LogInformation(
                        "PARSE complete (deterministic). ArtifactType={ArtifactType} Elements={ElementCount} Flows={FlowCount}",
                        input.ArtifactType, deterministic.RawElements.Length, deterministic.RawFlows.Length);
                    return AugmentWithUserContext(
                        deterministic,
                        input.ApplicationDescription,
                        input.ArchitectureDescription);
                }

                logger.LogInformation(
                    "Deterministic parse yielded insufficient structure; falling back to LLM. ArtifactType={ArtifactType}",
                    input.ArtifactType);
            }
        }

        var model = input.ArtifactType == "image"
            ? llmFactory.GetStrongModel()
            : llmFactory.GetLowCostModel();

        var llmClient = llmFactory.GetForModel(model);
        var userPrompt = PromptTemplates.BuildParseUser(
            input.ArtifactType,
            artifactContent,
            input.LowConfidenceArtifactType,
            input.ApplicationDescription,
            input.ArchitectureDescription);

        var request = new LlmRequest(
            SystemPrompt: PromptTemplates.ParseSystem,
            UserPrompt: userPrompt,
            Model: model,
            Temperature: 0f,
            MaxTokens: stageTokenOpts.Value.Parse,
            ImageBase64: imageBase64,
            ImageMediaType: imageMediaType);

        var (output, inputTokens, outputTokens) = await StageRetryHelper.ExecuteWithRetryAsync<ParseOutput>(
            llmClient, request, Validate, "PARSE_FAILED", MaxAttempts, logger, ct);

        logger.LogInformation(
            "PARSE complete (LLM). ArtifactType={ArtifactType} Elements={ElementCount} Confidence={Confidence} InputTokens={InputTokens} OutputTokens={OutputTokens}",
            input.ArtifactType,
            output.RawElements.Length,
            output.ExtractionConfidence,
            inputTokens,
            outputTokens);

        return AugmentWithUserContext(
            output,
            input.ApplicationDescription,
            input.ArchitectureDescription);
    }

    private static ParseOutput AugmentWithUserContext(
        ParseOutput output,
        string? applicationDescription,
        string? architectureDescription)
    {
        var contextParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(applicationDescription))
            contextParts.Add($"Application context: {applicationDescription.Trim()}");
        if (!string.IsNullOrWhiteSpace(architectureDescription))
            contextParts.Add($"Architecture context: {architectureDescription.Trim()}");

        if (contextParts.Count == 0) return output;

        var context = string.Join(" ", contextParts);
        var rawDescription = string.IsNullOrWhiteSpace(output.RawDescription)
            ? context
            : $"{output.RawDescription} {context}";

        var parserNotes = string.IsNullOrWhiteSpace(output.ParserNotes)
            ? "User-provided context was included."
            : $"{output.ParserNotes} User-provided context was included.";

        return output with
        {
            RawDescription = rawDescription,
            ParserNotes = parserNotes
        };
    }

    private static ParseOutput? TryDeterministicParse(string artifactType, string content)
    {
        try
        {
            return artifactType switch
            {
                "mermaid" => ParseMermaid(content),
                "drawio" => ParseDrawIo(content),
                "plantuml" => ParsePlantUml(content),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static ParseOutput ParseMermaid(string content)
    {
        var idToLabel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var elements = new Dictionary<string, RawElement>(StringComparer.OrdinalIgnoreCase);
        var flows = new List<RawFlow>();
        var boundaries = new List<RawBoundary>();

        var lines = content.Split('\n');

        var nodeRegex = new Regex(
            @"\b(?<id>[A-Za-z_][A-Za-z0-9_]*)\s*(?:\[\((?<l1>[^\)]*)\)\]|\[\[(?<l2>[^\]]*)\]\]|\[(?<l3>[^\]]*)\]|\(\((?<l4>[^\)]*)\)\)|\((?<l5>[^\)]*)\)|\{\{(?<l6>[^\}]*)\}\}|\{(?<l7>[^\}]*)\})",
            RegexOptions.Compiled);

        var edgeWithLabelRegex = new Regex(
            @"(?<from>[A-Za-z_][A-Za-z0-9_]*)\s*[-.=ox]+>\s*\|(?<label>[^\|]+)\|\s*(?<to>[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        var edgeRegex = new Regex(
            @"(?<from>[A-Za-z_][A-Za-z0-9_]*)\s*[-.=ox]+>\s*(?<to>[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("%%")) continue;
            if (line.StartsWith("classDef", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("class ", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("style ", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("linkStyle", StringComparison.OrdinalIgnoreCase)) continue;

            foreach (Match m in nodeRegex.Matches(line))
            {
                var id = m.Groups["id"].Value.Trim();
                var label = FirstNonEmpty(
                    m.Groups["l1"].Value,
                    m.Groups["l2"].Value,
                    m.Groups["l3"].Value,
                    m.Groups["l4"].Value,
                    m.Groups["l5"].Value,
                    m.Groups["l6"].Value,
                    m.Groups["l7"].Value);

                label = NormalizeDiagramLabel(string.IsNullOrWhiteSpace(label) ? id : label);
                idToLabel[id] = label;
                elements[label] = new RawElement(label, GuessElementHints(label), new Dictionary<string, string>());
            }

            if (line.StartsWith("subgraph ", StringComparison.OrdinalIgnoreCase))
            {
                var b = line["subgraph ".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(b))
                {
                    boundaries.Add(new RawBoundary(NormalizeDiagramLabel(b), [], ["logical_group"]));
                }
            }
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("%%")) continue;

            var m1 = edgeWithLabelRegex.Match(line);
            if (m1.Success)
            {
                var from = ResolveAlias(m1.Groups["from"].Value.Trim(), idToLabel);
                var to = ResolveAlias(m1.Groups["to"].Value.Trim(), idToLabel);
                var label = NormalizeDiagramLabel(m1.Groups["label"].Value.Trim());

                EnsureElement(from, elements);
                EnsureElement(to, elements);
                flows.Add(new RawFlow(from, to, label, GuessFlowHints(label)));
                continue;
            }

            var m2 = edgeRegex.Match(line);
            if (m2.Success)
            {
                var from = ResolveAlias(m2.Groups["from"].Value.Trim(), idToLabel);
                var to = ResolveAlias(m2.Groups["to"].Value.Trim(), idToLabel);

                EnsureElement(from, elements);
                EnsureElement(to, elements);
                flows.Add(new RawFlow(from, to, null, []));
            }
        }

        return new ParseOutput(
            RawElements: elements.Values.ToArray(),
            RawFlows: flows.ToArray(),
            RawBoundaries: boundaries.ToArray(),
            RawDescription: "Deterministically parsed from Mermaid syntax.",
            ParserNotes: "Deterministic parser used; aliases normalized to display labels.",
            ExtractionConfidence: "high");
    }

    private static ParseOutput ParseDrawIo(string content)
    {
        var idToLabel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var elements = new Dictionary<string, RawElement>(StringComparer.OrdinalIgnoreCase);
        var flows = new List<RawFlow>();

        var doc = XDocument.Parse(content, LoadOptions.None);

        foreach (var obj in doc.Descendants().Where(x => x.Name.LocalName == "object"))
        {
            var label = obj.Attribute("label")?.Value ?? obj.Attribute("value")?.Value;
            var cell = obj.Descendants().FirstOrDefault(x => x.Name.LocalName == "mxCell");
            var id = cell?.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(id)) continue;

            var normalized = NormalizeDiagramLabel(string.IsNullOrWhiteSpace(label) ? id : label);
            idToLabel[id] = normalized;
            elements[normalized] = new RawElement(normalized, GuessElementHints(normalized), new Dictionary<string, string>());
        }

        var mxCells = doc.Descendants().Where(x => x.Name.LocalName == "mxCell").ToList();

        foreach (var cell in mxCells.Where(c => c.Attribute("vertex")?.Value == "1"))
        {
            var id = cell.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(id) || idToLabel.ContainsKey(id)) continue;
            var value = cell.Attribute("value")?.Value;
            var normalized = NormalizeDiagramLabel(string.IsNullOrWhiteSpace(value) ? id : value);
            idToLabel[id] = normalized;
            elements[normalized] = new RawElement(normalized, GuessElementHints(normalized), new Dictionary<string, string>());
        }

        foreach (var edge in mxCells.Where(c => c.Attribute("edge")?.Value == "1"))
        {
            var sourceId = edge.Attribute("source")?.Value;
            var targetId = edge.Attribute("target")?.Value;
            if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(targetId)) continue;

            var from = ResolveAlias(sourceId, idToLabel);
            var to = ResolveAlias(targetId, idToLabel);
            var label = NormalizeDiagramLabel(edge.Attribute("value")?.Value ?? string.Empty);

            EnsureElement(from, elements);
            EnsureElement(to, elements);
            flows.Add(new RawFlow(from, to, string.IsNullOrWhiteSpace(label) ? null : label, GuessFlowHints(label)));
        }

        return new ParseOutput(
            RawElements: elements.Values.ToArray(),
            RawFlows: flows.ToArray(),
            RawBoundaries: [],
            RawDescription: "Deterministically parsed from Draw.io XML.",
            ParserNotes: "Deterministic parser used for mxCell vertices and edges.",
            ExtractionConfidence: flows.Count > 0 ? "high" : "medium");
    }

    private static ParseOutput ParsePlantUml(string content)
    {
        var idToLabel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var elements = new Dictionary<string, RawElement>(StringComparer.OrdinalIgnoreCase);
        var flows = new List<RawFlow>();

        var lines = content.Split('\n');

        var aliasRegex = new Regex(
            @"^\s*(?:actor|participant|component|database|node|cloud|rectangle|queue)?\s*(?:""(?<label1>[^""]+)""|\[(?<label2>[^\]]+)\]|(?<label3>[A-Za-z_][A-Za-z0-9_ ]*))\s+as\s+(?<id>[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        var edgeRegex = new Regex(
            @"(?<from>[A-Za-z_][A-Za-z0-9_]*)\s*[-.]+(?:left|right|up|down)?-*>+\s*(?<to>[A-Za-z_][A-Za-z0-9_]*)(?:\s*:\s*(?<label>.+))?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("@")) continue;
            if (line.StartsWith("'")) continue;

            var alias = aliasRegex.Match(line);
            if (!alias.Success) continue;

            var id = alias.Groups["id"].Value.Trim();
            var label = FirstNonEmpty(alias.Groups["label1"].Value, alias.Groups["label2"].Value, alias.Groups["label3"].Value);
            label = NormalizeDiagramLabel(string.IsNullOrWhiteSpace(label) ? id : label);
            idToLabel[id] = label;
            elements[label] = new RawElement(label, GuessElementHints(label), new Dictionary<string, string>());
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("'")) continue;

            var m = edgeRegex.Match(line);
            if (!m.Success) continue;

            var from = ResolveAlias(m.Groups["from"].Value.Trim(), idToLabel);
            var to = ResolveAlias(m.Groups["to"].Value.Trim(), idToLabel);
            var label = NormalizeDiagramLabel(m.Groups["label"].Value.Trim());

            EnsureElement(from, elements);
            EnsureElement(to, elements);
            flows.Add(new RawFlow(from, to, string.IsNullOrWhiteSpace(label) ? null : label, GuessFlowHints(label)));
        }

        return new ParseOutput(
            RawElements: elements.Values.ToArray(),
            RawFlows: flows.ToArray(),
            RawBoundaries: [],
            RawDescription: "Deterministically parsed from PlantUML syntax.",
            ParserNotes: "Deterministic parser used for aliases and arrows.",
            ExtractionConfidence: flows.Count > 0 ? "high" : "medium");
    }

    private static string ResolveAlias(string key, IReadOnlyDictionary<string, string> idToLabel)
        => idToLabel.TryGetValue(key, out var mapped) ? mapped : NormalizeDiagramLabel(key);

    private static void EnsureElement(string label, IDictionary<string, RawElement> elements)
    {
        if (elements.ContainsKey(label)) return;
        elements[label] = new RawElement(label, GuessElementHints(label), new Dictionary<string, string>());
    }

    private static string NormalizeDiagramLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var noTags = Regex.Replace(value, "<[^>]+>", " ");
        var decoded = System.Net.WebUtility.HtmlDecode(noTags);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    private static string[] GuessElementHints(string label)
    {
        var l = label.ToLowerInvariant();
        var hints = new List<string>();

        if (l.Contains("db") || l.Contains("database") || l.Contains("postgres") || l.Contains("sql")) hints.Add("database");
        if (l.Contains("api")) hints.Add("api");
        if (l.Contains("service") || l.Contains("backend")) hints.Add("service");
        if (l.Contains("queue") || l.Contains("bus")) hints.Add("queue");
        if (l.Contains("cache")) hints.Add("cache");
        if (l.Contains("user") || l.Contains("actor") || l.Contains("client")) hints.Add("actor");
        if (l.Contains("auth")) hints.Add("auth");
        if (l.Contains("storage") || l.Contains("blob") || l.Contains("bucket")) hints.Add("storage");

        return hints.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] GuessFlowHints(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return [];

        var l = label.ToLowerInvariant();
        var hints = new List<string>();

        if (l.Contains("https")) hints.Add("https");
        if (l.Contains("http")) hints.Add("http");
        if (l.Contains("tls")) hints.Add("encrypted");
        if (l.Contains("auth") || l.Contains("token") || l.Contains("credential")) hints.Add("authenticated");
        if (l.Contains("async")) hints.Add("async");

        return hints.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    private static string? Validate(ParseOutput o)
    {
        if (o.RawElements is null) return "rawElements is null";
        if (o.RawFlows is null) return "rawFlows is null";
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
        return "image/png";
    }
}
