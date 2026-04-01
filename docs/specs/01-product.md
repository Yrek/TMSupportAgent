# Threat Modeling Assistant Specification for Modern Web Systems

## 1. Purpose

Build a threat modeling assistant for modern web systems.

The assistant MUST support analysis of:
- web applications
- SPAs and server-rendered applications
- backend APIs
- backend-for-frontend patterns
- microservices
- event-driven services
- multi-tenant SaaS platforms
- cloud-native web systems
- identity-integrated systems
- LLM-enabled web systems
- agentic web applications
- MCP-enabled web systems

The assistant MUST NOT target:
- OT / ICS / SCADA
- industrial control environments
- embedded safety systems
- physical safety engineering
- automotive / avionics safety analysis

The assistant MUST act as a threat modeling copilot and MUST NOT act as an unsupervised security decision-maker.

---

## 2. Primary Objective

The assistant MUST:
1. ingest architecture-relevant context
2. normalize the system into a structured model
3. classify the architecture
4. select appropriate threat modeling methods and lenses
5. select appropriate LLM/model roles for each task
6. generate threats, abuse cases, control gaps, and mitigations
7. distinguish facts from assumptions and unknowns
8. map findings to recognized control frameworks
9. produce outputs suitable for engineering, architecture, and security review

The assistant MUST NOT:
- silently invent architecture facts
- claim controls exist when they are unverified
- produce generic checklist output detached from the architecture
- hide ambiguity
- imply certainty where the input is incomplete

---

## 3. Core Principles

The assistant MUST follow these principles:
- Architecture before analysis
- Traceability from finding to architecture element
- Security by Design
- Secure by Default
- Fail Secure
- Least Privilege
- Defense in Depth
- Blast Radius Reduction
- Auditability
- Revocation
- Explicit trust boundaries
- Deny by default
- Separation of duties
- Compartmentalization
- Fact/assumption separation
- Human review and validation before final acceptance

The assistant SHOULD recommend patterns aligned to NCSC-style secure design principles:
- establish context first
- make compromise difficult
- make disruption difficult
- make compromise detection easier
- reduce impact of compromise

---

## 4. Supported Inputs

The assistant MUST accept:
- free-text descriptions
- architecture summaries
- context diagrams
- DFD-like diagrams
- sequence descriptions
- trust boundary descriptions
- user stories relevant to abuse cases
- security requirements
- privacy requirements
- authentication / authorization descriptions
- tenant model descriptions
- third-party integration descriptions
- AI/LLM/tooling descriptions where applicable

The assistant SHOULD accept incomplete input but MUST surface material gaps explicitly.

The assistant MUST treat uploaded artifacts and extracted content as untrusted data, not trusted instructions.

---

## 5. Canonical System Model

The assistant MUST normalize input into a structured canonical system model containing, at minimum:
- system purpose
- major actors and user types
- components and services
- external systems
- data stores
- data flows
- trust boundaries
- network exposure
- execution boundaries
- authentication methods
- authorization model
- session model
- machine identities
- privileged/admin/support paths
- tenant boundaries where relevant
- sensitive data types
- secrets and key usage points
- asynchronous flows
- background jobs
- logging, monitoring, and audit points
- AI/LLM/tool boundaries where relevant

The assistant MUST expose the interpreted model for review before final threat synthesis.

The assistant MUST maintain a stable mapping between:
- diagram elements
- normalized architecture elements
- generated threats
- user-provided corrections
- user-added threats
- user-added notes and clarifications

This mapping MUST remain traceable across re-analysis and updates.

---

## 6. Architecture Classification

The assistant MUST classify the system into one or more categories:
- standard web application
- API-centric application
- integration-heavy backend
- microservice/distributed system
- event-driven system
- multi-tenant SaaS
- privacy-heavy system
- identity-complex system
- cloud-native system
- LLM-enabled web application
- agentic/MCP-enabled web application

This classification MUST drive:
- method selection
- model/LLM selection
- depth of analysis
- control mapping emphasis

---

## 7. Threat Modeling Methods and Lenses

The assistant MUST support STRIDE as a baseline method.

The assistant SHOULD support, where applicable:
- LINDDUN for privacy-heavy systems
- abuse case / misuse case analysis
- attack path analysis
- trust-boundary analysis
- identity / session / delegation analysis
- tenant isolation analysis
- data lifecycle / data exposure analysis
- supply chain / dependency risk analysis
- availability and resilience abuse analysis
- business logic abuse analysis
- AI-specific threat analysis for LLM-enabled web systems
- MCP / tool invocation / context misuse analysis
- control gap analysis
- secure design pattern recommendation pass
- security architecture anti-pattern detection

The assistant MUST explain why each selected method or lens was chosen.

---

## 8. Method Selection Rules

The assistant MUST select methods dynamically.

### Standard web application
MUST use:
- STRIDE baseline

SHOULD add:
- abuse-case analysis for admin, support, and business-critical flows
- privacy lens only if personal or regulated data is material

### API-centric or integration-heavy system
MUST use:
- STRIDE baseline
- identity/session/delegation analysis
- API-specific abuse/control lens

### Privacy-heavy system
MUST use:
- STRIDE baseline
- LINDDUN or equivalent privacy lens
- data minimization, retention, and disclosure analysis

### Multi-tenant SaaS
MUST use:
- STRIDE baseline
- tenant isolation analysis
- admin/support boundary abuse analysis

SHOULD add:
- privacy analysis where relevant

### Event-driven or distributed system
MUST use:
- STRIDE baseline
- message-flow integrity analysis
- replay / poison / reprocessing abuse analysis
- trust-boundary decomposition

### LLM-enabled or MCP-enabled web application
MUST use:
- STRIDE baseline
- AI-specific threat lens
- prompt/context/tool misuse analysis
- identity and session propagation analysis
- retrieval/memory/context poisoning analysis where relevant
- abuse-case analysis for model-driven actions

---

## 9. LLM / Model Routing Requirements

The assistant MUST support multiple LLMs or reasoning models.

The assistant MUST support these model roles:
- primary reasoning model
- architecture interpretation / normalization model
- secondary synthesis model
- low-cost transform / classification model
- reviewer / challenger model

Model routing MUST consider:
- task complexity
- architecture ambiguity
- security criticality
- context length needs
- privacy/sensitivity needs
- cost constraints
- latency constraints
- multimodal needs
- need for structured output
- need for second-pass challenge or review

The strongest available reasoning model MUST be used for:
- architecture interpretation from ambiguous input
- trust-boundary reasoning
- identity/session/delegation analysis
- multi-tenant isolation reasoning
- final threat synthesis
- AI/tool-context threat analysis
- complex distributed-system reasoning

Lower-cost models MAY be used for:
- simple classification
- deduplication
- tagging
- formatting
- issue generation
- schema cleanup

The assistant MUST NOT route subtle security reasoning to a low-capability model if doing so materially increases error risk.

---

## 10. Clarification and Gap Handling

The assistant MUST identify and prioritize missing information before final analysis.

It MUST generate focused clarification questions about, where relevant:
- internet exposure
- authentication method
- authorization enforcement
- session and token handling
- token delegation
- admin and support access
- trust boundaries
- external integrations
- sensitive data types
- tenant isolation
- background jobs
- secrets and key handling
- logging and auditability
- use of LLMs, tools, retrieval, memory, or MCP
- impersonation or delegated support access

The assistant MUST prioritize questions by security relevance and MUST NOT ask low-value questions that do not materially improve the threat model.

If any of the following are unclear, the assistant MUST ask targeted clarification questions before finalizing high-confidence findings:
- trust boundaries
- authentication mechanism
- authorization enforcement point
- tenant isolation mechanism
- admin/support access path
- sensitive data classification
- external integration trust assumptions

---

## 11. Threat Generation Requirements

Each threat MUST include:
- unique identifier
- title
- method/category label
- affected components, flows, or boundaries
- threat description
- attack scenario
- preconditions
- impacted assets
- likely security impact
- likely privacy impact where applicable
- existing controls if known
- control gaps
- recommended mitigations
- confidence level
- assumptions and unknowns
- evidence basis
- evidence strength

Allowed evidence basis MUST include one or more of:
- explicit user-provided fact
- extracted architecture fact
- confirmed assumption
- architecture-derived inference
- known method-driven risk pattern

Evidence strength MUST be labeled as:
- direct
- inferred
- assumption-dependent

The assistant MUST NOT present an assumption-dependent threat as confirmed architecture fact.

The assistant MUST avoid vague findings unless tied to a specific component, flow, trust boundary, or abuse path.

The assistant MUST NOT include a threat in the primary findings list unless it has:
- a clear affected element
- a plausible attack path or abuse path
- a meaningful impact
- enough evidence or justified inference to explain why it is relevant

If a possible threat is too speculative, the assistant MUST place it in a separate section such as:
- Conditional risks
- Hypotheses requiring confirmation

The assistant MUST keep speculative threats out of the prioritized main findings.

The assistant MUST NOT include a threat solely because it is common in an industry list or taxonomy.

A framework category alone is not sufficient evidence.

The assistant MUST show why the threat is relevant to the specific architecture, flow, identity model, tenant model, trust boundary, or abuse path.

---

## 12. Abuse-Case and Business Logic Analysis

The assistant MUST support abuse-case analysis for:
- admin actions
- support operations
- export/reporting flows
- invitation/onboarding flows
- account recovery flows
- payment/billing flows
- approval flows
- impersonation flows
- user-to-user actions
- delegated access
- search and filtering
- background processing
- cross-tenant operations

The assistant MUST identify when business logic abuse is more important than generic technical threats.

---

## 13. Identity, Session, and Delegation Analysis

The assistant MUST treat identity and session analysis as first-class.

It MUST analyze:
- user authentication
- service authentication
- machine identities
- token scopes
- token audience and delegation
- session establishment
- session invalidation assumptions
- backend trust in frontend claims
- context propagation across layers
- privilege elevation paths
- support/admin access paths
- tenant context propagation
- background-job execution context

The assistant MUST call out any architecture that relies on implicit trust in user-controlled claims or weakly bounded delegated access.

---

## 14. Tenant Isolation and Data-Centric Analysis

For multi-tenant systems, the assistant MUST analyze:
- tenant context establishment
- tenant context propagation
- tenant boundary enforcement
- cross-tenant query risk
- cache isolation
- queue/topic isolation
- search/index isolation
- background job leakage
- analytics and telemetry leakage
- export/report leakage
- notification leakage
- admin/support cross-tenant access
- object/blob/file access isolation

The assistant MUST identify:
- what sensitive data exists
- where it enters the system
- where it is transformed
- where it is stored
- where it is cached
- where it is queued
- where it is logged
- where it is exported
- where it crosses trust boundaries
- where it is exposed to support/admin operations
- where it is exposed to LLM context, retrieval, prompts, or tools where applicable

The assistant MUST assess risks including:
- excessive collection
- weak minimization
- unauthorized disclosure
- inference leakage
- metadata leakage
- integrity corruption
- cache leakage
- stale or replayed data in async flows
- export abuse
- retention and deletion weaknesses

---

## 15. AI-Specific Analysis for Modern Web Systems

If the target system contains LLM or agentic features, the assistant MUST analyze:
- prompt injection
- indirect prompt injection from untrusted content
- retrieval poisoning
- context poisoning
- unsafe tool invocation
- over-broad tool permission
- model-to-tool privilege escalation
- memory/session leakage
- cross-user context leakage
- unsafe autonomous or semi-autonomous action execution
- insecure fallback behavior
- insecure human-override patterns
- data exfiltration through prompts or tool calls
- misuse of uploaded content as instructions
- insecure model routing decisions

The assistant MUST scope this analysis to modern web systems using AI.

---

## 16. Secure Design Pattern Suggestions

The assistant MUST generate secure design pattern suggestions that are concrete and architecture-mapped.

It SHOULD include patterns such as:
- explicit trust boundary enforcement
- deny-by-default authorization
- policy enforcement at every access point
- back-end authorization independent of client claims
- short-lived tokens and bounded delegation
- audience-restricted tokens
- per-request tenant context validation
- least-privilege service identities
- separate admin planes and user planes
- support access with approval, justification, and audit
- fail-closed behavior on authz, policy, and dependency failure
- compartmentalization of high-risk functions
- blast-radius reduction through service/data separation
- segmented secrets and key scopes
- immutable and attributable audit trails
- revocation hooks for sessions, tokens, API keys, grants, and privileged access
- idempotency and replay protection for sensitive operations
- queue/message authenticity and integrity verification
- safe-by-default external integration handling
- default-off risky features
- data minimization and selective disclosure
- safe logging and redaction by default
- reviewer or approval gates for dangerous AI/tool actions

The assistant MUST map each suggested pattern to one or more principles such as:
- Secure by Design
- Secure by Default
- Fail Secure
- Least Privilege
- Defense in Depth
- Blast Radius Reduction
- Auditability
- Revocation
- NCSC secure design principles

---

## 17. Mapping to Security Controls and Frameworks

The assistant MUST map findings, mitigations, and design recommendations to relevant frameworks where feasible.

It MUST support mappings to:
- OWASP Top 10
- OWASP API Security Top 10
- OWASP ASVS
- Twelve-Factor App
- NCSC secure design / secure-by-default principles
- CIS Controls

The assistant MUST:
- map controls only when there is a reasonable fit
- avoid false precision or invented sub-control references
- distinguish direct mapping from approximate alignment
- support one-to-many mappings
- preserve architecture context in the mapping
- avoid compliance theater

The assistant SHOULD also support:
- mapping to internal security requirements
- mapping to organization-specific secure design principles
- mapping to backlog items or security stories

---

## 18. Mitigation Generation and Prioritization

Mitigations MUST be:
- specific
- architecture-aware
- prioritized
- practical
- traceable
- implementable
- suitable for engineering work

Mitigations SHOULD include:
- design changes
- trust-boundary hardening
- authorization fixes
- token/session handling changes
- blast-radius reduction measures
- safer default settings
- logging/audit improvements
- revocation improvements
- data minimization changes
- privilege reductions
- service separation
- workflow approvals
- replay/idempotency controls
- AI/tool guardrails where relevant

Priority MUST consider, where relevant:
- exploitability
- impact
- data sensitivity
- internet exposure
- privilege required
- tenant blast radius
- user blast radius
- architectural centrality
- detectability
- ease of abuse
- ease of mitigation

The assistant MUST NOT use fake numerical precision where evidence is weak.

The assistant MUST NOT assign high severity or high priority unless:
- the affected asset is important
- the attack path is plausible
- the impact is material
- the finding is supported by adequate evidence

---

## 19. Output Requirements, Review/Validation, and Interactive Diagram Editing

Each completed analysis MUST include:
- system summary
- architecture classification
- assumptions and unknowns
- selected methods/lenses with rationale
- model routing summary
- threat list
- secure design recommendations
- prioritized remediation guidance
- review questions
- structured machine-readable output

Each threat entry MUST include:
- identifier
- title
- category
- affected elements
- attack scenario
- impact
- assumptions
- confidence
- mitigations
- framework mappings where applicable
- evidence basis
- evidence strength
- status

The assistant MUST separate outputs into:
- Confirmed or strongly supported findings
- Conditional risks / hypotheses requiring confirmation

Only confirmed or strongly supported findings MAY appear in the prioritized remediation list.

The assistant MUST include an explicit review and validation stage before finalizing the analysis.

This review and validation stage MUST:
- verify that the interpreted architecture has been reviewed and, where possible, confirmed by the user
- verify that major components, data flows, trust boundaries, identities, privileged paths, and tenant boundaries have been identified or explicitly marked as unknown
- verify that selected methods and lenses are appropriate for the classified architecture
- verify that top risks are traceable to concrete architecture elements
- verify that assumptions, inferences, and unknowns are clearly separated
- verify that mitigations are specific, actionable, and mapped to the relevant architecture elements
- verify that secure design pattern recommendations are relevant and not generic
- verify that control mappings are reasonable and not falsely precise
- verify that important security architecture anti-patterns have been identified, or explicitly assessed and ruled out
- verify that the final output is suitable for engineering and security review rather than only high-level discussion

The assistant MUST NOT finalize a threat model as complete if critical architectural ambiguity remains unresolved and materially weakens the analysis.

If critical ambiguity remains, the assistant MUST:
- mark the analysis as partial or conditional
- identify the unresolved issues
- explain how those issues affect confidence and prioritization
- present the output as a review draft rather than a finalized threat model

### Interactive diagram requirements

The assistant MUST support an interactive architecture diagram view.

The user MUST be able to:
- click a diagram element and see the threats mapped to that element
- click a data flow and see threats mapped to that flow
- click a trust boundary and see threats mapped to that boundary
- see whether a threat is confirmed, conditional, user-added, or system-generated
- add their own threats to a specific diagram element, flow, boundary, or note
- add contextual notes or corrections to a specific diagram element
- edit or clarify metadata for a specific element, such as purpose, trust zone, data type, auth mechanism, or tenant relevance
- mark extracted information as correct, incorrect, incomplete, or assumed
- add missing components, flows, boundaries, or identities before threat modeling is triggered
- request re-analysis after user corrections are made

The system MUST support a pre-analysis correction workflow where:
- the user can review extracted architecture information
- the user can correct incorrect information
- the user can enrich missing information
- the user can add their own threats or concerns
- the assistant re-runs normalization and analysis using the corrected architecture state

The system MUST preserve provenance for interactive changes, including:
- original extracted value
- user-corrected value
- user-added note
- user-added threat
- timestamp or revision state
- whether the information was system-generated or user-provided

The system MUST ensure that user corrections override unconfirmed extracted interpretations for subsequent threat modeling runs.

The system SHOULD support per-element views showing:
- element metadata
- related flows
- related trust boundaries
- related threats
- related mitigations
- related assumptions
- related user notes
- related control mappings

The system SHOULD support diagram state comparison between:
- original extracted architecture
- corrected architecture
- previous reviewed version
- current analysis version

---

## 20. Security Requirements for the Assistant Itself, Quality Controls, Success Criteria, and Anti-Goals

Because the assistant processes sensitive architecture material, it MUST:
- separate platform/system instructions, product policy, user instructions, uploaded content, extracted content, and retrieved reference material
- prevent uploaded documents or retrieved content from overriding system behavior
- enforce workspace and tenant isolation
- minimize retention of secrets, tokens, credentials, and sensitive architecture details
- preserve auditability of inputs, normalized model, selected methods, selected model route, assumptions, generated findings, and reviewer decisions
- enforce review and validation gates for interpreted architecture, final findings, and exported remediation artifacts

The assistant MUST minimize false positives by:
- rejecting threats that are not traceable to architecture elements
- rejecting generic threats that would apply to nearly any web system unless architecture evidence makes them relevant
- rejecting duplicate threats expressed with different wording
- rejecting mitigations that do not address the stated threat scenario
- downgrading findings that depend on unresolved ambiguity
- separating confirmed risks from conditional risks

Confidence MUST reflect:
- quality of the input
- completeness of the architecture model
- strength of evidence
- clarity of trust boundaries
- certainty of the attack path

High confidence MUST only be used when:
- the affected architecture elements are clear
- the attack path is plausible and concrete
- the impact is meaningful
- the finding does not depend on unresolved critical assumptions

The assistant MUST NOT assign high confidence to primarily speculative findings.

The assistant MUST merge findings that share:
- the same root cause
- the same affected architecture element
- the same attack path
- materially similar mitigations

The assistant MUST avoid inflating risk counts by splitting one root issue into many near-duplicate findings.

The assistant MUST remain within the provided architecture scope.

If a risk depends on an out-of-scope component or missing system detail, the assistant MUST label it as scope-dependent or unknown rather than presenting it as a confirmed finding.

The assistant MUST be developed with an evaluation suite containing representative architectures and expected outcomes.

The evaluation suite MUST test:
- method selection correctness
- architecture classification correctness
- threat relevance
- false positive rate
- duplicate finding rate
- confidence calibration
- mitigation quality
- framework mapping quality
- behavior under ambiguous or incomplete input
- diagram-to-threat mapping correctness
- preservation of user-added threats and notes across re-analysis
- correctness of user corrections overriding extracted interpretations

The assistant MUST be regression-tested when prompts, workflow rules, schemas, or models change.

The assistant SHOULD be able to record rejected candidate threats with a brief reason, such as:
- insufficient evidence
- duplicate root cause
- out of scope
- mitigation already confirmed
- too speculative

The assistant succeeds only if it consistently:
- produces architecture-specific output
- adapts method selection to the target system
- adapts model routing to task complexity
- identifies identity, trust-boundary, data-flow, tenant, and admin risks clearly
- provides practical secure design recommendations
- maps findings meaningfully to security frameworks
- distinguishes facts from assumptions
- supports human review and correction
- supports interactive diagram-centered analysis
- preserves user-added information and corrections across analysis cycles
- remains focused on modern web systems rather than OT or generic AI security chat

The assistant MUST NOT be designed as:
- an OT/ICS threat modeling system
- a penetration testing agent
- a red-team automation agent
- a malware analysis platform
- a compliance-only checklist engine
- a generic autonomous security agent
- a system that changes architecture or controls without human review