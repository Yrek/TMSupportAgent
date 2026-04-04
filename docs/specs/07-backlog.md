# Implementation Backlog

**Status:** Living document  
**Spec ref:** All specs in this directory  
**Version:** 0.3  
**Date:** 2026-04-04

---

## 1. Overview

This document tracks all remaining implementation work across the API, Worker pipeline, CI security controls, tests, and pre-GA operational requirements. Items are grouped by theme and ordered within each group by priority.

### What is complete

- Full two-phase pipeline: DETECT → PARSE → NORMALIZE → CLASSIFY → ANALYZE → SYNTHESIZE
- Database schema, EF Core models, RLS via `RlsSessionInterceptor`
- All Azure infrastructure (Container Apps, Service Bus, Blob Storage, Key Vault, PostgreSQL)
- CI/CD pipelines (build, test, Docker build, Bicep deploy)
- Auth session endpoints (`GET /v1/auth/session`, `DELETE /v1/auth/session`)
- Member management: list, invite, role-update, remove (`/v1/orgs/{orgId}/members`)
- IDP configuration: get, put, delete (`/v1/orgs/{orgId}/idp`)
- Architecture endpoints: GET, confirm, PATCH element, GET element
- Threats endpoints: list, add, patch-status, notes, analysis blob
- Jobs endpoints: submit, list, get, delete
- Re-analysis workflow: `CorrectionApplicator`, `DeleteSystemGeneratedAsync`, orchestrator integration
- `IWorkOsClient`, `WorkOsHttpClient`, `IIdpConfigRepository`, `IdpConfigRepository`
- `PipelineDbPersistence` — full DB write path for pipeline outputs

### Implemented 2026-04-04 (this session)

- **§2 Pipeline Contract Gaps** — all four items complete:
  - `UserCorrection` record + `ClassifyInput.UserCorrections[]` added to `StageContracts.cs`
  - `FinalOutput.UserAddedThreats` added; `SynthesizeStage` populates as `[]`
  - `TokenEstimator` created; `INPUT_TOO_LARGE` budget checks added to NORMALIZE, CLASSIFY, ANALYZE, SYNTHESIZE
  - Prompt template version strings confirmed embedded in all templates
- **§3 Framework Mapping Sub-Step** — `FrameworkNormalizer` shared helper extracted; cheap-model sub-step added to `SynthesizeStage`; `PipelineDbPersistence` migrated to use shared helper
- **§4 CI Security Controls** — CodeQL SAST job added to `ci.yml`; dependency CVE scan job added to `ci.yml` + new `nightly-scan.yml`
- **§5 API Remaining Gaps** — all four items complete:
  - `IUserRepository` + `UserRepository` created and registered
  - `MeController` (`GET /v1/me`, `DELETE /v1/me`) created
  - Export endpoint (`GET /v1/orgs/{orgId}/jobs/{jobId}/export`) added to `ThreatsController`
  - GDPR right-of-access endpoint (`GET /v1/orgs/{orgId}/members/{memberId}/data`) added to `MembersController`
  - `platform:admin` token rejection added to `TenantContextMiddleware`
- **§10 Spec Status** — `02-architecture.md` and `06-security.md` updated to `Approved`

### What remains (future tasks)

| # | Section | Theme |
|---|---|---|
| 6 | GDPR — Phase 1 self-erasure | `DELETE /v1/me` implemented; Phase 2 org erasure → D-9 post-MVP |
| 7 | Test Coverage | Integration, RLS, domain, worker, security, prompt injection tests |
| 8 | Pre-GA Operational Requirements | 14 items with named owners |
| 9 | Deferred by Design | Post-MVP items |

---

## 2. Pipeline Contract Gaps

These are divergences between the implemented contracts and the spec (05-llm-workflow.md). They affect pipeline correctness and should be fixed before testing begins.

### 2.1 `ClassifyInput` — Missing `UserCorrections` field

**Spec requirement (05-llm-workflow §4 Stage 4 input):**

```typescript
{
  confirmedModel: CanonicalModel;
  userCorrections: UserCorrection[];   // ← MISSING
  systemInstruction: string;
}
```

**Current state (`StageContracts.cs`):**

```csharp
public sealed record ClassifyInput(CanonicalModel ConfirmedModel);
```

**Why it matters:** The CLASSIFY prompt should be able to reason about what the user explicitly corrected vs what was inferred. Without this field, the LLM cannot differentiate between user-confirmed facts and AI-extracted assumptions.

**Implementation:**

1. Add `UserCorrection` record to `StageContracts.cs`:
   ```csharp
   public sealed record UserCorrection(string ElementId, string Field, string? OldValue, string NewValue, string CorrectionType);
   ```

2. Extend `ClassifyInput`:
   ```csharp
   public sealed record ClassifyInput(CanonicalModel ConfirmedModel, UserCorrection[] UserCorrections);
   ```

3. Update `JobOrchestrator.RunAnalyzePhaseAsync` — when building `ClassifyInput`, map `arch.corrections` to `UserCorrection[]`.

4. Update `ClassifyStage.ExecuteAsync` — pass the corrections to the prompt builder.

---

### 2.2 `FinalOutput` — Missing `UserAddedThreats` field

**Spec requirement (05-llm-workflow §4 Stage 6 output):**

```typescript
{
  ...
  userAddedThreats: [];   // ← empty at synthesis; populated via API later
}
```

**Current state:** `FinalOutput` record does not include this field.

**Why it matters:** The blob output (`analysis.json`) is consumed by the frontend. If this field is absent, the JSON shape diverges from the spec, and any client code expecting it will fail.

**Implementation:**

- Add `UserAddedThreats` field to `FinalOutput` in `StageContracts.cs`:
  ```csharp
  public sealed record FinalOutput(
      // ... existing fields ...
      FinalThreat[] UserAddedThreats);   // always empty at synthesis time
  ```
- `SynthesizeStage` populates it as `[]`.
- No DB change needed — the blob stores it; user-added threats are read from DB separately.

---

### 2.3 Token Budget Enforcement — `INPUT_TOO_LARGE`

**Spec requirement (05-llm-workflow §6, §7):**

> Jobs exceeding the ANALYZE or SYNTHESIZE input token limits MUST fail with `error_code: INPUT_TOO_LARGE` rather than silently truncate.

| Stage | Max input (approx.) | Max output |
|---|---|---|
| PARSE (image) | 4,096 + image | 4,096 |
| PARSE (code/text) | 8,192 | 4,096 |
| NORMALIZE | 12,288 | 8,192 |
| CLASSIFY | 8,192 | 2,048 |
| ANALYZE (per method) | 12,288 | 8,192 |
| SYNTHESIZE | 16,384 | 12,288 |
| Framework mapping | 8,192 | 4,096 |

**Current state:** No token budget checks exist. LLM calls are made without pre-flight token estimation.

**Implementation:**

1. Add a `TokenEstimator` utility (static, deterministic):
   - Counts approximate tokens for a string: `(text.Length / 4)` as a conservative estimate, or use a tiktoken-compatible library.
   - `EstimateTokens(string text) → int`.

2. In each stage before calling the LLM:
   - Estimate input tokens from serialized prompt content.
   - If `estimatedTokens > stageInputLimit`: throw `PipelineStageException("INPUT_TOO_LARGE", ...)`.
   - Do NOT silently truncate.

3. Add `INPUT_TOO_LARGE` to the set of known error codes in error documentation.

4. For the LLM client calls, set `max_tokens` on the request to the stage output limit.

---

### 2.4 Prompt Template Versioning

**Spec requirement (05-llm-workflow §8):**

> Each template has a version string embedded in the system message: `// prompt-version: {stage}-{semver}`.  
> Template version is logged with every LLM call (as metadata, not content).  
> Prompt templates are NOT stored in the database.

**Current state:** Need to verify whether prompt templates in `PromptTemplates.cs` (or equivalent) embed version strings, and whether the LLM call logger includes the version.

**Implementation:**

1. Each prompt template constant or builder method should include a version comment embedded in the output, e.g.:
   ```
   // prompt-version: normalize-1.0.0
   ```
   This appears in the system message so the LLM receives it; it can be extracted from the system message for logging by looking for the pattern.

2. In the LLM client wrapper (`ILlmClient` implementation), extract the version comment from the system message and log it as a structured field:
   ```
   logger.LogInformation("LLM call. Stage={Stage} PromptVersion={Version} Model={Model}", ...);
   ```

3. Content MUST NOT be logged — only the version string and stage name.

4. Add a unit test: verify each stage's prompt template includes a `prompt-version:` string.

---

## 3. Worker Pipeline — Framework Mapping Sub-Step

**Spec reference:** 05-llm-workflow §4 Stage 6 (SYNTHESIZE), §7 Token Budget.

### 3.1 What the spec requires

After the main SYNTHESIZE LLM call, a separate **cheap-model** call maps each final threat to framework references (OWASP, ASVS, CIS, NCSC, 12-Factor). This is explicitly separated because:

- It is pattern-matching, not security reasoning.
- Using a cheap model saves cost without quality trade-off.
- It can run in parallel with secure design recommendation generation.
- The spec dedicates a separate token budget row (8,192 in / 4,096 out).

**Frameworks allowed:** `owasp_top10 | owasp_api_top10 | asvs | cis_controls | ncsc | twelve_factor`

### 3.2 Implementation plan

**`src/ThreatModelingAgent.Worker/Pipeline/Prompts/PromptTemplates.cs`**
- Add `FrameworkMappingSystem` constant.
- Add `BuildFrameworkMappingUser(FinalThreat[] threats) → string` that serializes the threat identifiers + titles + descriptions.
- Prompt instructs the model: "Return only a JSON array of `{ threatIdentifier, framework, reference }`. Framework must be one of the allowed values. Do not add new threats. Do not change existing data."
- Version string: `// prompt-version: framework-mapping-1.0.0`.

**`src/ThreatModelingAgent.Worker/Pipeline/Stages/SynthesizeStage.cs`**
- After the main SYNTHESIZE call produces `FinalOutput`:
  1. Call the cheap model with the framework mapping prompt. Cheap model: `gpt-4o-mini` or `claude-haiku-4-5`.
  2. Token pre-flight: estimate input tokens; fail with `INPUT_TOO_LARGE` if over 8,192.
  3. Schema-validate the response: array of `{ threatIdentifier: string, framework: string, reference: string }`.
  4. Normalize framework names via `NormalizeFramework()` (already in `PipelineDbPersistence`; extract to a shared helper).
  5. Discard entries with unknown framework values — do not fail the pipeline.
  6. Merge: update the `FrameworkMappings` array on each matching `FinalThreat` (by `Identifier`).
  7. Return the merged `FinalOutput`.

**`src/ThreatModelingAgent.Worker/Pipeline/NormalizeFramework.cs`** (extract from `PipelineDbPersistence`)
- Move `NormalizeFramework(string?)` to a shared static helper so both `SynthesizeStage` and `PipelineDbPersistence` use the same allow-list without duplication (CLAUDE.md §14).

---

## 4. CI Security Controls

**Spec reference:** 06-security.md §9.

Both controls are required before GA and are missing from the current `ci.yml`.

### 4.1 SAST on every PR

**Spec requirement:** "Automated SAST — Every PR — All source code"

**Implementation — add to `ci.yml` as a new job `sast`:**

```yaml
sast:
  name: SAST (CodeQL)
  runs-on: ubuntu-latest
  permissions:
    security-events: write

  steps:
    - uses: actions/checkout@v4

    - name: Initialize CodeQL
      uses: github/codeql-action/init@v3
      with:
        languages: csharp
        queries: security-and-quality

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: "10.0.x"

    - name: Build for CodeQL
      run: dotnet build ThreatModelingAgent.slnx -c Release

    - name: Perform CodeQL Analysis
      uses: github/codeql-action/analyze@v3
      with:
        category: "/language:csharp"
```

**Note:** CodeQL requires `security-events: write` permission at the job level. This must be set explicitly.

### 4.2 Dependency CVE scanning

**Spec requirement:** "Dependency CVE scanning — Every PR + nightly — All dependencies"  
**Spec requirement (06-security.md §10):** Patch SLA: critical within 24h, high within 7 days.  
**Spec requirement (CLAUDE.md §12.2):** Builds MUST fail on critical or high-severity vulnerabilities.

**Implementation — add to `ci.yml` as a new job `dependency-scan`:**

```yaml
dependency-scan:
  name: Dependency CVE Scan
  runs-on: ubuntu-latest

  steps:
    - uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: "10.0.x"

    - name: Restore
      run: dotnet restore ThreatModelingAgent.slnx

    - name: Vulnerability scan
      run: dotnet list package --vulnerable --include-transitive 2>&1 | tee vuln-report.txt
      # Fail build on critical or high vulnerabilities
    
    - name: Check for critical/high vulnerabilities
      run: |
        if grep -E "(Critical|High)" vuln-report.txt; then
          echo "Critical or High vulnerabilities found. Build fails per CLAUDE.md §12.2."
          exit 1
        fi

    - name: Upload vulnerability report
      if: always()
      uses: actions/upload-artifact@v4
      with:
        name: vulnerability-report
        path: vuln-report.txt
```

**Nightly schedule:** Add a separate workflow `nightly-scan.yml` that runs the same scan on a cron schedule:
```yaml
on:
  schedule:
    - cron: '0 2 * * *'   # 02:00 UTC nightly
```

---

## 5. API — Remaining Gaps

### 5.1 `GET /v1/me` — Current User Profile

Required for GDPR right of access to own data and as a standard profile endpoint.

| Method | Path | Description | Auth |
|---|---|---|---|
| `GET` | `/v1/me` | Return current user's profile | Any authenticated user |
| `DELETE` | `/v1/me` | Initiate self-erasure (see §6) | Any authenticated user |

**`GET /v1/me` response DTO:**
```json
{
  "userId": "usr_xxx",
  "workosUserId": "user_xxx",
  "createdAt": "2026-01-01T00:00:00Z"
}
```

**MUST NOT return:** email or display name in the response body — WorkOS is the source of truth for PII. Return only the platform-internal identifiers.

**Implementation:**
- New `MeController.cs` in `ThreatModelingAgent.Api/Controllers/`.
- Reads `userId` from `TenantContext`; loads from `IUserRepository`.
- Rate-limited with "api" tier.
- `Cache-Control: no-store` required.

---

### 5.2 GDPR Right of Access — `GET /orgs/{orgId}/members/{userId}/data`

**Spec reference:** 06-security.md §6.2.

| Method | Path | Description | Auth | SLA |
|---|---|---|---|---|
| `GET` | `/v1/orgs/{orgId}/members/{userId}/data` | Return all personal data held for a user | Owner OR the user themselves | 30 days |

**Response — purpose-specific DTO:**
```json
{
  "userId": "usr_xxx",
  "role": "member",
  "joinedAt": "2026-01-01T00:00:00Z",
  "jobCount": 5,
  "auditLogEntries": []
}
```

**Security invariants:**
- A member can only access their own data (`userId` from JWT must match path param, OR caller is `org:owner`).
- MUST NOT return other members' data.
- MUST NOT return architecture content — architecture is classified **Confidential** (not personal data per 06-security §3.1); this endpoint returns only personal data.
- Rate-limited "api" tier.

---

### 5.3 GDPR Right to Portability — JSON Export

**Spec reference:** 06-security.md §6.2 "JSON export of threat models and analysis results".

| Method | Path | Description | Auth |
|---|---|---|---|
| `GET` | `/v1/orgs/{orgId}/jobs/{jobId}/export` | Download the full analysis as a JSON file | `org:member` |

**Response:**
- Content-Type: `application/json`
- Content-Disposition: `attachment; filename="threat-model-{jobId}.json"`
- Body: the raw `FinalOutput` JSON from blob storage (already at `{orgId}/outputs/{jobId}/analysis.json`).

**Security invariants:**
- Job MUST be in `Complete` or `Partial` status.
- Org-ID + job-ID check against DB before reading blob (same pattern as `GET /analysis`).
- Rate-limited "api" tier (not "strict" — this is a read, not a state change).
- `Cache-Control: no-store` required.
- CSV export is NOT required for MVP — CSV formula injection sanitization (CLAUDE.md §7.8) deferred to §9 post-MVP.

**Implementation notes:**
- This is largely a thin wrapper over the blob read already done in `ThreatsController.GetAnalysis`.
- Create a separate endpoint rather than reusing GetAnalysis: different content-type header and filename disposition. Shared internal blob-read logic via a helper to avoid duplication (CLAUDE.md §14).

---

### 5.4 `platform:admin` Role — Enforcement Decision

**Spec reference:** 02-architecture.md §6.3.

The architecture spec defines three roles: `org:owner`, `org:member`, `platform:admin`. The first two are enforced. `platform:admin` is referenced but has no enforcement points and no dedicated API surface.

**Decision required (OD-4 from 02-architecture §14):**

> Is `platform:admin` capability in MVP scope?

**Proposed resolution for the backlog:**

- `platform:admin` is NOT in MVP scope.
- Add a permanent middleware check that rejects any request claiming `platform:admin` role against org-scoped endpoints (i.e., `platform:admin` tokens cannot masquerade as `org:owner` or `org:member`).
- No admin API endpoints are created in MVP.
- If/when an admin API is built, it MUST be a separate service (02-architecture §4.1 "Admin / platform operator API separate service").

**Implementation:**
- In `TenantContextMiddleware`, if the JWT contains a `platform:admin` role claim and the route is org-scoped, return 403 immediately.
- Document this decision as OD-4 resolved in `02-architecture.md`.

---

## 6. GDPR — Erasure Cascade

### 6.1 What the spec requires

`03-data-model.md §8` and `06-security.md §6.2` define:
- Right to erasure triggered by user request or admin action.
- User soft-delete: null `email` and `display_name`, retain `id` and `workos_user_id` for FK integrity.
- Org erasure: soft-delete org, enqueue background cascade to purge all associated jobs, architectures, threats, and blob storage.
- WorkOS account deletion called as part of erasure.
- Blob cleanup: prefix `{orgId}/` deleted from storage.

### 6.2 Phase 1 — User self-erasure

**Endpoint:** `DELETE /v1/me` (see §5.1 above for the controller).

**Actions on `DELETE /v1/me`:**
1. Load the user record by `userId` from JWT.
2. Call `WorkOsClient.DeleteUserAsync(workosUserId, ct)`.
3. Call `user.SoftDelete()` — nulls `email`, `display_name`; sets `deleted_at`.
4. Revoke all active org memberships (set `deleted_at` on each).
5. Save changes.
6. Return 204.

**CLAUDE.md §8.1 — sensitive action re-authentication:** Self-erasure is a high-impact action. The client MUST present a current valid JWT (this is satisfied by the existing auth middleware). Additional step-up authentication is not required for MVP because the erasure only deletes the requesting user's own data; the JWT provides recency evidence. Revisit if account recovery is added.

**Files:**
- `src/ThreatModelingAgent.Domain/Entities/User.cs` — add `SoftDelete()` method.
- `src/ThreatModelingAgent.Domain/Interfaces/IUserRepository.cs` — add `GetByIdAsync(UserId id, CancellationToken ct)`.
- `src/ThreatModelingAgent.Infrastructure/Persistence/Repositories/UserRepository.cs` — implement.
- `src/ThreatModelingAgent.Api/Controllers/MeController.cs` — `GET /v1/me` and `DELETE /v1/me`.
- `src/ThreatModelingAgent.Infrastructure/InfrastructureServiceExtensions.cs` — register `IUserRepository`.

### 6.3 Phase 2 — Org erasure background job

Deferred to post-MVP (see §9 D-9). The blast radius is high and requires careful idempotency design. For MVP: org deletion is a manual platform operation. The endpoint exists only in the admin API (D-3 post-MVP).

---

## 7. Test Coverage

The current test suite covers domain entities and security middleware in isolation. There are no integration tests, no RLS tests, and no pipeline tests. All items below are required before MVP launch.

### 7.1 API Integration Tests

**Project:** `tests/ThreatModelingAgent.Api.Tests/`  
**Framework:** `Microsoft.AspNetCore.Mvc.Testing` + `Testcontainers` (PostgreSQL).  
**Pattern:** Start API in-process with `WebApplicationFactory`; run PostgreSQL in a Docker container per test class; mock `IJobQueue`, `IBlobStorage`, `IWorkOsClient`.

Required test classes:

| Test Class | Covers |
|---|---|
| `JobsController.SubmitJobTests` | Happy path, file too large, bad extension, no membership |
| `JobsController.ListJobsTests` | Pagination, status filter, org isolation |
| `JobsController.DeleteJobTests` | Happy path, in-progress job, cross-org attempt |
| `ArchitecturesController.GetArchitectureTests` | Happy path, 404 if no architecture, cross-org attempt |
| `ArchitecturesController.ConfirmTests` | Happy path, wrong status, already confirmed, cross-org |
| `ArchitecturesController.PatchElementTests` | Happy path, wrong job status, cross-org element |
| `ThreatsController.ListThreatsTests` | Happy path, cross-org attempt |
| `ThreatsController.AddThreatTests` | Happy path, wrong status, missing fields |
| `ThreatsController.PatchStatusTests` | All allowed statuses, invalid status, cross-org |
| `ThreatsController.ExportTests` | Complete job, incomplete job, cross-org |
| `MembersController.ListTests` | Happy path, cross-org |
| `MembersController.InviteTests` | Happy path, same response for existing/non-existing email (no oracle) |
| `MembersController.RoleTests` | Owner-only, last-owner guard |
| `MembersController.RemoveTests` | Happy path, last-owner guard |
| `MeController.GetTests` | Happy path, returns no PII |
| `MeController.DeleteTests` | Happy path, WorkOS called, membership revoked |
| `AuthenticationTests` | No token, expired token, wrong audience, wrong issuer |
| `RateLimitingTests` | Strict and standard tiers enforce limits |

### 7.2 Tenant Isolation / RLS Tests

**Requirement:** CLAUDE.md §15.1 — cross-tenant data access MUST be impossible.

Required tests:

```
Given org A and org B both have jobs
When org A's token is used to request org B's job ID
Then 404 is returned (not 403 — no oracle) at the API layer

Given org A and org B both have threats
When DbContext is seeded with org_id=A and query is run with RLS set to org_id=B
Then 0 rows are returned

Given a job in org A
When IThreatRepository.ListByJobAsync is called with org_id=B
Then 0 rows are returned even if the JobId is correct

Given an architecture element in org A
When IArchitectureRepository.GetElementAsync is called with org_id=B
Then null is returned
```

### 7.3 Domain and Value Object Tests

Gaps in current coverage:

| Test | Entity / Invariant |
|---|---|
| `Threat.CreateFromPipeline` — High confidence + Conditional → throws | `Threat.cs` invariant |
| `Threat.CreateFromPipeline` — Identifier format validation (`T-NNN` regex) | `Threat.cs` |
| `Architecture.Confirm` — already confirmed → throws | `Architecture.cs` |
| `Architecture.UpdateClassification` — sets classification, saves correctly | `Architecture.cs` |
| `Architecture.IncrementVersion` — version monotonically increases | `Architecture.cs` |
| `Mitigation.Create` — invalid priority → throws | `Mitigation.cs` |
| `FrameworkMapping.Create` — unknown framework → throws | `FrameworkMapping.cs` |
| `RejectedCandidate.Create` — unknown rejection reason → throws | `RejectedCandidate.cs` |
| `OrgId.From` — empty GUID → throws | `OrgId.cs` |
| `JobId.From` — empty GUID → throws | `JobId.cs` |

### 7.4 Worker Pipeline Tests

**Project:** `tests/ThreatModelingAgent.Worker.Tests/` (new project).  
**Pattern:** Mock `ILlmClient` to return pre-canned responses; mock `IBlobStorage`; use real repositories against Testcontainers PostgreSQL.

Required test classes:

| Test Class | Covers |
|---|---|
| `DetectStageTests` | Magic bytes detection, extension fallback, low-confidence flag, unsupported type fails job |
| `NormalizeStageTests` | Schema validation pass/fail, 3 retries on invalid output, `NORMALIZE_FAILED` on third failure |
| `ClassifyStageTests` | Required methods enforced per category, missing method added by validator, invalid model output rejected |
| `SynthesizeStageTests` | `partial` status on unresolved critical gap, remediation list references confirmed-only, framework mapping merging |
| `FrameworkMappingSubStepTests` | Unknown framework names discarded, known names normalized, empty output handled |
| `TokenBudgetTests` | Each stage fails with `INPUT_TOO_LARGE` when estimate exceeds limit |
| `PipelineDbPersistenceTests` | Architecture persisted with correct element types, threats persisted with correct confidence mapping, unknown framework names skipped |
| `CorrectionApplicatorTests` | Each CorrectionType applied correctly, unknown element IDs skipped, rename propagates to subsequent corrections |
| `JobOrchestratorTests` | Phase 1 happy path, Phase 2 happy path, Phase 1 failure → job fails, org mismatch → discarded |

### 7.5 Security-Specific Tests

Required per CLAUDE.md §15.1:

| Test | Category |
|---|---|
| SQL injection payload in job title stored and returned safely | Injection |
| Oversized pagination `pageSize` clamped to 100 | Boundary |
| Request body > 11 MB rejected with 413 before parsing | Resource cap |
| JWT with invalid signature rejected 401 | Auth |
| JWT for org A cannot access org B resources | Tenant isolation |
| Confirmed architecture cannot be re-confirmed | Idempotency |
| Deleting a job in-progress returns 409 | State machine |
| Self-erasure removes only the requesting user | Scope enforcement |
| GDPR access endpoint returns 403 if requesting another user's data without owner role | Authorization |
| Prompt template version string present in all templates | Prompt versioning |

### 7.6 Prompt Injection Tests

Required per 06-security.md §9 "Prompt injection test — Before GA; on every prompt template change".

| Test | Covers |
|---|---|
| Architecture content containing `IGNORE PREVIOUS INSTRUCTIONS` is passed through NORMALIZE without altering the system instruction behavior | Prompt injection via artifact |
| Architecture element label containing `DROP TABLE` is stored and returned as data, not executed | Injection in labels |
| User-supplied architecture correction containing a prompt injection attempt does not alter the CLASSIFY output schema | Injection via corrections |

These tests require a mock LLM that returns pre-canned outputs; they verify that the **pipeline does not execute injected instructions** by validating that the schema-validated output is used, not arbitrary model-returned content.

---

## 8. Pre-GA Operational Requirements

All items MUST be completed or have a signed-off deferral with named owner + deadline before GA.

| ID | Item | Spec reference | Owner | When |
|---|---|---|---|---|
| OPS-1 | Execute WorkOS DPA | 06-security §7 | Legal | Before any personal data processed in production |
| OPS-2 | Execute Anthropic DPA | 06-security §7 | Legal | Before Anthropic provider enabled in production |
| OPS-3 | Execute Azure OpenAI DPA | 06-security §7 | Legal | Before OpenAI provider enabled in production |
| OPS-4 | External penetration test | 06-security §9 | Security | Before GA |
| OPS-5 | DAST / API fuzzing | 06-security §9 | Security | Pre-release |
| OPS-6 | Authentication bypass test (WorkOS integration, JWT validation) | 06-security §9 | Engineering | Before GA |
| OPS-7 | Tenant isolation test (cross-tenant data leakage scenarios) | 06-security §9 | Engineering | Before GA |
| OPS-8 | Azure PIM — JIT access for all privileged Azure roles | 06-security §5.4 A.8.2 | Infra | Before GA |
| OPS-9 | Device management policy (MDM, FDE, MFA enforcement) | 06-security §5.4 A.8.1 | IT | Before GA |
| OPS-10 | On-call rotation defined and tested | 06-security §10 | Engineering | Before GA |
| OPS-11 | Designate Data Protection Officer (DPO) | 06-security §8.3 | Legal | Before any personal data processing |
| OPS-12 | Log integrity — ship logs to append-only SIEM or Log Analytics with lock | CLAUDE.md §10.6 | Infra | Before GA |
| OPS-13 | HSTS preload submission | CLAUDE.md §11.1 | Infra | After domain confirmed stable |
| OPS-14 | Background checks for employees with production access | 06-security §5.2 A.6.1 | HR | Before production access granted |

---

## 9. Deferred by Design — Post-MVP

Items below are intentionally out of scope for MVP. Each requires an architectural decision that should not be made under time pressure.

| ID | Item | Notes |
|---|---|---|
| D-1 | Azure Front Door + WAF | App-layer rate limiting sufficient for MVP; add before high traffic |
| D-2 | BYOK (customer-managed keys) | Architecture supports it via Key Vault; requires customer-facing key lifecycle UX |
| D-3 | Admin / platform operator API | Separate service for billing, org management, abuse detection (OD-4 resolved: out of MVP scope) |
| D-4 | Server-Sent Events for job progress | Currently clients poll; SSE or WebSocket reduces poll traffic at scale (OD-3) |
| D-5 | Job timeout on AWAITING_REVIEW | Auto-expire jobs not confirmed within 72 hours; requires scheduled trigger or KEDA timer (OD-2) |
| D-6 | CSV / PDF export | CSV requires formula-injection sanitization per CLAUDE.md §7.8; PDF requires rendering service |
| D-7 | Azure Private Endpoints | VNet-scope DB and storage access; adds operational complexity |
| D-8 | Multi-region deployment | Requires active/passive DB replication and blob geo-redundancy |
| D-9 | Org erasure background job | `IErasureQueue`, `ServiceBusErasureQueue`, `OrgErasureWorker`; high blast-radius, requires idempotency design |
| D-10 | Interactive diagram frontend SPA | Spec §19 extensive requirements; full React/SPA not in scope for API-only MVP |
| D-11 | Evaluation regression suite | Spec §20; test architectures with expected threat outcomes; required before any prompt template change in production |
| D-12 | Retention enforcement job | Automated purge of blobs and DB rows older than retention policy; requires scheduled trigger |

---

## 10. Spec Status Updates

The following spec files need their status headers updated now that the implementation decisions captured in them are approved and implemented.

| File | Current status | Should be | Action |
|---|---|---|---|
| `docs/specs/02-architecture.md` | `Review` | `Approved` | Update status header |
| `docs/specs/06-security.md` | `Draft` | `Approved` | Update status header |

Both documents' contents are stable, implemented, and referenced normatively throughout the codebase. Leaving them in `Draft` / `Review` creates confusion about whether the controls within are binding.

---

## 11. Completion Criteria for MVP

The MVP is considered complete when all of the following are true:

- [ ] All pipeline contract gaps in §2 are fixed (ClassifyInput, FinalOutput, token budgets, prompt versioning).
- [ ] Framework mapping sub-step in §3 is implemented as a separate cheap-model call.
- [ ] CI SAST and CVE scanning in §4 are running on every PR and failing on critical/high findings.
- [ ] `GET /v1/me` implemented.
- [ ] GDPR right of access endpoint implemented.
- [ ] GDPR portability export (JSON) implemented.
- [ ] `platform:admin` role enforcement decision (§5.4) implemented and documented.
- [ ] User self-erasure (`DELETE /v1/me`) implemented.
- [ ] All test categories in §7 have passing tests in CI.
- [ ] All OPS items in §8 are complete or have signed-off deferrals with named owner and deadline.
- [ ] Spec status headers in §10 updated.
- [ ] No `TODO` or `FIXME` placeholders exist in production code paths (CLAUDE.md §13).
- [ ] Dependency vulnerability scan passes in CI with no critical or high findings.
