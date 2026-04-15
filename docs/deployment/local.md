# Local Development Setup

This guide gets the API and Worker running on your machine.

---

## Prerequisites

| Tool | Version | Install |
|---|---|---|
| .NET SDK | 10.0+ | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) |
| Docker Desktop | Latest | [docker.com](https://www.docker.com/products/docker-desktop) |
| EF Core CLI | Latest | `dotnet tool install --global dotnet-ef` |

You will also need:
- A **WorkOS** account and application (free tier) — [workos.com](https://workos.com)
- An **Anthropic API key** or **Azure OpenAI** resource for the LLM pipeline

---

## 1. Clone and configure

```bash
git clone <repo>
cd TMSupportAgent
```

Copy the example settings files and fill in your values:

```bash
cp src/ThreatModelingAgent.Api/appsettings.Development.json.example \
   src/ThreatModelingAgent.Api/appsettings.Development.json

cp src/ThreatModelingAgent.Worker/appsettings.Development.json.example \
   src/ThreatModelingAgent.Worker/appsettings.Development.json
```

Edit each file. Minimum required values:

**API** (`src/ThreatModelingAgent.Api/appsettings.Development.json`):
- `WorkOS:ClientId` — from WorkOS dashboard → API Keys
- `WorkOS:ApiKey` — from WorkOS dashboard → API Keys (required for user invitations and erasure)

**Worker** (`src/ThreatModelingAgent.Worker/appsettings.Development.json`):
- `AzureServiceBus:ConnectionString` — from the Service Bus emulator (see step 3)
- `Anthropic:ApiKey` or `AzureOpenAI:Endpoint` + `AzureOpenAI:ApiKey` — at least one LLM provider

**Anthropic-only setup** (no Azure OpenAI):
Change `LlmRouting` in the Worker config to route to Claude instead of GPT:
```json
"LlmRouting": {
  "StrongModel": "claude-sonnet-4-6",
  "LowCostModel": "claude-haiku-4-5"
}
```

**Azure OpenAI-only setup** (default): set `AzureOpenAI:Endpoint` and `AzureOpenAI:ApiKey`. The defaults (`gpt-4o` / `gpt-4o-mini`) require those deployments to exist in your resource.

> These files are git-ignored (CLAUDE.md §10.1). Never commit them.

---

## 2. Supported architecture formats

The following file formats are accepted when submitting a job:

| Extension | Type | Detection method | Notes |
|---|---|---|---|
| `.png` | Image | Magic bytes `\x89PNG` | Architecture diagram screenshot or export |
| `.jpg` / `.jpeg` | Image | Magic bytes `\xFF\xD8` | Architecture diagram screenshot or export |
| `.gif` | Image | Magic bytes `GIF8` | Architecture diagram screenshot or export |
| `.webp` | Image | Extension fallback | Architecture diagram export |
| `.puml` | PlantUML | `@startuml` marker | Text-based diagram; most reliable input |
| `.txt` | PlantUML / text | Extension fallback | PlantUML or free-text description |
| `.md` | Mermaid | Mermaid keywords | `graph`, `flowchart`, `sequenceDiagram`, etc. |
| `.mmd` | Mermaid | Mermaid keywords | Mermaid-native extension |
| `.drawio` | Draw.io XML | `mxfile`/`mxGraph` root | Export from draw.io / diagrams.net |
| `.xml` | Draw.io XML | `mxfile`/`mxGraph` root | Same as `.drawio` |

**Size limit:** 10 MB per file.

**Recommended format:** `.puml` (PlantUML) or `.drawio` produce the most accurate extractions. Image files require vision-capable models (gpt-4o or claude-sonnet-4-6).

> **Note on images and model routing:** PNG/JPEG/GIF uploads are routed to the strong model with vision enabled. Both `AzureOpenAiClient` and `AnthropicClient` support vision. Make sure your configured `StrongModel` is a vision-capable deployment (`gpt-4o` with vision enabled, or `claude-sonnet-4-6`).

---

## 3. Start local services

```bash
docker compose up -d
```

This starts:

| Container | Purpose | Port |
|---|---|---|
| `tma-postgres` | PostgreSQL 16 | `localhost:5432` |
| `tma-azurite` | Azure Blob Storage emulator | `localhost:10000` |
| `tma-servicebus` | Azure Service Bus emulator | `localhost:5672` (AMQP), `localhost:5300` (management endpoint) |
| `tma-sqledge` | SQL Edge (required by Service Bus emulator) | — |

Wait for all containers to be healthy:

```bash
docker compose ps
```

### Service Bus emulator connection string

The emulator uses a fixed well-known key — no UI visit required. The Worker's `appsettings.Development.json.example` already contains the correct value:

```
Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KbHBXKmv/+Kg==;UseDevelopmentEmulator=true;
```

No further configuration is needed for the Service Bus connection string.

### Azurite connection string

Azurite uses a fixed well-known development connection string. The API's example config already sets `AzureStorage:AccountName=devstoreaccount1` and `UseDevelopmentStorage=true`. No further configuration is needed for blob storage.

---

## 4. Run database migrations

```bash
dotnet ef database update \
  --project src/ThreatModelingAgent.Infrastructure \
  --startup-project src/ThreatModelingAgent.Api \
  --connection "Host=localhost;Port=5432;Database=threatmodeling_dev;Username=postgres;Password=localdev"
```

The `--connection` flag is required because `dotnet ef` runs a design-time host that may not load `appsettings.Development.json` automatically. Passing the connection string explicitly bypasses this.

This applies all migrations including the Row-Level Security policies (spec §7.2).

To verify:

```bash
docker exec -it tma-postgres psql -U postgres -d threatmodeling_dev -c "\dt"
```

---

## 5. Run the API

```bash
dotnet run --project src/ThreatModelingAgent.Api
```

The API starts on:
- HTTP: http://localhost:5240
- HTTPS: https://localhost:7036

OpenAPI docs (development only): https://localhost:7036/openapi/v1.json

To test authentication, you need a valid WorkOS JWT. Use the WorkOS AuthKit or CLI to get a test token, then:

```bash
curl -H "Authorization: Bearer <token>" https://localhost:7036/orgs
```

---

## 6. Run the Worker

In a second terminal:

```bash
dotnet run --project src/ThreatModelingAgent.Worker
```

The Worker connects to Service Bus and polls for `analysis-jobs` messages. It logs to the console at `Debug` level in development.

---

## 7. Run the frontend (dev mode)

In a third terminal:

```bash
cd frontend
npm ci
npm run dev
```

Frontend URL:
- http://localhost:5173

Required frontend env values (copy `frontend/.env.example` to `frontend/.env.local`):
- `VITE_API_BASE_URL=http://localhost:5240/v1`
- `VITE_WORKOS_CLIENT_ID=<client_...>`
- `VITE_WORKOS_REDIRECT_URI=http://localhost:5173/auth/callback`

---

## 8. Local frontend deployment (containerized static build)

This builds the production frontend bundle and serves it with Nginx via Docker Compose.

```bash
# Optional: override build-time frontend env vars
export VITE_API_BASE_URL=http://host.docker.internal:5240/v1
export VITE_WORKOS_CLIENT_ID=client_XXXXXXXXXXXX
export VITE_WORKOS_REDIRECT_URI=http://localhost:4173/auth/callback

# Build + run frontend container only
docker compose --profile frontend up -d --build frontend
```

Frontend deployed URL:
- http://localhost:4173

To stop it:

```bash
docker compose --profile frontend stop frontend
```

---

## 9. Run tests

```bash
dotnet test ThreatModelingAgent.slnx
```

Integration tests that need a database use the `ConnectionStrings__DefaultConnection` environment variable. Set it or let them use the default from `appsettings.Development.json`.

---

## Troubleshooting

**`WorkOS:ClientId is required` at startup**  
The API cannot start without a WorkOS Client ID. Ensure `appsettings.Development.json` has it set.

**`Connection string 'DefaultConnection' is missing`**  
Either Docker is not running or the file is not configured. Run `docker compose ps` to check.

**`pg_isready` fails / can't connect to PostgreSQL**  
Give PostgreSQL a few seconds to initialize, then retry migrations.

**HTTPS certificate not trusted**  
Run `dotnet dev-certs https --trust` once.

**Service Bus emulator not ready**  
The emulator depends on SQL Edge which can take 30–60 seconds to start. Check `docker compose logs tma-sqledge`.

---

**Frontend build fails with missing `VITE_*` values**  
Set `VITE_API_BASE_URL`, `VITE_WORKOS_CLIENT_ID`, and `VITE_WORKOS_REDIRECT_URI` before running `npm run build` or the containerized frontend deployment.

---

## Resetting local state

```bash
# Stop and remove all containers and volumes (wipes DB and blobs)
docker compose down -v

# Then start fresh
docker compose up -d
dotnet ef database update \
  --project src/ThreatModelingAgent.Infrastructure \
  --startup-project src/ThreatModelingAgent.Api \
  --connection "Host=localhost;Port=5432;Database=threatmodeling_dev;Username=postgres;Password=localdev"
```
