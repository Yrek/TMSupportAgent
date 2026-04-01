# LLM Workflow Specification

**Status:** Draft  
**Spec ref:** [01-product.md](01-product.md) §9 (model routing), §10 (clarification), §11 (threats), §19 (output)  
**Architecture ref:** [02-architecture.md](02-architecture.md) §8 (LLM routing)  
**Security ref:** [CLAUDE.md](../../CLAUDE.md) §16, [06-security.md](06-security.md)  
**Version:** 0.1  
**Date:** 2026-03-31

---

## 1. Scope

This document specifies:
- The typed input/output contract for each pipeline stage
- Model selection rules per stage
- Prompt template structure (structure and constraints — not the prompt text itself)
- Stage chaining and worker orchestration
- Error handling and retry policy
- Re-analysis flow after user corrections
- Security constraints for LLM usage

It does not specify prompt text. Prompt text is implementation detail, versioned in code, and subject to evaluation regression testing (spec §20).

---

## 2. Principles

- **Each stage is a pure function**: typed input → typed output. Stages do not share state other than through their declared outputs.
- **LLM output is always untrusted**: every stage output is validated against its output schema before being used downstream (CLAUDE.md §16.5).
- **Cheapest model sufficient for the task**: strong models are reserved for security-critical reasoning. Routing lower-complexity work to cheap models is not a trade-off — it is the correct design.
- **Fail closed**: a stage that cannot produce a valid output MUST fail the job, not silently continue with partial output.
- **No tenant data in prompts**: `org_id` and tenant context are never included in prompts. They are applied server-side when persisting outputs.
- **No secrets in prompts** (CLAUDE.md §16.3): credentials, connection strings, or tokens MUST NOT appear in any prompt.

---

## 3. Pipeline Overview

```
          ┌───────────────────────────────────────────────────────────┐
          │  Worker service                                           │
          │                                                           │
Artifact ─►  STAGE 1: DETECT       ─►  artifact_type                │
          │                                                           │
          │  STAGE 2: PARSE        ─►  ParsedArtifact               │
          │                                                           │
          │  STAGE 3: NORMALIZE    ─►  CanonicalModel               │
          │                                                           │
          │  [PERSIST + NOTIFY USER: status = AWAITING_REVIEW]       │
          │                                                           │
          │  [WAIT for user confirmation via API]                     │
          │                                                           │
          │  [APPLY user corrections from architecture_corrections]   │
          │                                                           │
          │  STAGE 4: CLASSIFY     ─►  ClassificationResult         │
          │                                                           │
          │  STAGE 5: ANALYZE      ─►  ThreatCandidates[]           │
          │  (one sub-stage per selected method, may parallelize)    │
          │                                                           │
          │  STAGE 6: SYNTHESIZE   ─►  FinalOutput                  │
          │                                                           │
          │  [PERSIST output: status = COMPLETE | PARTIAL]           │
          └───────────────────────────────────────────────────────────┘
```

---

## 4. Stage Contracts

### Stage 1 — DETECT

**Purpose:** Determine artifact type from uploaded file. No LLM required.

**Input:**
```typescript
{
  blobPath: string;        // /{org_id}/uploads/{job_id}/original.{ext}
  mimeType: string;        // from multipart upload; treated as a hint, not authoritative
  filename: string;        // original filename before rename; used for extension hint only
  fileSizeBytes: number;
}
```

**Output:**
```typescript
{
  artifactType: 'image' | 'plantuml' | 'mermaid' | 'drawio' | 'text';
  detectionMethod: 'magic_bytes' | 'extension' | 'content_sniff';
  confidence: 'high' | 'medium' | 'low';
}
```

**Rules:**
- Magic bytes take precedence over extension for image detection
- PlantUML detected by `@startuml` / `@enduml` markers
- Mermaid detected by diagram-type keywords (`graph`, `sequenceDiagram`, `flowchart`, etc.)
- Draw.io detected by XML with `mxfile` or `mxGraph` root element
- If confidence is `low`, the artifact type is still used but the normalization stage receives a `lowConfidenceArtifactType: true` flag and adjusts accordingly
- Files that cannot be classified MUST fail the job immediately with `error_code: UNSUPPORTED_ARTIFACT_TYPE`

**Model:** None (deterministic)

---

### Stage 2 — PARSE

**Purpose:** Extract a structured intermediate representation from the artifact. Output is not yet the canonical model — it is the raw material for normalization.

**Input:**
```typescript
{
  artifactType: ArtifactType;
  blobPath: string;
  lowConfidenceArtifactType: boolean;
  systemInstruction: string;  // versioned template; injected by worker
}
```

**Output:**
```typescript
{
  rawElements: RawElement[];
  rawFlows: RawFlow[];
  rawBoundaries: RawBoundary[];
  rawDescription: string;        // freeform extraction for ambiguous inputs
  parserNotes: string;           // parser observations for normalizer
  extractionConfidence: 'high' | 'medium' | 'low';
}

interface RawElement {
  label: string;
  elementHints: string[];        // e.g. ['database', 'external', 'api']
  rawProperties: Record<string, string>;
}

interface RawFlow {
  from: string;                  // matches element label
  to: string;
  label: string | null;
  flowHints: string[];
}

interface RawBoundary {
  label: string;
  containedElements: string[];
  boundaryHints: string[];
}
```

**Model selection:**

| Artifact type | Model | Reason |
|---|---|---|
| `image` | `gpt-4o` (vision) | Image understanding requires multimodal |
| `plantuml` | `gpt-4o-mini` or `claude-haiku-4-5` | Structured text; low reasoning needed |
| `mermaid` | `gpt-4o-mini` or `claude-haiku-4-5` | Structured text |
| `drawio` | `gpt-4o-mini` or `claude-haiku-4-5` | XML parsing + label extraction |
| `text` | `gpt-4o-mini` or `claude-haiku-4-5` | Initial extraction; normalization handles complexity |

**Prompt template structure (mandatory constraints):**
- System message: role definition (architecture parser), output schema, what NOT to do (do not invent elements, do not interpret intent, do not add security judgements)
- User message: artifact content (image or text), artifact type, parser notes if `lowConfidenceArtifactType`
- Output format: JSON matching `ParseOutput` schema above; validated on receipt
- Max tokens: 4,096 output
- Temperature: 0 (deterministic extraction)

**Retry:** Up to 3 attempts on schema validation failure. Fail job on third failure with `error_code: PARSE_FAILED`.

---

### Stage 3 — NORMALIZE

**Purpose:** Transform the raw parsed representation into the canonical system model (spec §5). This is the most reasoning-intensive stage — it infers architectural intent, identifies trust boundaries, classifies data flows, and surfaces gaps.

**Input:**
```typescript
{
  parsed: ParseOutput;
  artifactType: ArtifactType;
  systemInstruction: string;      // versioned template
}
```

**Output — `CanonicalModel`:**
```typescript
{
  systemPurpose: string | null;
  actors: CanonicalActor[];
  components: CanonicalComponent[];
  externalSystems: CanonicalExternalSystem[];
  dataStores: CanonicalDataStore[];
  dataFlows: CanonicalDataFlow[];
  trustBoundaries: CanonicalTrustBoundary[];
  networkExposure: NetworkExposure;
  authenticationMethods: AuthMethod[];
  authorizationModel: AuthzModel | null;
  sessionModel: SessionModel | null;
  machineIdentities: MachineIdentity[];
  privilegedPaths: PrivilegedPath[];
  tenantModel: TenantModel | null;
  sensitiveDataTypes: string[];
  secretsUsage: SecretsUsage[];
  asyncFlows: AsyncFlow[];
  backgroundJobs: BackgroundJob[];
  loggingMonitoring: LoggingMonitoring | null;
  aiLlmBoundaries: AiLlmBoundary[];
  assumptions: Assumption[];
  gaps: Gap[];
  clarificationQuestions: ClarificationQuestion[];
}

interface Gap {
  area: string;          // e.g. 'authentication_mechanism', 'trust_boundary', 'tenant_isolation'
  description: string;
  securityRelevance: 'critical' | 'high' | 'medium';
}

interface ClarificationQuestion {
  question: string;
  priority: 'high' | 'medium' | 'low';
  topic: string;         // from spec §10 topic list
  reason: string;        // why this question materially affects the threat model
}
```

**Model:** `gpt-4o` or `claude-sonnet-4-6` — MUST use strong model. Normalization involves trust-boundary reasoning and architectural inference from ambiguous input.

**Prompt template structure:**
- System message: role (security architect), canonical model schema, reasoning rules (fact/assumption separation, do not invent, surface all gaps, ask only high-value clarification questions)
- User message: parsed output as structured JSON; artifact type context
- Output format: JSON matching `CanonicalModel` schema; validated on receipt
- Max tokens: 8,192 output
- Temperature: 0.2 (allows structured inference but minimizes hallucination)

**Retry:** Up to 3 attempts on schema validation failure.

**Post-normalization:**
- Job status → `AWAITING_REVIEW`
- `CanonicalModel` and all elements persisted to database via `architectures` + `architecture_elements` tables
- User notified (in-app; polling)

---

### Stage 4 — CLASSIFY

**Purpose:** Classify the confirmed (user-reviewed) architecture into categories and select appropriate threat modeling methods.

**Input:**
```typescript
{
  confirmedModel: CanonicalModel;  // after user corrections applied
  userCorrections: UserCorrection[];
  systemInstruction: string;
}
```

**Output — `ClassificationResult`:**
```typescript
{
  categories: ArchitectureCategory[];   // from spec §6 enum
  selectedMethods: SelectedMethod[];
  modelRoutingPlan: ModelRoutingPlan;
}

type ArchitectureCategory =
  | 'standard_web_app'
  | 'api_centric'
  | 'integration_heavy'
  | 'microservice_distributed'
  | 'event_driven'
  | 'multi_tenant_saas'
  | 'privacy_heavy'
  | 'identity_complex'
  | 'cloud_native'
  | 'llm_enabled'
  | 'agentic_mcp_enabled';

interface SelectedMethod {
  method: string;           // 'stride' | 'linddun' | 'abuse_case' | 'tenant_isolation' | etc.
  rationale: string;        // why chosen for this specific architecture
  requiredBySpec: boolean;  // true if method is MUST for this category per spec §8
  stages: string[];         // which analyze sub-stages will use this method
}

interface ModelRoutingPlan {
  normalizeStage: ModelChoice;
  analyzeStageSecurity: ModelChoice;   // for security-critical methods
  analyzeStageLight: ModelChoice;      // for classification/tagging
  synthesizeStage: ModelChoice;
}

type ModelChoice = 'gpt-4o' | 'gpt-4o-mini' | 'claude-sonnet-4-6' | 'claude-haiku-4-5';
```

**Model:** `gpt-4o-mini` or `claude-haiku-4-5` — classification is structured pattern-matching against known categories.

**Method selection MUST follow spec §8 rules** — required methods per category are not optional. The LLM is not permitted to omit a required method.

**Validation:** After LLM output is received, a deterministic validator checks that all required methods per spec §8 are present for the classified categories. If a required method is missing, it is added by the validator (not re-prompted) and the omission is logged as a quality signal.

---

### Stage 5 — ANALYZE

**Purpose:** Generate threat candidates by applying each selected method to the confirmed canonical model.

This stage runs one sub-stage per selected method. Sub-stages MAY run in parallel where resource limits allow.

#### 5.1 Sub-stage contract (all methods)

**Input:**
```typescript
{
  method: string;                  // e.g. 'stride', 'tenant_isolation', 'abuse_case'
  canonicalModel: CanonicalModel;
  classificationResult: ClassificationResult;
  systemInstruction: string;       // method-specific versioned template
}
```

**Output — `ThreatCandidateSet`:**
```typescript
{
  method: string;
  candidates: ThreatCandidate[];
  rejectedCandidates: RejectedCandidate[];
}

interface ThreatCandidate {
  title: string;
  methodCategory: string;            // STRIDE category, LINDDUN category, etc.
  affectedElementLabels: string[];   // labels from canonical model; resolved to IDs after persist
  description: string;
  attackScenario: string;
  preconditions: string | null;
  impactedAssets: string[];
  securityImpact: string | null;
  privacyImpact: string | null;
  existingControls: string | null;
  controlGaps: string | null;
  confidence: 'high' | 'medium' | 'low';
  evidenceBasis: EvidenceBasis[];
  evidenceStrength: 'direct' | 'inferred' | 'assumption_dependent';
  assumptions: string | null;
  findingType: 'confirmed' | 'conditional';
}

interface RejectedCandidate {
  title: string;
  rejectionReason: 'insufficient_evidence' | 'duplicate_root_cause' | 'out_of_scope' | 'mitigation_confirmed' | 'too_speculative';
  rejectionNote: string;
}

type EvidenceBasis =
  | 'explicit_user_provided_fact'
  | 'extracted_architecture_fact'
  | 'confirmed_assumption'
  | 'architecture_derived_inference'
  | 'known_method_driven_risk_pattern';
```

**Model selection per method:**

| Method | Model | Reason |
|---|---|---|
| `stride` | `gpt-4o` or `claude-sonnet-4-6` | Security-critical; covers all STRIDE categories |
| `tenant_isolation` | `gpt-4o` or `claude-sonnet-4-6` | Security-critical; requires multi-step reasoning |
| `identity_session_delegation` | `gpt-4o` or `claude-sonnet-4-6` | Security-critical |
| `ai_llm_threat` | `gpt-4o` or `claude-sonnet-4-6` | Security-critical; AI-specific threats |
| `linddun` | `gpt-4o` or `claude-sonnet-4-6` | Privacy reasoning; medium-high complexity |
| `abuse_case` | `gpt-4o-mini` or `claude-haiku-4-5` | Pattern-driven; lower complexity for most abuse cases |
| `supply_chain` | `gpt-4o-mini` or `claude-haiku-4-5` | Pattern-driven |
| `availability_resilience` | `gpt-4o-mini` or `claude-haiku-4-5` | Pattern-driven |

**Prompt template constraints:**
- System message: method role, canonical model schema, threat schema, quality rules (per spec §11: reject vague/generic/untraceable threats; record rejected candidates with reason), output format
- User message: canonical model JSON (omitting any fields irrelevant to this method); method-specific focus instructions
- Architecture content is injected as data to be analyzed, delimited clearly from instructions
- Output format: JSON matching `ThreatCandidateSet`; validated on receipt
- Max tokens: 8,192 output per method
- Temperature: 0.3

**Validation on output:**
1. Schema validation — all required fields present
2. Traceability check — every `affectedElementLabels` entry MUST match a label in the canonical model; unmatched labels cause the threat to be moved to `rejectedCandidates` with reason `out_of_scope`
3. Quality floor — threats with `confidence: high` and `findingType: conditional` are downgraded to `findingType: confirmed` only if `evidenceStrength` is `direct` (otherwise remain conditional)

---

### Stage 6 — SYNTHESIZE

**Purpose:** Merge all method outputs into a final, deduplicated, prioritized threat list. Produce secure design recommendations. Assemble final output artifact.

**Input:**
```typescript
{
  allCandidateSets: ThreatCandidateSet[];   // from all analyze sub-stages
  canonicalModel: CanonicalModel;
  classificationResult: ClassificationResult;
  systemInstruction: string;
}
```

**Output — `FinalOutput`:**
```typescript
{
  systemSummary: string;
  architectureClassification: ArchitectureCategory[];
  selectedMethodsWithRationale: SelectedMethod[];
  modelRoutingSummary: Record<string, string>;  // stage → model used
  confirmedThreats: FinalThreat[];
  conditionalThreats: FinalThreat[];
  userAddedThreats: [];                         // empty at synthesis; populated via API later
  secureDesignRecommendations: DesignRecommendation[];
  prioritizedRemediationList: RemediationItem[];
  reviewQuestions: string[];
  analysisStatus: 'complete' | 'partial';
  partialReason: string | null;                 // populated if analysisStatus = 'partial'
}

interface FinalThreat {
  // All fields from ThreatCandidate
  // Plus:
  identifier: string;              // T-001, T-002, ...
  mitigations: Mitigation[];
  frameworkMappings: FrameworkMapping[];
}

interface DesignRecommendation {
  title: string;
  description: string;
  principles: string[];           // from spec §16: 'Secure by Default', 'Least Privilege', etc.
  affectedElementLabels: string[];
}

interface RemediationItem {
  threatIdentifier: string;
  title: string;
  priority: 'critical' | 'high' | 'medium' | 'low';
  mitigationSummary: string;
}
```

**Model:** `gpt-4o` or `claude-sonnet-4-6` — synthesis requires judgment about duplicate root causes, merging, and producing coherent final output.

**Synthesis rules (enforced in prompt + validated deterministically):**
1. Threats sharing the same root cause, affected element, and attack path MUST be merged (spec §20)
2. Only confirmed + strongly supported findings appear in `confirmedThreats`; conditional findings go to `conditionalThreats`
3. `prioritizedRemediationList` MUST contain only items from `confirmedThreats`
4. Secure design recommendations MUST be mapped to at least one principle from spec §16
5. `analysisStatus = 'partial'` if any gap with `securityRelevance: 'critical'` was unresolved before analysis was triggered

**Framework mapping sub-step:**
After synthesis LLM call, a separate cheap-model call maps each final threat to framework references (OWASP, ASVS, CIS, NCSC). This is separated because:
- It is pattern-matching, not security reasoning
- Using a cheap model here saves cost without quality trade-off
- It can run in parallel with secure design recommendation generation

**Final persist:**
- All threats written to `threats` table
- All mitigations written to `mitigations` table
- Framework mappings written to `framework_mappings` table
- Rejected candidates written to `rejected_candidates` table
- Full `FinalOutput` JSON written to blob: `/{org_id}/outputs/{job_id}/analysis.json`
- Job status → `COMPLETE` or `PARTIAL`

---

## 5. Re-analysis After User Corrections

When a user corrects the architecture model and re-triggers analysis:

1. Worker reads all `architecture_corrections` for the job, ordered by `created_at`
2. Corrections are applied to the `CanonicalModel` in memory before CLASSIFY stage
3. User-corrected values override extracted values for the same field/element
4. Pipeline resumes from CLASSIFY (not from PARSE or NORMALIZE — the parsed artifact is unchanged)
5. New threats are generated; previous system-generated threats for this job are replaced
6. User-added threats (source = 'user') are preserved across re-analysis
7. Job version is incremented

---

## 6. Error Handling

| Condition | Action |
|---|---|
| Schema validation failure (stage output) | Retry stage up to 3 times; fail job with `PARSE_FAILED` / `NORMALIZE_FAILED` / `ANALYZE_FAILED` / `SYNTHESIZE_FAILED` after max retries |
| LLM provider timeout (>30s) | Retry once with backoff; fail job if second attempt also times out |
| LLM provider error (5xx) | Retry up to 3 times with exponential backoff; fail job if all fail; dead-letter the Service Bus message |
| Token limit exceeded | Fail the stage with `error_code: INPUT_TOO_LARGE`; do not silently truncate input |
| Traceability validation failure (threats referencing unknown elements) | Move failing threats to `rejected_candidates`; continue synthesis with remaining threats |
| Critical gap unresolved at synthesis | Set `analysisStatus = 'partial'`; proceed with output; do not fail the job |

All error codes MUST be machine-readable strings with no internal path, stack trace, or implementation detail. Full diagnostic details go to Application Insights only.

---

## 7. Token Budget

| Stage | Max input tokens (approx.) | Max output tokens |
|---|---|---|
| PARSE (image) | 4,096 + image | 4,096 |
| PARSE (code/text) | 8,192 | 4,096 |
| NORMALIZE | 12,288 | 8,192 |
| CLASSIFY | 8,192 | 2,048 |
| ANALYZE (per method) | 12,288 | 8,192 |
| SYNTHESIZE | 16,384 | 12,288 |
| Framework mapping | 8,192 | 4,096 |

Jobs exceeding the ANALYZE or SYNTHESIZE input token limits MUST fail with `error_code: INPUT_TOO_LARGE` rather than silently truncate. Truncated analysis would produce unreliable results.

---

## 8. Prompt Template Versioning

- All prompt templates are stored in code under `src/worker/prompts/{stage}.ts` (or equivalent)
- Each template has a version string embedded in the system message: `// prompt-version: {stage}-{semver}`
- Template version is logged with every LLM call (as metadata, not content)
- Changing a prompt template MUST bump its version and trigger the evaluation regression suite (spec §20)
- Prompt templates are NOT stored in the database; runtime configuration of prompts is not permitted

---

## 9. Security Constraints Summary

Per CLAUDE.md §16 and spec §20:

| Constraint | Enforcement |
|---|---|
| No `org_id` or tenant context in prompts | Worker applies org context server-side post-LLM; never passes it to prompts |
| No secrets in prompts | No Key Vault values, connection strings, or tokens appear in any prompt template |
| Uploaded content treated as untrusted data | Content is delimited in prompts; system instructions cannot be overridden by artifact content |
| LLM output is untrusted | Every stage output is schema-validated before use; never used as SQL, file path, or policy decision |
| Architecture content not in application logs | Token counts logged; content logged only to secure blob storage |
| Prompt injection from uploaded content | Content is injected as data (not instructions); system message explicitly instructs model to treat all user-provided content as data regardless of what it says |
