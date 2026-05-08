using System.Text.Json;
using Microsoft.Extensions.Options;
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
    ILogger<NormalizeStage> logger,
    IOptions<StageMaxOutputTokensOptions> stageTokenOpts) : IPipelineStage<NormalizeInput, CanonicalModel>
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
            var deterministic = TryDeterministicNormalize(
                input.Parsed, input.ApplicationDescription, input.ArchitectureDescription);

            if (deterministic is not null && deterministic.DataFlows.Length > 0)
            {
                // A5: Enrich the structurally-extracted model with security context the
                // deterministic parser cannot produce (assumptions, gaps, privileged paths, etc.)
                var enriched = await EnrichWithLlmAsync(
                    deterministic, input.ApplicationDescription, input.ArchitectureDescription, ct);

                logger.LogInformation(
                    "NORMALIZE complete (deterministic+enrichment). Components={Components} Actors={Actors} DataStores={DataStores} DataFlows={DataFlows} Gaps={Gaps} Assumptions={Assumptions}",
                    enriched.Components.Length, enriched.Actors.Length,
                    enriched.DataStores.Length, enriched.DataFlows.Length,
                    enriched.Gaps.Length, enriched.Assumptions.Length);
                return enriched;
            }

            logger.LogInformation(
                "Deterministic normalize yielded insufficient structure; falling back to LLM. ArtifactType={ArtifactType}",
                input.ArtifactType);
        }

        var model = llmFactory.GetStrongModel();
        var llmClient = llmFactory.GetForModel(model);

        var parsedJson = JsonSerializer.Serialize(input.Parsed, SerializeOptions);
        var userPrompt = PromptTemplates.BuildNormalizeUser(
            parsedJson, input.ArtifactType,
            input.ApplicationDescription, input.ArchitectureDescription);

        // Token budget: raised to 30,000 to accommodate large-context models (GPT-5+).
        TokenEstimator.AssertWithinBudget(PromptTemplates.NormalizeSystem, userPrompt, 30_000, "NORMALIZE");

        var request = new LlmRequest(
            SystemPrompt: PromptTemplates.NormalizeSystem,
            UserPrompt: userPrompt,
            Model: model,
            Temperature: 0.2f,
            MaxTokens: stageTokenOpts.Value.Normalize.ToMaxTokens());

        var (output, inputTokens, outputTokens) = await StageRetryHelper.ExecuteWithRetryAsync<CanonicalModel>(
            llmClient, request, Validate, "NORMALIZE_FAILED", MaxAttempts, logger, ct);

        // User-supplied context fields are injected here — not part of the LLM's output schema
        output = output with
        {
            ApplicationDescription = input.ApplicationDescription,
            ArchitectureDescription = input.ArchitectureDescription
        };

        logger.LogInformation(
            "NORMALIZE complete (LLM). Components={Components} DataFlows={DataFlows} Gaps={Gaps} " +
            "InputTokens={InputTokens} OutputTokens={OutputTokens}",
            output.Components.Length, output.DataFlows.Length, output.Gaps.Length,
            inputTokens, outputTokens);

        return output;
    }

    // A5: LLM enrichment for the deterministic path — fills security fields the
    // structural parser cannot produce. Non-fatal: skeletal model returned on failure.
    private async Task<CanonicalModel> EnrichWithLlmAsync(
        CanonicalModel skeletal,
        string? applicationDescription,
        string? architectureDescription,
        CancellationToken ct)
    {
        var model = llmFactory.GetStrongModel();
        var llmClient = llmFactory.GetForModel(model);

        var structuralJson = JsonSerializer.Serialize(skeletal, SerializeOptions);
        var userPrompt = PromptTemplates.BuildNormalizeEnrichUser(
            structuralJson, applicationDescription, architectureDescription);

        TokenEstimator.AssertWithinBudget(PromptTemplates.NormalizeEnrichSystem, userPrompt, 30_000, "NORMALIZE_ENRICH");

        var request = new LlmRequest(
            SystemPrompt: PromptTemplates.NormalizeEnrichSystem,
            UserPrompt: userPrompt,
            Model: model,
            Temperature: 0.2f,
            MaxTokens: stageTokenOpts.Value.NormalizeEnrich.ToMaxTokens());

        try
        {
            var (enrichment, inputTokens, outputTokens) = await StageRetryHelper.ExecuteWithRetryAsync<EnrichmentOutput>(
                llmClient, request, ValidateEnrichment, "NORMALIZE_ENRICH_FAILED", 2, logger, ct);

            logger.LogInformation(
                "NORMALIZE enrichment complete. Assumptions={A} Gaps={G} PrivilegedPaths={P} InputTokens={IT} OutputTokens={OT}",
                enrichment.Assumptions.Length, enrichment.Gaps.Length, enrichment.PrivilegedPaths.Length,
                inputTokens, outputTokens);

            return skeletal with
            {
                DeploymentContext            = enrichment.DeploymentContext ?? skeletal.DeploymentContext,
                TrustBoundaries              = enrichment.TrustBoundaries.Length > 0 ? enrichment.TrustBoundaries : skeletal.TrustBoundaries,
                Assumptions                  = enrichment.Assumptions.Length > 0 ? enrichment.Assumptions : skeletal.Assumptions,
                Gaps                         = enrichment.Gaps.Length > 0 ? enrichment.Gaps : skeletal.Gaps,
                PrivilegedPaths              = enrichment.PrivilegedPaths.Length > 0 ? enrichment.PrivilegedPaths : skeletal.PrivilegedPaths,
                ClarificationQuestions       = enrichment.ClarificationQuestions.Length > 0 ? enrichment.ClarificationQuestions : skeletal.ClarificationQuestions,
                SensitiveDataTypes           = enrichment.SensitiveDataTypes.Length > 0 ? enrichment.SensitiveDataTypes : skeletal.SensitiveDataTypes,
                SecretsUsage                 = enrichment.SecretsUsage.Length > 0 ? enrichment.SecretsUsage : skeletal.SecretsUsage,
                HasLoggingMonitoring         = enrichment.HasLoggingMonitoring,
                UntrustedContentProcessors   = enrichment.UntrustedContentProcessors.Length > 0 ? enrichment.UntrustedContentProcessors : (skeletal.UntrustedContentProcessors ?? []),
                OutboundInternetComponents   = enrichment.OutboundInternetComponents.Length > 0 ? enrichment.OutboundInternetComponents : (skeletal.OutboundInternetComponents ?? []),
                FederatedIdentityProviders   = enrichment.FederatedIdentityProviders.Length > 0 ? enrichment.FederatedIdentityProviders : (skeletal.FederatedIdentityProviders ?? []),
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NORMALIZE enrichment failed — returning skeletal model. This is non-critical.");
            return skeletal;
        }
    }

    private sealed record EnrichmentOutput(
        DeploymentContext? DeploymentContext,
        CanonicalTrustBoundary[] TrustBoundaries,
        Assumption[] Assumptions,
        Gap[] Gaps,
        PrivilegedPath[] PrivilegedPaths,
        ClarificationQuestion[] ClarificationQuestions,
        string[] SensitiveDataTypes,
        SecretsUsage[] SecretsUsage,
        bool HasLoggingMonitoring,
        string[] UntrustedContentProcessors,
        string[] OutboundInternetComponents,
        string[] FederatedIdentityProviders);

    private static string? ValidateEnrichment(EnrichmentOutput o)
    {
        if (o.TrustBoundaries is null)             return "trustBoundaries is null";
        if (o.Assumptions is null)                 return "assumptions is null";
        if (o.Gaps is null)                        return "gaps is null";
        if (o.PrivilegedPaths is null)             return "privilegedPaths is null";
        if (o.ClarificationQuestions is null)      return "clarificationQuestions is null";
        if (o.SensitiveDataTypes is null)          return "sensitiveDataTypes is null";
        if (o.SecretsUsage is null)                return "secretsUsage is null";
        if (o.UntrustedContentProcessors is null)  return "untrustedContentProcessors is null";
        if (o.OutboundInternetComponents is null)  return "outboundInternetComponents is null";
        if (o.FederatedIdentityProviders is null)  return "federatedIdentityProviders is null";
        return null;
    }

    private static CanonicalModel? TryDeterministicNormalize(
        ParseOutput parsed,
        string? applicationDescription,
        string? architectureDescription)
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
                            IsDataStoreSensitive(label),
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
                SystemPurpose: applicationDescription?.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?.Trim(),
                Components: components.ToArray(),
                Actors: actors.ToArray(),
                ExternalSystems: externalSystems.ToArray(),
                DataStores: dataStores.ToArray(),
                DataFlows: dataFlows,
                TrustBoundaries: DetectTrustBoundaries(actors, components, externalSystems, dataStores, parsed.RawBoundaries),
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
                ClarificationQuestions: [],
                ApplicationDescription: applicationDescription,
                ArchitectureDescription: architectureDescription,
                DeploymentContext: DetectDeploymentContext(parsed));
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

    // Used for flow labels — checks whether the flow carries sensitive data based on label keywords.
    private static bool ContainsSensitiveData(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.ToLowerInvariant();
        return t.Contains("password") || t.Contains("credential") || t.Contains("token")
            || t.Contains("secret") || t.Contains("sas") || t.Contains("customer");
    }

    // Used for data store labels — more permissive than flow label check.
    // Databases, object stores, message buses, and log sinks in customer-facing systems
    // almost universally contain sensitive data; default true for structural storage types.
    private static bool IsDataStoreSensitive(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return false;
        var l = label.ToLowerInvariant();
        return l.Contains("sql") || l.Contains("database") || l.Contains("storage")
            || l.Contains("blob") || l.Contains("vault") || l.Contains("log")
            || l.Contains("analytics") || l.Contains("queue") || l.Contains("bus")
            || l.Contains("cosmos") || l.Contains("redis") || l.Contains("table")
            || ContainsSensitiveData(label);
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

    // Builds trust boundaries from raw diagram boundaries when present; falls back to
    // synthetic boundaries derived from element categories (external/internal/data tier).
    private static CanonicalTrustBoundary[] DetectTrustBoundaries(
        List<CanonicalActor> actors,
        List<CanonicalComponent> components,
        List<CanonicalExternalSystem> externalSystems,
        List<CanonicalDataStore> dataStores,
        RawBoundary[] rawBoundaries)
    {
        if (rawBoundaries.Length > 0)
        {
            return rawBoundaries
                .Where(rb => !string.IsNullOrWhiteSpace(rb.Label))
                .Select(rb => new CanonicalTrustBoundary(
                    rb.Label.Trim(),
                    rb.ContainedElements,
                    InferBoundaryType(rb.Label, rb.BoundaryHints)))
                .ToArray();
        }

        var boundaries = new List<CanonicalTrustBoundary>();

        var externalLabels = actors.Select(a => a.Label)
            .Concat(externalSystems.Select(e => e.Label))
            .ToArray();
        if (externalLabels.Length > 0)
            boundaries.Add(new CanonicalTrustBoundary(
                "External / Internet Boundary", externalLabels, "internet_facing"));

        if (components.Count > 0)
            boundaries.Add(new CanonicalTrustBoundary(
                "Internal Services Boundary",
                components.Select(c => c.Label).ToArray(),
                "internal"));

        if (dataStores.Count > 0)
            boundaries.Add(new CanonicalTrustBoundary(
                "Data Tier Boundary",
                dataStores.Select(d => d.Label).ToArray(),
                "data_tier"));

        return boundaries.ToArray();
    }

    private static string InferBoundaryType(string label, string[] hints)
    {
        var l = label.ToLowerInvariant();
        var hs = new HashSet<string>(hints ?? [], StringComparer.OrdinalIgnoreCase);
        if (hs.Contains("vpc") || l.Contains("vpc"))                                   return "vpc";
        if (hs.Contains("dmz") || l.Contains("dmz"))                                   return "dmz";
        if (hs.Contains("untrusted") || l.Contains("internet") || l.Contains("extern")) return "internet_facing";
        if (hs.Contains("trusted") || l.Contains("internal"))                          return "internal";
        if (l.Contains("data") || l.Contains("db") || l.Contains("database"))          return "data_tier";
        if (l.Contains("ml") || l.Contains("ai") || l.Contains("model"))               return "ml_boundary";
        return "unknown";
    }

    // Keyword-based detection of deployment environment and infra controls from diagram labels.
    // Results seed the DeploymentContext that users can review and adjust before confirming.
    private static DeploymentContext DetectDeploymentContext(ParseOutput parsed)
    {
        var allText = string.Join(" ",
            parsed.RawElements.Select(e => e.Label)
            .Concat(parsed.RawFlows.Select(f => f.Label ?? ""))
            .Concat(parsed.RawFlows.Select(f => f.From ?? ""))
            .Concat(parsed.RawFlows.Select(f => f.To ?? "")))
            .ToLowerInvariant();

        var environment = "unknown";
        if (HasAny(allText, "aws", "amazon", "s3", "ec2", "lambda", "dynamodb", "cloudfront", "ecs", "eks"))
            environment = "aws";
        else if (HasAny(allText, "azure", "cosmos", "servicebus", "apim", "keyvault", "aks"))
            environment = "azure";
        else if (HasAny(allText, "gcp", "google cloud", "bigquery", "pub/sub", "cloud run", "gke"))
            environment = "gcp";
        else if (HasAny(allText, "on-prem", "on_prem", "on premise", "datacenter", "data center"))
            environment = "on_prem";

        var containerized = HasAny(allText, "docker", "container", "kubernetes", "k8s", "pod", "helm", "ecs", "eks", "aks", "gke");
        var serverless = HasAny(allText, "lambda", "function", "cloud run", "azure function", "serverless", "faas");

        var controls = new List<string>();
        if (HasAny(allText, "waf", "web application firewall")) controls.Add("waf");
        if (HasAny(allText, "cdn", "cloudfront", "fastly", "akamai", "cloudflare")) controls.Add("cdn");
        if (HasAny(allText, "api gateway", "api-gateway", "apigw", "apim")) controls.Add("api_gateway");
        if (HasAny(allText, "load balancer", "alb", "elb", "nlb", "ingress")) controls.Add("load_balancer");
        if (HasAny(allText, "ddos", "shield", "ddos protection", "ddos mitigation")) controls.Add("ddos_protection");

        return new DeploymentContext(environment, containerized, serverless, controls.ToArray());
    }

    private static bool HasAny(string haystack, params string[] needles)
        => needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));

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
