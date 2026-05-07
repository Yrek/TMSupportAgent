using System.Text.Json;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;
using ThreatModelingAgent.Worker.Pipeline.Contracts;
using ThreatModelingAgent.Worker.Pipeline.Stages;
using DomainMitigation = ThreatModelingAgent.Domain.Entities.Mitigation;
using DomainFrameworkMapping = ThreatModelingAgent.Domain.Entities.FrameworkMapping;
using DomainRejectedCandidate = ThreatModelingAgent.Domain.Entities.RejectedCandidate;

namespace ThreatModelingAgent.Worker.Pipeline;

/// <summary>
/// Handles all database persistence of pipeline stage outputs.
/// Keeps JobOrchestrator focused on orchestration logic.
///
/// SECURITY:
/// - org_id is always threaded through every write — no cross-tenant writes possible.
/// - LLM output values are mapped via strict allow-lists (framework names, confidence, etc.)
///   before being stored. Unknown values are skipped or defaulted rather than crashing the
///   pipeline or persisting unvalidated model text. (CLAUDE.md §16.5, §6.3)
/// - No architecture content is logged — only record counts and IDs. (CLAUDE.md §16.6)
/// </summary>
internal sealed class PipelineDbPersistence(
    IArchitectureRepository architectures,
    IThreatRepository threats,
    ILogger<PipelineDbPersistence> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ── Phase 1: Persist architecture after NORMALIZE ─────────────────────────

    /// <summary>
    /// Creates and persists the Architecture record + ArchitectureElements after NORMALIZE.
    /// Returns the persisted Architecture so its ID is available for Phase 2.
    /// </summary>
    public async Task<Architecture> PersistArchitectureAsync(
        JobId jobId, OrgId orgId, CanonicalModel model, CancellationToken ct)
    {
        var deploymentContextJson = model.DeploymentContext is not null
            ? JsonSerializer.Serialize(model.DeploymentContext, JsonOptions)
            : "{}";

        var arch = Architecture.Create(
            jobId: jobId,
            orgId: orgId,
            systemPurpose: model.SystemPurpose,
            classification: [],  // populated after CLASSIFY in Phase 2
            assumptionsJson: JsonSerializer.Serialize(model.Assumptions, JsonOptions),
            gapsJson: JsonSerializer.Serialize(model.Gaps, JsonOptions),
            clarificationQuestionsJson: JsonSerializer.Serialize(model.ClarificationQuestions, JsonOptions),
            deploymentContextJson: deploymentContextJson);

        await architectures.AddAsync(arch, ct);

        // Components
        foreach (var c in model.Components)
            await architectures.AddElementAsync(
                CreateExtractedElement(arch.Id, orgId, ElementType.Component, c.Label, c.Description,
                    new { type = c.Type, tags = c.Tags }), ct);

        // Actors
        foreach (var a in model.Actors)
            await architectures.AddElementAsync(
                CreateExtractedElement(arch.Id, orgId, ElementType.Actor, a.Label, null,
                    new { type = a.Type, isExternal = a.IsExternal }), ct);

        // External systems
        foreach (var e in model.ExternalSystems)
            await architectures.AddElementAsync(
                CreateExtractedElement(arch.Id, orgId, ElementType.ExternalSystem, e.Label, null,
                    new { protocol = e.Protocol, trustLevel = e.TrustLevel }), ct);

        // Data stores
        foreach (var d in model.DataStores)
            await architectures.AddElementAsync(
                CreateExtractedElement(arch.Id, orgId, ElementType.DataStore, d.Label, null,
                    new { storeType = d.StoreType, containsSensitiveData = d.ContainsSensitiveData, encrypted = d.Encrypted }), ct);

        // Data flows (name derived from from→to since flows don't have a label field)
        foreach (var f in model.DataFlows)
            await architectures.AddElementAsync(
                CreateExtractedElement(arch.Id, orgId, ElementType.DataFlow,
                    $"{f.From} → {f.To}", f.Label,
                    new { from = f.From, to = f.To, protocol = f.Protocol, containsSensitiveData = f.ContainsSensitiveData, authenticated = f.Authenticated }), ct);

        // Async flows
        foreach (var af in model.AsyncFlows)
            await architectures.AddElementAsync(
                CreateExtractedElement(arch.Id, orgId, ElementType.DataFlow,
                    $"async: {af.From} → {af.To}", af.Label,
                    new { from = af.From, to = af.To, protocol = af.Protocol, isAsync = true, containsSensitiveData = af.ContainsSensitiveData, authenticated = af.Authenticated }), ct);

        // Trust boundaries
        foreach (var tb in model.TrustBoundaries)
            await architectures.AddElementAsync(
                CreateExtractedElement(arch.Id, orgId, ElementType.TrustBoundary, tb.Label, null,
                    new { boundaryType = tb.BoundaryType, containedComponents = tb.ContainedComponentLabels }), ct);

        // Background jobs
        foreach (var bj in model.BackgroundJobs)
            await architectures.AddElementAsync(
                CreateExtractedElement(arch.Id, orgId, ElementType.BackgroundJob, bj.Label, null,
                    new { trigger = bj.Trigger, accessedResources = bj.AccessedResources }), ct);

        // LLM/AI boundaries
        foreach (var ai in model.AiLlmBoundaries)
            await architectures.AddElementAsync(
                CreateExtractedElement(arch.Id, orgId, ElementType.LlmBoundary, ai.Label, null,
                    new { provider = ai.Provider, userInputPassedToModel = ai.UserInputPassedToModel, modelOutputUsedInResponse = ai.ModelOutputUsedInResponse, modelOutputUsedInToolCall = ai.ModelOutputUsedInToolCall, modelOutputWrittenToStore = ai.ModelOutputWrittenToStore }), ct);

        await architectures.SaveChangesAsync(ct);

        logger.LogInformation(
            "Architecture persisted to DB. JobId={JobId} ArchId={ArchId}",
            jobId, arch.Id);

        return arch;
    }

    // ── Phase 2: Update classification after CLASSIFY ─────────────────────────

    /// <summary>
    /// Updates the Architecture's classification after the CLASSIFY stage completes.
    /// </summary>
    public async Task UpdateArchitectureClassificationAsync(
        JobId jobId, OrgId orgId, string[] classification, CancellationToken ct)
    {
        var arch = await architectures.GetByJobIdAsync(jobId, orgId, ct)
            ?? throw new PipelineStageException("PERSIST_ERROR",
                "Architecture record not found for classification update.");

        arch.UpdateClassification(classification);
        await architectures.SaveChangesAsync(ct);
    }

    // ── Phase 2: Persist threats after SYNTHESIZE ─────────────────────────────

    /// <summary>
    /// Persists all threats, mitigations, framework mappings, and rejected candidates
    /// after the SYNTHESIZE stage. Element labels in FinalThreats are resolved to IDs
    /// using the architecture elements already in DB.
    /// </summary>
    public async Task PersistFinalOutputAsync(
        JobId jobId,
        OrgId orgId,
        FinalOutput output,
        ThreatCandidateSet[] allCandidateSets,
        CancellationToken ct)
    {
        // Build element label → ID map for AffectedElementIds resolution.
        // Uses case-insensitive lookup to tolerate minor label casing differences from the LLM.
        var arch = await architectures.GetByJobIdAsync(jobId, orgId, ct);
        var labelMap = arch is not null
            ? (await architectures.ListElementsAsync(arch.Id, orgId, ct))
                .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        // Persist confirmed + conditional threats
        var allFinalThreats = output.ConfirmedThreats
            .Select(t => (threat: t, findingType: FindingType.Confirmed))
            .Concat(output.ConditionalThreats
                .Select(t => (threat: t, findingType: FindingType.Conditional)));

        foreach (var (ft, findingType) in allFinalThreats)
        {
            var affectedIds = ft.AffectedElementLabels
                .Select(label => labelMap.TryGetValue(label, out var id) ? id : (Guid?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToArray();

            var riskRatingJson = ft.RiskRating is not null
                ? JsonSerializer.Serialize(ft.RiskRating, JsonOptions)
                : null;

            var threat = Threat.CreateFromPipeline(
                jobId: jobId,
                orgId: orgId,
                identifier: ft.Identifier,
                title: ft.Title,
                methodCategory: ft.MethodCategory,
                affectedElementIds: affectedIds,
                description: ft.Description,
                attackScenario: ft.AttackScenario,
                preconditions: ft.Preconditions,
                impactedAssets: ft.ImpactedAssets ?? [],
                securityImpact: ft.SecurityImpact,
                privacyImpact: ft.PrivacyImpact,
                existingControls: ft.ExistingControls,
                controlGaps: ft.ControlGaps,
                confidence: ParseConfidence(ft.Confidence),
                evidenceBasis: [],
                evidenceStrength: ParseEvidenceStrength(ft.EvidenceStrength),
                assumptions: null,
                findingType: findingType,
                riskRatingJson: riskRatingJson);

            await threats.AddAsync(threat, ct);

            foreach (var m in ft.Mitigations ?? [])
            {
                var priority = m.Priority?.ToLowerInvariant() ?? "medium";
                if (priority is not ("critical" or "high" or "medium" or "low"))
                    priority = "medium";

                var mitigation = DomainMitigation.Create(
                    threatId: threat.Id,
                    orgId: orgId,
                    title: m.Title,
                    description: m.Description,
                    priority: priority,
                    category: null);
                await threats.AddMitigationAsync(mitigation, ct);
            }

            foreach (var fm in ft.FrameworkMappings ?? [])
            {
                var normalizedFramework = NormalizeFramework(fm.Framework);
                if (normalizedFramework is null) continue; // Skip unknown frameworks — don't crash the pipeline

                var mapping = DomainFrameworkMapping.Create(
                    threatId: threat.Id,
                    orgId: orgId,
                    framework: normalizedFramework,
                    reference: fm.Reference,
                    mappingType: "direct");
                await threats.AddFrameworkMappingAsync(mapping, ct);
            }
        }

        // Persist rejected candidates from all method candidate sets
        foreach (var candidateSet in allCandidateSets)
        {
            foreach (var rc in candidateSet.RejectedCandidates ?? [])
            {
                // Only persist if reason is in the allowed set; skip others silently
                var reason = rc.RejectionReason?.ToLowerInvariant();
                if (reason is not ("insufficient_evidence" or "duplicate_root_cause" or
                    "out_of_scope" or "mitigation_confirmed" or "too_speculative"))
                    continue;

                var rejected = DomainRejectedCandidate.Create(
                    jobId: jobId,
                    orgId: orgId,
                    title: rc.Title,
                    methodCategory: candidateSet.Method,
                    rejectionReason: reason,
                    rejectionNote: rc.RejectionNote);
                await threats.AddRejectedCandidateAsync(rejected, ct);
            }
        }

        await threats.SaveChangesAsync(ct);

        logger.LogInformation(
            "Final output persisted to DB. JobId={JobId} Confirmed={Confirmed} Conditional={Conditional}",
            jobId, output.ConfirmedThreats.Length, output.ConditionalThreats.Length);
    }

    // ── Manual job helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="CanonicalModel"/> from user-defined elements stored in DB.
    /// Used by manual jobs (no file artifact) at the start of Phase 2 so the normal
    /// CLASSIFY → ANALYZE → SYNTHESIZE stages can proceed without modification.
    /// </summary>
    public async Task<CanonicalModel> BuildCanonicalModelFromElementsAsync(
        JobId jobId, OrgId orgId, CancellationToken ct)
    {
        var arch = await architectures.GetByJobIdAsync(jobId, orgId, ct)
            ?? throw new PipelineStageException("PERSIST_ERROR",
                "Architecture record not found for manual job processing.");

        var elements = await architectures.ListElementsAsync(arch.Id, orgId, ct);

        var components = new List<CanonicalComponent>();
        var actors = new List<CanonicalActor>();
        var externalSystems = new List<CanonicalExternalSystem>();
        var dataStores = new List<CanonicalDataStore>();
        var dataFlows = new List<CanonicalDataFlow>();
        var trustBoundaries = new List<CanonicalTrustBoundary>();
        var backgroundJobs = new List<BackgroundJob>();
        var llmBoundaries = new List<AiLlmBoundary>();

        foreach (var el in elements)
        {
            JsonElement? props = TryParseProperties(el.PropertiesJson);

            switch (el.ElementType)
            {
                case ElementType.Component:
                case ElementType.Identity:
                    components.Add(new CanonicalComponent(
                        Label: el.Name,
                        Type: GetString(props, "type") ?? "component",
                        Description: el.Description,
                        Tags: GetStringArray(props, "tags")));
                    break;

                case ElementType.Actor:
                    actors.Add(new CanonicalActor(
                        Label: el.Name,
                        Type: GetString(props, "type") ?? "user",
                        IsExternal: GetBool(props, "isExternal")));
                    break;

                case ElementType.ExternalSystem:
                    externalSystems.Add(new CanonicalExternalSystem(
                        Label: el.Name,
                        Protocol: GetString(props, "protocol"),
                        TrustLevel: GetString(props, "trustLevel")));
                    break;

                case ElementType.DataStore:
                    dataStores.Add(new CanonicalDataStore(
                        Label: el.Name,
                        StoreType: GetString(props, "storeType") ?? "unknown",
                        ContainsSensitiveData: GetBool(props, "containsSensitiveData"),
                        Encrypted: GetBool(props, "encrypted")));
                    break;

                case ElementType.DataFlow:
                    dataFlows.Add(new CanonicalDataFlow(
                        From: GetString(props, "from") ?? el.Name,
                        To: GetString(props, "to") ?? string.Empty,
                        Label: el.Description,
                        Protocol: GetString(props, "protocol"),
                        ContainsSensitiveData: GetBool(props, "containsSensitiveData"),
                        Authenticated: GetBool(props, "authenticated")));
                    break;

                case ElementType.TrustBoundary:
                    trustBoundaries.Add(new CanonicalTrustBoundary(
                        Label: el.Name,
                        ContainedComponentLabels: GetStringArray(props, "containedComponents"),
                        BoundaryType: GetString(props, "boundaryType") ?? "network"));
                    break;

                case ElementType.BackgroundJob:
                    backgroundJobs.Add(new BackgroundJob(
                        Label: el.Name,
                        Trigger: GetString(props, "trigger") ?? "unknown",
                        AccessedResources: GetStringArray(props, "accessedResources")));
                    break;

                case ElementType.LlmBoundary:
                    llmBoundaries.Add(new AiLlmBoundary(
                        Label: el.Name,
                        Provider: GetString(props, "provider") ?? "unknown",
                        UserInputPassedToModel: GetBool(props, "userInputPassedToModel"),
                        ModelOutputUsedInResponse: GetBool(props, "modelOutputUsedInResponse"),
                        ModelOutputUsedInToolCall: GetBool(props, "modelOutputUsedInToolCall"),
                        ModelOutputWrittenToStore: GetBool(props, "modelOutputWrittenToStore")));
                    break;
            }
        }

        return new CanonicalModel(
            SystemPurpose: arch.SystemPurpose,
            Components: components.ToArray(),
            Actors: actors.ToArray(),
            ExternalSystems: externalSystems.ToArray(),
            DataStores: dataStores.ToArray(),
            DataFlows: dataFlows.ToArray(),
            TrustBoundaries: trustBoundaries.ToArray(),
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
            BackgroundJobs: backgroundJobs.ToArray(),
            HasLoggingMonitoring: false,
            AiLlmBoundaries: llmBoundaries.ToArray(),
            Assumptions: TryDeserialize<Assumption[]>(arch.AssumptionsJson) ?? [],
            Gaps: TryDeserialize<Gap[]>(arch.GapsJson) ?? [],
            ClarificationQuestions: TryDeserialize<ClarificationQuestion[]>(arch.ClarificationQuestionsJson) ?? []);
    }

    // ── Re-analysis helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Loads the architecture record, its elements, and any corrections for Phase 2.
    /// Returns null if no architecture exists (first-run — no corrections possible).
    /// </summary>
    public async Task<(Architecture architecture, IReadOnlyList<ArchitectureElement> elements, IReadOnlyList<ArchitectureCorrection> corrections)?> TryGetArchitectureWithCorrectionsAsync(
        JobId jobId, OrgId orgId, CancellationToken ct)
    {
        var arch = await architectures.GetByJobIdAsync(jobId, orgId, ct);
        if (arch is null) return null;

        var elements = await architectures.ListElementsAsync(arch.Id, orgId, ct);
        var corrections = await architectures.ListCorrectionsAsync(arch.Id, orgId, ct);

        return (arch, elements, corrections);
    }

    /// <summary>
    /// Deletes all system-generated threats for a job and saves.
    /// User-added threats (source='user') are preserved.
    /// </summary>
    public async Task DeleteSystemThreatsAndSaveAsync(JobId jobId, OrgId orgId, CancellationToken ct)
    {
        await threats.DeleteSystemGeneratedAsync(jobId, orgId, ct);
        await threats.SaveChangesAsync(ct);
        await architectures.SaveChangesAsync(ct); // persist architecture version increment
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static ArchitectureElement CreateExtractedElement(
        Guid architectureId, OrgId orgId, ElementType type,
        string name, string? description, object properties)
        => ArchitectureElement.CreateExtracted(
            architectureId, orgId, type,
            name.Length > 255 ? name[..255] : name,  // guard against LLM output exceeding max length
            description,
            JsonSerializer.Serialize(properties, JsonOptions),
            ConfidenceLevel.High);

    private static ConfidenceLevel ParseConfidence(string? value) => value?.ToLowerInvariant() switch
    {
        "high" => ConfidenceLevel.High,
        "low" => ConfidenceLevel.Low,
        _ => ConfidenceLevel.Medium
    };

    private static EvidenceStrength ParseEvidenceStrength(string? value) => value?.ToLowerInvariant() switch
    {
        "direct" => EvidenceStrength.Direct,
        "assumption_dependent" => EvidenceStrength.AssumptionDependent,
        _ => EvidenceStrength.Inferred
    };

    // Framework name normalization is in the shared FrameworkNormalizer (CLAUDE.md §14 — no duplication).
    private static string? NormalizeFramework(string? framework) => FrameworkNormalizer.Normalize(framework);

    // ── Property extraction helpers (manual job canonical model build) ─────────

    private static readonly JsonSerializerOptions CaseInsensitiveOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static JsonElement? TryParseProperties(string json)
    {
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return null; }
    }

    private static string? GetString(JsonElement? el, string key)
    {
        if (el is null) return null;
        if (el.Value.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static bool GetBool(JsonElement? el, string key)
    {
        if (el is null) return false;
        if (el.Value.TryGetProperty(key, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
        }
        return false;
    }

    private static string[] GetStringArray(JsonElement? el, string key)
    {
        if (el is null) return [];
        if (!el.Value.TryGetProperty(key, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return [];
        return prop.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToArray();
    }

    private static T? TryDeserialize<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, CaseInsensitiveOptions); }
        catch { return default; }
    }
}
