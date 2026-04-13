# Go-Live Requirements

**Status:** Pre-GA tracking document  
**Version:** 1.0  
**Date:** 2026-04-12  
**Backlog ref:** [07-backlog.md §8](specs/07-backlog.md)  
**Security ref:** [06-security.md](specs/06-security.md) · [CLAUDE.md](../CLAUDE.md)

This document is the definitive go-live checklist for the Threat Modeling Agent. It must be completed — or have a formally signed-off deferral with a named owner and deadline — **before any personal data is processed in a production environment** and **before GA launch**.

It can be handed directly to Legal, IT, Engineering, HR, and Infra leads as their source of required actions. For technical deployment steps, see [deployment/azure.md](deployment/azure.md).

---

## How to use this document

- Each item has an **owner**, a **deadline gate**, an **acceptance criterion**, and a **why** explanation.
- An item is *complete* when the named owner has confirmed the criterion is met and filled in the **Signed off by / Date** column in the tracking table at the end.
- Items marked **Before any personal data in production** block all other onboarding. Process no customer data until those are cleared.
- A formally signed-off deferral (named owner + future date) is acceptable for GA-only items during controlled beta, but must be remediated before general availability.

---

## Technical Readiness (code-complete — no action required)

These gates are already passed. Listed here for completeness and sign-off audit trail.

| # | Gate | Status |
|---|------|--------|
| T-1 | All pipeline stages (DETECT → SYNTHESIZE) implemented and tested | ✅ Done |
| T-2 | API endpoint coverage per OpenAPI spec | ✅ Done |
| T-3 | 255+ unit and integration tests passing (including tenant isolation, auth bypass, rate limiting, prompt injection) | ✅ Done |
| T-4 | SAST (CodeQL) running on every PR — fails on critical/high findings | ✅ Done |
| T-5 | Dependency CVE scan running nightly and on every PR | ✅ Done |
| T-6 | App-layer rate limiting on all abuse-prone endpoints | ✅ Done |
| T-7 | GDPR user self-erasure (`DELETE /v1/me`) implemented | ✅ Done |
| T-8 | Prompt injection defenses: delimiter wrapping + schema validation | ✅ Done |
| T-9 | Security headers middleware (CSP, HSTS staged, no-store, nosniff, X-Frame-Options) | ✅ Done |
| T-10 | All secrets sourced from Key Vault via managed identity — no hardcoded credentials | ✅ Done |

---

## Part 1 — Legal and Compliance

### OPS-1 — Execute WorkOS Data Processing Agreement (DPA)

**Owner:** Legal  
**Deadline gate:** Before any personal data processed in production  
**Spec reference:** 06-security.md §7, GDPR Art. 28

**What needs to happen:**  
WorkOS processes personal data on behalf of this platform (user identity, email addresses, SSO session data). Under GDPR Art. 28, a signed Data Processing Agreement (DPA) must be in place before any EU personal data flows through WorkOS. WorkOS offers a standard DPA under their Terms of Service — Legal must review, negotiate any required amendments, and obtain a countersigned copy before the first real user account is created.

**Acceptance criterion:**  
Signed, countersigned DPA on file with Legal. DPA reference number recorded here.

**Why this matters:**  
Operating without a DPA while processing personal data through a sub-processor is a direct GDPR violation and exposes the organisation to regulatory fines (up to 4% of global annual turnover under Art. 83). Enforcement action can also require immediate suspension of data processing.

---

### OPS-2 — Execute Anthropic Data Processing Agreement (DPA)

**Owner:** Legal  
**Deadline gate:** Before Anthropic provider enabled in production  
**Spec reference:** 06-security.md §7, GDPR Art. 28

**What needs to happen:**  
Anthropic processes customer architecture descriptions (confidential data) and potentially user-generated content when the Anthropic Claude provider is active. A DPA is required before this data is sent to Anthropic's API. Obtain Anthropic's DPA, review data residency clauses (confirm EU data doesn't leave EU-resident endpoints or obtain SCCs), and countersign.

**Acceptance criterion:**  
Signed, countersigned DPA on file. Confirm whether standard contractual clauses (SCCs) are required for US data transfer. DPA reference number recorded here.

**Why this matters:**  
Customer architecture descriptions are classified as **Confidential** (06-security.md §3). Sending them to a US-based processor without a DPA and appropriate transfer mechanism violates GDPR Chapter V.

---

### OPS-3 — Execute Azure OpenAI Data Processing Agreement (DPA)

**Owner:** Legal  
**Deadline gate:** Before Azure OpenAI provider enabled in production  
**Spec reference:** 06-security.md §7, GDPR Art. 28

**What needs to happen:**  
Azure OpenAI is already provisioned in West Europe (EU data residency). However, the Azure Data Processing Addendum (DPA) must be formally accepted in the Azure portal under **Azure Active Directory → Compliance → Data Protection**. Additionally, confirm that the Azure OpenAI resource is configured with **abuse monitoring opt-out** if customer architecture data must not be retained by Microsoft for abuse-detection logging (review Azure OpenAI data privacy documentation and request opt-out if required by the customer data handling policy).

**Acceptance criterion:**  
Azure DPA addendum accepted in portal. Screenshot or portal confirmation on file. Abuse monitoring opt-out decision documented.

**Why this matters:**  
The Azure OpenAI service may log prompts and completions for abuse detection by default. Customer architecture content in prompts is **Confidential** — retaining it in Microsoft's systems without customer consent requires explicit contractual basis.

---

### OPS-11 — Designate a Data Protection Officer (DPO)

**Owner:** Legal  
**Deadline gate:** Before any personal data processing in production  
**Spec reference:** 06-security.md §8.3, GDPR Art. 37

**What needs to happen:**  
Assess whether a formal DPO appointment is legally required under GDPR Art. 37 (required for organisations that carry out large-scale systematic monitoring of individuals, or process special category data). Even if not strictly required, appoint a named data protection contact responsible for: (a) monitoring GDPR compliance, (b) acting as the single point of contact for data subjects exercising rights, and (c) liaising with supervisory authorities. Document the appointment in writing and publish the contact in the privacy policy.

**Acceptance criterion:**  
DPO (or data protection contact) named and documented. Internal appointment record on file. Privacy policy updated with contact details.

**Why this matters:**  
Data subjects can only exercise GDPR rights (access, erasure, portability — all implemented in the API) if there is a reachable contact. Absence of a DPO when required is itself a GDPR violation.

---

## Part 2 — Security Testing

### OPS-4 — External Penetration Test

**Owner:** Security (Engineering lead)  
**Deadline gate:** Before GA  
**Spec reference:** 06-security.md §9, CLAUDE.md §15

**What needs to happen:**  
Commission a scoped external penetration test covering:
- API authentication and authorisation (JWT validation, tenant isolation bypass attempts, BOLA/IDOR)
- Rate limiting bypass techniques (IP rotation, header spoofing)
- Prompt injection via the architecture upload API (attempting to influence the pipeline output)
- OIDC callback security (state parameter validation, open redirect)
- Azure infrastructure exposure (public endpoints, key vault accessibility)
- Dependency vulnerabilities (supplement to automated CVE scanning)

Provide the tester with: OpenAPI spec, a staging environment with a dedicated test tenant, and the security specification (06-security.md). All critical and high findings must be remediated before GA. Medium findings require a remediation plan with timeline.

**Acceptance criterion:**  
Signed pentest report on file. All critical/high findings resolved or have written accepted-risk sign-off from the CISO or equivalent. Remediation verification (re-test of resolved findings) documented.

**Why this matters:**  
Automated testing (CodeQL, CVE scan, integration tests) covers known patterns. A skilled tester finds logic flaws, chained vulnerabilities, and business-logic bypasses that automated tools miss. The platform processes confidential architecture data — a breach would expose customer security posture.

---

### OPS-5 — DAST and API Fuzzing

**Owner:** Engineering  
**Deadline gate:** Pre-release (before first beta users)  
**Spec reference:** 06-security.md §9, CLAUDE.md §15

**What needs to happen:**  
Run a DAST (Dynamic Application Security Testing) scan and API fuzzer against the staging environment:
1. **OWASP ZAP** (or equivalent) active scan against the API base URL with authentication configured (Bearer token from a test account). Minimum: authenticated scan of all endpoints in the OpenAPI spec.
2. **API fuzzer** (e.g. `restler-fuzzer` or ZAP's OpenAPI fuzzer) against `docs/api/openapi.yaml`. Look for: 500 errors that leak stack traces, unhandled edge cases in input validation, header injection.
3. Review all findings — fix 500-level responses that expose internals, missing rate limiting on discovered endpoints, and any input that bypasses validation.

Integrate into CI as a nightly or pre-release workflow pointing at staging.

**Acceptance criterion:**  
DAST scan report showing no critical/high findings, or all findings triaged with remediation notes. No endpoint returns a 500 with stack trace or internal detail to an authenticated user for any input in the OpenAPI schema.

**Why this matters:**  
Unit and integration tests use controlled inputs. Fuzzing generates inputs the developers didn't think of. This catches unhandled edge cases before real users do.

---

### OPS-6 — Authentication Bypass Test (WorkOS / JWT Validation)

**Owner:** Engineering  
**Deadline gate:** Before GA  
**Spec reference:** 06-security.md §9, CLAUDE.md §8.1

**What needs to happen:**  
Write and run targeted security tests against the JWT validation path:
- Expired token → 401 (not 200)
- Token with invalid signature → 401
- Token signed with a different algorithm (algorithm confusion, e.g. HS256 instead of RS256) → 401
- Token with tampered `org_id` claim → 403 (tenant isolation enforced at application layer)
- Token with missing `sub` claim → 401 or 403
- `platform:admin` role token → 403 (enforced in `TenantContextMiddleware`)
- WorkOS JWKS endpoint returns a different key set (simulate key rotation) → existing tokens rejected, new tokens accepted

These can be written as integration tests using the test auth handler pattern already in the project, or as standalone scripts against staging.

**Acceptance criterion:**  
All scenarios above pass. Test code committed to the repository or test report on file.

**Why this matters:**  
WorkOS handles OIDC but the application layer must independently validate claims. Algorithm confusion attacks are a well-known JWT vulnerability. Any bypass here grants full access to tenant data.

---

### OPS-7 — Tenant Isolation Test (Cross-Tenant Leakage)

**Owner:** Engineering  
**Deadline gate:** Before GA  
**Spec reference:** 06-security.md §9, CLAUDE.md §8.2

**What needs to happen:**  
Extend the existing `TenantIsolationTests.cs` with scenarios covering all resource types:
- Org A user cannot read Org B's jobs, architectures, threats, elements, corrections, exports, or members
- Org A user cannot modify Org B's resources (PATCH, DELETE, POST)
- Org A user using Org B's `orgId` in the URL but their own JWT gets 404 (not 403 — no enumeration oracle)
- Verify that list endpoints never include cross-tenant records even if DB RLS is bypassed (application-layer filter as defence-in-depth)

Additionally: validate database-layer RLS in a separate test environment using a non-superuser PostgreSQL role (current Testcontainers setup uses superuser which bypasses RLS). This requires provisioning a test-specific DB user with only the `app` role.

**Acceptance criterion:**  
All above scenarios tested and passing. DB-layer RLS validation run against non-superuser connection and documented.

**Why this matters:**  
Multi-tenancy is a core security property. A cross-tenant leak exposes one customer's architecture designs to another. This is the highest-severity class of breach for this platform.

---

## Part 3 — Infrastructure and Operations

### OPS-8 — Azure PIM: Just-In-Time Access for Privileged Roles

**Owner:** Infrastructure  
**Deadline gate:** Before GA  
**Spec reference:** 06-security.md §5.4, ISO 27001 A.8.2

**What needs to happen:**  
Enable Azure Privileged Identity Management (PIM) for all permanently assigned privileged roles:
1. Identify all Azure AD accounts with standing Owner, Contributor, or Key Vault Administrator assignments on the production resource group.
2. Convert standing assignments to **eligible** (JIT) assignments — operators must request access, provide justification, and have access approved (or auto-approved with MFA) for a time-limited window (recommend: 8 hours maximum).
3. Configure PIM alerts: alert on standing Owner assignments, alert on PIM bypass attempts.
4. For the GitHub Actions service principal: confirm it has the minimum required role (currently Contributor at subscription scope — narrow to resource group scope post-MVP).

**Acceptance criterion:**  
No standing Owner or Contributor assignments to human accounts in the production resource group. All privileged access requires PIM activation with MFA. PIM audit logs enabled. Screenshot of PIM configuration on file.

**Why this matters:**  
Standing privileged access means a compromised operator account gives an attacker immediate, permanent access to all production data. JIT access limits the blast radius to the activation window.

---

### OPS-9 — Device Management Policy (MDM, FDE, MFA)

**Owner:** IT  
**Deadline gate:** Before GA  
**Spec reference:** 06-security.md §5.4, ISO 27001 A.8.1

**What needs to happen:**  
Enforce the following on all devices used by team members with production access:
1. **Mobile Device Management (MDM):** All devices enrolled in an approved MDM solution (e.g. Microsoft Intune, Jamf).
2. **Full Disk Encryption (FDE):** BitLocker (Windows) or FileVault (macOS) enabled and encryption key escrowed.
3. **MFA:** All accounts with production access (Azure, WorkOS admin, GitHub) require MFA. Phishing-resistant MFA (FIDO2/passkey) is preferred; TOTP is the minimum.
4. **Screen lock:** Automatic lock after ≤5 minutes of inactivity on all devices.
5. **Patch policy:** OS and browser patches applied within 7 days of release.

**Acceptance criterion:**  
MDM enrollment report showing 100% of production-access devices enrolled. FDE compliance report. MFA enforcement confirmed via Azure AD and GitHub org settings. Policy document signed by IT lead on file.

**Why this matters:**  
Platform secrets (Key Vault access, deployment credentials) are accessed from developer machines. An unencrypted or unmanaged device is the most common vector for credential theft. The platform processes customer confidential data — endpoint security is part of the overall security posture.

---

### OPS-10 — On-Call Rotation Defined and Tested

**Owner:** Engineering  
**Deadline gate:** Before GA  
**Spec reference:** 06-security.md §10

**What needs to happen:**  
1. Define an on-call rotation covering all production incidents: API outage, worker failure, security alerts from Application Insights, and suspicious access patterns in audit logs.
2. Configure Azure Monitor alerts for: 5xx error rate spike (>5% over 5 minutes), worker processing queue depth >50 messages (backlog), Key Vault access failures, Container App restart loops.
3. Route alerts to PagerDuty or equivalent with defined escalation paths.
4. Run a **game day**: simulate an API outage and a security incident (e.g. a rate-limit bypass attempt visible in logs) and verify the on-call engineer can respond within SLA.

**Acceptance criterion:**  
On-call schedule documented and published to the team. Alert thresholds configured in Azure Monitor. Game day completed with no critical gaps in response process. On-call runbook (or link to it) committed to the repository under `docs/ops/`.

**Why this matters:**  
Without a defined response process, security incidents (including potential data breaches requiring 72-hour GDPR notification) will be mishandled under pressure. The game day ensures the process actually works before a real incident.

---

### OPS-12 — Log Integrity: Append-Only SIEM or Log Analytics Lock

**Owner:** Infrastructure  
**Deadline gate:** Before GA  
**Spec reference:** CLAUDE.md §10.6

**What needs to happen:**  
Current logs are shipped to Azure Application Insights. While Application Insights provides retention and query capabilities, logs can be modified or purged by an operator with sufficient Azure RBAC. For tamper-evidence:
1. Enable **Azure Monitor Workspace** with an **immutability policy** (Log Analytics workspace lock): set the workspace retention policy and enable the `ImmutableStorage` option so logs cannot be deleted or modified within the retention window.
2. Alternatively, configure **Diagnostic Settings** to ship logs to an **Azure Storage Account** with **immutable blob storage** (WORM — Write Once Read Many) enabled with a time-based retention policy.
3. Ensure audit logs (Application Insights custom events for privileged operations, auth failures, rate limit triggers) are included in the immutable store.

**Acceptance criterion:**  
Azure Log Analytics workspace immutability policy enabled (or WORM storage configured). Retention period set to ≥90 days (minimum for GDPR breach notification and audit purposes). Configuration screenshot on file.

**Why this matters:**  
CLAUDE.md §10.6 MUST: logs shipped to an append-only or tamper-evident destination. In a breach scenario, audit logs are evidence. An attacker (or rogue insider) who can delete logs can cover their tracks. Immutable storage prevents this.

---

### OPS-13 — HSTS Preload Submission

**Owner:** Infrastructure  
**Deadline gate:** After production domain is confirmed stable  
**Spec reference:** CLAUDE.md §11.1

**What needs to happen:**  
The API and SPA currently serve `Strict-Transport-Security: max-age=0` (staged rollout per spec — safe while domain/certificate is being established). Before GA:
1. Confirm the production domain (e.g. `api.threatmodeling.example`) is stable and the TLS certificate is correctly provisioned and will not change.
2. Ramp HSTS `max-age` in stages: first `max-age=300` (5 minutes), monitor for HTTPS issues for 1 week, then ramp to `max-age=63072000; includeSubDomains; preload`.
3. Submit the domain to the [HSTS preload list](https://hstspreload.org). Preloading ensures browsers connect directly via HTTPS even before any prior visit.

The HSTS value is currently set in:
- `src/ThreatModelingAgent.Api/Middleware/SecurityHeadersMiddleware.cs` — server-side API responses
- `frontend/staticwebapp.config.json` — SPA responses via Azure Static Web Apps

**Acceptance criterion:**  
`Strict-Transport-Security: max-age=63072000; includeSubDomains; preload` confirmed in production HTTP response headers. Domain submitted to hstspreload.org and showing `preload` status (note: preload propagation can take weeks).

**Why this matters:**  
Without HSTS preload, a user's very first visit (or a visit after HSTS expiry) is vulnerable to SSL-strip attacks. Preloading eliminates this window entirely. CLAUDE.md §11.1 MUST: `max-age=63072000; includeSubDomains; preload` on HTTPS deployments.

---

### OPS-14 — Background Checks for Production Access

**Owner:** HR  
**Deadline gate:** Before production access granted to any individual  
**Spec reference:** 06-security.md §5.2, ISO 27001 A.6.1

**What needs to happen:**  
Before any individual is granted production access (Azure portal, Key Vault, deployment credentials, or direct database access):
1. Confirm a background check (criminal record check, employment history verification) has been completed and cleared for that individual.
2. Document the completion in HR records with reference to the specific access granted.
3. Background checks should be repeated at intervals consistent with the organisation's HR policy (typically every 3–5 years for roles with persistent privileged access).

**Acceptance criterion:**  
HR confirmation on file for every individual with production access. Access provisioning checklist includes background check gate. No production access granted before check is cleared.

**Why this matters:**  
The platform processes confidential customer architecture data. Insider threats are a real risk. Background checks are a basic due-diligence control required by ISO 27001 A.6.1 and are standard practice before granting access to systems that hold sensitive data.

---

## Part 4 — Azure Infrastructure Hardening

These items are currently deferred from day-one MVP but **MUST** be completed before GA. They are also listed in [deployment/azure.md §Security post-MVP hardening](deployment/azure.md).

| # | Item | Owner | Deadline | What to do |
|---|------|-------|----------|-----------|
| H-1 | Private endpoints for PostgreSQL, Service Bus, Key Vault, Blob Storage | Infra | Before GA | Enable private endpoints in the Container Apps VNet. Remove public access from PostgreSQL and Key Vault. Update Bicep in `infra/modules/`. |
| H-2 | Key Vault network ACLs | Infra | Before GA | Restrict Key Vault to the Container Apps managed environment subnet only. |
| H-3 | Geo-redundant PostgreSQL backup | Infra | Before GA | Enable geo-redundant backup on the PostgreSQL flexible server. Confirm backup retention ≥7 days. |
| H-4 | Azure Defender for Containers and PostgreSQL | Infra | Before GA | Enable Microsoft Defender for Containers (covers ACR and Container Apps) and Defender for PostgreSQL. Costs ~€15/month. |
| H-5 | GitHub Actions service principal scope | Infra | Before GA | Narrow the GitHub Actions service principal role from subscription-level Contributor to resource-group-level Contributor (`tma-prod-rg` only). |
| H-6 | Container App ingress — IP allowlist for admin routes | Infra | Post-GA | If an admin API is ever added (D-3 deferred), restrict ingress to known IP ranges. |

---

## Part 5 — E2E Test Infrastructure

The frontend E2E tests exist as structural stubs (`test.fixme`) in `frontend/tests/e2e/`. They require a live API and auth helpers before the assertions can be activated.

| # | Item | Owner | Deadline | What to do |
|---|------|-------|----------|-----------|
| E-1 | Staging environment with real WorkOS test tenant | Engineering/Infra | Before beta | Deploy API + Worker + PostgreSQL to a `staging` Container Apps environment. Create a dedicated WorkOS application for staging (separate from production). |
| E-2 | Playwright auth helper | Engineering | Before beta | Implement `frontend/tests/e2e/helpers/auth.ts`: log in via WorkOS test credentials, obtain a session token, inject it into Playwright browser context. |
| E-3 | Seed API for E2E setup | Engineering | Before beta | Write a test seed endpoint (or use the API directly) to create an org, a job, and a completed analysis in the staging environment before each E2E test suite run. |
| E-4 | Activate `test.fixme` flows | Engineering | Before beta | Remove `test.fixme` from: `auth.spec.ts`, `cross-org.spec.ts`, `export.spec.ts`, `upload.spec.ts`, `manual.spec.ts`, `threats.spec.ts`, `members.spec.ts` and implement full flow assertions. |
| E-5 | CI E2E job | Engineering | Before GA | Add a CI workflow job that runs the Playwright E2E suite against staging on every merge to main. |

---

## Sign-Off Tracking Table

Complete this table before GA. All items must have a sign-off date or a formally approved deferral.

| ID | Item | Owner | Criterion met? | Signed off by | Date |
|----|------|-------|---------------|--------------|------|
| OPS-1 | WorkOS DPA | Legal | | | |
| OPS-2 | Anthropic DPA | Legal | | | |
| OPS-3 | Azure OpenAI DPA | Legal | | | |
| OPS-4 | External penetration test | Security | | | |
| OPS-5 | DAST / API fuzzing | Engineering | | | |
| OPS-6 | Auth bypass test | Engineering | | | |
| OPS-7 | Tenant isolation test | Engineering | | | |
| OPS-8 | Azure PIM / JIT access | Infra | | | |
| OPS-9 | Device management (MDM, FDE, MFA) | IT | | | |
| OPS-10 | On-call rotation + game day | Engineering | | | |
| OPS-11 | DPO designation | Legal | | | |
| OPS-12 | Log integrity (SIEM lock) | Infra | | | |
| OPS-13 | HSTS preload | Infra | | | |
| OPS-14 | Background checks | HR | | | |
| H-1 | Private endpoints | Infra | | | |
| H-2 | Key Vault network ACLs | Infra | | | |
| H-3 | Geo-redundant backup | Infra | | | |
| H-4 | Azure Defender | Infra | | | |
| H-5 | GitHub Actions scope | Infra | | | |
| E-1 | Staging environment | Engineering | | | |
| E-2 | Playwright auth helper | Engineering | | | |
| E-3 | Seed API | Engineering | | | |
| E-4 | E2E flows activated | Engineering | | | |
| E-5 | CI E2E job | Engineering | | | |
