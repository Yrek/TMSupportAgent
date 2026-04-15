# ADR-002: Azure Region Strategy (Sweden Central Primary, West Europe Fallback)

**Status:** Accepted  
**Date:** 2026-04-15  
**Deciders:** Project lead, Infra

---

## Context

The platform must keep EU data residency while staying within subscription/plan limits and regional service availability constraints.

We want most resources in `swedencentral` because:
1. It aligns with current Azure plan/credit constraints
2. It keeps primary compute and data close to the intended operating region
3. It reduces operational drift versus current deployment automation defaults

However, not all Azure services support `swedencentral` for every resource type. In this solution, Azure Static Web Apps management metadata is one such case.

---

## Decision

Adopt a hybrid EU-region strategy:

1. Use `swedencentral` as the **primary** region for compute, data, messaging, and core platform resources.
2. Use `westeurope` only for services that do not support `swedencentral` (currently Static Web Apps management location via `swaLocation`).
3. Keep this split explicit in IaC (`location` vs `swaLocation`) and deployment documentation.
4. Re-evaluate periodically; if service support changes, prefer converging remaining fallback resources to `swedencentral`.

---

## Alternatives Considered

### Single-region West Europe for everything

**Rejected.** Technically simpler, but conflicts with plan/credit constraints and our preferred regional placement strategy.

### Single-region Sweden Central for everything

**Rejected.** Not currently feasible because some required services do not support `swedencentral`.

### Sweden Central primary with North Europe fallback

**Not selected.** Possible for some services, but `westeurope` is currently the configured and documented fallback path in this repo.

---

## Consequences

### Positive
- Most resources stay in `swedencentral` as intended by cost/plan constraints
- Keeps full EU-region posture
- Makes unsupported-service exceptions explicit and controlled

### Negative / Trade-offs
- Multi-region operations complexity (monitoring, inventory, documentation)
- Need to keep docs and runbooks clear about which resources are exceptions
- Potential confusion if teams assume all resources are in one region

### Operational guardrails
- Keep `location=swedencentral` and `swaLocation=westeurope` defaults in IaC unless a conscious ADR updates this
- Document every fallback resource explicitly in deployment docs
- Review Azure regional support at least quarterly

---

## References

- [infra/main.bicep](../../infra/main.bicep)
- [infra/parameters/production.bicepparam.example](../../infra/parameters/production.bicepparam.example)
- [docs/deployment/azure.md](../deployment/azure.md)
