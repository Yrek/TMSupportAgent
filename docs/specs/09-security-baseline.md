# Security Baseline (CLAUDE.md Enforcement)

**Status:** Approved  
**Spec refs:** [../../CLAUDE.md](../../CLAUDE.md), [06-security.md](06-security.md)  
**Version:** 0.1  
**Date:** 2026-04-15

---

## 1. Purpose

This specification makes `CLAUDE.md` the canonical security baseline for implementation work in this repository.

`06-security.md` defines system-level security posture (ISO/GDPR/go-live controls).  
`CLAUDE.md` defines implementation-time mandatory coding controls.  
When they overlap, the stricter requirement applies.

---

## 2. Normative Rule

All implementation work **MUST** satisfy the mandatory controls in [CLAUDE.md](../../CLAUDE.md), including (non-exhaustive):

1. Fail-secure behavior for authn/authz/validation/config failures
2. Server-side authorization on every request; deny-by-default
3. Tenant isolation and BOLA prevention with org-scoped enforcement
4. Allow-list input validation and domain invariants
5. Safe output/error handling with minimal disclosure
6. No secrets/tokens/PII in logs; auditability for privileged actions
7. Managed secrets/config (no hardcoded credentials)
8. Rate limiting, request size limits, and resource caps
9. Secure file upload handling
10. LLM/MCP controls: untrusted output handling, no secret leakage in prompts, deterministic validation

---

## 3. Implementation Checklist

Before merging security-relevant changes:

1. Verify affected code paths against `CLAUDE.md` mandatory sections
2. Verify endpoint-level authz + tenant scope checks
3. Verify invariant enforcement in domain/service layer (not UI-only)
4. Verify authenticated responses remain `Cache-Control: no-store` unless explicitly approved
5. Verify security tests exist/updated for changed behavior

---

## 4. Current Explicit TODOs

The following items are tracked as open security-alignment TODOs (not silently ignored):

1. Queue purge on destructive delete:
`docs/specs/07-backlog.md` GAP-SEC1
2. CSP hardening for production:
`docs/specs/07-backlog.md` GAP-SEC2
3. Per-job aggregate token accounting persistence:
`docs/specs/07-backlog.md` GAP-SEC3

---

## 5. References

- [CLAUDE.md](../../CLAUDE.md)
- [06-security.md](06-security.md)
- [07-backlog.md](07-backlog.md)
