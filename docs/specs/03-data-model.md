# Data Model Specification

**Status:** Approved  
**Spec ref:** [02-architecture.md](02-architecture.md), [01-product.md](01-product.md)  
**Security ref:** [CLAUDE.md](../../CLAUDE.md) §7, §8  
**Version:** 0.1  
**Date:** 2026-03-31

---

## 1. Scope

Covers:
- PostgreSQL relational schema (tables, columns, constraints, indexes, RLS policies)
- Blob Storage layout
- Job state machine (canonical definition)
- GDPR / data subject considerations
- Invariants and constraints

Does not cover: API request/response shapes (→ openapi.yaml), LLM pipeline contracts (→ 05-llm-workflow.md).

---

## 2. Design Principles

- Every tenant-scoped table has an `org_id` column; RLS enforces it at the database layer
- UUIDs for all primary keys — no sequential integer IDs (prevents enumeration)
- Soft-delete with `deleted_at` on user-facing entities to support GDPR erasure audit trail
- `audit_log` is append-only; no `UPDATE` or `DELETE` permitted on that table
- Minimal PII: email is stored only where technically required; display names are optional
- No LLM prompts, completions, or architecture content stored in plaintext in the DB (stored in Blob; DB holds metadata and paths only)

---

## 3. Enum Types

```sql
CREATE TYPE job_status AS ENUM (
  'PENDING',
  'PARSING',
  'NORMALIZING',
  'AWAITING_REVIEW',
  'CLASSIFYING',
  'ANALYZING',
  'SYNTHESIZING',
  'COMPLETE',
  'FAILED',
  'PARTIAL'
);

CREATE TYPE element_type AS ENUM (
  'component',
  'actor',
  'data_flow',
  'trust_boundary',
  'data_store',
  'external_system',
  'identity',
  'background_job',
  'llm_boundary'
);

CREATE TYPE evidence_strength AS ENUM (
  'direct',
  'inferred',
  'assumption_dependent'
);

CREATE TYPE confidence_level AS ENUM (
  'high',
  'medium',
  'low'
);

CREATE TYPE finding_type AS ENUM (
  'confirmed',
  'conditional',
  'user_added'
);

CREATE TYPE threat_status AS ENUM (
  'open',
  'accepted',
  'mitigated',
  'rejected'
);

CREATE TYPE org_member_role AS ENUM (
  'owner',
  'member'
);

CREATE TYPE correction_type AS ENUM (
  'update',
  'mark_incorrect',
  'mark_assumed',
  'mark_confirmed',
  'add_note'
);
```

---

## 4. Schema

### 4.1 `organizations`

Top-level tenant. All tenant-scoped data references this.

```sql
CREATE TABLE organizations (
  id                uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
  name              varchar(255) NOT NULL,
  slug              varchar(63)  NOT NULL,           -- URL-safe identifier; used in UI paths
  workos_org_id     varchar(255) UNIQUE,             -- WorkOS Organization ID; null until WorkOS org is created
  created_at        timestamptz  NOT NULL DEFAULT now(),
  updated_at        timestamptz  NOT NULL DEFAULT now(),
  deleted_at        timestamptz                      -- soft delete; GDPR erasure workflow
);

CREATE UNIQUE INDEX organizations_slug_idx
  ON organizations (slug)
  WHERE deleted_at IS NULL;
```

**Constraints:**
- `slug` is unique among active orgs; allows reuse after deletion
- `workos_org_id` is set when org is provisioned in WorkOS; MUST be set before any user can log in via org IDP

---

### 4.2 `users`

Platform user identity. Source of truth is WorkOS; this table stores a local reference only.

```sql
CREATE TABLE users (
  id                uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
  workos_user_id    varchar(255) NOT NULL UNIQUE,    -- WorkOS User ID; immutable
  email             varchar(255) NOT NULL,            -- from WorkOS; treated as display only; may change on user update
  display_name      varchar(255),                     -- optional; from WorkOS profile
  created_at        timestamptz  NOT NULL DEFAULT now(),
  updated_at        timestamptz  NOT NULL DEFAULT now(),
  deleted_at        timestamptz                       -- soft delete; GDPR erasure workflow
);

CREATE INDEX users_email_idx ON users (email) WHERE deleted_at IS NULL;
```

**Notes:**
- Email is stored for display and lookup convenience only; not used as an identity key
- WorkOS is the authoritative identity store; this table is a local projection
- On GDPR erasure: `email` and `display_name` MUST be nulled; `workos_user_id` retained as reference for audit log linkage

---

### 4.3 `org_memberships`

Links users to organisations with a role.

```sql
CREATE TABLE org_memberships (
  id          uuid            PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id      uuid            NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  user_id     uuid            NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  role        org_member_role NOT NULL DEFAULT 'member',
  created_at  timestamptz     NOT NULL DEFAULT now(),
  updated_at  timestamptz     NOT NULL DEFAULT now(),
  UNIQUE (org_id, user_id)
);

CREATE INDEX org_memberships_user_idx ON org_memberships (user_id);
CREATE INDEX org_memberships_org_idx  ON org_memberships (org_id);
```

---

### 4.4 `org_idp_configs`

Per-organisation IDP configuration. At most one active config per org.

```sql
CREATE TABLE org_idp_configs (
  id                    uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id                uuid        NOT NULL UNIQUE REFERENCES organizations(id) ON DELETE CASCADE,
  workos_connection_id  varchar(255) NOT NULL,        -- WorkOS SSO Connection ID
  provider_type         varchar(50)  NOT NULL,        -- 'okta' | 'google_workspace' | 'entra_id' | 'oidc' | 'saml'
  domain_hints          text[]       NOT NULL DEFAULT '{}', -- email domains routed to this IDP
  created_at            timestamptz  NOT NULL DEFAULT now(),
  updated_at            timestamptz  NOT NULL DEFAULT now()
);
```

**Security notes (CLAUDE.md §8.1):**
- `domain_hints` drives login routing; MUST be validated to prevent domain squatting
- Only `org:owner` role can create or modify IDP config
- IDP configuration changes MUST be written to `audit_log`

---

### 4.5 `jobs`

Represents a single threat modeling analysis request.

```sql
CREATE TABLE jobs (
  id                  uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id              uuid        NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  created_by          uuid        NOT NULL REFERENCES users(id),
  title               varchar(255),
  status              job_status  NOT NULL DEFAULT 'PENDING',
  error_code          varchar(100),                  -- minimal error code; no internal stack trace
  artifact_blob_path  varchar(2000),                 -- path in Blob Storage: /{org_id}/uploads/{job_id}/{filename}
  artifact_type       varchar(50),                   -- 'image' | 'plantuml' | 'mermaid' | 'drawio' | 'text'
  llm_token_usage     jsonb,                         -- {input_tokens, output_tokens, cost_estimate_usd}
  created_at          timestamptz NOT NULL DEFAULT now(),
  updated_at          timestamptz NOT NULL DEFAULT now(),
  completed_at        timestamptz
);

CREATE INDEX jobs_org_id_idx         ON jobs (org_id, created_at DESC);
CREATE INDEX jobs_status_idx         ON jobs (status) WHERE status NOT IN ('COMPLETE', 'FAILED', 'PARTIAL');
CREATE INDEX jobs_created_by_idx     ON jobs (created_by);
```

**RLS policy:**
```sql
ALTER TABLE jobs ENABLE ROW LEVEL SECURITY;

CREATE POLICY jobs_org_isolation ON jobs
  USING (org_id = current_setting('app.current_org_id')::uuid);
```

---

### 4.6 `architectures`

The normalized canonical system model for a job (spec §5). One per job; versioned on user correction.

```sql
CREATE TABLE architectures (
  id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
  job_id          uuid        NOT NULL UNIQUE REFERENCES jobs(id) ON DELETE CASCADE,
  org_id          uuid        NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  version         int         NOT NULL DEFAULT 1,
  classification  text[]      NOT NULL DEFAULT '{}',  -- from spec §6 categories
  system_purpose  text,
  assumptions     jsonb       NOT NULL DEFAULT '[]',  -- list of {text, confirmed: bool}
  gaps            jsonb       NOT NULL DEFAULT '[]',  -- material unknowns
  clarification_questions jsonb NOT NULL DEFAULT '[]', -- prioritized questions
  confirmed_at    timestamptz,                         -- when user confirmed model
  confirmed_by    uuid        REFERENCES users(id),
  created_at      timestamptz NOT NULL DEFAULT now(),
  updated_at      timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE architectures ENABLE ROW LEVEL SECURITY;
CREATE POLICY architectures_org_isolation ON architectures
  USING (org_id = current_setting('app.current_org_id')::uuid);
```

---

### 4.7 `architecture_elements`

Individual components, actors, flows, boundaries, etc. that make up the architecture model.

```sql
CREATE TABLE architecture_elements (
  id                      uuid         PRIMARY KEY DEFAULT gen_random_uuid(),
  architecture_id         uuid         NOT NULL REFERENCES architectures(id) ON DELETE CASCADE,
  org_id                  uuid         NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  element_type            element_type NOT NULL,
  name                    varchar(255) NOT NULL,
  description             text,
  properties              jsonb        NOT NULL DEFAULT '{}',  -- trust_zone, auth_mechanism, data_types, tenant_relevant, etc.
  source                  varchar(20)  NOT NULL CHECK (source IN ('extracted', 'user_added')),
  extraction_confidence   confidence_level,                    -- null for user_added elements
  created_at              timestamptz  NOT NULL DEFAULT now(),
  updated_at              timestamptz  NOT NULL DEFAULT now()
);

CREATE INDEX arch_elements_arch_id_idx ON architecture_elements (architecture_id);
CREATE INDEX arch_elements_type_idx    ON architecture_elements (architecture_id, element_type);

ALTER TABLE architecture_elements ENABLE ROW LEVEL SECURITY;
CREATE POLICY arch_elements_org_isolation ON architecture_elements
  USING (org_id = current_setting('app.current_org_id')::uuid);
```

**`properties` JSONB structure (illustrative):**
```json
{
  "trust_zone": "internal | external | dmz | untrusted",
  "auth_mechanism": "jwt | api_key | none | mtls",
  "data_types": ["PII", "financial", "architecture_metadata"],
  "internet_facing": true,
  "tenant_relevant": true,
  "notes": "..."
}
```

---

### 4.8 `architecture_corrections`

Provenance trail for user corrections on extracted elements. Immutable once written.

```sql
CREATE TABLE architecture_corrections (
  id                uuid            PRIMARY KEY DEFAULT gen_random_uuid(),
  element_id        uuid            REFERENCES architecture_elements(id) ON DELETE SET NULL,
  architecture_id   uuid            NOT NULL REFERENCES architectures(id) ON DELETE CASCADE,
  org_id            uuid            NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  corrected_by      uuid            NOT NULL REFERENCES users(id),
  correction_type   correction_type NOT NULL,
  field_name        varchar(100),
  original_value    text,
  corrected_value   text,
  note              text,
  created_at        timestamptz     NOT NULL DEFAULT now()
  -- no updated_at; corrections are immutable
);

CREATE INDEX arch_corrections_arch_id_idx ON architecture_corrections (architecture_id);

ALTER TABLE architecture_corrections ENABLE ROW LEVEL SECURITY;
CREATE POLICY arch_corrections_org_isolation ON architecture_corrections
  USING (org_id = current_setting('app.current_org_id')::uuid);
```

---

### 4.9 `threats`

Generated and user-added threats. Core output of the threat modeling process.

```sql
CREATE TABLE threats (
  id                    uuid              PRIMARY KEY DEFAULT gen_random_uuid(),
  job_id                uuid              NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
  org_id                uuid              NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  identifier            varchar(20)       NOT NULL,          -- e.g. 'T-001'; unique within job
  title                 varchar(500)      NOT NULL,
  method_category       varchar(100)      NOT NULL,          -- 'STRIDE-S' | 'STRIDE-T' | ... | 'LINDDUN' | 'ABUSE_CASE' | 'TENANT_ISOLATION' | etc.
  affected_element_ids  uuid[]            NOT NULL DEFAULT '{}',
  description           text              NOT NULL,
  attack_scenario       text              NOT NULL,
  preconditions         text,
  impacted_assets       text[]            NOT NULL DEFAULT '{}',
  security_impact       text,
  privacy_impact        text,
  existing_controls     text,
  control_gaps          text,
  confidence            confidence_level  NOT NULL,
  evidence_basis        text[]            NOT NULL DEFAULT '{}',  -- from allowed set in spec §11
  evidence_strength     evidence_strength NOT NULL,
  assumptions           text,
  finding_type          finding_type      NOT NULL,
  status                threat_status     NOT NULL DEFAULT 'open',
  source                varchar(20)       NOT NULL CHECK (source IN ('system', 'user')),
  created_at            timestamptz       NOT NULL DEFAULT now(),
  updated_at            timestamptz       NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX threats_job_identifier_idx ON threats (job_id, identifier);
CREATE INDEX threats_job_id_idx              ON threats (job_id);
CREATE INDEX threats_finding_type_idx        ON threats (job_id, finding_type);

ALTER TABLE threats ENABLE ROW LEVEL SECURITY;
CREATE POLICY threats_org_isolation ON threats
  USING (org_id = current_setting('app.current_org_id')::uuid);
```

---

### 4.10 `threat_notes`

User annotations on threats (separate from status changes).

```sql
CREATE TABLE threat_notes (
  id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
  threat_id   uuid        NOT NULL REFERENCES threats(id) ON DELETE CASCADE,
  org_id      uuid        NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  created_by  uuid        NOT NULL REFERENCES users(id),
  body        text        NOT NULL,
  created_at  timestamptz NOT NULL DEFAULT now(),
  updated_at  timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE threat_notes ENABLE ROW LEVEL SECURITY;
CREATE POLICY threat_notes_org_isolation ON threat_notes
  USING (org_id = current_setting('app.current_org_id')::uuid);
```

---

### 4.11 `mitigations`

Recommended mitigations linked to threats.

```sql
CREATE TABLE mitigations (
  id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
  threat_id   uuid        NOT NULL REFERENCES threats(id) ON DELETE CASCADE,
  org_id      uuid        NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  title       varchar(500) NOT NULL,
  description text        NOT NULL,
  priority    varchar(20)  NOT NULL CHECK (priority IN ('critical', 'high', 'medium', 'low')),
  category    varchar(100),  -- 'design_change' | 'authorization_fix' | 'token_handling' | etc.
  created_at  timestamptz NOT NULL DEFAULT now(),
  updated_at  timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX mitigations_threat_id_idx ON mitigations (threat_id);

ALTER TABLE mitigations ENABLE ROW LEVEL SECURITY;
CREATE POLICY mitigations_org_isolation ON mitigations
  USING (org_id = current_setting('app.current_org_id')::uuid);
```

---

### 4.12 `framework_mappings`

Maps threats to security framework controls (OWASP, ASVS, CIS, NCSC, etc.).

```sql
CREATE TABLE framework_mappings (
  id            uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
  threat_id     uuid        NOT NULL REFERENCES threats(id) ON DELETE CASCADE,
  org_id        uuid        NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  framework     varchar(100) NOT NULL,   -- 'owasp_top10' | 'owasp_api_top10' | 'asvs' | 'cis_controls' | 'ncsc' | 'twelve_factor'
  reference     varchar(200) NOT NULL,   -- e.g. 'A01:2021', 'ASVS 4.1.1'
  mapping_type  varchar(20)  NOT NULL CHECK (mapping_type IN ('direct', 'approximate')),
  created_at    timestamptz  NOT NULL DEFAULT now()
);

CREATE INDEX framework_mappings_threat_idx ON framework_mappings (threat_id);

ALTER TABLE framework_mappings ENABLE ROW LEVEL SECURITY;
CREATE POLICY framework_mappings_org_isolation ON framework_mappings
  USING (org_id = current_setting('app.current_org_id')::uuid);
```

---

### 4.13 `rejected_candidates`

Candidate threats that were generated but rejected before final output (spec §20 — record with reason).

```sql
CREATE TABLE rejected_candidates (
  id            uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
  job_id        uuid        NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
  org_id        uuid        NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  title         varchar(500) NOT NULL,
  method_category varchar(100),
  rejection_reason varchar(100) NOT NULL,  -- 'insufficient_evidence' | 'duplicate_root_cause' | 'out_of_scope' | 'mitigation_confirmed' | 'too_speculative'
  rejection_note  text,
  created_at    timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE rejected_candidates ENABLE ROW LEVEL SECURITY;
CREATE POLICY rejected_candidates_org_isolation ON rejected_candidates
  USING (org_id = current_setting('app.current_org_id')::uuid);
```

---

### 4.14 `audit_log`

Immutable append-only audit record. No UPDATE or DELETE permitted (enforced by DB role: app user has INSERT only on this table).

```sql
CREATE TABLE audit_log (
  id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id          uuid,                               -- null for platform-level events
  user_id         uuid,                               -- null for system events
  correlation_id  uuid        NOT NULL,
  event_type      varchar(100) NOT NULL,              -- e.g. 'job.submitted', 'idp_config.created', 'auth.failure'
  resource_type   varchar(100),
  resource_id     uuid,
  details         jsonb       NOT NULL DEFAULT '{}',  -- non-PII only; IDs not names
  ip_address      inet,
  created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX audit_log_org_idx        ON audit_log (org_id, created_at DESC);
CREATE INDEX audit_log_user_idx       ON audit_log (user_id, created_at DESC);
CREATE INDEX audit_log_event_type_idx ON audit_log (event_type, created_at DESC);
```

**Note:** No RLS on audit_log — access is restricted by application role only. Platform admins query directly; no tenant should query other tenants' audit records. This is enforced at the API layer.

---

## 5. Blob Storage Layout

```
/{org_id}/
  uploads/
    {job_id}/
      original.{ext}          ← uploaded artifact (original filename replaced at write time)

  parsed/
    {job_id}/
      parsed.json              ← structured output from PARSING stage

  outputs/
    {job_id}/
      analysis.json            ← full structured analysis output (spec §19)
      threats.json             ← threats array (for diagram UI consumption)
      architecture.json        ← canonical system model snapshot at analysis time
```

**Security:**
- All paths are prefixed with `/{org_id}/`; no cross-org path access
- Uploaded files are renamed to a random identifier on write — original filename is stored in `jobs.artifact_blob_path` metadata only
- SAS tokens for upload are short-lived (5 minutes), write-once, scoped to the specific blob path
- No public blob access; all reads go through `api` which validates org scope before generating read SAS

---

## 6. Job State Machine

Canonical state machine for `jobs.status`. This is the single source of truth; all other representations (UI labels, worker logic) derive from this.

```
                    ┌──────────┐
                    │ PENDING  │  ← job created by api
                    └────┬─────┘
                         │ worker dequeues
                    ┌────▼─────┐
                    │ PARSING  │  ← artifact type detection, diagram parsing
                    └────┬─────┘
                         │ parsed representation stored
                    ┌────▼──────────┐
                    │  NORMALIZING  │  ← canonical system model built
                    └────┬──────────┘
                         │ model stored; user notified
                    ┌────▼────────────────┐
                    │  AWAITING_REVIEW    │  ← async wait for user confirmation
                    └────┬────────────────┘
                         │ user confirms (or timeout per OD-2)
                    ┌────▼──────────┐
                    │  CLASSIFYING  │  ← architecture classification + method selection
                    └────┬──────────┘
                         │
                    ┌────▼──────────┐
                    │   ANALYZING   │  ← threat generation per selected methods
                    └────┬──────────┘
                         │
                    ┌────▼──────────────┐
                    │   SYNTHESIZING    │  ← merge, prioritize, assemble output
                    └────┬──────────────┘
                         │
          ┌──────────────┼──────────────┐
     ┌────▼────┐   ┌─────▼──────┐  ┌───▼─────┐
     │COMPLETE │   │   FAILED   │  │ PARTIAL │
     └─────────┘   └────────────┘  └─────────┘
```

Any stage MAY transition to `FAILED` on unrecoverable error. `PARTIAL` is used when spec §19 conditions apply (critical ambiguity unresolved). 

Retries: PARSING and NORMALIZING stages retry up to 3 times before transitioning to FAILED. ANALYZING and SYNTHESIZING do not auto-retry (results may be partial if retried inconsistently).

---

## 7. RLS Session Variable

The application MUST set the PostgreSQL session variable before executing any query:

```sql
SET app.current_org_id = '<org_id_from_validated_jwt>';
```

This variable is used by all RLS policies. It MUST be derived from the validated JWT — never from request parameters.

---

## 8. GDPR / Data Subject Considerations

| Data subject right | Implementation |
|---|---|
| Right of access | `GET /orgs/{orgId}/members/{userId}/data` — returns all data held for that user within that org |
| Right to erasure | Soft-delete user; null `email` and `display_name`; retain `id` and `workos_user_id` for audit log FK integrity; purge org data per retention schedule |
| Right to portability | Export of threat models and analysis results as JSON via export endpoint |
| Data minimization | Email stored for display only; no address, phone, or payment data in this schema |

Open Decision OD-5: full erasure workflow design (cascading org data deletion, WorkOS account deletion coordination) is deferred pending legal input.

---

## 9. Invariants

The following MUST be enforced at the service/domain layer (not only at the database):

- A `job` MAY only be created by a user who is a member of `job.org_id`
- A `threat` MUST reference at least one `architecture_element` that belongs to the same `job`
- `architecture_corrections` for an element MUST belong to the same `org_id` as the element
- `jobs.status` transitions MUST follow the state machine in §6; no arbitrary status updates
- `audit_log` rows MUST NOT be updated or deleted by the application user role
- `threats.identifier` MUST be unique within a job and MUST follow the format `T-NNN`
- `threats.confidence = 'high'` MUST NOT be set unless `finding_type = 'confirmed'`
