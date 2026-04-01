# Traditional Web Security Specification

<!-- AGENT: This file is a mandatory coding constraint, not documentation.
     Every MUST/MUST NOT is a hard rule with zero exceptions unless an explicit approved exception is documented.
     Before writing any non-trivial code: run the Pre-Code Checklist (§18).
     After writing a new route: run the Per-Endpoint Checklist (§19).
     When a requirement is ambiguous: apply the Security Decision Rule (§3.2).
     Security takes precedence over speed, convenience, and existing code patterns. -->

## 1. Scope

This specification applies to **traditional web applications, APIs, backend services, and supporting integrations**.

It defines mandatory security requirements for:

* authentication and authorization
* input validation and domain modeling
* data protection and output minimization
* API and resource security
* logging and configuration
* infrastructure-facing security controls
* dependency and supply-chain security
* secure use of **LLM-backed features** and **MCP integrations** within a traditional application

This specification **does not** define security requirements for autonomous or agentic AI systems. Those requirements belong in a separate **Agentic AI Security Specification**.

---

## 2. Instruction Precedence

If instructions conflict, apply them in this order:

1. This security specification
2. Approved architectural decisions
3. Feature specification
4. Existing codebase patterns
5. Framework defaults

**Existing code is not proof of correctness.** If existing code conflicts with this specification, new code **MUST** follow this specification. The conflict **MUST** be called out explicitly.

---

## 3. Normative Language

The key words **MUST**, **MUST NOT**, **SHOULD**, and **MAY** are to be interpreted as requirement levels.

* **MUST / MUST NOT** = mandatory requirement
* **SHOULD / SHOULD NOT** = strong recommendation; deviations require explicit justification
* **MAY** = optional

### 3.1 Definition of "Approved"

The word **approved** in this specification means: explicitly documented in one of the following project governance artifacts —

* an Architecture Decision Record (ADR)
* a security exception register
* an explicit written confirmation from the project lead recorded in the PR, issue, or equivalent

If no such artifact exists for the project, treat **approved** as requiring explicit human confirmation before proceeding. An implementation agent **MUST NOT** self-approve an exception or assume one exists. When in doubt, raise it and wait.

### 3.2 Security Decision Rule (Tiebreaker)

When a requirement is ambiguous or two valid interpretations exist, choose the option that:

1. grants less privilege
2. exposes less data
3. narrows trust boundaries
4. keeps enforcement in deterministic code rather than assumptions about external behavior
5. requires explicit approval for destructive or high-impact actions

If uncertainty remains after applying this rule, the implementation **MUST NOT** choose the riskier behavior by default. Stop and ask.

### 3.3 Existing Code is Not Proof of Correctness

Before writing code, check whether any existing code in the files being modified conflicts with a **MUST** in this specification. If a conflict exists, it **MUST** be called out explicitly before writing new code. New code **MUST** follow this specification regardless of what surrounding code does.

---

## 4. Architectural Mandates

### 4.1 Security by Design

* Security requirements **MUST** be treated as functional acceptance criteria.
* A feature **MUST NOT** be considered complete if mandatory security controls are missing.

### 4.2 Secure by Default

* All systems **MUST** default to **deny all**.
* Access **MUST** be granted only through explicit allow-lists or explicit authorization rules.

### 4.3 Fail Secure

* Any failure in authentication, authorization, validation, secret loading, or security middleware **MUST** fail closed.
* A system failure **MUST NOT** result in a bypass of security controls.

### 4.4 Least Privilege

* Every process, user, service identity, role, and token **MUST** have only the minimum permissions required for the specific action.
* When in doubt, grant less.

### 4.5 Defence in Depth

* No single control **MUST** be relied upon as the sole enforcement point.
* Controls **MUST** be layered across trust boundaries.

### 4.6 Blast-Radius Reduction

* Systems **MUST** be designed so that compromise of one account, token, session, or component cannot cascade broadly.
* For every credential or identity, the design **MUST** consider maximum damage if stolen.

### 4.7 Auditability and Revocation

* Every privileged action **MUST** be auditable.
* Every access grant, session, token, or permission assignment **MUST** be revocable.

---

## 5. Threat Modelling

Threat modelling is **REQUIRED** before implementing any non-trivial feature or security control.

A task is **non-trivial** if it introduces or changes any of the following:

* authentication or authorization logic
* a new route, endpoint, or externally reachable handler
* a new data flow involving sensitive or tenant-scoped data
* a new MCP integration or external service call
* an LLM-backed feature
* file upload, export, import, or document processing
* background jobs or asynchronous processing
* schema changes
* secrets or key handling
* privileged or destructive actions

For each non-trivial change, explicitly identify:

* **Trust boundaries** — where control passes from one trust level to another
* **Attacker-controlled inputs** — any values a malicious actor can influence
* **Assets** — what is being protected
* **Data flows** — where sensitive data travels, rests, and is logged
* **Privileged operations** — actions with elevated impact if abused
* **Abuse cases** — how an attacker could misuse the feature

The following **MUST** be treated as untrusted until explicitly validated:

* IDs
* tenant scope
* URLs
* file paths
* email addresses
* money values
* role names
* sort fields
* pagination parameters
* headers
* request bodies
* query parameters
* uploaded content
* MCP inputs and outputs
* LLM inputs and outputs

---

## 6. Input Validation and Domain Modelling

### 6.1 Strict Type Modelling

* Raw primitives such as `string`, `int`, or loosely typed objects **MUST NOT** cross domain boundaries for security-relevant values.
* Security-relevant input **MUST** be wrapped in value objects such as `EmailAddress`, `UserId`, `TenantId`, or `TransactionAmount`.

### 6.2 Validation at Birth

* All validation **MUST** occur at construction time.
* Invalid objects **MUST NOT** be instantiated.

### 6.3 Allow-list Validation

* External input **MUST** be validated against expected type, length, format, and allowed values.
* Input not explicitly allowed **MUST** be rejected.

### 6.4 Cross-Layer Consistency

* Constraints such as length, nullability, format, and range **MUST** be consistent across:
  * code
  * API schema
  * database schema

### 6.5 Domain Invariants

* Business and security invariants **MUST** be enforced in the domain or service layer, not only in controllers, DTOs, or the UI.

### 6.6 DTO Discipline

* Full domain models **MUST NOT** be passed across lower-trust or lower-privilege layer boundaries.
* Every response shape **MUST** use a purpose-specific DTO containing only the fields permitted for that role and context.

### 6.7 Anti-Patterns

The following are violations:

* passing raw request DTOs directly into privileged domain objects
* mapping raw request bodies directly onto domain models
* returning full domain models to callers

---

## 7. Data Integrity and Injection Prevention

### 7.1 Database Access

* All database interactions **MUST** use parameterized queries, prepared statements, or approved ORM mechanisms.
* String interpolation in SQL **MUST NOT** be used with user-influenced values.

### 7.2 Mass Assignment Protection

* Create and update operations **MUST** use explicit, purpose-specific DTOs.
* Raw request bodies **MUST NOT** bind directly to domain models.

### 7.3 Output Encoding

* Output **MUST** be encoded according to context.
* HTML output **MUST** escape special characters.
* JSON output **MUST** use framework serializers rather than manual string construction.

### 7.4 Response Schema Minimization

* API responses **MUST** expose only the fields explicitly defined for the caller's role and context.
* Returning additional fields is a violation.

### 7.5 Deserialization Safety

* Unsafe deserializers that permit polymorphic type creation from untrusted input **MUST NOT** be used.
* Parsers for structured formats such as XML **MUST** be configured securely.
* Where XML is accepted, DTDs and external entity resolution **MUST** be disabled unless an explicit approved exception exists.

### 7.6 Error Detail Minimization

* Error responses **MUST** expose only the minimum detail required by the caller.
* Internal paths, stack traces, DB errors, field names that aid probing, and security-sensitive internals **MUST NOT** be returned to clients.
* Full diagnostics **MUST** be written only to protected logs.

### 7.7 Query Timeouts

* Database queries **MUST** have explicit timeouts where the operation can become expensive or attacker-amplifiable.
* Long-running list and export queries **MUST** have explicit limits and timeouts.

### 7.8 Export Security

* CSV and spreadsheet exports **MUST** sanitize all cell values before writing.
* Any value whose first character is `=`, `+`, `-`, `@`, tab, or carriage return **MUST** be prefixed with a single quote (`'`).
* Downstream tooling **MUST NOT** be relied upon to sanitize this.

---

## 8. Identity and Access Control

Authorization **MUST** be enforced separately from authentication.  
A valid token or authenticated session is **not sufficient** on its own.

### 8.1 Authentication

* Applications **SHOULD** use OAuth 2.0 / OIDC where appropriate.
* PKCE **MUST** be used for public client authorization code flows.
* Local password storage **SHOULD** be avoided where an approved identity provider is available.

#### Token Policy

* Access tokens **MUST** be short-lived.
* Refresh tokens **MUST** use rotation where supported.
* Expired, revoked, malformed, or signature-invalid tokens **MUST** be rejected.

#### External Identity Provider Tokens

* Third-party tokens **MUST** be validated server-side against trusted metadata, JWKS, or equivalent provider validation mechanisms.
* The following **MUST** be verified where applicable:
  * issuer
  * audience
  * expiration
  * signature
  * email verification state if email is used as identity evidence
* Validation endpoints and issuer metadata **MUST** come from trusted configuration, never from user input.
* Optional social-login or external identity providers that are not configured **MUST** fail with a clean application-level error and **MUST NOT** cause startup failure or an unhandled runtime exception.

#### Password Hashing

When local password storage is unavoidable:

* Passwords **MUST** be hashed with **Argon2id** or **bcrypt**.
* Bcrypt cost factor **MUST NOT** be below 12.
* Fast hashes such as MD5, SHA-1, or raw SHA-256 **MUST NOT** be used for password storage.
* Hash work factors **MUST** be explicit in code or configuration.

#### Password Reset and Recovery

Reset and recovery tokens **MUST** be:

* cryptographically random and at least 128 bits
* single use
* time-limited to 1 hour or less
* stored as a hash, not plaintext

Additionally:

* Reset and recovery flows **MUST** return the same externally visible response regardless of whether the account exists.
* All outstanding reset tokens **MUST** be invalidated when a new reset is requested or when the password changes.

#### Session Revocation

* On password change, account lockout, or confirmed breach, all active sessions and refresh tokens for the user **MUST** be invalidated.
* A user-scoped revoke-all capability **MUST** exist.

#### Sensitive Action Re-Authentication

* Sensitive account or security actions such as password change, email change, MFA reset, account recovery changes, and equivalent high-impact profile changes **MUST** require current credential verification, step-up authentication, or an equivalent re-authentication control.

#### Timing-Safe Comparison

* API keys, webhook signatures, session secrets, and other non-password secrets **MUST** be compared using constant-time comparison functions from approved libraries.

### 8.2 Authorization

> **Role definitions:** Before implementing any authorization check, locate the project's canonical role definitions in the codebase (search for `Role`, `RoleConstants`, or the equivalent enum/class). Do **not** infer role names or hierarchy from usage patterns in existing code — existing code may itself be non-compliant.

#### General Rules

* Authorization **MUST** be checked server-side on every request.
* Authorization **MUST NOT** rely on client-side checks.
* Authorization decisions **MUST** be made using trusted server-side identity and resource context.

#### Function-Level Access Control

* Every route or handler **MUST** enforce the minimum required role, permission, or policy at the function entry point.
* Role inheritance **MUST** be explicit.
* A higher-privilege role **MUST NOT** automatically inherit all lower-privilege capabilities unless the access matrix explicitly defines that inheritance.

#### Object-Level Access Control (BOLA)

* Every resource access **MUST** include tenant, owner, or equivalent scope enforcement.
* Queries **MUST** constrain both the resource identifier and the caller's authorized scope.

#### Property-Level Access Control

* Response DTOs **MUST** include only properties permitted for the caller's role and context.
* Lower-privilege callers **MUST NOT** receive admin-only, internal, audit, or unrelated personal fields.

#### Self-Access Enforcement

* Low-privilege users **MUST** be limited to their own records unless explicit business rules state otherwise.
* Unauthorized access to another user's record **MUST** be denied.

#### Service Identity Separation

* User identities and service identities **MUST** be treated as separate trust domains.
* Platform routes **MUST NOT** accept a user token where a service credential is required.
* User-facing routes **MUST NOT** accept service credentials as if they were user identity.

#### Session Hardening

If cookies are used:

* `HttpOnly` **MUST** be enabled
* `Secure` **MUST** be enabled
* `SameSite=Strict` **MUST** be the default unless a documented and approved flow requires a different setting
* session identifiers **MUST** be generated using approved secure randomness
* session identifiers **MUST** be renewed after authentication and privilege changes to reduce session fixation risk
* session inactivity and absolute lifetime limits **MUST** be defined for authenticated sessions; default baseline is **30 minutes inactivity / 8 hours absolute** unless a documented risk assessment justifies a different value

#### CSRF Protection

* Browser-reachable state-changing operations that rely on cookies or other automatically attached credentials **MUST** be protected against CSRF.
* Protection **MUST** use framework-native CSRF defenses, synchronizer tokens, double-submit cookies, or an equivalent approved mechanism.
* `SameSite` alone **MUST NOT** be treated as the only CSRF defense where stronger protection is applicable.

---

## 9. API and Resource Security

### 9.1 Rate Limiting

Rate limiting **MUST** be applied to:

* authentication and account recovery flows
* endpoints that send emails, SMS, or other external messages
* endpoints that trigger external side effects
* endpoints that allow enumeration or bulk exfiltration
* list, search, and export operations where abuse can amplify load or data disclosure

Default baseline:

* **SHOULD** start from 5 attempts per 10 minutes per IP and, where applicable, per account identifier
* higher-risk endpoints **MAY** require stricter controls

Implementation requirements:

* rate limiting **MUST** use a durable shared store or approved centralized infrastructure control; per-instance in-memory throttling alone is not sufficient in distributed deployments
* naming specific endpoint paths in the specification **SHOULD NOT** be relied upon as the complete scope; any new endpoint matching the risk pattern inherits the requirement automatically

### 9.2 Client IP Derivation

* Client IP used for security decisions **MUST** be derived only through trusted proxy configuration controlled by infrastructure.
* The application **MUST NOT** trust arbitrary forwarding headers from the client.
* If forwarded headers are used, parsing **MUST** follow platform trusted-proxy configuration rather than ad hoc index selection.

### 9.3 Pagination and Resource Caps

* List endpoints **MUST** enforce a maximum page size.
* Maximum page size **SHOULD NOT** exceed 100 unless explicitly justified.
* Internal service methods **MUST NOT** fetch unbounded result sets where attacker influence exists.

### 9.4 CORS

* Allowed origins **MUST** be defined centrally as an explicit allow-list.
* Wildcard `*` with credentials **MUST NOT** be used.
* Per-route ad hoc CORS configuration **MUST NOT** be used unless explicitly approved.

### 9.5 SSRF Prevention

* External service destinations **MUST** come from hardcoded constants, trusted service discovery, or approved allow-listed configuration.
* Arbitrary user-supplied URLs **MUST NOT** be fetched directly.

### 9.6 Secure File Uploads

If file upload is supported:

* Content-Type headers **MUST NOT** be trusted alone.
* File type **MUST** be validated using server-side inspection such as magic bytes where applicable.
* Uploaded files **MUST** be renamed to random identifiers.
* Upload directories **MUST** block execution via platform controls.

### 9.7 Request Body Size Limits

* Maximum request body size **MUST** be enforced at middleware, gateway, or infrastructure level before expensive parsing occurs.
* Oversized requests **MUST** be rejected before body processing with an appropriate client error such as HTTP 413 where the protocol supports it.
* Framework defaults alone **MUST NOT** be relied upon unless they have been reviewed and explicitly accepted.

### 9.8 Outbound HTTP Timeouts

* Outbound HTTP calls **MUST** use explicit connect and read timeouts.
* Timeouts **SHOULD NOT** exceed 30 seconds unless a specific integration requires it and the exception is documented.
* Per-request raw client instantiation **MUST NOT** be used when a managed client or factory is available.

### 9.9 Open Redirect Prevention

* Redirect destinations derived from caller input **MUST** be validated against an explicit allow-list of internal paths.
* External URLs, protocol-relative URLs, and data URIs **MUST** be rejected.

### 9.10 Stale, Debug, and Deprecated Endpoints

* Debug, test, internal-only, and deprecated endpoints **MUST** be removed from production builds unless explicitly approved and strongly protected.
* Commenting them out, hiding them behind undocumented paths, or relying only on feature flags is not sufficient.
* Endpoints missing from the current API inventory **SHOULD** be treated as violations pending review.

---

## 10. Configuration and Logging

### 10.1 Configuration and Secrets

* Secrets **MUST** come from environment configuration, secret stores, or workload identity mechanisms.
* Secrets **MUST NOT** be hardcoded in code or committed files.
* Required configuration **MUST** be validated at startup.
* The application **MUST** refuse to start when mandatory secrets or security-critical settings are missing.
* Workload identity or managed identity **SHOULD** be preferred over long-lived credentials.

### 10.2 Structured Logging

* Logs **MUST** use structured logging with named fields.
* String interpolation of sensitive or user-controlled values into logs **MUST NOT** be relied upon.
* Log fields derived from untrusted input **MUST** be sanitized or encoded to prevent log injection, log forging, or parser-breaking control characters.

### 10.3 Security Logging

* Authentication failures, authorization denials, and other security-relevant deny actions **MUST** be logged.
* Security logs **MUST** include relevant non-PII identifiers such as user ID, tenant ID where applicable, correlation ID, and trusted client IP when available.

### 10.4 PII and Secret Handling in Logs

* Tokens, passwords, raw credentials, connection strings, and secrets **MUST NOT** be logged.
* Personal data **MUST NOT** be logged unless explicitly required and approved.
* IDs **MUST** be logged instead of names, emails, addresses, or phone numbers unless an approved exception exists.

### 10.5 Correlation IDs

* A unique correlation ID **MUST** be created or adopted at request entry.
* Correlation IDs **MUST** be propagated through logs and outbound requests where applicable.
* Client-supplied correlation IDs **MAY** be forwarded only after validation and length restriction.

### 10.6 Log Integrity

* Logs **MUST** be shipped to a centralized destination in near real time.
* The destination **SHOULD** be append-only or tamper-evident.
* Host-local logs alone are not sufficient for audit purposes.

---

## 11. Hardened HTTP Responses

Security-relevant headers **MUST** be set centrally in middleware, hosting, ingress, or edge configuration. They **MUST NOT** be managed ad hoc per route unless explicitly approved.

### 11.1 Required Headers

The following headers **MUST** be present on all HTTP responses. Set them once in central middleware — never per-route.

| Header | Required value |
|---|---|
| `Content-Security-Policy` | `default-src 'none'` for API-only services; frontend CSP defined centrally in hosting config |
| `X-Frame-Options` | `DENY` (or equivalent `frame-ancestors 'none'` in CSP) |
| `X-Content-Type-Options` | `nosniff` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Strict-Transport-Security` | `max-age=63072000; includeSubDomains; preload` on HTTPS deployments. **Staged rollout:** start with `max-age=0` during initial deployment to avoid locking browsers before the domain and certificate are confirmed stable; increase to a short value (e.g. `max-age=300`) once verified, then ramp to the full value before public launch. |
| `Permissions-Policy` | `camera=(), microphone=(), geolocation=(), payment=()` unless a feature explicitly requires otherwise |
| Correlation ID header | Value from request context, if used by the platform |

### 11.2 Header Suppression

The following identifying headers **MUST** be removed from all responses. Strip them centrally in middleware.

* `Server`
* `X-Powered-By`
* framework-identifying version headers (e.g. `X-AspNet-Version`, `X-Powered-By`)

### 11.3 Cache Control

* Responses to authenticated requests **MUST** include `Cache-Control: no-store` unless an approved exception exists.

### 11.4 Transport Security

* Production deployments **MUST** use HTTPS for all authenticated and sensitive traffic.
* HTTP **MUST** be redirected to HTTPS where the application is intended to be reachable over the web.
* TLS certificate validation **MUST** remain enabled except in tightly controlled local-only testing.
* Internal service-to-service traffic carrying sensitive data **SHOULD** also use authenticated transport protections appropriate to the environment.

---

## 12. Dependency and Supply-Chain Security

### 12.1 Version Pinning

* Dependencies **MUST** be pinned to exact versions where the ecosystem supports it.
* Lock files **MUST** be committed and enforced.
* **Note:** Version pinning and lock file CI enforcement are distinct controls. In early-stage projects, CI enforcement of lock files **MAY** be deferred provided all direct dependencies are pinned to exact versions and enabling enforcement before public launch is documented as a planned step.

### 12.2 Automated Scanning

* CI/CD **MUST** run dependency and vulnerability scanning.
* Builds **MUST** fail on critical or high-severity vulnerabilities unless an approved risk exception exists.

### 12.3 Dependency Review

* New dependencies **MUST** receive a brief review for:
  * maintenance status
  * license suitability
  * known vulnerability history
  * necessity
  * source trustworthiness

### 12.3.1 Supply-Chain Integrity

* Build dependencies, packages, containers, and third-party artifacts **MUST** be pulled only from approved and trusted sources.
* CI/CD pipelines **MUST** protect against unauthorized changes to build definitions, dependency sources, and release artifacts.
* Automatic updates, builds, or deployments from untrusted sources **MUST NOT** be enabled by default.

### 12.4 Minimal Footprint

* The dependency footprint **SHOULD** be kept as small as practical.
* Unused dependencies **MUST** be removed.

### 12.5 Third-Party Web Assets

* Third-party CDN assets **MUST** use Subresource Integrity where supported.

---

## 13. Forbidden Patterns

The following patterns **MUST NOT** appear in committed production code unless explicitly approved as a documented exception:

* client-side-only authorization
* returning full domain models to lower-trust callers
* mapping raw request DTOs directly onto domain objects
* string concatenation in SQL, shell commands, file paths, or security-sensitive URL fetches from user input
* hardcoded secrets, credentials, or API keys
* logging tokens, passwords, raw credentials, or unnecessary personal data
* disabled TLS verification outside tightly controlled local-only testing
* custom cryptography instead of approved libraries
* TODO or FIXME placeholders for security-critical production behavior
* feature flags or bypass toggles that silently disable security controls
* mixing user-token and service-credential authorization in the same check
* broad wildcard permissions without explicit justification
* duplicating security-critical logic instead of calling shared helpers
* using untrusted forwarding headers directly for security decisions
* returning distinct record-existence errors that create enumeration oracles
* failing to sanitize CSV or spreadsheet formula-leading characters
* redirecting to user-supplied URLs without strict validation
* using standard string equality for secret comparison
* omitting `Cache-Control: no-store` on authenticated responses without documented exception
* leaving debug, test, or deprecated endpoints reachable in production
* bcrypt cost below 12 or use of fast password hashes
* using LLM output directly as SQL, shell commands, file paths, URLs, or policy input without deterministic validation
* deriving authorization decisions, tenant scope, or user identity from prompt content or model output
* forwarding a raw user bearer token to an MCP server or downstream service without explicit architectural approval
* placing secrets, credentials, tokens, or connection strings in prompts

---

## 14. Code Reuse for Security Logic

Security-critical logic **MUST NOT** be duplicated.

The following logic **MUST** live in shared implementations:

* authorization checks
* role or permission hierarchy evaluation
* token or claim extraction
* client IP derivation
* rate-limit invocation patterns where centralized helpers exist
* DTO projection for sensitive fields
* token or secret hashing
* CSV/export sanitization logic

The pattern of **calling** these helpers may repeat.  
The security logic implementation itself **MUST NOT** be copied across files.

---

## 15. Testing Requirements

Every security-relevant feature **MUST** include tests covering the relevant risk profile.

### 15.1 Required Test Categories

* **Validation tests** — malformed, missing, oversized, wrong-type, and disallowed input is rejected
* **Authorization tests** — each role or policy can access exactly what it should and nothing more
* **Tenant isolation tests** — cross-tenant data access is impossible at service and data-access layers
* **Self-access tests** — low-privilege users can access only their own records unless explicitly allowed otherwise
* **Negative or hostile input tests** — injection payloads and malformed inputs are rejected safely
* **Boundary tests** — size, range, count, timeout, and pagination constraints are enforced
* **Abuse-case tests** — high-risk flows include attacker misuse scenarios
* **Regression tests** — previously identified weaknesses have coverage preventing recurrence
* **Session and CSRF tests** — session fixation, session revocation, cookie attributes, and CSRF protections are verified where applicable
* **Transport and parser tests** — transport enforcement and secure parser behavior are verified where applicable

### 15.2 High-Risk Flows Requiring Abuse-Case Coverage

The following flows **SHOULD** include abuse-case tests where implemented:

* login
* token refresh
* password reset
* export
* deletion
* role assignment
* billing or financial actions
* any tenant-scoped bulk operation

---

## 16. LLM and MCP Integration Security

This section applies when a traditional web application uses an LLM-backed feature, an MCP server, or both.  
It does **not** define agent autonomy requirements.

### 16.1 Trust Model

* LLM output **MUST** be treated as untrusted until validated.
* MCP server responses **MUST** be treated as untrusted until validated and authorized in application code.
* Neither the LLM nor the MCP integration **MAY** act as the enforcement point for authorization, tenant isolation, or data-classification decisions.

### 16.2 Authorization Boundaries

* All access control decisions **MUST** be enforced in deterministic server-side code or backend services.
* Tenant scope, user identity, and permitted actions **MUST** come from trusted server-side context, not from prompts or model-generated text.

### 16.3 Prompt and Data Handling

* User input, retrieved content, MCP responses, and model output **MUST** be treated as untrusted input.
* Untrusted content **MUST NOT** override system rules, authorization rules, or application security policy.
* Secrets, tokens, credentials, and connection strings **MUST NOT** be placed in prompts unless explicitly required and approved.

### 16.4 MCP Invocation

* Calls to MCP servers **MUST** use scoped service identities or approved delegated identities.
* Raw user bearer tokens **MUST NOT** be forwarded to downstream MCP or other services unless explicitly approved by architecture.
* MCP inputs **MUST** be validated with strict schemas and allow-lists before invocation.
* Free-form user input **MUST NOT** be passed directly into privileged MCP operations without deterministic validation.
* If MCP-backed operations set database session context such as tenant or user identifiers, that context **MUST** be derived from trusted server-side identity, applied only for the intended operation scope, and cleared or reset afterward.

### 16.5 Output Handling

* Model output **MUST NOT** be executed directly as code, SQL, shell commands, URLs, file paths, or policy input without deterministic validation and approval.
* Model output displayed to users **MUST** be encoded appropriately for its output context.

### 16.6 Logging and Audit

* Prompts, completions, MCP requests, and MCP responses **MUST NOT** log secrets, credentials, tokens, or unnecessary personal data.
* Privileged MCP-backed operations **MUST** be auditable with initiating user, delegated identity, tenant scope where applicable, action taken, and correlation ID.

### 16.7 Testing

Implementations using LLM or MCP **MUST** include relevant tests for:

* prompt injection in user-controlled content
* cross-tenant leakage through MCP-backed queries
* server-side authorization enforcement even when prompt content requests forbidden data
* malformed, oversized, or out-of-scope MCP inputs
* prevention of sensitive data leakage to logs

---

## 17. Agent Response Structure for Substantial Work

For non-trivial implementation work, the implementation agent **MUST** present the following sections before or alongside code where applicable:

* security assumptions
* threats considered
* security design choices
* implementation
* validation and authorization points
* tests
* residual risks

---

## 18. Pre-Code Checklist

Before outputting non-trivial implementation, verify the following.

### Existing Code Compliance

* Does any existing code in the files being modified conflict with a **MUST** in this specification? If yes, call it out explicitly before writing new code — do not silently follow the existing pattern.

### Threat Model

* Have trust boundaries been identified?
* Have attacker-controlled inputs been enumerated?
* Has at least one abuse case been considered?

### Input and Validation

* Is all external input validated at the boundary?
* Are domain invariants enforced in service or domain logic?
* Are raw primitives absent from security-relevant domain signatures where required?

### Access and Authorization

* Is deny-by-default preserved?
* Does every resource query enforce tenant or owner scope?
* Is self-access enforcement in place where required?
* Are privileged routes gated explicitly?
* Is the response DTO purpose-specific for the caller?
* If redirects exist, are destinations allow-listed?

### Data and Database

* Are queries parameterized?
* Is mass assignment blocked?
* Does the response expose only permitted fields?
* Are query limits and timeouts in place where needed?

### API and Infrastructure

* Is rate limiting applied where required?
* Are list endpoints capped?
* Do error responses minimize detail?
* Is request body size bounded?
* Do outbound HTTP calls use managed clients and explicit timeouts?

### Logging and Config

* Are secrets sourced from secure configuration rather than code?
* Do logs include useful identifiers without secrets or unnecessary personal data?
* Is no full domain model serialized into logs?

### Forbidden Patterns

* Does the change avoid all forbidden patterns in this specification?

### Dependencies

* Are new dependencies pinned and CVE-checked?
* Have newly unused dependencies been removed?

---

## 19. Per-Endpoint Completion Checklist

Run this after implementing a new route or externally reachable handler.

### Every New Route

* Rate-limited if it is auth-related, recovery-related, enumeration-prone, or triggers side effects
* List results capped or paginated
* Response DTO purpose-specific for the caller
* Error messages do not create enumeration oracles
* Authorization uses shared helpers or shared infrastructure
* Authenticated responses include `Cache-Control: no-store` unless explicitly exempted
* Password reset and recovery flows meet token security requirements
* Redirect destinations are validated against allow-listed internal paths only

### Every Route That Reads Client-Supplied Headers

* Header values validated and length-limited before use
* Security decisions use trusted proxy parsing, not arbitrary client header values

### Every Service Method That Writes Logs or Audit Records

* No secrets in logs
* No unnecessary personal data in logs
* Audit data uses identifiers rather than display names where possible

### Every New Model Field or DTO Property

* Length and format constraints defined consistently across model, DTO, and DB schema
* Field omitted from DTOs where the caller does not need it

### Every New Dependency

* Pinned to an exact version where supported
* CVE-checked
* Added to the lock file
* Any dependency made unused by the change removed

### Every New Secret or Local Config File

* Added to `.gitignore` before first commit if local-only
* Added to startup validation if required for operation
* Optional configuration handled gracefully at the call site

---

## 20. Security Decision Rule

> This rule is also summarized in **§3.2** so it is encountered early. Both copies are normative.

When a requirement is ambiguous, choose the option that:

1. grants less privilege
2. exposes less data
3. narrows trust boundaries
4. keeps enforcement in deterministic code rather than assumptions about external behavior
5. requires explicit approval for destructive or high-impact actions

If uncertainty remains, the implementation **MUST NOT** choose the riskier behavior by default.

---

## 21. Control Mapping Appendix

This appendix is a **cross-reference aid**, not the source of truth. The normative requirements remain the sections above.

### 21.1 Mapping to OWASP Top 10

| This Specification | Primary OWASP Top 10 Coverage |
|---|---|
| 4. Architectural Mandates | A01 Broken Access Control, A04 Insecure Design, A05 Security Misconfiguration, A09 Security Logging and Monitoring Failures |
| 5. Threat Modelling | A04 Insecure Design |
| 6. Input Validation and Domain Modelling | A03 Injection, A04 Insecure Design |
| 7. Data Integrity and Injection Prevention | A03 Injection, A05 Security Misconfiguration, A08 Software and Data Integrity Failures |
| 8. Identity and Access Control | A01 Broken Access Control, A07 Identification and Authentication Failures |
| 9. API and Resource Security | A01 Broken Access Control, A04 Insecure Design, A05 Security Misconfiguration, A10 Server-Side Request Forgery |
| 10. Configuration and Logging | A05 Security Misconfiguration, A09 Security Logging and Monitoring Failures |
| 11. Hardened HTTP Responses | A05 Security Misconfiguration |
| 12. Dependency and Supply-Chain Security | A06 Vulnerable and Outdated Components, A08 Software and Data Integrity Failures |
| 13. Forbidden Patterns | Cross-cutting, especially A01, A03, A05, A07, A08 |
| 14. Code Reuse for Security Logic | A04 Insecure Design |
| 15. Testing Requirements | A04 Insecure Design, A09 Security Logging and Monitoring Failures |
| 16. LLM and MCP Integration Security | A01 Broken Access Control, A04 Insecure Design, A08 Software and Data Integrity Failures |
| 17–20 Agent Structure and Checklists | A04 Insecure Design |

### 21.2 Mapping to OWASP API Security Top 10

| This Specification | Primary OWASP API Security Coverage |
|---|---|
| 6. Input Validation and Domain Modelling | API08 Security Misconfiguration |
| 7. Data Integrity and Injection Prevention | API03 Broken Object Property Level Authorization, API08 Security Misconfiguration, API10 Unsafe Consumption of APIs |
| 8. Identity and Access Control | API01 Broken Object Level Authorization, API02 Broken Authentication, API03 Broken Object Property Level Authorization, API05 Broken Function Level Authorization |
| 9. API and Resource Security | API04 Unrestricted Resource Consumption, API07 Server Side Request Forgery, API08 Security Misconfiguration |
| 10. Configuration and Logging | API08 Security Misconfiguration, API10 Unsafe Consumption of APIs |
| 11. Hardened HTTP Responses | API08 Security Misconfiguration |
| 12. Dependency and Supply-Chain Security | API09 Improper Inventory Management, API10 Unsafe Consumption of APIs |
| 15. Testing Requirements | Cross-cutting, especially API01–API05, API08, API10 |
| 16. LLM and MCP Integration Security | API01, API03, API05, API10 |
| 19. Per-Endpoint Completion Checklist | API01–API05, API08 |

### 21.3 Mapping to OWASP ASVS 5.0 (High-Level)

| This Specification | Closest ASVS Areas |
|---|---|
| 4. Architectural Mandates | Architecture, Design and Threat Modeling |
| 5. Threat Modelling | Architecture, Design and Threat Modeling |
| 6. Input Validation and Domain Modelling | Input Validation and Encoding |
| 7. Data Integrity and Injection Prevention | Stored Cryptography (where relevant), File Handling, Deserialization, Output Encoding, Injection Prevention |
| 8. Identity and Access Control | Authentication, Session Management, Access Control, Credential Recovery |
| 9. API and Resource Security | API and Web Service, Malicious Input Handling, File Handling, Communications |
| 10. Configuration and Logging | Configuration, Error Handling and Logging |
| 11. Hardened HTTP Responses | Communications, Browser Security Configuration |
| 12. Dependency and Supply-Chain Security | Configuration, Secure Build and Deployment, Dependency Management |
| 15. Testing Requirements | Verification across all ASVS control families |
| 16. LLM and MCP Integration Security | API and Web Service, Access Control, Communications, Configuration |

### 21.4 Mapping to 12-Factor App

| This Specification | 12-Factor Alignment |
|---|---|
| 10.1 Configuration and Secrets | Factor III: Config |
| 10.2–10.6 Logging | Factor XI: Logs |
| 12. Dependency and Supply-Chain Security | Supports disciplined dependency management even though this is broader than 12-Factor |
| 11.4 Transport Security and 9.8 Outbound HTTP Timeouts | Supports operational resilience and safe service communication |

### 21.5 Mapping to Secure by Design / Secure by Default / NCSC-Style Principles

| This Specification | Principle Alignment |
|---|---|
| 4.1 Security by Design | Security as a core design requirement |
| 4.2 Secure by Default | Safe defaults, deny by default |
| 4.3 Fail Secure | Failure does not create bypass |
| 4.4 Least Privilege | Minimize authority |
| 4.5 Defence in Depth | Layered control design |
| 4.6 Blast-Radius Reduction | Compartmentalization and containment |
| 4.7 Auditability and Revocation | Recoverability, detection, response |
| 5. Threat Modelling | Design-time abuse-case thinking |
| 14. Code Reuse for Security Logic | Centralized and consistent enforcement |
| 20. Security Decision Rule | Prefer safer behavior under ambiguity |

### 21.6 Mapping to Security-Relevant DDD Practices

| This Specification | DDD / Domain Safety Alignment |
|---|---|
| 6.1 Strict Type Modelling | Value objects for security-relevant input |
| 6.2 Validation at Birth | Invariants enforced at construction |
| 6.4 Cross-Layer Consistency | Ubiquitous constraints across boundaries |
| 6.5 Domain Invariants | Invariants enforced in domain and service logic |
| 6.6 DTO Discipline | Explicit anti-corruption / boundary shaping |
| 7.2 Mass Assignment Protection | Prevent raw external models from mutating domain state |
| 8.2 Authorization | Access checks tied to trusted domain context, not UI assumptions |

### 21.7 How to Use This Mapping

Use this appendix to:

* explain why a requirement exists
* support control-to-standard traceability in architecture or audit reviews
* identify which sections to emphasize for a given risk area

Do **not** use this appendix to weaken or reinterpret a normative requirement in the main body of the specification.