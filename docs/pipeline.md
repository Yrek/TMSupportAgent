# Threat Modeling Pipeline

This document describes every stage of the analysis pipeline — what it does, how it works, what model it uses, and what can go wrong. Stages run in a fixed order. Each stage must succeed before the next begins. The pipeline pauses once for human review between Stage 3 and Stage 4.

---

## Overview

```
Upload → [1 DETECT] → [2 PARSE] → [3 NORMALIZE] → ⏸ Human Review ⏸
                                                         ↓
              [4 CLASSIFY] → [5 ANALYZE] → [6 SYNTHESIZE] → Result
```

| Stage | Name | Model | Job status |
|---|---|---|---|
| 1 | Detect | none (deterministic) | Parsing |
| 2 | Parse | low-cost (text) / strong (images) | Parsing |
| 3 | Normalize | strong + low-cost (enrichment) | Normalizing |
| — | Human review | — | AwaitingReview |
| 4 | Classify | low-cost | Classifying |
| 5 | Analyze | strong / low-cost per method | Analyzing |
| 6 | Synthesize | strong + low-cost (sub-steps) | Synthesizing |

**Strong model** = `gpt-5` / `claude-sonnet-4-6` (configured via `LlmRouting:StrongModel`)  
**Low-cost model** = `gpt-4o-mini` / `claude-haiku-4-5` (configured via `LlmRouting:LowCostModel`)

---

## Stage 1 — Detect

**Job status:** `Parsing`  
**Code:** `DetectStage.cs`  
**No LLM call — fully deterministic.**

Reads the first 8 KB of the uploaded blob and classifies the artifact type using three methods in priority order:

1. **Magic bytes** — image formats are identified by their binary header (JPEG `FF D8 FF`, PNG `89 50 4E 47`, GIF `47 49 46`, BMP `42 4D`, WebP `52 49 46 46`).
2. **Content sniff** — text artifacts are identified by keywords: `@startuml` → PlantUML; `graph`, `flowchart`, `sequenceDiagram`, etc. → Mermaid; `<mxfile>` or `<mxGraph>` → Draw.io.
3. **Extension fallback** — used only when sniffing fails. Marked as low-confidence, which causes Parse to note this and apply extra care.

If the API upload already validated the type, that is reused and no blob read is needed.

**Failure mode:** If the artifact cannot be classified at all, the job fails immediately with `UNSUPPORTED_ARTIFACT_TYPE`. No retry — it is a permanent user error.

**Supported types:** `image`, `plantuml`, `mermaid`, `drawio`, `text`

---

## Stage 2 — Parse

**Job status:** `Parsing`  
**Code:** `ParseStage.cs`  
**Model:** low-cost (text) / strong (images, because vision is needed)

Converts the raw artifact into a structured list of elements, flows, and boundaries.

### How it works

**Structured formats (Mermaid, Draw.io, PlantUML)** go through a deterministic parser first:
- **Mermaid** — regex-based extraction of node declarations and edge arrows; aliases (`A[User]`) are resolved to display labels.
- **Draw.io** — XML parse of `mxCell` vertices and edges.
- **PlantUML** — regex-based extraction of `as`-aliased declarations and arrows.

If the deterministic parser extracts at least one element and one flow, its output is used directly (no LLM call, lower cost, higher reliability).

If deterministic parsing yields insufficient structure, or the artifact is `text` or `image`, the LLM is called instead. For images the entire file is base64-encoded and sent as a vision message to the strong model. For text the raw content is injected as a delimited `[ARCHITECTURE_CONTENT]` block to prevent prompt injection.

**Output:** `ParseOutput` — lists of `RawElement`, `RawFlow`, `RawBoundary` plus extraction confidence.

**Limits:** Text artifacts are capped at 80 000 bytes (~20K tokens). Larger files fail with `INPUT_TOO_LARGE`.

**Retries:** Up to 3 LLM attempts on schema validation failure; then `PARSE_FAILED`.

**Token ceiling:** `StageMaxOutputTokens:Parse` (default 8 192).

---

## Stage 3 — Normalize

**Job status:** `Normalizing`  
**Code:** `NormalizeStage.cs`  
**Model:** strong (normalization) + low-cost (security enrichment)

Transforms the raw parse output into the **Canonical Model** — the authoritative typed representation that every downstream stage reads. This is the most important structural stage.

### Two-pass approach

**Pass 1 — Structure extraction (strong model or deterministic)**  
For structured formats (Mermaid, Draw.io, PlantUML) a deterministic normalizer builds the canonical model directly from `ParseOutput` without an LLM call. For text/image artifacts, the strong model reads `ParseOutput` and produces the structured model.

The canonical model captures:
- Components, actors, external systems, data stores, data flows, trust boundaries
- Network exposure, authentication methods, authorization model, session model
- Machine identities, secrets usage, async flows, background jobs
- Tenant model, sensitive data types, AI/LLM boundaries

**Pass 2 — Security enrichment (low-cost model)**  
A second LLM call takes the structurally-correct model and adds security-specific analysis that requires judgment:
- **Gaps** — missing controls (e.g. no row-level security, no JIT/PIM). Rated `critical`, `high`, or `medium`.
- **Assumptions** — inferences made where the diagram is ambiguous.
- **Privileged paths** — specific access paths with high blast radius if compromised.
- **Clarification questions** — open questions the reviewer should answer.
- **Untrusted content processors** — components that handle user-submitted files/messages.
- **Outbound internet components** — components with unrestricted outbound access (SSRF risk).
- **Federated identity providers** — external IdPs / federated tenant patterns.

The canonical model is then **persisted to blob storage** so it survives the pipeline pause and can be read by Phase 2 without re-running normalization.

**Token ceilings:** `StageMaxOutputTokens:Normalize` and `StageMaxOutputTokens:NormalizeEnrich`  
**Retries:** Up to 3 attempts per pass; then `NORMALIZE_FAILED`.

---

## ⏸ Human Review (AwaitingReview)

Between Stage 3 and Stage 4, the job pauses and the user is directed to the **Review** page.

The reviewer sees the canonical model rendered as an interactive architecture diagram and can:
- Confirm or correct element labels, types, and properties
- Mark elements as external/internal
- Add notes and context
- Delete elements added by mistake
- Answer clarification questions

Every change is recorded as a `UserCorrection`. These corrections are forwarded to Stage 4 so the LLM can distinguish confirmed facts from AI inferences.

The reviewer then clicks **Approve** to release the job into Phase 2 (`Classifying`).

---

## Stage 4 — Classify

**Job status:** `Classifying`  
**Code:** `ClassifyStage.cs`  
**Model:** low-cost (pattern-matching task)

Reads the confirmed canonical model and the user corrections, then decides:
1. Which **architecture categories** apply (e.g. `multi_tenant_saas`, `identity_complex`, `llm_enabled`).
2. Which **threat modeling methods** to run in Stage 5 (e.g. `stride`, `tenant_isolation`, `abuse_case`).
3. Which **model** to use per method — strong for security-critical analysis, low-cost for pattern-driven analysis.

### Method selection

The LLM selects up to 6 methods from the allowed list. A **deterministic enforcement pass** then runs in code: if the canonical model falls into a category that requires certain methods (e.g. `multi_tenant_saas` always requires `tenant_isolation`), those methods are added regardless of what the LLM chose. Omissions are logged as a quality signal.

A **`security_expert_baseline`** method is always injected — it runs without a framework lens and applies pure expert security judgment to catch threats that no framework specifically covers.

**Available methods:** `stride`, `linddun`, `abuse_case`, `tenant_isolation`, `identity_session_delegation`, `ai_llm_threat`, `vast`, `pasta`, `octave`, `trike`, `mitre_attack`, `owasp_cumulus`, `owasp_cornucopia`, `maestro`, `supply_chain`, `availability_resilience`

**Token ceiling:** `StageMaxOutputTokens:Classify`  
**Retries:** Up to 3 attempts; then `CLASSIFY_FAILED`.

---

## Stage 5 — Analyze

**Job status:** `Analyzing`  
**Code:** `AnalyzeStage.cs`  
**Model:** strong (security-critical methods) or low-cost (pattern-driven methods), one call per method

Runs one LLM call per selected method, all in parallel (capped by `AnalyzeThrottling:MaxConcurrentMethods`). Each call applies a different analytical lens to the canonical model and produces a list of threat candidates.

### Per-method model routing

**Strong model methods** (require precise reasoning about access control, trust boundaries, multi-step attack chains):  
`stride`, `tenant_isolation`, `identity_session_delegation`, `ai_llm_threat`, `linddun`, `maestro`, `mitre_attack`, `abuse_case`, `owasp_cumulus`, `owasp_cornucopia`, `supply_chain`, `security_expert_baseline`

**Low-cost model methods** (pattern-matching, lower reasoning depth needed):  
`availability_resilience`, `vast`, `pasta`, `octave`, `trike`

### What each candidate contains

Every threat candidate includes:
- Title and description
- Affected element labels (must match the canonical model exactly)
- Attack scenario (numbered step-by-step attacker path)
- Preconditions, impacted assets, existing controls, control gaps
- `findingType`: `confirmed` (direct evidence) or `conditional` (inferred)
- `evidenceStrength`: `direct`, `inferred`, or `assumption_dependent`
- OWASP Risk Rating: likelihood × impact → severity (`critical`/`high`/`medium`/`low`/`note`)
- `groupKey`: an attack-vector classifier from the allowed list (used by Stage 6 to prevent over-merging)

### Group keys

Group keys label the fundamental attack vector so that Stage 6 does not accidentally merge distinct threats. Examples:

| Key | Attack vector |
|---|---|
| `storage_shared_key` | Permanent account-level storage credential |
| `sas_token_access` | Delegated time-limited token (SAS, presigned URL) |
| `cicd_platform_permissions` | CI/CD identity with broad cloud platform roles |
| `cicd_external_api_token` | CI/CD secret for an external service (WAF, DNS, CDN) |
| `bola_request_parameter` | BOLA/IDOR via attacker-controlled request parameter |
| `no_database_rls` | Missing database row-level security |
| `break_glass_no_ca` | Break-glass account excluded from Conditional Access |
| `standing_operational_access` | Operational roles without JIT/PIM |
| `managed_identity_overpriv` | Workload identity with excessive permissions |
| `api_bypass_edge` | Backend reachable without passing through edge security |
| `sensitive_data_in_logs` | Credentials or tokens written to logs/telemetry |
| `cross_tenant_isolation_flaw` | Application-code-only tenant isolation |
| `supply_chain_ci_cd` | Dependency poisoning or build-step injection |
| `storage_prefix_isolation` | Shared storage container with prefix-only tenant isolation |
| `no_bulk_export_approval` | Bulk data export without approval/four-eyes workflow |
| `file_content_attack` | Malicious payload in uploaded file (archive bomb, XXE, formula injection) |
| `ssrf_imds` | SSRF to cloud instance metadata endpoint (169.254.169.254) |
| `xss_token_theft` | XSS via stored content stealing tokens from the browser |
| `federated_claim_manipulation` | Federated-tenant admin issuing tokens for another tenant |

### Post-LLM validation

After each method call, a deterministic check verifies that every `affectedElementLabel` in every candidate exists in the canonical model. Candidates referencing unknown elements are moved to `rejectedCandidates`.

**Limits:**  
- `AnalyzeThrottling:MaxConcurrentMethods` — parallel cap (default 4)
- `AnalyzeThrottling:MaxOutputTokens` — per-method output ceiling (default 64 000)
- `AnalyzeThrottling:InputBudgetPerMethod` — input token budget per call

**Retries:** Up to 5 attempts per method; then `ANALYZE_FAILED` for that method (other methods continue).

---

## Stage 6 — Synthesize

**Job status:** `Synthesizing`  
**Code:** `SynthesizeStage.cs`  
**Model:** strong (main synthesis) + low-cost (two sub-steps)

Merges all candidate sets from Stage 5 into a final deduplicated, prioritized threat model. This is the most token-intensive stage. It runs four operations in sequence:

### 6.1 Main synthesis (strong model)

The strong model receives all candidates from all methods and produces the final output:

- **Confirmed threats** — `findingType=confirmed`, `evidenceStrength=direct`. Go into the main threat list and the remediation list.
- **Conditional threats** — `findingType=conditional`. Shown separately; require more evidence before acting.
- Secure design recommendations
- Prioritized remediation list (confirmed threats only)
- Review questions for remaining ambiguity

**Merge rules:** Candidates with the same root cause, attack path, and affected elements may be merged. Candidates with **different group keys on the same element must never be merged** — each group key represents a distinct attack vector and mitigation.  

Hotspot pre-computation tells the model which elements were independently flagged by multiple methods, so it treats those as higher-confidence risks.

**Token budget:** `Synthesis:TokenCeiling` − `Synthesis:MaxOutputTokens` = available input tokens.

**Retries:** Up to 5 attempts; then `SYNTHESIZE_FAILED`.

### 6.2 Partial status enforcement (deterministic)

After synthesis, C# code checks the canonical model's gaps. If any gap is rated `critical` and was not resolved during normalization, `analysisStatus` is forced to `partial` regardless of what the LLM returned. A `partialReason` string is set explaining the incompleteness.

### 6.3 Post-synthesis diagnostics (deterministic, no LLM)

Two warning checks run in code:

**Cross-group-key merge check** — for each confirmed threat, finds candidates from its source methods whose element set is fully contained in the threat's element set, collects their distinct group keys, and warns if 2–5 distinct group keys are found. This signals a specific accidental merge of distinct attack vectors.

**Gap coverage check** — for each critical/high gap in the canonical model, checks whether any confirmed or conditional threat references the gap area by keyword. Logs a warning for uncovered gaps.

**Over-merge check** — counts how many distinct group keys appear across all confirmed candidates and compares to the confirmed threat count. If group keys outnumber threats, synthesis may have over-merged.

### 6.4 Adversarial review sub-step (low-cost model)

A separate cheap-model call asks: *"what attack paths are NOT covered by the threats already found?"*

It receives a stripped canonical model (structure and gaps only, no large text blobs) and the full confirmed + conditional threat list (title and description only). It returns up to 5 missed attack paths which are appended as conditional threats with identifiers `ADV-001`, `ADV-002`, etc., confidence `low`, and `methodCategory=AdversarialReview`.

This sub-step runs **before** framework mapping so the new threats also receive framework references.

Non-fatal: if the call fails or returns empty, the main synthesis output is unchanged.

**Token budget:** `Synthesis:ReviewInputBudget` / `Synthesis:ReviewMaxOutputTokens`

### 6.5 Framework mapping sub-step (low-cost model)

A second cheap-model call maps all final threats (confirmed + conditional + adversarial) to security framework references:

`stride`, `mitre_attack`, `owasp_top10`, `owasp_api_top10`, `asvs`, `cis_controls`, `ncsc`, `cwe`, and others.

Only framework values from the allowed list are accepted. Unknown framework values are silently discarded. The pipeline does not fail if this sub-step fails — framework mappings are supplementary.

**Token budget:** `Synthesis:FrameworkMappingInputBudget` / `Synthesis:FrameworkMappingMaxOutputTokens`

---

## Job status flow

```
Pending
  → Parsing          (Stage 1 DETECT + Stage 2 PARSE)
  → Normalizing      (Stage 3 NORMALIZE)
  → AwaitingReview   (paused — waiting for human)
  → Classifying      (Stage 4 CLASSIFY)
  → Analyzing        (Stage 5 ANALYZE)
  → Synthesizing     (Stage 6 SYNTHESIZE)
  → Complete         (all threats confirmed, no critical gaps)
  → Partial          (analysis done but critical gaps unresolved)
  → Failed           (unrecoverable error; errorCode set)
```

`Partial` is a valid final state. The threat model is usable but the reviewer should be aware that some areas were not fully analyzed due to architectural ambiguity.

---

## Configuration reference

All token ceilings and throttling values are in `appsettings.json` under the `Synthesis`, `AnalyzeThrottling`, and `StageMaxOutputTokens` sections, and can be overridden per environment in `appsettings.{Environment}.json`.

| Key | What it controls |
|---|---|
| `StageMaxOutputTokens:Parse` | PARSE LLM output ceiling |
| `StageMaxOutputTokens:Normalize` | NORMALIZE structure pass output ceiling |
| `StageMaxOutputTokens:NormalizeEnrich` | NORMALIZE enrichment pass output ceiling |
| `StageMaxOutputTokens:Classify` | CLASSIFY output ceiling |
| `AnalyzeThrottling:MaxConcurrentMethods` | Max parallel ANALYZE calls |
| `AnalyzeThrottling:MaxOutputTokens` | Per-method ANALYZE output ceiling |
| `AnalyzeThrottling:InputBudgetPerMethod` | Per-method ANALYZE input budget |
| `Synthesis:TokenCeiling` | Total token budget for main synthesis call |
| `Synthesis:MaxOutputTokens` | Main synthesis output ceiling |
| `Synthesis:FrameworkMappingInputBudget` | Framework mapping sub-step input budget |
| `Synthesis:FrameworkMappingMaxOutputTokens` | Framework mapping sub-step output ceiling |
| `Synthesis:ReviewInputBudget` | Adversarial review sub-step input budget |
| `Synthesis:ReviewMaxOutputTokens` | Adversarial review sub-step output ceiling |
| `LlmRouting:StrongModel` | Model name used for security-critical calls |
| `LlmRouting:LowCostModel` | Model name used for pattern-matching calls |

---

## Error codes

| Code | Stage | Meaning |
|---|---|---|
| `UNSUPPORTED_ARTIFACT_TYPE` | Detect | File type cannot be identified or is not supported |
| `INPUT_TOO_LARGE` | Parse | Artifact exceeds the 80 000-byte limit |
| `PARSE_FAILED` | Parse | LLM failed to produce a valid parse after 3 retries |
| `NORMALIZE_FAILED` | Normalize | LLM failed to produce a valid canonical model after 3 retries |
| `CLASSIFY_FAILED` | Classify | LLM failed to produce a valid classification after 3 retries |
| `ANALYZE_FAILED` | Analyze | One or more methods failed after 5 retries each |
| `SYNTHESIZE_FAILED` | Synthesize | LLM failed to produce a valid synthesis after 5 retries |
| `PIPELINE_CANCELLED` | Any | Job was cancelled mid-flight (service restart, shutdown) |
