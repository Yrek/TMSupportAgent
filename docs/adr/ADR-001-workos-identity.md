# ADR-001: Identity Provider — WorkOS

**Status:** Accepted  
**Date:** 2026-03-31  
**Deciders:** Project lead  

---

## Context

The Threat Modeling Agent is a multi-tenant SaaS platform. It requires:

1. **Consumer authentication** — social login (Google, Microsoft, GitHub) and email/password with MFA for individual users
2. **Per-organisation enterprise IDP federation** — each onboarded organisation must be able to connect their own identity provider (Microsoft Entra ID, Google Workspace, Okta, or any standards-compliant OIDC/SAML provider)
3. **EU data residency** — identity data must be stored in an EU-region service
4. **Cost efficiency** — the identity layer must not become a dominant cost item before the product reaches revenue

The original architecture proposed **Azure Entra External ID** (formerly Azure AD B2C). This was rejected because:
- Azure AD B2C is on a deprecation path; its migration successor (Entra External ID CIAM) is a relatively new product with an uncertain feature roadmap
- Operational risk of building on a product mid-transition was deemed unacceptable

---

## Decision

Use **WorkOS** as the identity platform.

WorkOS is selected over alternatives because:
- It is purpose-built for the per-org enterprise IDP federation pattern (their primary product)
- Free tier covers up to **1 million MAU**, including SSO connections — eliminates identity cost through the entire MVP and early growth phase
- Supports all required social providers (Google, Microsoft, GitHub) and enterprise connections (Entra ID, Google Workspace, Okta, SAML, generic OIDC)
- EU region available
- The WorkOS `Organizations` model maps directly to our multi-tenant org model
- Actively maintained with enterprise customers; not on a deprecation path

---

## Alternatives Considered

### Auth0 (Okta)

**Rejected.** Auth0 is well-proven for this pattern and has EU region support. However:
- Enterprise SSO connections (required for per-org IDP) are on the Auth0 Enterprise plan: typically €800–1,500+/month
- This cost is prohibitive before the product has paying customers
- Okta suffered a significant security breach in 2023; the security posture of the supplier was flagged as a concern

### Azure Entra External ID (CIAM successor to B2C)

**Rejected.** See context above. Azure AD B2C is being deprecated; Entra External ID CIAM is the replacement but is new and still maturing. Building on it introduces product lifecycle risk.

### Keycloak (self-hosted on Azure Container Apps)

**Deferred, not fully rejected.** Keycloak on ACA would cost ~€15–25/month compute. It supports all required features. However:
- Operational overhead (upgrades, HA, realm management) is significant for a small team at MVP stage
- Should be reconsidered if WorkOS introduces unacceptable pricing changes at scale

### Zitadel (self-hosted or cloud)

**Not selected for now.** Zitadel is a strong open-source option with built-in multi-tenant org support. The managed cloud option has EU region. However:
- Smaller community and ecosystem than WorkOS or Auth0
- WorkOS is more directly aligned to the enterprise SSO-per-org use case
- Can be revisited if WorkOS costs become a concern at scale

---

## Consequences

### Positive
- No identity infrastructure cost at MVP; WorkOS free tier is generous
- Per-org IDP federation is a first-class feature, not a workaround
- Reduces implementation complexity — WorkOS handles SAML/OIDC federation per org with a clean API

### Negative / Trade-offs
- **Vendor dependency**: WorkOS is a third-party SaaS. If WorkOS changes pricing, is acquired, or has an outage, we are impacted
- **Supplier risk**: WorkOS is a smaller vendor than Microsoft or Okta; supplier continuity should be assessed before GA
- **EU data processing**: A Data Processing Agreement (DPA) with WorkOS MUST be in place before any personal data is processed. WorkOS publishes a standard DPA; this must be executed before go-live
- **Egress path**: If we need to migrate off WorkOS, user identity data can be exported; the migration path should be documented before GA

### Required before go-live
- [ ] Execute WorkOS DPA (EU data processing)
- [ ] Confirm WorkOS EU region is used for all identity data
- [ ] Document migration egress path in ops runbook

---

## References

- [WorkOS Docs: Organizations and SSO](https://workos.com/docs)
- [docs/specs/02-architecture.md §6 — Identity and Access Architecture](../specs/02-architecture.md)
