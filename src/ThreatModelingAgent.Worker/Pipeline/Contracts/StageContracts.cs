using System.Text.Json.Serialization;

namespace ThreatModelingAgent.Worker.Pipeline.Contracts;

// ── Stage 1 — DETECT ─────────────────────────────────────────────────────────

public sealed record DetectOutput(
    string ArtifactType,       // image | plantuml | mermaid | drawio | text
    string DetectionMethod,    // magic_bytes | extension | content_sniff
    string Confidence,         // high | medium | low
    bool LowConfidence);

// ── Stage 2 — PARSE ──────────────────────────────────────────────────────────

public sealed record ParseInput(
    string ArtifactType,
    string BlobPath,
    bool LowConfidenceArtifactType);

public sealed record ParseOutput(
    RawElement[] RawElements,
    RawFlow[] RawFlows,
    RawBoundary[] RawBoundaries,
    string RawDescription,
    string ParserNotes,
    string ExtractionConfidence);  // high | medium | low

public sealed record RawElement(
    string Label,
    string[] ElementHints,
    Dictionary<string, string> RawProperties);

public sealed record RawFlow(
    string From,
    string To,
    string? Label,
    string[] FlowHints);

public sealed record RawBoundary(
    string Label,
    string[] ContainedElements,
    string[] BoundaryHints);

// ── Stage 3 — NORMALIZE ──────────────────────────────────────────────────────

public sealed record NormalizeInput(
    ParseOutput Parsed,
    string ArtifactType);

// CanonicalModel — the authoritative model used by all downstream stages.
// Produced by NORMALIZE, confirmed by user review, consumed by CLASSIFY+ANALYZE+SYNTHESIZE.
public sealed record CanonicalModel(
    string? SystemPurpose,
    CanonicalComponent[] Components,
    CanonicalActor[] Actors,
    CanonicalExternalSystem[] ExternalSystems,
    CanonicalDataStore[] DataStores,
    CanonicalDataFlow[] DataFlows,
    CanonicalTrustBoundary[] TrustBoundaries,
    string NetworkExposure,               // internet_facing | internal | hybrid | unknown
    string[] AuthenticationMethods,
    string? AuthorizationModel,           // rbac | abac | acl | none | unknown
    string? SessionModel,                 // stateful | stateless | hybrid | unknown
    string[] MachineIdentities,
    PrivilegedPath[] PrivilegedPaths,
    string? TenantModel,                  // single_tenant | multi_tenant | unknown
    string[] SensitiveDataTypes,
    SecretsUsage[] SecretsUsage,
    CanonicalDataFlow[] AsyncFlows,
    BackgroundJob[] BackgroundJobs,
    bool HasLoggingMonitoring,
    AiLlmBoundary[] AiLlmBoundaries,
    Assumption[] Assumptions,
    Gap[] Gaps,
    ClarificationQuestion[] ClarificationQuestions);

public sealed record CanonicalComponent(string Label, string Type, string? Description, string[] Tags);
public sealed record CanonicalActor(string Label, string Type, bool IsExternal);
public sealed record CanonicalExternalSystem(string Label, string? Protocol, string? TrustLevel);
public sealed record CanonicalDataStore(string Label, string StoreType, bool ContainsSensitiveData, bool Encrypted);
public sealed record CanonicalDataFlow(string From, string To, string? Label, string? Protocol, bool ContainsSensitiveData, bool Authenticated);
public sealed record CanonicalTrustBoundary(string Label, string[] ContainedComponentLabels, string BoundaryType);
public sealed record PrivilegedPath(string Description, string[] InvolvedComponentLabels, string ImpactIfCompromised);
public sealed record SecretsUsage(string ComponentLabel, string SecretType, string StorageLocation);
public sealed record BackgroundJob(string Label, string Trigger, string[] AccessedResources);
public sealed record AiLlmBoundary(string Label, string Provider, bool UserInputPassedToModel, bool ModelOutputUsedInResponse);
public sealed record Assumption(string Description, string ImpactIfWrong);
public sealed record Gap(string Area, string Description, string SecurityRelevance);  // critical | high | medium
public sealed record ClarificationQuestion(string Question, string Priority, string Topic, string Reason);

// ── Stage 4 — CLASSIFY ───────────────────────────────────────────────────────

/// <summary>
/// Represents a single user correction applied during the AWAITING_REVIEW window.
/// Passed to CLASSIFY so the model can distinguish user-confirmed facts from AI inferences.
/// </summary>
public sealed record UserCorrection(
    string ElementId,
    string Field,
    string? OldValue,
    string NewValue,
    string CorrectionType);   // Update | MarkIncorrect | MarkAssumed | MarkConfirmed | AddNote

public sealed record ClassifyInput(
    CanonicalModel ConfirmedModel,
    UserCorrection[] UserCorrections);

public sealed record ClassificationResult(
    string[] Categories,
    SelectedMethod[] SelectedMethods,
    ModelRoutingPlan ModelRoutingPlan);

public sealed record SelectedMethod(
    string Method,
    string Rationale,
    bool RequiredBySpec,
    string[] Stages);

public sealed record ModelRoutingPlan(
    string AnalyzeStageSecurity,   // model name for security-critical methods
    string AnalyzeStageLight,      // model name for pattern-matching methods
    string SynthesizeStage);       // model name for synthesis

// ── Stage 5 — ANALYZE ────────────────────────────────────────────────────────

public sealed record AnalyzeInput(
    string Method,
    CanonicalModel CanonicalModel,
    ClassificationResult ClassificationResult);

public sealed record ThreatCandidateSet(
    string Method,
    ThreatCandidate[] Candidates,
    RejectedCandidate[] RejectedCandidates);

public sealed record ThreatCandidate(
    string Title,
    string MethodCategory,
    string[] AffectedElementLabels,
    string Description,
    string AttackScenario,
    string? Preconditions,
    string[] ImpactedAssets,
    string? SecurityImpact,
    string? PrivacyImpact,
    string? ExistingControls,
    string? ControlGaps,
    string Confidence,             // high | medium | low
    string[] EvidenceBasis,
    string EvidenceStrength,       // direct | inferred | assumption_dependent
    string? Assumptions,
    string FindingType);           // confirmed | conditional

public sealed record RejectedCandidate(
    string Title,
    string RejectionReason,        // insufficient_evidence | duplicate_root_cause | out_of_scope | mitigation_confirmed | too_speculative
    string RejectionNote);

// ── Stage 6 — SYNTHESIZE ─────────────────────────────────────────────────────

public sealed record SynthesizeInput(
    ThreatCandidateSet[] AllCandidateSets,
    CanonicalModel CanonicalModel,
    ClassificationResult ClassificationResult);

public sealed record FinalOutput(
    string SystemSummary,
    string[] ArchitectureClassification,
    SelectedMethod[] SelectedMethodsWithRationale,
    Dictionary<string, string> ModelRoutingSummary,
    FinalThreat[] ConfirmedThreats,
    FinalThreat[] ConditionalThreats,
    FinalThreat[] UserAddedThreats,    // always empty at synthesis; populated via API after job completes
    DesignRecommendation[] SecureDesignRecommendations,
    RemediationItem[] PrioritizedRemediationList,
    string[] ReviewQuestions,
    string AnalysisStatus,             // complete | partial
    string? PartialReason);

public sealed record FinalThreat(
    string Identifier,
    string Title,
    string MethodCategory,
    string[] AffectedElementLabels,
    string Description,
    string AttackScenario,
    string? Preconditions,
    string[] ImpactedAssets,
    string? SecurityImpact,
    string? PrivacyImpact,
    string? ExistingControls,
    string? ControlGaps,
    string Confidence,
    string EvidenceStrength,
    string FindingType,
    Mitigation[] Mitigations,
    FrameworkMapping[] FrameworkMappings);

public sealed record Mitigation(
    string Title,
    string Description,
    string Priority);             // critical | high | medium | low

public sealed record FrameworkMapping(
    string Framework,            // OWASP | ASVS | CIS | NCSC | STRIDE
    string Reference,
    string? Notes);

public sealed record DesignRecommendation(
    string Title,
    string Description,
    string[] Principles,
    string[] AffectedElementLabels);

public sealed record RemediationItem(
    string ThreatIdentifier,
    string Title,
    string Priority,             // critical | high | medium | low
    string MitigationSummary);
