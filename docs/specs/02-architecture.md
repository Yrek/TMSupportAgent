# Architecture Specification

**Status:** Approved  
**Spec ref:** [01-product.md](01-product.md)  
**Security ref:** [CLAUDE.md](../../CLAUDE.md), [06-security.md](06-security.md)  
**ADR:** [ADR-001 — WorkOS identity](../adr/ADR-001-workos-identity.md)  
**Version:** 0.2 (WorkOS replaces Entra External ID)  
**Date:** 2026-03-31

---

## 1. Scope

Covers Azure service selection, service decomposition, multi-tenancy, identity, LLM routing, data architecture, network topology, encryption (including BYOK path), observability, cost model, and ISO 27001 control mapping.

Does not cover: API contracts, database schema, UI design, prompt templates, deployment pipelines. Those are in separate spec documents.

---

## 2. Design Principles

In order of precedence:

1. **Security by design** — security is a functional requirement (CLAUDE.md §4.1)
2. **Least privilege** — every service, identity, and token has minimum required permissions (CLAUDE.md §4.4)
3. **Tenant isolation** — tenant data MUST be isolated at every layer; a bug MUST NOT leak one tenant's data to another
4. **Fail secure** — failures in auth, authz, or dependency MUST deny, never grant (CLAUDE.md §4.3)
5. **Cost-conscious MVP** — prefer managed services that scale to zero; do not over-provision before demand is known
6. **BYOK-ready** — encryption architecture MUST support customer-managed keys as a future upgrade without re-architecture
7. **EU data residency** — all persistent data MUST be stored in EU Azure regions
8. **Spec-driven** — no implementation begins without an agreed spec

---

## 3. Azure Region Strategy

| Concern | Decision |
|---|---|
| Primary region | **West Europe** (Amsterdam) — EU data residency, GDPR jurisdiction |
| Secondary / DR | **North Europe** (Dublin) — geo-paired; DR runbook required before GA |
| MVP stance | Primary region only; no geo-replication until DR is explicitly activated |
| Data residency | Zone-redundant storage within West Europe; no cross-region transfer of tenant data |

---

## 4. Service Decomposition

### 4.1 Deployable units

| Service | Responsibility | Runtime |
|---|---|---|
| `api` | REST API — auth enforcement, job submission, result retrieval, org/user management | Azure Container App (always-on, HTTPS ingress) |
| `worker` | Async analysis pipeline — ingestion, normalization, threat generation, output assembly | Azure Container App (queue-triggered, scales to zero) |
| `frontend` | SPA — interactive diagram UI, threat review, org/user management | Azure Static Web Apps |
| _(future)_ `admin-api` | Platform operator API — tenant management, billing hooks, audit access | Container App (internal ingress only) |

### 4.2 Rationale: Container Apps over AKS

- Scales to zero — zero cost when idle; critical pre-revenue
- No Kubernetes cluster management overhead
- Native KEDA-based queue-length scaling for the worker
- Managed identity support for all Azure resources (no stored credentials)

### 4.3 Trust boundaries and service flow

```
[Browser / SPA]
      │  HTTPS only (TLS 1.2+)
      ▼
[Azure Static Web Apps]          ← public; serves SPA shell only; no auth required
      │  HTTPS only
      ▼
[api — Container App]            ← authenticated boundary; JWT validated on every request
      │  managed identity
      ├──► [Azure Service Bus]        ← submits analysis jobs
      ├──► [Azure PostgreSQL]         ← reads/writes scoped by org_id
      └──► [Azure Blob Storage]       ← uploads scoped by org_id prefix

[worker — Container App]         ← no public ingress; triggered by Service Bus only
      │  managed identity
      ├──► [Azure Service Bus]        ← dequeues jobs; dead-letter on failure
      ├──► [Azure PostgreSQL]         ← writes analysis results scoped by org_id
      ├──► [Azure Blob Storage]       ← reads uploads; writes outputs
      ├──► [Azure OpenAI]             ← GPT-4o, GPT-4o-mini (private endpoint, West Europe)
      ├──► [Anthropic API]            ← Claude Sonnet/Haiku (outbound HTTPS; key from Key Vault)
      └──► [Azure Key Vault]          ← secret reads only

[WorkOS]                         ← external identity provider; HTTPS only; EU region
      │  OIDC callback
      └──► [api]
```

All service-to-Azure-resource communication uses **managed identity** — no connection strings, no API keys in environment variables (except LLM provider keys sourced from Key Vault at startup).

---

## 5. Azure Services

### 5.1 Selected services and cost estimates

| Service | Tier (MVP) | Purpose | Est. monthly cost |
|---|---|---|---|
| Azure Container Apps | Consumption | `api` and `worker` | ~€20–60 (scales to zero) |
| Azure Static Web Apps | Standard | Frontend SPA | €9 |
| Azure Database for PostgreSQL — Flexible Server | Burstable B2s (2 vCore, 4 GB) | Primary relational store | ~€50 |
| Azure Blob Storage | LRS, Hot tier | Uploaded diagrams, analysis outputs | ~€2–5 |
| Azure Service Bus | Standard | Async job queue with dead-letter support | €10 + usage |
| Azure Container Registry | Basic | Container images | €5 |
| Azure Key Vault | Standard | Secrets; CMK for BYOK path | ~€1–3 |
| WorkOS | Free (up to 1M MAU) | Identity platform — social login, email/password, per-org IDP federation | €0 MVP |
| Azure Application Insights | Pay-as-you-go | Structured logging, traces, metrics | First 5 GB/month free |

**Estimated MVP running cost: ~€100–140/month at near-zero usage.**

### 5.2 Deferred services (post-MVP)

| Service | Reason deferred |
|---|---|
| Azure API Management | Adds €50–150/month; Container Apps ingress sufficient at MVP |
| Azure Front Door | WAF / CDN; add when L7 attack mitigation or global distribution is needed |
| Azure Private DNS Zones | Add with private endpoints (see §11.1) |
| Azure PIM (Privileged Identity Management) | JIT operator access; required before GA, deferred from day-one MVP |

---

## 6. Identity and Access Architecture

### 6.1 Platform identity (user-facing)

**Provider: WorkOS** (see [ADR-001](../adr/ADR-001-workos-identity.md))

WorkOS handles:
- **Social login**: Google, Microsoft, GitHub (built-in connections)
- **Email + password** with MFA via WorkOS AuthKit
- **Per-organisation enterprise IDP federation**: each org configures their own OIDC or SAML connection (Entra ID, Google Workspace, Okta, or any standards-compliant provider) through the WorkOS dashboard and API

#### Per-organisation custom IDP

- Each org MAY configure a custom IDP connection via the WorkOS API
- Email domain(s) bound to the connection; login attempts for those domains are automatically routed to the org IDP
- Once a custom IDP is active for a domain, direct platform login for that domain MUST be disabled
- Custom IDP config is org-scoped; it MUST NOT affect other orgs
- IDP configuration is stored in WorkOS, not in our database (we store the WorkOS `connection_id` reference only)

#### Token policy (CLAUDE.md §8.1)

- WorkOS issues short-lived access tokens (15 minutes) and rotating refresh tokens
- Tokens validated server-side on every `api` request against WorkOS JWKS
- Claims verified: issuer, audience, expiry, signature, email verified state
- JWKS endpoint and issuer metadata come from WorkOS configuration — never from user input

### 6.2 Service identity (internal)

All service-to-service and service-to-Azure-resource authentication uses **Azure Managed Identity** exclusively:

| Service | Identity | Permissions |
|---|---|---|
| `api` | System-assigned managed identity | Service Bus sender; Blob contributor (upload container); PostgreSQL user; Key Vault secret reader |
| `worker` | System-assigned managed identity | Service Bus receiver; Blob contributor (output container + read uploads); PostgreSQL user; Key Vault secret reader; Azure OpenAI user |

No service principal credentials. No connection strings. No API keys in environment variables.

### 6.3 Application roles and authority boundaries

Three roles, defined canonically in the `api` service codebase. Roles are enforced server-side on every request. Client-supplied role claims are never trusted without server-side validation.

| Role | Scope | Capabilities |
|---|---|---|
| `org:owner` | Organisation | Organisation admin role in MVP: org settings, member management, IDP config, all threat models in own org |
| `org:member` | Organisation | Create and view threat models within own org |
| `platform:admin` | Platform | Platform-level organisation lifecycle and oversight only; no access to org threat model data |

Authority model:
- Only `platform:admin` may create new organisations.
- Org-internal administration is performed by `org:owner` (org admin in MVP).
- Platform admin and org admin planes are separate; a platform admin token is not accepted on org-scoped routes.

Role definitions are in a single canonical location in code. Do not infer role hierarchy from usage patterns. See CLAUDE.md §8.2.

### 6.4 Platform operator access

- Operators access Azure resources via Azure RBAC with MFA-enforced Entra ID accounts
- No shared credentials; no service account logins for humans
- Azure PIM (Just-In-Time privileged access) MUST be in place before GA

---

## 7. Multi-Tenancy Model

### 7.1 Tenant definition

An **organisation** is the top-level tenant. All tenant data is scoped to an organisation. Users belong to one or more organisations. A user's data access is always scoped to the organisation context of the current request.

### 7.2 Isolation at every layer

| Layer | Isolation mechanism |
|---|---|
| API | `org_id` extracted from validated JWT on every request; never from request body or query string |
| Database | PostgreSQL Row-Level Security (RLS) on all tenant-scoped tables; `org_id` column required on every tenant table; all queries include `org_id` predicate enforced by RLS policy |
| Blob Storage | Path prefix `/{org_id}/` on all uploads and outputs; managed identity + SAS tokens scoped to org prefix only |
| Service Bus messages | `org_id` in message metadata; validated by worker before processing; messages with mismatched `org_id` are dead-lettered |
| LLM context | `org_id` is never passed to LLM prompts; tenant context is applied server-side after LLM output is received |
| Logs | `org_id` as a structured log field; log access is platform-operator only |

### 7.3 Cross-tenant access prohibition

- No endpoint MAY return data from an org the authenticated user does not belong to
- Admin access to org data on behalf of support MUST be explicitly designed with per-access logging before it is implemented (deferred)
- Non-admin authentication MUST satisfy two checks before any org-scoped access:
  - JWT `org_id` resolves to a known internal organisation.
  - JWT `sub` (user) is mapped to that organisation in `org_memberships`.
- If either check fails, access is denied (fail-secure); self-service org bootstrap is not allowed.

---

## 8. LLM Provider Integration and Routing

### 8.1 Providers (MVP)

| Provider | Access | Models |
|---|---|---|
| Azure OpenAI (West Europe) | Managed identity or API key in Key Vault | `gpt-4o`, `gpt-4o-mini` |
| Anthropic | API key in Key Vault; outbound HTTPS | `claude-sonnet-4-6`, `claude-haiku-4-5` |

### 8.2 Model routing

| Task (spec §9) | Model tier | Assigned model |
|---|---|---|
| Architecture interpretation from ambiguous input | Strong | `gpt-4o` or `claude-sonnet-4-6` |
| Trust-boundary reasoning | Strong | `gpt-4o` or `claude-sonnet-4-6` |
| Multi-tenant isolation reasoning | Strong | `gpt-4o` or `claude-sonnet-4-6` |
| Final threat synthesis | Strong | `gpt-4o` or `claude-sonnet-4-6` |
| AI/tool-context threat analysis | Strong | `gpt-4o` or `claude-sonnet-4-6` |
| Diagram parsing — image input | Multimodal | `gpt-4o` (vision) |
| Diagram parsing — code input (PlantUML, Mermaid, Draw.io) | Low-cost | `gpt-4o-mini` or `claude-haiku-4-5` |
| Architecture classification | Low-cost | `gpt-4o-mini` or `claude-haiku-4-5` |
| Method selection | Low-cost | `gpt-4o-mini` or `claude-haiku-4-5` |
| Deduplication, tagging, framework mapping | Low-cost | `gpt-4o-mini` or `claude-haiku-4-5` |
| Output formatting | Low-cost | `gpt-4o-mini` or `claude-haiku-4-5` |

The `worker` selects model per pipeline stage, not per job.

### 8.3 LLM security constraints (CLAUDE.md §16, spec §20)

- LLM output is **untrusted** at every step
- LLM output MUST NOT be used as SQL, file paths, shell commands, or authorization decisions without deterministic validation
- Prompts MUST NOT contain secrets, credentials, or connection strings
- Uploaded content is injected into prompts as data to be analyzed, clearly separated from system instructions
- `org_id` and tenant context are NEVER in prompts; applied server-side after LLM output
- All prompt templates are versioned and stored in code, not the database

### 8.4 Cost controls

- Low-cost models MUST be used wherever spec permits
- Token counts per job MUST be logged (counts only, not content) for cost monitoring
- Per-job soft token limit SHOULD be enforced; jobs exceeding it are flagged, not silently continued
- Azure OpenAI quota limits MUST be set per deployment to cap spend

---

## 9. Async Job Pipeline

### 9.1 Decision: async with polling

Analysis takes 60–180 seconds depending on architecture complexity. Jobs are submitted and the user returns to view results. SSE progress updates are a UX improvement deferred to post-MVP (Open Decision OD-3).

### 9.2 Pipeline stages

```
PENDING
  │
  ▼ worker picks up message
PARSING          ← detect artifact type; parse to structured representation
  │
  ▼
NORMALIZING      ← build canonical system model (spec §5); strong model
  │
  ▼
AWAITING_REVIEW  ← user reviews and corrects normalized model in UI
  │              (auto-proceed timeout: Open Decision OD-2)
  ▼ user confirms
CLASSIFYING      ← classify architecture; select methods (spec §6, §7, §8); cheap model
  │
  ▼
ANALYZING        ← run selected threat modeling methods; strong model for security-critical steps
  │
  ▼
SYNTHESIZING     ← merge duplicates; separate confirmed vs conditional; prioritize; strong model
  │
  ▼
COMPLETE  |  FAILED  |  PARTIAL
```

`PARTIAL` is used when critical architectural ambiguity remains unresolved (spec §19).

### 9.3 Queue: Azure Service Bus Standard

Chosen over Azure Storage Queue because:
- Dead-letter queue — failed jobs inspectable without data loss
- Message lock renewal — analysis jobs safely exceed 30 seconds
- Per-message TTL control

---

## 10. Data Architecture

### 10.1 Stores

| Store | Technology | Contents |
|---|---|---|
| Relational | Azure PostgreSQL Flexible Server | Orgs, users, memberships, jobs, architecture models, elements, corrections, threats, mitigations, mappings, audit log |
| Object | Azure Blob Storage | Uploaded diagrams, parsed artifacts, analysis output JSON |
| Secrets | Azure Key Vault | LLM API keys; CMK keys for BYOK |

### 10.2 Data classification

| Class | Examples | Controls |
|---|---|---|
| **Highly sensitive** | Architecture descriptions, threat models, LLM output | Encrypted at rest (CMK-ready); org-scoped access only; minimal retention |
| **Personal data** | User email, display name, login events | GDPR scope; minimize collection; no PII in logs |
| **Platform operational** | Job status, metrics, log events | App Insights; no tenant architecture data in platform logs |
| **Public** | SPA static assets | HTTPS only |

### 10.3 Retention defaults

| Data | Default | Configurable per org |
|---|---|---|
| Analysis results (blob + DB) | 12 months | Yes |
| Uploaded artifacts | 30 days after job completion | Yes |
| Audit log | 24 months | No (platform minimum) |
| Application logs | 90 days | No (platform minimum) |

Deletion is cascading: deleting an org or job MUST purge all associated blobs, DB rows, and queue messages.

### 10.4 Encryption

| Layer | Mechanism |
|---|---|
| In transit | TLS 1.2+ on all endpoints; internal service-to-service HTTPS |
| PostgreSQL at rest | Azure-managed AES-256 (default); CMK via Key Vault when BYOK is enabled |
| Blob Storage at rest | Azure-managed AES-256 (default); CMK via Key Vault when BYOK is enabled |
| BYOK upgrade path | Both PostgreSQL Flexible Server and Blob Storage support CMK via Key Vault. Enabling BYOK per customer = configure Key Vault with org-scoped key + grant managed identity access + update CMK setting. No schema or application code changes required. |

---

## 11. Network Topology

### 11.1 MVP network model

All Container Apps run in a single **Container Apps Environment** with VNet integration:

```
Internet
   │ HTTPS (TLS 1.2+)
   ▼
┌─────────────────────────────────────────────────────────┐
│ Container Apps Environment (VNet-integrated)            │
│                                                         │
│  ┌──────────┐        ┌──────────┐                       │
│  │   api    │        │  worker  │                       │
│  │ (public  │        │ (no      │                       │
│  │  ingress)│        │  ingress)│                       │
│  └────┬─────┘        └────┬─────┘                       │
│       │                   │  Private endpoints          │
└───────┼───────────────────┼─────────────────────────────┘
        │                   │
        ▼                   ▼
   PostgreSQL          Blob Storage
   Service Bus         Key Vault
   (private endpoints within VNet)

worker → Azure OpenAI (Azure backbone, West Europe)
worker → Anthropic API (outbound HTTPS, internet)
```

- `worker` has no inbound network access; only initiates outbound connections
- Outbound from `worker` to Anthropic: HTTPS port 443 only
- All Azure services accessed via private endpoints (no public endpoint for PostgreSQL, Blob, Service Bus, Key Vault)

---

## 12. Observability

### 12.1 Structured logging

Every log record includes: `correlation_id`, `org_id` (where applicable), `user_id` (where applicable), `service`, `severity`, `timestamp`.

No secrets, tokens, passwords, or PII in logs (CLAUDE.md §10.4). Architecture content and LLM prompts/completions are NOT logged. Token usage counts (not content) ARE logged for cost monitoring.

### 12.2 Security events (CLAUDE.md §10.3)

The following events MUST be logged with `user_id`, `org_id`, `correlation_id`, `ip`, `timestamp`:

- Authentication success and failure
- Authorization denial
- Job submitted, completed, failed
- Custom IDP configuration created, updated, deleted
- User role changes
- Org created, deleted
- Data deletion requests

### 12.3 Alerting thresholds

| Alert | Threshold |
|---|---|
| Auth failure rate | >20/min per IP |
| Job failure rate | >10% of jobs in 15-min window |
| Worker queue depth | >50 messages |
| LLM provider error rate | >5 consecutive failures |
| PostgreSQL connection errors | Any |

---

## 13. ISO 27001:2022 Control Mapping

Traceability reference. Normative requirements are in the sections above and in [06-security.md](06-security.md).

| Control | How addressed |
|---|---|
| A.5.15 Access control | WorkOS + server-side RBAC; deny by default |
| A.5.16 Identity management | Managed identity for services; WorkOS for users; no shared credentials |
| A.5.17 Authentication information | Short-lived tokens; rotating refresh; MFA via WorkOS |
| A.5.18 Access rights | Three-role model; least-privilege managed identity RBAC |
| A.8.3 Information access restriction | Tenant isolation at every layer; `org_id` scoping enforced at DB (RLS) and storage |
| A.8.10 Information deletion | Retention policy and cascading deletion |
| A.8.11 Data masking | PII minimization; IDs in audit records; no PII in logs |
| A.8.12 Data leakage prevention | Tenant isolation; no cross-tenant queries; org-scoped Blob access |
| A.8.15 Logging | Structured logging; security event logging; 24-month audit retention |
| A.8.16 Monitoring | Application Insights; defined alert thresholds |
| A.8.20 Network security | VNet integration; private endpoints; no unnecessary public exposure |
| A.8.21 Security of network services | TLS 1.2+ everywhere; HTTPS-only ingress |
| A.8.24 Cryptography | AES-256 at rest; TLS in transit; BYOK path via Key Vault |
| A.8.28 Secure coding | Spec-driven development; CLAUDE.md security spec; code review |
| A.5.19 Supplier relationships | Azure, WorkOS, OpenAI, Anthropic: DPAs required before processing personal/tenant data |
| A.5.23 Cloud services | Azure-native controls; region-scoped data residency; private endpoints |

---

## 14. Open Decisions

| ID | Decision | Blocking |
|---|---|---|
| OD-1 | Azure OpenAI GPT-4o availability in West Europe — confirm before worker implementation | Worker spec |
| OD-2 | Auto-proceed on AWAITING_REVIEW timeout — MVP or deferred? | Job pipeline |
| OD-3 | Job completion notification: polling vs SSE | Frontend spec |
| OD-4 | `platform:admin` capability in MVP scope? | Admin-api spec |
| OD-5 | GDPR right-to-erasure workflow | Data model spec |
| OD-6 | Rate limiting: app-layer vs Azure Front Door WAF | API spec |

---

## 15. Out of Scope for This Document

API contracts → [openapi.yaml](../api/openapi.yaml)  
Database schema → [03-data-model.md](03-data-model.md)  
LLM prompt structure → [05-llm-workflow.md](05-llm-workflow.md)  
Deployment pipeline → devops spec (future)  
Disaster recovery runbook → ops spec (future)
