# Security Specification

**Status:** Approved  
**Foundation:** [CLAUDE.md](../../CLAUDE.md) — all requirements therein are binding and incorporated by reference  
**Architecture ref:** [02-architecture.md](02-architecture.md)  
**Standard:** ISO/IEC 27001:2022  
**Version:** 0.1  
**Date:** 2026-03-31

---

## 1. Purpose and Scope

This document layers ISO 27001:2022 compliance requirements and system-specific security controls on top of the mandatory application security requirements already defined in [CLAUDE.md](../../CLAUDE.md).

**Reading order:**
1. CLAUDE.md — read first; its requirements are unconditional
2. This document — adds ISO 27001 framing, system-specific controls, supplier obligations, and GDPR considerations
3. [02-architecture.md](02-architecture.md) — architectural decisions that implement these controls

If this document conflicts with CLAUDE.md, CLAUDE.md wins.

---

## 2. Information Security Policy Statement

The Threat Modeling Agent processes sensitive architecture information on behalf of customer organisations. A compromise of this data could expose confidential system designs, security weaknesses, and business-critical architectural decisions of our customers.

We treat information security as a core product requirement, not an operational overhead. Security controls are functional acceptance criteria. A feature is not complete if mandatory controls are missing.

---

## 3. Asset Register and Data Classification

### 3.1 Information assets

| Asset | Classification | Owner | Location |
|---|---|---|---|
| Customer architecture descriptions | **Confidential** | Customer org (tenant) | Azure Blob Storage (West Europe) |
| Generated threat models and analysis | **Confidential** | Customer org (tenant) | Azure Blob Storage + PostgreSQL (West Europe) |
| User identity data (email, name) | **Restricted — Personal Data** | User (data subject) | PostgreSQL + WorkOS (EU) |
| Organisation metadata (name, slug, IDP config) | **Restricted** | Customer org | PostgreSQL (West Europe) |
| Audit logs | **Restricted — Internal** | Platform | Application Insights (West Europe) |
| LLM prompt templates | **Internal** | Platform | Source code repository |
| Platform secrets (API keys) | **Restricted — Secret** | Platform | Azure Key Vault (West Europe) |
| SPA static assets | **Public** | Platform | Azure Static Web Apps |

### 3.2 Classification definitions

| Label | Meaning | Handling |
|---|---|---|
| **Confidential** | Tenant-owned data; disclosure would harm the customer | Encrypted at rest and in transit; org-scoped access only; minimal retention; no logging of content |
| **Restricted — Personal Data** | GDPR-scoped personal data | Minimize collection; GDPR rights support; DPA with all processors |
| **Restricted — Secret** | Credentials, API keys, tokens | Key Vault only; never in code, logs, or prompts; rotation schedule required |
| **Restricted — Internal** | Platform operational data | Internal access only; no exposure to tenants |
| **Public** | No sensitivity | Standard HTTPS delivery |

---

## 4. Threat Model for the Platform Itself

This section satisfies the requirement in spec §20 that the platform, as a processor of sensitive architecture material, must have its own threat model.

### 4.1 Trust boundaries

| Boundary | From | To | Controls |
|---|---|---|---|
| TB-1 | Internet | `api` Container App | TLS 1.2+; JWT validation; rate limiting |
| TB-2 | `api` | `worker` | Service Bus (no direct HTTP); message `org_id` validated |
| TB-3 | `api` / `worker` | PostgreSQL | Private endpoint; managed identity; RLS |
| TB-4 | `api` / `worker` | Blob Storage | Private endpoint; managed identity; org-scoped SAS |
| TB-5 | `worker` | Azure OpenAI | Azure backbone; managed identity or API key from Key Vault |
| TB-6 | `worker` | Anthropic API | Outbound HTTPS; API key from Key Vault |
| TB-7 | Browser | WorkOS | HTTPS; WorkOS-managed OIDC flow |
| TB-8 | WorkOS | `api` | OIDC callback; state parameter validation; JWKS token validation |

### 4.2 Attacker-controlled inputs

All of the following MUST be treated as untrusted (per CLAUDE.md §5):

- Uploaded diagram artifacts (image, PlantUML, Mermaid, Draw.io XML, text)
- All HTTP request headers, body, and query parameters
- WorkOS JWT claims (validated but derived from external source)
- LLM outputs (all stages)
- Architecture corrections submitted by users
- User-added threats and notes
- User-supplied organisation names, slugs, IDP domain hints

### 4.3 Key threats to the platform

| ID | Threat | Control |
|---|---|---|
| PT-1 | Prompt injection via uploaded architecture content | Content delimited in prompts; system instructions not overridable by user content (05-llm-workflow §9) |
| PT-2 | Cross-tenant data leakage | PostgreSQL RLS; org-scoped Blob paths; `org_id` from JWT not request params (03-data-model §7, 02-architecture §7.2) |
| PT-3 | JWT forgery or claims manipulation | JWKS validation; audience/issuer/expiry all checked; no client-side-only validation (CLAUDE.md §8.1) |
| PT-4 | LLM output used as SQL or code execution path | All LLM outputs schema-validated; never used as SQL, shell commands, or file paths (CLAUDE.md §16.5) |
| PT-5 | Malicious file upload | Magic-byte validation; file renamed on write; upload dir has no execution permissions (CLAUDE.md §9.6) |
| PT-6 | Secret exfiltration via logs | No secrets, credentials, or architecture content in logs (CLAUDE.md §10.4) |
| PT-7 | Tenant impersonation via manipulated message | `org_id` in Service Bus messages validated against job record before processing |
| PT-8 | Operator privilege abuse | Azure PIM JIT access before GA; all privileged actions in audit log |
| PT-9 | LLM provider compromise | LLM output is always untrusted regardless of provider; schema validation on all outputs |
| PT-10 | GDPR data subject rights not honoured | Erasure workflow; data minimization; retention enforcement |
| PT-11 | Unmapped-but-authenticated user gains tenant access | Enforce org resolution + `org_memberships` mapping at middleware before org-scoped routes |

---

## 5. ISO 27001:2022 Controls

The following table maps ISO 27001:2022 Annex A controls to implementation requirements for this system. Controls not listed are not applicable at this stage or are addressed by Azure platform controls.

### 5.1 Organizational controls (A.5)

| Control | Title | Implementation |
|---|---|---|
| A.5.1 | Policies for information security | This document and CLAUDE.md constitute the information security policy |
| A.5.9 | Inventory of information and other assets | §3.1 of this document; maintained and reviewed each quarter |
| A.5.10 | Acceptable use of information | Access limited to job function; IDP enforced; no personal use of customer data |
| A.5.12 | Classification of information | §3.2 of this document |
| A.5.13 | Labelling of information | Classification applied in code via data model; blob path prefixes enforce org scoping |
| A.5.14 | Information transfer | HTTPS only; no email transfer of customer architecture data |
| A.5.15 | Access control | WorkOS + server-side RBAC; deny by default; least privilege (CLAUDE.md §8) |
| A.5.16 | Identity management | WorkOS for users; managed identity for services; no shared accounts |
| A.5.17 | Authentication information | Short-lived tokens; MFA via WorkOS; no password storage in our system |
| A.5.18 | Access rights | Three-role model; provisioned and de-provisioned via API; reviewed on org member change |
| A.5.19 | Information security in supplier relationships | §7 of this document — supplier register and obligations |
| A.5.20 | Addressing security in supplier agreements | DPAs required with WorkOS, Microsoft (Azure), OpenAI, Anthropic before data processing |
| A.5.21 | Managing security in ICT supply chain | Dependencies pinned; vulnerability scanning in CI/CD (CLAUDE.md §12) |
| A.5.23 | Information security for use of cloud services | Azure RBAC; private endpoints; region-locked storage; Key Vault for secrets |
| A.5.33 | Protection of records | Audit log is append-only; 24-month retention; shipped to Application Insights |
| A.5.34 | Privacy and PII | §6 of this document — GDPR controls |

### 5.2 People controls (A.6)

| Control | Title | Implementation |
|---|---|---|
| A.6.1 | Screening | Background checks for employees with access to production systems — required before production access |
| A.6.2 | Terms of employment | Confidentiality obligations in employment contracts; annual security awareness |
| A.6.3 | Security awareness | Annual security training for all staff; CLAUDE.md orientation for all developers |
| A.6.7 | Remote working | MFA enforced; device management policy (deferred — required before GA) |
| A.6.8 | Information security event reporting | Incident reporting channel defined in §8 of this document |

### 5.3 Physical controls (A.7)

All infrastructure is hosted in Azure. Physical security controls are inherited from Microsoft's Azure data centre certifications (ISO 27001, SOC 2). No on-premises systems are in scope.

### 5.4 Technological controls (A.8)

| Control | Title | Implementation |
|---|---|---|
| A.8.1 | User endpoint devices | Developer device policy: full-disk encryption, MFA for Azure access (deferred — required before GA) |
| A.8.2 | Privileged access rights | Azure PIM JIT before GA; operator access logged |
| A.8.3 | Information access restriction | Tenant isolation at DB (RLS), storage (org-prefix), and API layers |
| A.8.4 | Access to source code | Repository access requires MFA; branch protection rules |
| A.8.5 | Secure authentication | CLAUDE.md §8.1 — token policy, PKCE, JWT validation |
| A.8.6 | Capacity management | Container Apps autoscale; Service Bus monitoring; token budget per job |
| A.8.7 | Protection against malware | Azure Defender for Containers; dependency scanning in CI/CD |
| A.8.8 | Management of technical vulnerabilities | Dependency pinning; CI/CD CVE scanning; patch SLA: critical within 24h, high within 7 days |
| A.8.9 | Configuration management | Infrastructure as Code; no manual config changes to production |
| A.8.10 | Information deletion | Retention policy per §3.2; cascading deletion on org/job delete; GDPR erasure workflow |
| A.8.11 | Data masking | PII minimization; IDs in logs not names; no architecture content in logs |
| A.8.12 | Data leakage prevention | Org-scoped access; no cross-tenant queries; no architecture content in application logs |
| A.8.15 | Logging | Structured logging; security events logged; 24-month audit retention; App Insights |
| A.8.16 | Monitoring activities | Alert thresholds defined in 02-architecture §12.3; on-call rotation required before GA |
| A.8.17 | Clock synchronization | Azure-managed NTP; all log timestamps in UTC |
| A.8.20 | Networks security | VNet integration; private endpoints; no unnecessary public exposure |
| A.8.21 | Security of network services | TLS 1.2+; HTTPS-only ingress; HSTS |
| A.8.23 | Web filtering | Not applicable (no general internet browsing; all outbound is API calls) |
| A.8.24 | Use of cryptography | AES-256 at rest; TLS in transit; BYOK path via Key Vault; no custom crypto (CLAUDE.md §13) |
| A.8.25 | Secure development lifecycle | Spec-driven development; CLAUDE.md mandatory; code review; automated security scanning |
| A.8.26 | Application security requirements | CLAUDE.md §5–16 — threat modeling, input validation, auth, injection prevention |
| A.8.27 | Secure system architecture | Architecture spec reviewed for security; ADRs for significant decisions |
| A.8.28 | Secure coding | CLAUDE.md as coding standard; forbidden patterns list (CLAUDE.md §13) |
| A.8.29 | Security testing | Security test requirements in CLAUDE.md §15; penetration test before GA |
| A.8.30 | Outsourced development | N/A — internal development only |
| A.8.31 | Separation of development, testing, production | Three environments: dev, staging, production; no production data in dev/staging |
| A.8.32 | Change management | All changes via PR; spec change requires spec update; ADR for architectural changes |
| A.8.33 | Test information | No production data (including customer architecture data) in test environments |
| A.8.34 | Protection of information systems during audit | Audit access read-only; no production config changes during audit |

Additional mandatory authorization rules:
- Only `platform:admin` can create organisations.
- `platform:admin` is restricted to platform endpoints; org-scoped endpoints require org membership.
- Non-admin users must be mapped in `org_memberships` for the resolved org before requests are accepted.

---

## 6. GDPR and Personal Data

The Threat Modeling Agent processes personal data as a **data processor** on behalf of customer organisations (data controllers) for customer user data, and as a **data controller** for platform user accounts.

### 6.1 Personal data inventory

| Data | Basis for processing | Retention | GDPR rights |
|---|---|---|---|
| User email address | Contract (platform account) | Duration of account | Access, erasure, portability |
| User display name | Contract (optional profile) | Duration of account | Access, erasure, rectification |
| Login timestamps | Legitimate interest (security) | 90 days | Access |
| Audit log entries (user_id) | Legitimate interest (security) | 24 months | Access (via platform) |

### 6.2 Data subject rights implementation

| Right | Implementation | SLA |
|---|---|---|
| Right of access | `GET /orgs/{orgId}/members/{userId}/data` | 30 days |
| Right to erasure | Soft-delete + PII nulling; WorkOS account deletion; cascading data purge per retention policy | 30 days |
| Right to rectification | User can update profile via WorkOS | Immediate |
| Right to portability | JSON export of threat models and analysis results | 30 days |
| Right to object | Opt-out of non-essential processing (none currently); erasure path available | 30 days |

### 6.3 Data transfers

All personal data is stored in EU Azure regions. Transfers outside the EU:
- **Anthropic API**: architecture content (not personal data) may be sent to Anthropic's servers (US). This is mitigated by: (a) architecture content is not personal data; (b) Anthropic DPA covers EU-to-US transfer; (c) no personal data (email, name) is included in prompts
- **WorkOS**: identity data is processed by WorkOS. WorkOS EU region is used; Standard Contractual Clauses (SCCs) apply
- **Azure OpenAI (West Europe)**: data stays within Azure EU boundary

---

## 7. Supplier Register

A Data Processing Agreement (DPA) MUST be executed with each supplier before processing personal or confidential data.

| Supplier | Purpose | Data processed | DPA required | ISO 27001 certified |
|---|---|---|---|---|
| Microsoft Azure | Infrastructure hosting | All platform data | Yes (Microsoft Online Services DPA) | Yes |
| WorkOS | Identity platform | User identity data | Yes — execute before go-live | Yes |
| OpenAI (Azure OpenAI) | LLM inference | Architecture content (non-personal) | Yes (Azure OpenAI data processing terms) | Yes (via Azure) |
| Anthropic | LLM inference | Architecture content (non-personal) | Yes — execute before go-live | Review required |
| GitHub / equivalent | Source code repository | Source code (no customer data) | Standard terms | Review required |

**Supplier review cadence:** Annual review of each supplier's security posture and DPA currency.

---

## 8. Incident Response

### 8.1 Classification

| Severity | Definition | Response time |
|---|---|---|
| P1 — Critical | Confirmed data breach; cross-tenant data leakage; active exploitation | Immediate (within 1 hour) |
| P2 — High | Suspected breach; authentication bypass; privilege escalation | Within 4 hours |
| P3 — Medium | Suspicious activity; anomalous access patterns; failed exploitation attempt | Within 24 hours |
| P4 — Low | Policy violation; minor misconfiguration | Within 7 days |

### 8.2 GDPR breach notification

- Personal data breach MUST be assessed within 24 hours of detection
- If breach is notifiable (risk to individuals): supervisory authority (Data Protection Authority) notification within 72 hours
- If breach poses high risk to individuals: data subjects notified without undue delay

### 8.3 Incident contacts

- On-call: defined in ops runbook (to be authored before GA)
- DPA (Data Protection Officer): designated before any personal data processing begins
- Supervisory authority: relevant EU DPA for country of establishment

---

## 9. Security Testing Requirements

In addition to CLAUDE.md §15, the following tests are required before GA:

| Test | Timing | Scope |
|---|---|---|
| Automated SAST | Every PR | All source code |
| Dependency CVE scanning | Every PR + nightly | All dependencies |
| DAST / API fuzzing | Pre-release | API endpoints |
| External penetration test | Before GA; annually thereafter | Full application scope |
| Tenant isolation test | Before GA | Cross-tenant data leakage scenarios |
| Prompt injection test | Before GA; on every prompt template change | All LLM pipeline stages |
| Authentication bypass test | Before GA | WorkOS integration; JWT validation |

---

## 10. Compliance Gaps and Accepted Risks

The following controls are deferred with an explicit acceptance:

| Gap | ISO Control | Accepted risk | Required by |
|---|---|---|---|
| Azure PIM JIT not configured | A.8.2 | Elevated operator privilege window | GA |
| Device management policy not enforced | A.8.1, A.6.7 | Developer device compromise could expose production access | GA |
| External penetration test not completed | A.8.29 | Unknown vulnerabilities in go-live state | GA |
| WorkOS DPA not yet executed | A.5.20 | Cannot legally process personal data | Before any personal data processing |
| Anthropic DPA not yet executed | A.5.20 | Cannot legally send architecture content to Anthropic | Before enabling Anthropic provider |
| On-call rotation not established | A.8.16 | Delayed incident response | GA |

Each gap MUST be resolved by the stated milestone. No new gaps may be accepted without explicit sign-off from the project lead, recorded as an entry in this table.

---

## 11. Security Review Cadence

| Activity | Frequency |
|---|---|
| Security spec review | Each major feature; quarterly otherwise |
| Supplier DPA review | Annually |
| Asset register review | Quarterly |
| Penetration test | Before GA; annually |
| Threat model for the platform itself | Each significant architecture change |
| CLAUDE.md compliance check | Each PR (automated where possible) |
