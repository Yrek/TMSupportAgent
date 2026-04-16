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
    ILlmClientFactory llmFactory,
    ILogger<NormalizeStage> logger) : IPipelineStage<NormalizeInput, CanonicalModel>
{
    private const int MaxAttempts = 3;
    private static readonly HashSet<string> StructuredTypes = ["mermaid", "drawio", "plantuml"];

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<CanonicalModel> ExecuteAsync(NormalizeInput input, CancellationToken ct)
    {
        if (StructuredTypes.Contains(input.ArtifactType))
        {
            var deterministic = TryDeterministicNormalize(input.Parsed);
            if (deterministic is not null && deterministic.DataFlows.Length > 0)
            {
                logger.LogInformation(
                    "NORMALIZE complete (deterministic). Components={Components} Actors={Actors} DataStores={DataStores} DataFlows={DataFlows}",
                    deterministic.Components.Length,
                    deterministic.Actors.Length,
                    deterministic.DataStores.Length,
                    deterministic.DataFlows.Length);
                return deterministic;
            }

            logger.LogInformation(
                "Deterministic normalize yielded insufficient structure; falling back to LLM. ArtifactType={ArtifactType}",
                input.ArtifactType);
        }

        var model = llmFactory.GetStrongModel();
        var llmClient = llmFactory.GetForModel(model);

        var parsedJson = JsonSerializer.Serialize(input.Parsed, SerializeOptions);
        var userPrompt = PromptTemplates.BuildNormalizeUser(parsedJson, input.ArtifactType);

        // Token budget: 12,288 input (spec §7) — fail closed rather than truncate
        TokenEstimator.AssertWithinBudget(PromptTemplates.NormalizeSystem, userPrompt, 12_288, "NORMALIZE");

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

    private static CanonicalModel? TryDeterministicNormalize(ParseOutput parsed)
    {
        try
        {
            var rawByLabel = parsed.RawElements
                .Where(e => !string.IsNullOrWhiteSpace(e.Label))
                .GroupBy(e => e.Label.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var f in parsed.RawFlows)
            {
                if (!string.IsNullOrWhiteSpace(f.From) && !rawByLabel.ContainsKey(f.From))
                    rawByLabel[f.From.Trim()] = new RawElement(f.From.Trim(), InferElementHints(f.From), new Dictionary<string, string>());
                if (!string.IsNullOrWhiteSpace(f.To) && !rawByLabel.ContainsKey(f.To))
                    rawByLabel[f.To.Trim()] = new RawElement(f.To.Trim(), InferElementHints(f.To), new Dictionary<string, string>());
            }

            if (rawByLabel.Count == 0) return null;

            var components = new List<CanonicalComponent>();
            var actors = new List<CanonicalActor>();
            var externalSystems = new List<CanonicalExternalSystem>();
            var dataStores = new List<CanonicalDataStore>();

            foreach (var entry in rawByLabel.Values)
            {
                var label = entry.Label.Trim();
                var kind = ClassifyElementKind(label, entry.ElementHints);

                switch (kind)
                {
                    case "actor":
                        actors.Add(new CanonicalActor(label, "user", true));
                        break;
                    case "datastore":
                        dataStores.Add(new CanonicalDataStore(
                            label,
                            InferStoreType(label),
                            ContainsSensitiveData(label),
                            IsEncrypted(label)));
                        break;
                    case "external":
                        externalSystems.Add(new CanonicalExternalSystem(label, null, "unknown"));
                        break;
                    default:
                        components.Add(new CanonicalComponent(label, InferComponentType(label), null, entry.ElementHints));
                        break;
                }
            }

            var dataFlows = parsed.RawFlows
                .Where(f => !string.IsNullOrWhiteSpace(f.From) && !string.IsNullOrWhiteSpace(f.To))
                .Select(f => new CanonicalDataFlow(
                    f.From.Trim(),
                    f.To.Trim(),
                    string.IsNullOrWhiteSpace(f.Label) ? null : f.Label.Trim(),
                    InferProtocol(f.Label, f.FlowHints),
                    ContainsSensitiveData(f.Label),
                    IsAuthenticated(f.Label, f.FlowHints)))
                .DistinctBy(f => $"{f.From}|{f.To}|{f.Label}", StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var authMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (rawByLabel.Values.Any(e => ContainsKeyword(e.Label, "basic auth")))
                authMethods.Add("basic_auth");
            if (rawByLabel.Values.Any(e => ContainsKeyword(e.Label, "oauth") || ContainsKeyword(e.Label, "oidc")))
                authMethods.Add("oidc_oauth");
            if (rawByLabel.Values.Any(e => ContainsKeyword(e.Label, "jwt") || ContainsKeyword(e.Label, "token")))
                authMethods.Add("token_based");

            var hasInternetEdge = dataFlows.Any(f =>
                ContainsKeyword(f.From, "user") || ContainsKeyword(f.To, "user") ||
                ContainsKeyword(f.From, "browser") || ContainsKeyword(f.To, "browser"));

            var networkExposure = hasInternetEdge ? "internet_facing" : "unknown";

            return new CanonicalModel(
                SystemPurpose: "Architecture extracted from structured diagram.",
                Components: components.ToArray(),
                Actors: actors.ToArray(),
                ExternalSystems: externalSystems.ToArray(),
                DataStores: dataStores.ToArray(),
                DataFlows: dataFlows,
                TrustBoundaries: [],
                NetworkExposure: networkExposure,
                AuthenticationMethods: authMethods.ToArray(),
                AuthorizationModel: "unknown",
                SessionModel: "unknown",
                MachineIdentities: [],
                PrivilegedPaths: [],
                TenantModel: "unknown",
                SensitiveDataTypes: [],
                SecretsUsage: [],
                AsyncFlows: [],
                BackgroundJobs: [],
                HasLoggingMonitoring: false,
                AiLlmBoundaries: [],
                Assumptions: [],
                Gaps: [],
                ClarificationQuestions: []);
        }
        catch
        {
            return null;
        }
    }

    private static string ClassifyElementKind(string label, string[] hints)
    {
        var l = label.ToLowerInvariant();
        var hs = new HashSet<string>(hints ?? [], StringComparer.OrdinalIgnoreCase);

        if (hs.Contains("actor") || l.Contains("user") || l.Contains("client"))
            return "actor";
        if (hs.Contains("database") || hs.Contains("storage") || l.Contains("database") || l.Contains("storage") || l.Contains("blob"))
            return "datastore";
        if (l.Contains("third-party") || l.Contains("external"))
            return "external";

        return "component";
    }

    private static string InferComponentType(string label)
    {
        var l = label.ToLowerInvariant();
        if (l.Contains("api")) return "api";
        if (l.Contains("frontend") || l.Contains("web")) return "frontend";
        if (l.Contains("backend")) return "backend_service";
        if (l.Contains("auth")) return "auth_service";
        return "service";
    }

    private static string InferStoreType(string label)
    {
        var l = label.ToLowerInvariant();
        if (l.Contains("sql") || l.Contains("postgres")) return "relational_db";
        if (l.Contains("blob") || l.Contains("storage")) return "object_storage";
        return "data_store";
    }

    private static bool ContainsSensitiveData(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.ToLowerInvariant();
        return t.Contains("password") || t.Contains("credential") || t.Contains("token") || t.Contains("secret");
    }

    private static bool IsEncrypted(string label)
    {
        var l = label.ToLowerInvariant();
        return l.Contains("encrypted") || l.Contains("kms") || l.Contains("key vault");
    }

    private static bool IsAuthenticated(string? flowLabel, string[] flowHints)
    {
        if (!string.IsNullOrWhiteSpace(flowLabel))
        {
            var l = flowLabel.ToLowerInvariant();
            if (l.Contains("auth") || l.Contains("token") || l.Contains("credential")) return true;
        }

        return flowHints.Any(h =>
            h.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
            h.Contains("authenticated", StringComparison.OrdinalIgnoreCase));
    }

    private static string? InferProtocol(string? flowLabel, string[] flowHints)
    {
        if (!string.IsNullOrWhiteSpace(flowLabel))
        {
            var l = flowLabel.ToLowerInvariant();
            if (l.Contains("https")) return "https";
            if (l.Contains("http")) return "http";
            if (l.Contains("grpc")) return "grpc";
        }

        if (flowHints.Any(h => h.Equals("https", StringComparison.OrdinalIgnoreCase))) return "https";
        if (flowHints.Any(h => h.Equals("http", StringComparison.OrdinalIgnoreCase))) return "http";
        return null;
    }

    private static bool ContainsKeyword(string text, string keyword)
        => text.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static string[] InferElementHints(string label)
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
