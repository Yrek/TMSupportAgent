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
| 05 | [05-llm-workflow.md](05-llm-workflow.md) | LLM Workflow Specification | **Approved** | 2026-04-16 | Pipeline stages, model routing, typed contracts |
| 06 | [06-security.md](06-security.md) | Security Specification | **Approved** | 2026-04-16 | ISO 27001 + CLAUDE.md scoped to this system |
| 07 | [07-backlog.md](07-backlog.md) | Implementation Backlog | **Living** | 2026-04-16 | Task-first execution tracking and implementation status |
| 08 | [08-frontend.md](08-frontend.md) | Frontend Specification | **Implemented** | 2026-04-16 | SPA UX, architecture interactions, filtering, and auth flow |
| 09 | [09-security-baseline.md](09-security-baseline.md) | Security Baseline (CLAUDE.md Enforcement) | **Approved** | 2026-04-15 | Implementation baseline; maps code-time controls and tracked TODO gaps |

---

## Open Decisions (blocking Approved status)

Open decisions are tracked inside each spec document and related ADRs. This index is non-authoritative for per-spec decision state.

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

1. Create or update a task entry in [07-backlog.md](07-backlog.md) before implementation starts
2. Edit the spec document and change status to **Review**
3. If the change is an architectural decision, write an ADR in `docs/adr/`
4. Resolve any open decisions created by the change
5. Update this table with the new status and date once approved

## How to Propose a New Spec

1. Create a new file following the numbering convention
2. Add it to this table with status **Draft**
3. Follow the process above to move it to **Approved**
