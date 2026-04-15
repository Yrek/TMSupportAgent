# Spec Index

All specifications for the Threat Modeling Agent. **No implementation begins without an Approved spec.**

---

## Status Definitions

| Status | Meaning |
|---|---|
| **Draft** | Being written; not ready for review |
| **Review** | Ready for review; open decisions being resolved |
| **Approved** | Signed off; implementation may begin |
| **Implemented** | Spec is implemented; deviations noted |
| **Superseded** | Replaced by a newer spec; kept for history |

---

## Spec Status Table

| # | Document | Title | Status | Last Updated | Notes |
|---|---|---|---|---|---|
| 01 | [01-product.md](01-product.md) | Product Specification | **Approved** | 2026-03-31 | Functional requirements for the assistant |
| 02 | [02-architecture.md](02-architecture.md) | Architecture Specification | **Approved** | 2026-03-31 | Open decisions resolved |
| 03 | [03-data-model.md](03-data-model.md) | Data Model Specification | **Approved** | 2026-03-31 | PostgreSQL schema, blob layout, state machines |
| 04 | [../api/openapi.yaml](../api/openapi.yaml) | API Specification (OpenAPI) | **Approved** | 2026-03-31 | OpenAPI 3.1; authoritative API contract |
| 05 | [05-llm-workflow.md](05-llm-workflow.md) | LLM Workflow Specification | **Approved** | 2026-03-31 | Pipeline stages, model routing, typed contracts |
| 06 | [06-security.md](06-security.md) | Security Specification | **Approved** | 2026-03-31 | ISO 27001 + CLAUDE.md scoped to this system |
| 09 | [09-security-baseline.md](09-security-baseline.md) | Security Baseline (CLAUDE.md Enforcement) | **Approved** | 2026-04-15 | Implementation baseline; maps code-time controls and tracked TODO gaps |

---

## Open Decisions (blocking Approved status)

All decisions resolved for MVP implementation. Recorded below.

| ID | Spec | Decision | Resolution |
|---|---|---|---|
| OD-1 | 02-architecture | Azure OpenAI region | **Use standard Azure OpenAI endpoint with API key from Key Vault. West Europe deployment; fall back to Sweden Central if quota unavailable. Managed Identity added post-MVP.** |
| OD-2 | 02-architecture | AWAITING_REVIEW timeout | **Deferred. MVP requires explicit user confirmation. No auto-proceed.** |
| OD-3 | 02-architecture | Job completion notification | **Polling only for MVP. Client polls `GET /orgs/{orgId}/jobs/{jobId}` until status is terminal. SSE deferred.** |
| OD-4 | 02-architecture | Platform admin in MVP | **Deferred. No `platform:admin` role or admin-api service in MVP.** |
| OD-5 | 03-data-model | GDPR erasure workflow | **Soft-delete + PII null for MVP (email, display_name set to null). WorkOS account deletion is manual via WorkOS dashboard until erasure API is implemented post-MVP.** |
| OD-6 | 02-architecture / 04-api | Rate limiting | **App-layer rate limiting using ASP.NET Core built-in rate limiter (fixed window, per IP). Azure Front Door WAF deferred to post-MVP.** |

---

## Spec Dependency Order

Implementation MUST follow this dependency order:

```
01-product  ──►  02-architecture  ──►  03-data-model  ──►  04-api (openapi)
                                   └──►  05-llm-workflow
                                   └──►  06-security
```

---

## How to Propose a Spec Change

1. Edit the spec document and change status to **Review**
2. If the change is an architectural decision, write an ADR in `docs/adr/`
3. Resolve any open decisions created by the change
4. Update this table with the new status and date once approved

## How to Propose a New Spec

1. Create a new file following the numbering convention
2. Add it to this table with status **Draft**
3. Follow the process above to move it to **Approved**
