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

**Worker** (`src/ThreatModelingAgent.Worker/appsettings.Development.json`):
- `AzureServiceBus:ConnectionString` — from the Service Bus emulator (see step 3)
- `Anthropic:ApiKey` or `AzureOpenAI:Endpoint` — at least one LLM provider

> These files are git-ignored (CLAUDE.md §10.1). Never commit them.

---

## 2. Start local services

```bash
docker compose up -d
```

This starts:

| Container | Purpose | Port |
|---|---|---|
| `tma-postgres` | PostgreSQL 16 | `localhost:5432` |
| `tma-azurite` | Azure Blob Storage emulator | `localhost:10000` |
| `tma-servicebus` | Azure Service Bus emulator | `localhost:5672` (AMQP), `localhost:8080` (UI) |
| `tma-sqledge` | SQL Edge (required by Service Bus emulator) | — |

Wait for all containers to be healthy:

```bash
docker compose ps
```

### Service Bus emulator connection string

After the emulator starts, get the connection string from the management UI at http://localhost:8080, or use:

```
Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=<key-from-ui>
```

Set this as `AzureServiceBus:ConnectionString` in the Worker's `appsettings.Development.json`.

### Azurite connection string

Azurite uses a fixed well-known development connection string. The API's example config already sets `AzureStorage:AccountName=devstoreaccount1` and `UseDevelopmentStorage=true`. No further configuration is needed for blob storage.

---

## 3. Run database migrations

```bash
dotnet ef database update \
  --project src/ThreatModelingAgent.Infrastructure \
  --startup-project src/ThreatModelingAgent.Api
```

This applies all migrations including the Row-Level Security policies (spec §7.2).

To verify:

```bash
docker exec -it tma-postgres psql -U postgres -d threatmodeling_dev -c "\dt"
```

---

## 4. Run the API

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

## 5. Run the Worker

In a second terminal:

```bash
dotnet run --project src/ThreatModelingAgent.Worker
```

The Worker connects to Service Bus and polls for `analysis-jobs` messages. It logs to the console at `Debug` level in development.

---

## 6. Run tests

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

## Resetting local state

```bash
# Stop and remove all containers and volumes (wipes DB and blobs)
docker compose down -v

# Then start fresh
docker compose up -d
dotnet ef database update \
  --project src/ThreatModelingAgent.Infrastructure \
  --startup-project src/ThreatModelingAgent.Api
```
