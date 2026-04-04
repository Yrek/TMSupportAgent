# Threat Modeling Agent

A SaaS threat modeling assistant for modern web systems. Helps engineering teams produce architecture-grounded, traceable threat models — not generic checklists.

---

## Quick Start

**Prerequisites:** .NET 10 SDK, Docker Desktop, `dotnet-ef` global tool.

```bash
# 1. Start local services (PostgreSQL, Azurite, Service Bus emulator)
docker compose up -d

# 2. Configure local settings
cp src/ThreatModelingAgent.Api/appsettings.Development.json.example \
   src/ThreatModelingAgent.Api/appsettings.Development.json
cp src/ThreatModelingAgent.Worker/appsettings.Development.json.example \
   src/ThreatModelingAgent.Worker/appsettings.Development.json
# Edit both files — set WorkOS:ClientId at minimum

# 3. Apply database migrations
dotnet ef database update \
  --project src/ThreatModelingAgent.Infrastructure \
  --startup-project src/ThreatModelingAgent.Api

# 4. Run
dotnet run --project src/ThreatModelingAgent.Api     # terminal 1 → http://localhost:5240
dotnet run --project src/ThreatModelingAgent.Worker  # terminal 2
```

See **[docs/deployment/local.md](docs/deployment/local.md)** for the full local setup guide.  
See **[docs/deployment/azure.md](docs/deployment/azure.md)** for Azure deployment.

---

## How We Work: Spec-Driven Development

This project follows **spec-driven development (SDD)**. No implementation begins without an approved spec.

### The process

```
1. PROPOSE   Write or update a spec document in docs/specs/
2. REVIEW    Spec is discussed and any open decisions resolved
3. APPROVE   Spec status updated to Approved (recorded in docs/specs/README.md)
4. IMPLEMENT Code is written against the approved spec
5. TEST      Tests validate spec requirements, not just implementation details
6. CLOSE     Spec status updated to Implemented; any deviations noted
```

### Rules

- **No implementation without an approved spec.** Writing code against a Draft spec requires explicit sign-off.
- **Specs are the source of truth.** If code and spec conflict, the spec wins unless an ADR documents a justified deviation.
- **Architectural decisions get ADRs.** Any significant decision about technology, approach, or trade-off is recorded in `docs/adr/`. See [ADR process](docs/adr/README.md).
- **Existing code is not proof of correctness.** See CLAUDE.md §3.3.
- **Security requirements are functional acceptance criteria.** A feature is not done if security controls are missing. See CLAUDE.md §4.1.

---

## Project Structure

```
TMSupportAgent/
├── CLAUDE.md                    # Security specification (mandatory — read this first)
├── README.md                    # This file
├── docker-compose.yml           # Local dev services (PostgreSQL, Azurite, Service Bus)
│
├── docs/
│   ├── specs/
│   │   ├── README.md            # Spec index and status tracker
│   │   ├── 01-product.md        # What the assistant does (functional spec)
│   │   ├── 02-architecture.md   # Azure architecture, services, tenancy, identity
│   │   ├── 03-data-model.md     # Database schema, blob layout, state machines
│   │   ├── 04-api.md            # API design decisions and conventions
│   │   ├── 05-llm-workflow.md   # LLM pipeline stages, model routing, contracts
│   │   └── 06-security.md       # ISO 27001 + CLAUDE.md scoped to this system
│   │
│   ├── adr/
│   │   ├── README.md            # ADR index
│   │   └── ADR-001-*.md         # One file per architectural decision
│   │
│   ├── api/
│   │   └── openapi.yaml         # OpenAPI 3.1 contract (authoritative API spec)
│   │
│   └── deployment/
│       ├── local.md             # Local development setup
│       └── azure.md             # Azure deployment guide
│
├── infra/
│   ├── main.bicep               # Azure infrastructure entry point
│   ├── modules/                 # Bicep modules (one per Azure service)
│   ├── parameters/              # Bicep parameter files (git-ignored; .example committed)
│   └── local/                   # Local dev config (Service Bus emulator)
│
├── .github/workflows/
│   ├── ci.yml                   # Build and test on every PR
│   └── deploy.yml               # Build, push, migrate, deploy on merge to main
│
└── src/                         # Implementation (do not create without approved spec)
    ├── ThreatModelingAgent.Api/
    ├── ThreatModelingAgent.Worker/
    ├── ThreatModelingAgent.Domain/
    └── ThreatModelingAgent.Infrastructure/
```

---

## Spec Status

See [docs/specs/README.md](docs/specs/README.md) for the full status table.

---

## Security

This project is governed by a mandatory security specification in [CLAUDE.md](CLAUDE.md) and a system-scoped security spec in [docs/specs/06-security.md](docs/specs/06-security.md).

All implementation must comply with both documents. ISO 27001:2022 controls are mapped in the security spec.

If you find a conflict between existing code and a MUST in CLAUDE.md, raise it before writing new code — do not silently follow the existing pattern.

---

## Contributing

1. Read CLAUDE.md before writing any code.
2. Check [docs/specs/README.md](docs/specs/README.md) to understand what is specced and approved.
3. If your change requires an architectural decision, write an ADR first.
4. All PRs must reference the spec they implement.

---

## Deployment

| Target | Guide |
|---|---|
| Local development | [docs/deployment/local.md](docs/deployment/local.md) |
| Azure (production/staging) | [docs/deployment/azure.md](docs/deployment/azure.md) |

CI runs on every pull request ([.github/workflows/ci.yml](.github/workflows/ci.yml)).  
Deployment to staging runs automatically on merge to `main` ([.github/workflows/deploy.yml](.github/workflows/deploy.yml)).
