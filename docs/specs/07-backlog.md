# Implementation Backlog

**Status:** Living document  
**Spec ref:** All specs in this directory  
**Version:** 0.6  
**Date:** 2026-04-11

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
- Architecture endpoints: GET, confirm, PATCH element, GET element, POST element, DELETE element
- Manual job flow: POST /jobs/manual, add/delete/patch elements, confirm → Phase 2
- Threats endpoints: list, add, patch-status, notes, analysis blob
- Jobs endpoints: submit, list, get, delete
- Re-analysis workflow: `CorrectionApplicator`, `DeleteSystemGeneratedAsync`, orchestrator integration
- `IWorkOsClient`, `WorkOsHttpClient`, `IIdpConfigRepository`, `IdpConfigRepository`
- `PipelineDbPersistence` — full DB write path for pipeline outputs

### Implemented in session 1

- **§2 Pipeline Contract Gaps** — `UserCorrection` + `ClassifyInput.UserCorrections[]`, `FinalOutput.UserAddedThreats`, `TokenEstimator` + `INPUT_TOO_LARGE` checks, prompt template version strings
- **§3 Framework Mapping Sub-Step** — `FrameworkNormalizer` shared helper, cheap-model sub-step in `SynthesizeStage`, `PipelineDbPersistence` migrated
- **§4 CI Security Controls** — CodeQL SAST job in `ci.yml`, dependency CVE scan job in `ci.yml` + `nightly-scan.yml`
- **§5 API Remaining Gaps** — `IUserRepository` + `UserRepository`, `MeController` (`GET /v1/me`, `DELETE /v1/me`), JSON export endpoint, GDPR right-of-access endpoint, `platform:admin` token rejection in `TenantContextMiddleware`
- **§10 Spec Status** — `02-architecture.md` and `06-security.md` updated to `Approved`

### Implemented in session 2 (2026-04-04)

- **§7 Domain entity tests** — `ThreatTests`, `ArchitectureTests`, `MitigationTests`, `FrameworkMappingTests`, `RejectedCandidateTests`, `JobTests`, `OrganizationTests`, `UserTests`
- **§7 Value object tests** — `ValueObjectTests` (OrgId, UserId, JobId)
- **§7 API security tests** — `CorrelationIdTests`, `SecurityHeadersTests`, `TenantContextMiddlewareTests`
- **§7 API validation tests** — `OrgValidatorTests`
- **§7 Worker pipeline tests** — `CorrectionApplicatorTests`, `FrameworkNormalizerTests`, `TokenEstimatorTests`

### Implemented in session 3 (2026-04-11)

- **§7 Group A tests** — `DetectStageTests`, `StageRetryHelperTests`, `PromptTemplateVersionTests`, `PromptInjectionTests`
- **Build fixes** — `ArchitectureConfiguration` nullable `HasConversion`, `JobOrchestrator` nullable-tuple `.Value.` access, `PipelineDbPersistence` ambiguous type aliases, `ThreatsController` `using var` scoping, `Microsoft.Extensions.Http` package, `JobOrchestrator` accessibility
- **Config gap** — `WorkOS:ApiKey` added to API `appsettings.Development.json.example`, `keyvault.bicep`, `main.bicep`, `production.bicepparam.example`, `local.md`, `azure.md`
- **Code quality** — `SessionController.SignOut()` `new` keyword, README `04-api.md` reference corrected to `docs/api/openapi.yaml`
- Total passing: **255 tests** across 3 projects

### Implemented in session 4 (2026-04-11)

- **§7 Group B tests** — full integration test infrastructure + all controller integration tests:
  - `tests/ThreatModelingAgent.Api.Tests/Integration/TestAuthHandler.cs` — replaces JWT with test auth scheme (claims via `X-Test-Claims` header)
  - `tests/ThreatModelingAgent.Api.Tests/Integration/ApiWebApplicationFactory.cs` — `WebApplicationFactory<Program>` with Testcontainers PostgreSQL, EF migrations, NSubstitute mocks for `IBlobStorage`/`IJobQueue`/`IWorkOsClient`, seeding helpers
  - `tests/ThreatModelingAgent.Api.Tests/Integration/AuthenticationTests.cs` — no token → 401, missing org_id → 403, platform:admin → 403, valid claims → 200
  - `tests/ThreatModelingAgent.Api.Tests/Integration/TenantIsolationTests.cs` — cross-org job 404 (not 403), list scoped to own org only, cross-tenant members 403
  - `tests/ThreatModelingAgent.Api.Tests/Integration/RateLimitingTests.cs` — strict tier exhausted → 429, Retry-After header present, RATE_LIMIT_EXCEEDED code
  - `tests/ThreatModelingAgent.Api.Tests/Integration/JobsControllerTests.cs` — submit happy path, file too large, bad extension, no membership; list with filter and page size; get happy path and cross-org; delete happy path, in-progress, cross-org
  - `tests/ThreatModelingAgent.Api.Tests/Integration/ArchitecturesControllerTests.cs` — GET happy path, 404, cross-org; confirm happy path + phase-2 enqueue verified, wrong status, already confirmed; PATCH element happy path and wrong status
  - `tests/ThreatModelingAgent.Api.Tests/Integration/ThreatsControllerTests.cs` — list happy path, cross-org; add threat happy path, wrong status, missing fields; patch-status all 4 allowed states + invalid; export complete job, incomplete job
  - `tests/ThreatModelingAgent.Api.Tests/Integration/MembersControllerTests.cs` — list happy path, cross-org; invite happy path, 422 → same 202 (no enumeration oracle); role update owner-only, last-owner guard; remove happy path, last-owner guard
  - `tests/ThreatModelingAgent.Api.Tests/Integration/MeControllerTests.cs` — GET returns only platform IDs (no PII), unknown user 404; DELETE calls WorkOS + 204; WorkOS fail → 502, DB left intact
- **Packages added** — `NSubstitute 5.3.0`, `Testcontainers.PostgreSql 4.4.0` to `ThreatModelingAgent.Api.Tests.csproj`; `ThreatModelingAgent.Infrastructure` project reference added to enable test seeding via `AppDbContext`

### Implemented in session 5 (2026-04-11)

- **`AnthropicClient` vision support** — added multimodal content block format (image + text) so PNG/JPEG/GIF uploads work with `claude-sonnet-4-6` as the strong model, matching feature parity with `AzureOpenAiClient`
- **`AzureOpenAI:ApiKey` config gap** — added missing key to `Worker/appsettings.Development.json.example`; production uses managed identity so the key was absent from the example
- **`docs/deployment/local.md`** — added §2 "Supported architecture formats" table (all 10 extensions, detection method, notes); added Anthropic-only and Azure OpenAI-only setup instructions; renumbered sections
- **`docs/specs/05-llm-workflow.md`** — updated Stage 2 model selection table to note both clients support vision

### Implemented in session 6 (2026-04-11)

- **Manual architecture creation flow** — full end-to-end pipeline path for jobs with no file upload:
  - `POST /v1/orgs/{orgId}/jobs/manual` — creates job (status: `AwaitingReview`) + empty `Architecture` record immediately; accepts `{ title?, systemPurpose? }`
  - `POST /v1/orgs/{orgId}/jobs/{jobId}/elements` — adds a user-defined element to the architecture; accepts `{ elementType, name, description?, properties? }` where `elementType` is any `ElementType` enum value (case-insensitive) and `properties` is a free-form object (well-known keys: `port`, `protocol`, `auth`, `trustZone`, `technology`, `encryption`, plus any extra key-value pairs)
  - `DELETE /v1/orgs/{orgId}/jobs/{jobId}/elements/{elementId}` — removes an element; gated on `AwaitingReview` status
  - `PATCH /v1/orgs/{orgId}/jobs/{jobId}/elements/{elementId}` — now correctly applies `properties` updates (previously wired to `null`); all three fields (`name`, `description`, `properties`) are optional
  - `POST /v1/orgs/{orgId}/jobs/{jobId}/architecture/confirm` — fixed to handle manual jobs (no artifact blob path); passes `"manual"` artifact type to orchestrator
- **`Job.cs` state machine** — `Pending → AwaitingReview` transition added so manual jobs can skip the Parse phase
- **`PipelineDbPersistence.BuildCanonicalModelFromElementsAsync`** — converts user-defined `ArchitectureElement` DB records to a `CanonicalModel` for Phase 2; supports all nine `ElementType` values; deserializes stored `properties` JSON to extract well-known fields (`type`, `protocol`, `auth`, `from`, `to`, `storeType`, etc.)
- **`JobOrchestrator.RunAnalyzePhaseAsync`** — detects `message.ArtifactType == "manual"` and builds the canonical model from DB instead of loading from blob; persists it to `canonical.json` so the normal CLASSIFY → ANALYZE → SYNTHESIZE stages run unchanged
- **Threat model coverage for manual jobs**: same tenant isolation, status-gate, and audit trail as file-upload jobs; `ConfirmArchitecture` validates `IsConfirmed` and `AwaitingReview` status for all job types

### Implemented in session 7 (2026-04-12)

- **GAP-1 (ThreatsController)** — Added `GET /threats/:threatId` endpoint; confirmed PATCH path is `/threats/:threatId/status`; openapi.yaml corrected to match
- **GAP-2 (ArchitecturesController)** — `POST /elements/:elementId` (CorrectElement) endpoint now live; `CorrectionDto` included in all element responses; `CorrectElementRequest` with full validation (CorrectionType allow-list, FieldName required for Update, Note required for AddNote, length guards)
- **GAP-5 (re-analysis)** — `POST /architecture/reanalyze` endpoint added; state machine updated (`Complete/Partial → AwaitingReview`); `Architecture.ResetForReanalysis()` clears confirmation and bumps version; system-generated threats deleted on reanalyze; user-added threats preserved
- **openapi.yaml** — 7 fixes: `SessionResponse` (removed email/displayName), PATCH `/threats/:threatId` → `/status`, element paths changed from `/architecture/elements` → `/elements`, `/export` endpoint added, `/architecture/reanalyze` added, `/jobs/manual` added, `confirmArchitecture` response corrected (200 + ArchitectureModel), `PatchElementRequest` and `ConfirmArchitectureRequest` schemas added, `originalValue` added to `CorrectElementRequest`
- **08-frontend.md** — All GAP-1 through GAP-5 statuses updated to resolved; F-601 updated to include `useCorrectElement` and `useReanalyzeJob`; F-608 updated to include corrections section; F-700 updated to include `useThreat`; F-711 updated to include Re-analyze button; OD-F7 deferral removed; §6.8 Analysis Page re-analysis section updated

### Implemented in session 8 (2026-04-12)

- **Full React/Vite SPA** — all Groups 0–8 of `08-frontend.md` implemented:
  - Group 0: Vite + React 19 project, TypeScript strict, Tailwind, vitest/playwright config, CI frontend job, `staticwebapp.config.json` (HSTS staged, Cache-Control: no-store on authenticated routes)
  - Group 1: WorkOS AuthKit wiring, `client.ts` (axios + Bearer token + 401 retry), `OrgProvider`/`RequireAuth`/`RequireOwner`, `AuthCallbackPage` (open-redirect prevention via `isInternalPath()`), `router.tsx` (all routes, lazy-loaded), `main.tsx`
  - Group 2: `AppShell` (sidebar + mobile drawer), `OrgSwitcher`, `OrgPickerPage` (single-org auto-redirect), `CreateOrgPage`, error pages (404/401/error)
  - Group 3: `DashboardPage` (polling, status-transition toasts, delete confirm), `JobCard`, `JobStatusBadge`
  - Group 4: `UploadJobPage`, `ManualJobPage`, `UploadDropzone` (MIME + extension validation, file preview)
  - Group 5: `JobDetailPage` (pipeline stepper, polling, auto-navigate on transition)
  - Group 6: `ReviewPage`, `ArchCanvas` (ReactFlow + dagre), `ElementNode`, `ElementListPanel` (SR table), `AddElementModal`, `AddCorrectionModal`, `ElementDetailPanel`, `ArchitectureMetaPanel`
  - Group 7: `ThreatCard`, `ThreatDetailPanel`, `ThreatFilterBar` (URL search params), `AddThreatModal`, `AnalysisPage`, `RecommendationsPanel`, `RemediationPanel`, `ExportPanel` (JSON blob download + Markdown client-side)
  - Group 8: `MembersPage` (no enumeration oracle on invite), `OrgSettingsPage`, `IdpConfigPage`, `ProfilePage` (no PII, double-confirm delete)
  - Group 9 partial: `ErrorBoundary`, sonner Toaster, skeletons, `manifest.json`, `favicon.svg`, `rollup-plugin-visualizer` in devDependencies; **F-903** (title management), **F-904** (canvas keyboard nav), **F-907** (axe-core wiring) still pending
  - Group 10 partial: 6 unit tests (UploadDropzone, JobStatusBadge, ThreatCard, OrgContext, AuthCallbackPage, ThreatFilterBar), 3 E2E stubs (auth, cross-org, export); **F-T04/T06/T09/T10** and **F-E01/E02/E03/E06** still pending

### Implemented in session 9 (2026-04-12)

- **F-903** — `usePageTitle` hook (`src/hooks/usePageTitle.ts`); wired into DashboardPage, JobDetailPage, ReviewPage, AnalysisPage, OrgSettingsPage, MembersPage, ProfilePage with dynamic job titles where data is available
- **F-904** — Canvas keyboard navigation in `ArchCanvas`: Tab/Shift-Tab cycles non-DataFlow elements, Enter selects first element when nothing is focused, Delete calls `onDeleteElement` callback for UserAdded elements; `ReviewPage` wired with keyboard-delete confirm dialog
- **F-907** — `@axe-core/react` wired in `main.tsx` behind `import.meta.env.DEV` guard; runs at 1 s cadence in development, reports accessibility violations to the browser console
- **F-T04** — `AddElementModal.test.tsx`: empty-name validation, onSubmit payload, dialog close on success
- **F-T06** — `client.test.ts`: Bearer token attachment, 401 retry with refresh, redirect on null/throwing refresh, double-401 loop prevention, non-401 pass-through
- **F-T09** — `ElementDetailPanel.test.tsx`: display, edit/save → onPatch, delete → confirm → onDelete, readOnly mode suppresses buttons
- **F-T10** — `ExportPanel.test.tsx`: JSON download calls mutateAsync, Markdown download creates blob anchor, button labels contain no credentials
- **F-E01/E02/E03/E06** — E2E test files created (`upload.spec.ts`, `manual.spec.ts`, `threats.spec.ts`, `members.spec.ts`); static assertions run against dev server; full flow assertions are `test.fixme` pending auth helpers and a live API

### Implemented in session 10 (2026-04-12)

- **GAP-TH1** — `AddThreatModal`: required element multi-select (checkboxes, DataFlow elements excluded); Zod `min(1)` validation; `affectedElementIds` now always populated in submit payload; `preselectedElementId` prop pre-ticks the active canvas element
- **GAP-TH2** — `Threat.CreateUserAdded()`: throws `ArgumentException` on empty `affectedElementIds` (domain invariant enforcement, spec data-model §9); `ThreatsController.AddThreat`: returns HTTP 422 `ELEMENT_REQUIRED` for null/empty `affectedElementIds`
- **GAP-TH3** — `AnalysisPage`: canvas `onElementSelect` writes `elementId` to URL search params; `useThreats` passes it to backend; `ThreatFilterBar` shows active element chip with clear button; tab auto-switches to Threats; `ListThreats` controller accepts `?elementId=` query param and passes to repository; `ThreatRepository.ListByJobAsync` adds `.Where(t => elementId == null || t.AffectedElementIds.Contains(elementId.Value))`
- **GAP-TH4** — `canvasLayout.ts`: edges with threats render amber with `⚠ N` suffix in label; `ArchCanvas`: `onEdgeClick` prop wired, triggers same `elementId` filter as node click
- **GAP-TH5** — `ElementDetailPanel`: new `relatedThreats?: Threat[]` + `onThreatClick` props; renders "Threats (N)" section at bottom with status badges and clickable rows; `AnalysisPage` architecture tab shows 288px side panel for selected element with `threatsForSelectedElement` passed in
- **GAP-TH6** — Explicitly deferred to post-MVP as OD-F5 in `08-frontend.md §15`
- **GAP-TH7** — `ThreatsController.AddThreat`: status gate expanded to include `AwaitingReview`; `ReviewPage`: "Flag concern" button added to top bar (visible when `AwaitingReview` and elements exist), wired to `AddThreatModal` with current selected element pre-ticked

### Implemented in session 11 (2026-04-12)

- **Worker stage unit tests** — `ILlmClientFactory` interface extracted from the sealed `LlmClientFactory`; all five LLM-backed stage constructors updated to take `ILlmClientFactory`; DI registration updated (`Program.cs`). Five new test files added:
  - `tests/ThreatModelingAgent.Worker.Tests/Pipeline/ParseStageTests.cs` — text/image model routing, size cap enforcement, image media type detection, schema validation/retry, low-confidence flag
  - `tests/ThreatModelingAgent.Worker.Tests/Pipeline/NormalizeStageTests.cs` — strong model enforced, schema validation, token budget, `PersistAsync`/`LoadAsync` blob path and content
  - `tests/ThreatModelingAgent.Worker.Tests/Pipeline/ClassifyStageTests.cs` — low-cost model enforced, `EnforceRequiredMethods` for all categories, schema validation, user corrections in prompt
  - `tests/ThreatModelingAgent.Worker.Tests/Pipeline/AnalyzeStageTests.cs` — security-critical vs pattern-driven model routing, `EnforceTraceability` (unknown labels → rejected), `RunAllMethodsAsync` parallel execution, schema validation
  - `tests/ThreatModelingAgent.Worker.Tests/Pipeline/SynthesizeStageTests.cs` — strong model enforced, `UserAddedThreats` always normalised to `[]`, `EnforcePartialStatus` for critical gaps, remediation validation, framework mapping sub-step (merge, failure swallowed, unknown framework discarded), `PersistAsync` blob path
- **Go-live requirements** — `docs/go-live.md` created: comprehensive standalone document covering OPS-1–14 (each with description, owner, acceptance criteria, and why), Azure hardening items H-1–H-5, E2E infrastructure items E-1–E-5, and sign-off tracking table

### What remains (future tasks)

| # | Section | Theme |
|---|---|---|
| 8 | Pre-GA Operational Requirements | All items documented in detail in [docs/go-live.md](../go-live.md) — not implementable in code |
| 9 | Deferred by Design | Post-MVP items |
| 10 | Frontend | **Complete.** All F-000…F-E07 implemented. E2E flows needing a live API are `test.fixme` pending auth helpers (tracked in go-live.md E-1–E-5). |

---

## 2. Pipeline Contract Gaps

Complete. See git history.

---

## 3. Worker Pipeline — Framework Mapping Sub-Step

Complete.

---

## 4. CI Security Controls

Complete.

---

## 5. API — Remaining Gaps

Complete.

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

`DELETE /v1/me` is implemented (see §5, session 1).

### 6.3 Phase 2 — Org erasure background job

Deferred to post-MVP (see §9 D-9). The blast radius is high and requires careful idempotency design. For MVP: org deletion is a manual platform operation. The endpoint exists only in the admin API (D-3 post-MVP).

---

## 7. Test Coverage

255 unit/security tests passing as of 2026-04-11. Group B integration tests added in session 4 (compile-clean; require Docker for runtime). All groups now implemented.

### Done

| File | Project |
|---|---|
| `tests/ThreatModelingAgent.Domain.Tests/Entities/ThreatTests.cs` | Domain |
| `tests/ThreatModelingAgent.Domain.Tests/Entities/ArchitectureTests.cs` | Domain |
| `tests/ThreatModelingAgent.Domain.Tests/Entities/MitigationTests.cs` | Domain |
| `tests/ThreatModelingAgent.Domain.Tests/Entities/FrameworkMappingTests.cs` | Domain |
| `tests/ThreatModelingAgent.Domain.Tests/Entities/RejectedCandidateTests.cs` | Domain |
| `tests/ThreatModelingAgent.Domain.Tests/Entities/JobTests.cs` | Domain |
| `tests/ThreatModelingAgent.Domain.Tests/Entities/OrganizationTests.cs` | Domain |
| `tests/ThreatModelingAgent.Domain.Tests/Entities/UserTests.cs` | Domain |
| `tests/ThreatModelingAgent.Domain.Tests/ValueObjects/ValueObjectTests.cs` | Domain |
| `tests/ThreatModelingAgent.Api.Tests/Security/CorrelationIdTests.cs` | API |
| `tests/ThreatModelingAgent.Api.Tests/Security/SecurityHeadersTests.cs` | API |
| `tests/ThreatModelingAgent.Api.Tests/Security/TenantContextMiddlewareTests.cs` | API |
| `tests/ThreatModelingAgent.Api.Tests/Validation/OrgValidatorTests.cs` | API |
| `tests/ThreatModelingAgent.Worker.Tests/Pipeline/CorrectionApplicatorTests.cs` | Worker |
| `tests/ThreatModelingAgent.Worker.Tests/Pipeline/FrameworkNormalizerTests.cs` | Worker |
| `tests/ThreatModelingAgent.Worker.Tests/Pipeline/TokenEstimatorTests.cs` | Worker |
| `tests/ThreatModelingAgent.Worker.Tests/Pipeline/DetectStageTests.cs` | Worker |
| `tests/ThreatModelingAgent.Worker.Tests/Pipeline/StageRetryHelperTests.cs` | Worker |
| `tests/ThreatModelingAgent.Worker.Tests/Pipeline/PromptTemplateVersionTests.cs` | Worker |
| `tests/ThreatModelingAgent.Worker.Tests/Pipeline/PromptInjectionTests.cs` | Worker |

### Group D — Worker stage unit tests (session 11)

| File | Covers |
|---|---|
| `tests/ThreatModelingAgent.Worker.Tests/Pipeline/ParseStageTests.cs` | PARSE stage — model routing, size cap, image media type, schema validation |
| `tests/ThreatModelingAgent.Worker.Tests/Pipeline/NormalizeStageTests.cs` | NORMALIZE stage — strong model, schema validation, PersistAsync, LoadAsync |
| `tests/ThreatModelingAgent.Worker.Tests/Pipeline/ClassifyStageTests.cs` | CLASSIFY stage — low-cost model, EnforceRequiredMethods, schema validation |
| `tests/ThreatModelingAgent.Worker.Tests/Pipeline/AnalyzeStageTests.cs` | ANALYZE stage — model routing, EnforceTraceability, RunAllMethodsAsync |
| `tests/ThreatModelingAgent.Worker.Tests/Pipeline/SynthesizeStageTests.cs` | SYNTHESIZE stage — UserAddedThreats normalisation, EnforcePartialStatus, framework mapping, PersistAsync |

### Group B — Integration tests (Testcontainers + WebApplicationFactory)

Complete. All files in `tests/ThreatModelingAgent.Api.Tests/Integration/`.

| File | Covers |
|---|---|
| `TestAuthHandler.cs` | Test auth scheme infrastructure |
| `ApiWebApplicationFactory.cs` | PostgreSQL container, migrations, mock wiring, seeding helpers |
| `AuthenticationTests.cs` | No token → 401, missing org_id → 403, platform:admin → 403, valid claims → 200 |
| `TenantIsolationTests.cs` | Cross-org job 404, list scoped to own org, cross-tenant members 403 |
| `RateLimitingTests.cs` | Strict tier → 429, Retry-After header, error code |
| `JobsControllerTests.cs` | Submit, list (filter, page), get, delete — happy paths, error cases, cross-org |
| `ArchitecturesControllerTests.cs` | GET, confirm (+ phase-2 enqueue verified), PATCH element |
| `ThreatsControllerTests.cs` | List, add, patch-status (all 4 states), export |
| `MembersControllerTests.cs` | List, invite (no enumeration oracle), role update, remove |
| `MeControllerTests.cs` | GET (no PII), DELETE (WorkOS called, fail-secure DB check) |

**Note on RLS in Testcontainers:** Testcontainers connects as the PostgreSQL superuser, which bypasses RLS at the database layer. Tenant isolation tests therefore verify application-layer scope enforcement (membership checks + org_id query filtering). Database-layer RLS verification requires a non-superuser connection and is a pre-GA security test environment task.

### Group C — Prompt injection tests

Complete. `PromptInjectionTests.cs` covers schema enforcement, delimiter wrapping for all user-controlled content, and injection payload handling.

---

## 8. Pre-GA Operational Requirements

All items MUST be completed or have a signed-off deferral with named owner + deadline before GA. These are not implementable in code.

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
| D-10 | ~~Interactive diagram frontend SPA~~ | **Moved to MVP scope.** Full React/Vite SPA is part of the MVP. See [08-frontend.md](08-frontend.md) for spec and backlog. |
| D-11 | Evaluation regression suite | Spec §20; test architectures with expected threat outcomes; required before any prompt template change in production |
| D-12 | Retention enforcement job | Automated purge of blobs and DB rows older than retention policy; requires scheduled trigger |

---

## 10. Identified Gaps — Spec Review (2026-04-12)

These gaps were found during a full cross-spec review. Each violates a MUST or SHOULD in the relevant spec and was absent from both the backlog and the deferred-by-design list. They are tracked here and inline in the affected spec files.

| ID | Severity | Area | Gap | Spec reference |
|---|---|---|---|---|
| GAP-TH1 | **MUST** | Frontend / Domain | `AddThreatModal` accepts an `elements` prop but renders no element selector; `affectedElementIds` is always submitted as `[]`, violating the data-model §9 invariant "A threat MUST reference at least one `architecture_element`" | `03-data-model.md §9`, `01-product.md §19` |
| GAP-TH2 | **MUST** | Domain / API | `Threat.CreateUserAdded()` and `ThreatsController.AddThreat` accept empty `affectedElementIds` with no minimum-length validation; the data-model invariant is not enforced at the service or domain layer | `03-data-model.md §9`, `CLAUDE.md §6.5` |
| GAP-TH3 | **MUST** | Frontend | `AnalysisPage` canvas `onElementSelect` clears the selected threat but does NOT set an `elementId` URL filter param; clicking a diagram element does not filter the threat list to that element — violates `01-product.md §19` interactive diagram MUST | `01-product.md §19` |
| GAP-TH4 | **SHOULD** | Frontend | DataFlow edges do not receive threat-count overlays in the canvas and are not clickable to filter threats by flow — violates `01-product.md §19` MUST "click a data flow and see threats mapped to that flow" | `01-product.md §19` |
| GAP-TH5 | **SHOULD** | Frontend | `ElementDetailPanel` does not show related threats, mitigations, assumptions, or control mappings for the selected element — violates `01-product.md §19` SHOULD per-element views | `01-product.md §19` |
| GAP-TH6 | **SHOULD** | Frontend | Diagram state comparison (original extracted vs user-corrected overlay) is not implemented and is not listed in the deferred-by-design table — `01-product.md §19` SHOULD; should be explicitly deferred or implemented | `01-product.md §19` |
| GAP-TH7 | **MUST** | Frontend / API | Pre-analysis threat/concern addition (during `AwaitingReview` status) is blocked by the API status gate (`Complete or Partial` only) and not surfaced in the UI; `01-product.md §19` MUST "the user can add their own threats or concerns" in the pre-analysis correction workflow | `01-product.md §19` |

### Implementation notes

- **GAP-TH1 + GAP-TH2** are tightly coupled: both must be fixed together. `AddThreatModal` needs a multi-select element picker (sourced from `architecture.elements`), and `Threat.CreateUserAdded()` / the API controller must reject empty `affectedElementIds`. This applies equally to uploaded and manually drawn architectures — there is no discrimination at the API level.
- **GAP-TH3** requires `AnalysisPage` to write an `elementId` URL search param on canvas click and add it to `ThreatFilterBar` and the `useThreats` query filter.
- **GAP-TH4** requires `ArchCanvas` to compute per-edge threat counts from the `threats` list and render a badge on each `DataFlow` edge, plus wire `onEdgeClick` to set `elementId` filter.
- **GAP-TH5** is a SHOULD — the `ElementDetailPanel` in `ReviewPage` shows corrections but not threats; the `AnalysisPage` does not have a per-element panel at all.
- **GAP-TH6** should be explicitly deferred by adding to §9 Deferred by Design.
- **GAP-TH7** requires either (a) a separate endpoint that allows adding threats during `AwaitingReview`, or (b) expanding the status gate to include `AwaitingReview` for user-added threats only, plus surfacing the add-threat UI on the `ReviewPage`.

---

## 11. Completion Criteria for MVP

The MVP is considered complete when all of the following are true:

- [x] All pipeline contract gaps in §2 are fixed (ClassifyInput, FinalOutput, token budgets, prompt versioning).
- [x] Framework mapping sub-step in §3 is implemented as a separate cheap-model call.
- [x] CI SAST and CVE scanning in §4 are running on every PR and failing on critical/high findings.
- [x] `GET /v1/me` implemented.
- [x] GDPR right of access endpoint implemented.
- [x] GDPR portability export (JSON) implemented.
- [x] `platform:admin` role enforcement decision (§5.4) implemented and documented.
- [x] User self-erasure (`DELETE /v1/me`) implemented.
- [x] Group A tests (unit, no Testcontainers) — 255 tests passing.
- [x] Group C tests (prompt injection) — complete.
- [x] Group B tests (API integration, tenant isolation, auth, rate limiting) — complete (requires Docker for runtime).
- [ ] All OPS items in §8 are complete or have signed-off deferrals with named owner and deadline.
- [x] No `TODO` or `FIXME` placeholders exist in production code paths (CLAUDE.md §13).
- [ ] Dependency vulnerability scan passes in CI with no critical or high findings.
