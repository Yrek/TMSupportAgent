# Threat-Model Quality Benchmarks

The quality benchmark suite measures whether the pipeline produces the expected threat findings for known architecture inputs. It catches regressions after prompt changes, model swaps, or parser modifications.

---

## How it works

Each benchmark is a folder under `tests/ThreatModelingAgent.QualityTests/Benchmarks/` containing:

- `diagram.puml` — PlantUML architecture diagram; the primary upload input
- `input.md` — plain-text description of the same architecture; use this if the tool is being tested with text input instead of a diagram
- `expected.yaml` — the group keys that must and must not appear in the output

Scoring is deterministic: it matches `GroupKey` values from the pipeline output against the expected lists. No LLM-as-judge.

---

## Running benchmarks against a real pipeline run

### Step 1 — Upload the benchmark diagram

Open the app, create a new threat model job, and upload `Benchmarks/{benchmark-id}/diagram.puml` as the architecture input.

Alternatively, use `input.md` as a plain-text input if you want to test the text parsing path instead of the diagram path.

Via the API:

```http
POST /v1/orgs/{orgId}/architectures
Content-Type: multipart/form-data

file=@Benchmarks/{benchmark-id}/diagram.puml
name=Benchmark: {benchmark-id}
```

Then submit a job for that architecture.

### Step 2 — Wait for the job to complete

Poll `GET /v1/orgs/{orgId}/jobs/{jobId}` until `status` is `Complete` or `Partial`.

### Step 3 — Download the result JSON

```http
GET /v1/orgs/{orgId}/jobs/{jobId}/export
```

This returns the raw `FinalOutput` JSON used by the scorer.

### Step 4 — Save the result file

Save the downloaded JSON to:

```
tests/ThreatModelingAgent.QualityTests/Results/{benchmark-id}.json
```

Example:

```
tests/ThreatModelingAgent.QualityTests/Results/static-marketing-site.json
tests/ThreatModelingAgent.QualityTests/Results/azure-multitenant-shared-db.json
```

Result files are gitignored — they are local only and not committed.

### Step 5 — Run the benchmarks

```bash
dotnet test tests/ThreatModelingAgent.QualityTests
```

If a result file is missing the corresponding test passes vacuously. Failing tests print a summary showing which group keys were missing or unexpectedly present.

---

## Scoring model

| Metric | Description |
|--------|-------------|
| `must_find_recall` | Fraction of expected group keys that appeared in the output |
| `must_not_claim_violations` | Count of forbidden group keys that appeared in the output |

Thresholds are defined per benchmark in `expected.yaml`:

```yaml
scoring:
  min_must_find_recall: 0.80       # fail if fewer than 80% of must-find keys found
  max_must_not_claim_violations: 0 # fail if any forbidden key appears
```

---

## Available benchmarks

### `static-marketing-site`

**Purpose:** Negative control. Verifies the pipeline does not produce irrelevant or hallucinated findings for a minimal architecture with no API, no database, no auth, and no file upload.

**Must find:** `supply_chain_ci_cd`

**Must not claim:** 14 group keys including `bola_request_parameter`, `no_database_rls`, `cross_tenant_isolation_flaw`, `file_content_attack`, `xss_token_theft`, and others that have no basis in this architecture.

**Recall threshold:** 100% (1 of 1)

---

### `basic-web-api`

**Purpose:** Baseline for the most common web API pattern — single-tenant, authenticated, with file uploads and no edge protection. Covers object-level access, WAF bypass, upload attack surface, and public cloud data endpoints.

**Must find:**
- `bola_request_parameter` — authenticated users accessing each other's resources
- `api_bypass_edge` — App Service reachable without WAF, CDN, or API gateway
- `file_content_attack` — user file uploads (profile pictures, documents)
- `public_dataplane_endpoint` — SQL DB, Blob Storage, Key Vault without private endpoints

**Must not claim:** `cross_tenant_isolation_flaw`, `no_database_rls`, `per_tenant_quota_exhaustion`, `federated_claim_manipulation`, `storage_prefix_isolation`

**Recall threshold:** 75% (3 of 4 must-find keys)

---

### `azure-multitenant-shared-db`

**Purpose:** Verifies the pipeline reliably identifies the core multi-tenant isolation risks in a shared-database SaaS architecture on Azure.

**Must find:**
- `bola_request_parameter` — external users accessing per-tenant objects via API
- `no_database_rls` — shared SQL DB with no row-level security described
- `cross_tenant_isolation_flaw` — application-only tenant guard
- `storage_prefix_isolation` — shared Blob container with prefix-only isolation
- `file_content_attack` — user file uploads

**Must not claim:** none (all group keys are plausible)

**Recall threshold:** 80% (4 of 5 must-find keys)

---

### `azure-storage-sas-uploads`

**Purpose:** Focused storage security scenario. Tests whether SAS token risks, shared account key exposure, and path-prefix-only tenant isolation are reliably identified.

**Must find:**
- `sas_token_access` — SAS URLs issued for client-side direct uploads
- `storage_shared_key` — account key used to generate SAS, stored in config
- `file_content_attack` — arbitrary file types uploaded and displayed in-app
- `storage_prefix_isolation` — single shared container, user isolation by path prefix only

**Must not claim:** `cross_tenant_isolation_flaw`, `no_database_rls`, `per_tenant_quota_exhaustion`, `federated_claim_manipulation`

**Recall threshold:** 75% (3 of 4 must-find keys)

---

## Adding a new benchmark

1. Create a folder under `Benchmarks/` with a kebab-case ID.
2. Add `diagram.puml` — a C4-style PlantUML diagram. Annotate notes with security-relevant details the pipeline should pick up (missing controls, shared resources, unspecified isolation). This is the primary upload input.
3. Add `input.md` — a plain-text description of the same architecture. Useful for testing the text parsing path or as a fallback.
4. Add `expected.yaml` — list `must_find_group_keys` and `must_not_claim_group_keys` using keys from [`GroupKeyRegistry.cs`](../src/ThreatModelingAgent.Worker/Pipeline/GroupKeyRegistry.cs).
5. Run a real pipeline job by uploading `diagram.puml` and save the result JSON (see steps above).
6. Run the benchmarks and confirm the new case passes.

---

## When to run benchmarks

| Trigger | Recommended action |
|---------|--------------------|
| Prompt version bump | Run all benchmarks, compare recall before and after |
| Model change (e.g. gpt-5-mini → new model) | Run all benchmarks with both models |
| GroupKeyRegistry change | Re-check expected.yaml files that reference the modified key |
| Pre-release | Run all benchmarks as a manual quality gate |

Benchmarks are not wired into CI by default because LLM output is non-deterministic. Run them manually before significant releases.
