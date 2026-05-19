# Security Policy

## Reporting a Vulnerability

Please **do not** report security vulnerabilities through public GitHub issues. Disclosing a vulnerability publicly before it is fixed puts all users at risk.

Instead, report vulnerabilities by email:

**marcus@marcuspe.se**

Include as much of the following as possible:

- A description of the vulnerability and its potential impact
- The affected component (API, worker pipeline, frontend, authentication, data isolation)
- Steps to reproduce or a proof-of-concept
- Any suggested mitigations you have identified

You will receive an acknowledgement within **5 business days**. If you do not hear back, follow up at the same address.

---

## Scope

The following are in scope:

- Authentication and session handling
- Tenant isolation — cross-tenant data access of any kind
- Authorization bypasses in the API or pipeline
- Prompt injection leading to unauthorized actions or data disclosure
- Secrets exposure (in logs, responses, or prompts)
- MCP tool execution bypasses
- Supply chain or dependency vulnerabilities with a realistic exploit path

The following are **out of scope**:

- Theoretical vulnerabilities with no realistic exploit path
- Findings that require physical access to the host
- Social engineering
- Vulnerabilities in third-party services (Azure, WorkOS, OpenAI) — report those directly to the vendor

---

## Disclosure Policy

Once a report is received:

1. Vulnerability is confirmed or dismissed within **10 business days**
2. A fix is developed and tested
3. A patched release is made available
4. Credit is given to the reporter in the release notes (unless anonymity is preferred)

We ask that you give us reasonable time to fix the issue before any public disclosure.

---

## Preferred Languages

Reports may be submitted in English or Swedish.
