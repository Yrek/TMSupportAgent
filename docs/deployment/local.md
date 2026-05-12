# Local Development Setup

This guide gets the API and Worker running on your machine.

---

## Auth modes

There are three ways to authenticate:

| Mode | When to use | What's needed |
|---|---|---|
| **Dev auth** (recommended for local dev) | Running locally without a WorkOS or Entra account | Nothing — self-contained HMAC JWT |
| **WorkOS** | Testing real auth flows, invitations, or multi-tenant SaaS features | A WorkOS account and app |
| **Entra ID** | Self-hosting with an existing Azure AD / Microsoft 365 tenant | Azure App Registration |

The default `appsettings.Development.json` and `frontend/.env.local` ship with dev auth enabled.
See [Dev auth mode](#dev-auth-mode-no-workos-required), [WorkOS auth mode](#workos-auth-mode), or [Entra ID auth mode](#entra-id-auth-mode) for details.

---

## Prerequisites

| Tool | Version | Install |
|---|---|---|
| .NET SDK | 10.0+ | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) |
| Docker Desktop | Latest | [docker.com](https://www.docker.com/products/docker-desktop) |
| EF Core CLI | Latest | `dotnet tool install --global dotnet-ef` |

You will also need:
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
- `DevAuth:Enabled` — `true` by default; skip WorkOS entirely (see [dev auth mode](#dev-auth-mode-no-workos-required))
- `DevAuth:SigningKey` — at least 32 characters; pre-filled with a placeholder
- `AzureServiceBus:ConnectionString` — required because API enqueues jobs on submit
- `AzureServiceBus:QueueName` — queue to enqueue (`analysis-jobs` for local)

**Worker** (`src/ThreatModelingAgent.Worker/appsettings.Development.json`):
- `AzureServiceBus:ConnectionString` — from the Service Bus emulator (see step 3)
- `AzureServiceBus:QueueName` — optional in local emulator mode; defaults to `analysis-jobs` if omitted
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

## Dev auth mode (no WorkOS required)

Dev auth lets you run the full stack locally without a WorkOS account. It replaces the WorkOS JWT with a locally-signed HMAC JWT issued by `POST /v1/auth/dev-login`.

### How it works

1. The frontend shows a simple email form instead of the WorkOS sign-in button.
2. Submitting the form calls `POST /v1/auth/dev-login` with `{ "email": "..." }`.
3. The API finds or creates a local user + org in the database, then returns a signed JWT.
4. The JWT carries the user's internal UUID as `sub` and the org UUID as `org_id`.
5. `TenantContextMiddleware` detects these as GUIDs and skips the WorkOS lookup — everything else (authorization, tenant context, membership checks) works identically.

### Configuration

**API** (`appsettings.Development.json` — already pre-configured):
```json
"DevAuth": {
  "Enabled": true,
  "SigningKey": "dev-local-signing-key-change-me-32chars!!"
}
```

Change `SigningKey` to any string of 32+ characters. It only needs to be consistent between restarts.

**Frontend** (`.env.local` — already pre-configured):
```
VITE_DEV_AUTH=true
```

When `VITE_DEV_AUTH=true`, `VITE_WORKOS_CLIENT_ID` and `VITE_WORKOS_REDIRECT_URI` are not required.

### First sign-in

1. Start the API and run migrations (steps 3–5 below).
2. Open `http://localhost:5173` → you are redirected to `/login`.
3. Enter any email address (e.g. `admin@example.com`) and click **Sign in**.
4. The API creates the user and a `dev-org` organization automatically.
5. You are redirected to the app.

A new email creates a new user. The same email always maps to the same user across restarts.

### Safety

- The API refuses to start if `DevAuth:Enabled=true` **and** `ASPNETCORE_ENVIRONMENT=Production`.
- Dev JWTs are signed with a local HMAC key and are valid for 8 hours.
- The `POST /v1/auth/dev-login` endpoint returns 404 when DevAuth is disabled.

---

## Entra ID auth mode

Entra ID lets you self-host the app using your organisation's Azure Active Directory (Microsoft 365) tenant. All users sign in with their existing Microsoft accounts — no WorkOS account is needed.

### 1. Create an Azure App Registration

1. Go to [Azure Portal](https://portal.azure.com) → **Azure Active Directory** → **App registrations** → **New registration**
2. **Name**: e.g. `Threat Modeling Agent`
3. **Supported account types**: *Accounts in this organizational directory only* (single tenant)
4. **Redirect URI**: Platform = **Single-page application (SPA)** → `http://localhost:5173/auth/callback`
5. Click **Register** and copy:
   - **Application (client) ID** → your `ClientId` / `VITE_ENTRA_CLIENT_ID`
   - **Directory (tenant) ID** → your `TenantId` / `VITE_ENTRA_TENANT_ID`

6. Under **Expose an API**:
   - Set **Application ID URI** to `api://<your-client-id>` (click *Set* and accept the default)
   - Click **Add a scope** → name it `access_as_user`, consent: Admins and users, fill display names, save

7. Under **Authentication**, confirm the redirect URI is listed as a SPA redirect. No implicit grant tokens needed.

### 2. Create the organisation in the database

Before the first sign-in, create an org row and note its UUID:

```sql
INSERT INTO organizations (id, name, slug, is_suspended, created_at, updated_at)
VALUES (
  gen_random_uuid(),   -- or supply a fixed UUID; note it down for DefaultOrgId
  'My Company',
  'my-company',
  false,
  NOW(),
  NOW()
);

-- Find the UUID you just inserted:
SELECT id FROM organizations WHERE slug = 'my-company';
```

### 3. Configure the API

In `appsettings.Development.json`, disable DevAuth and enable Entra ID:

```json
"DevAuth": {
  "Enabled": false
},
"EntraId": {
  "Enabled": true,
  "TenantId": "<directory-tenant-id>",
  "ClientId": "<application-client-id>",
  "DefaultOrgId": "<org-uuid-from-step-2>",
  "AdminOids": "<oid-of-first-admin>"
}
```

**`DefaultOrgId`** — the UUID of the org all Entra users are provisioned into (self-hosted single-org mode).

**`AdminOids`** — comma-separated list of Entra Object IDs that receive `Owner` role on first sign-in. Find a user's Object ID in Azure Portal → Users → select the user → Object ID. Leave empty to provision everyone as `Member`.

### 4. Configure the frontend

In `.env.local`:

```
VITE_AUTH_MODE=entra
VITE_ENTRA_TENANT_ID=<directory-tenant-id>
VITE_ENTRA_CLIENT_ID=<application-client-id>
VITE_API_BASE_URL=http://localhost:5240/v1
```

`VITE_DEV_AUTH`, `VITE_WORKOS_CLIENT_ID`, and `VITE_WORKOS_REDIRECT_URI` are not required.

### 5. First sign-in

1. Start the API and run migrations (steps 3–5 in the main guide).
2. Open `http://localhost:5173` → you are redirected to `/login`.
3. Click **Sign in with Microsoft** → authenticate with your Azure AD account.
4. On first sign-in, the API creates a user and membership automatically (JIT provisioning).
5. You are redirected to the app.

Users listed in `AdminOids` receive `Owner` role. All other users receive `Member`.

### Troubleshooting Entra ID

**`AADSTS50011: The redirect URI ... was not expected`**
The redirect URI in the App Registration must exactly match `http://localhost:5173/auth/callback` (for local) including the `/auth/callback` path. Check under Authentication → Redirect URIs; the type must be **SPA**, not Web.

**`AADSTS65001: The user or administrator has not consented to use the application`**
Open the app once as an admin and grant tenant-wide admin consent, or grant consent for the `access_as_user` scope under API permissions in the App Registration.

**API returns `403 MISSING_ORG_CONTEXT: No organization is configured for this Entra tenant`**
`DefaultOrgId` is missing or the UUID does not match an existing org. Verify the org row exists and the UUID matches exactly.

**`Entra ID token is missing the required 'oid' claim`**
The token is not a user token (e.g., a client credentials machine-to-machine token). Entra ID auth mode only supports interactive user sign-in.

---

## WorkOS auth mode

To use real WorkOS auth instead, set `DevAuth:Enabled=false` in the API config and `VITE_DEV_AUTH=false` (or remove the line) in `.env.local`.

Additional required values for WorkOS mode:

**API** (`appsettings.Development.json`):
- `WorkOS:ClientId` — from WorkOS dashboard → API Keys
- `WorkOS:ApiKey` — from WorkOS dashboard → API Keys (required for user invitations and erasure)

**Frontend** (`.env.local`):
- `VITE_WORKOS_CLIENT_ID=<client_...>`
- `VITE_WORKOS_REDIRECT_URI=http://localhost:5173/auth/callback`

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

Azurite uses a fixed well-known development connection string. Prefer:

```text
UseDevelopmentStorage=true
```

The API/Worker now normalize localhost Azurite configs to this canonical value to avoid emulator signature mismatches.

If you see an error like `The API version ... is not supported by Azurite`, recreate the Azurite container so it starts with `--skipApiVersionCheck`:

```bash
docker compose up -d --force-recreate azurite
```

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
curl -H "Authorization: Bearer <token>" https://localhost:7036/v1/orgs
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

### WorkOS local AuthKit setup (required in WorkOS mode only)

Skip this section when using dev auth (`VITE_DEV_AUTH=true`).

In the WorkOS dashboard, open the AuthKit application that matches your `VITE_WORKOS_CLIENT_ID` and configure:

- Redirect URL: `http://localhost:5173/auth/callback`
- Allowed origin / CORS origin: `http://localhost:5173`
- Logout / return URL: `http://localhost:5173`

Important:
- Ensure this is the same WorkOS environment/project as your `client_...` value (not a different tenant or environment).

If these values are missing or set on the wrong app/environment, login can fail with:
- CORS errors against `https://api.workos.com/user_management/authenticate`
- Frontend stuck on `/auth/callback` with "Completing sign-in…"

### Bootstrap the first platform admin (manual DB + WorkOS)

For the very first platform admin, you can bootstrap manually.

Important:
- This API authorizes platform admin from the JWT role claim (`platform:admin`).
- Adding a user in Postgres alone is not enough; the user must also have `platform:admin` in WorkOS.

Steps:

1. Sign in once with the target account via the frontend (`http://localhost:5173`).
2. Get the WorkOS user id (`sub`) from the access token (example: `user_01...`).
3. In WorkOS dashboard, assign role `platform:admin` to that user in the same environment as your `client_...`.
4. Insert (or upsert) the user row in local Postgres:

```sql
INSERT INTO users (
  id, workos_user_id, email, display_name, created_at, updated_at, deleted_at
)
VALUES (
  '11111111-1111-1111-1111-111111111111',
  'user_01YOUR_WORKOS_USER_ID',
  'admin@example.com',
  'First Platform Admin',
  NOW(),
  NOW(),
  NULL
)
ON CONFLICT (workos_user_id) DO UPDATE SET
  email = EXCLUDED.email,
  display_name = EXCLUDED.display_name,
  updated_at = NOW(),
  deleted_at = NULL;
```

Use any valid UUID for `id` (the example value is just a placeholder).

Run it in local container:

```bash
docker exec -it tma-postgres psql -U postgres -d threatmodeling_dev
```

5. Verify with the user’s token:

```bash
curl -H "Authorization: Bearer <token>" http://localhost:5240/v1/auth/session
curl -H "Authorization: Bearer <token>" http://localhost:5240/v1/admin/stats
```

Expected:
- `/v1/auth/session` includes `"isPlatformAdmin": true`
- `/v1/admin/stats` returns `200 OK`

Notes:
- Platform admins are intentionally rejected on org-scoped routes.
- If you also want this person to operate inside a specific org, add an `org_memberships` row separately (`owner` or `member`):

```sql
-- 0) (Optional) Create an organization manually
--    Use the WorkOS organization id if you already created it in WorkOS (recommended).
INSERT INTO organizations (
  id, name, slug, workos_org_id, is_suspended, suspended_at, created_at, updated_at, deleted_at
)
VALUES (
  '33333333-3333-3333-3333-333333333333',
  'Acme Corp',
  'acme-corp',
  'org_01YOUR_WORKOS_ORG_ID', -- or NULL if not yet created in WorkOS
  false,
  NULL,
  NOW(),
  NOW(),
  NULL
)
ON CONFLICT (slug) WHERE deleted_at IS NULL DO UPDATE SET
  name = EXCLUDED.name,
  workos_org_id = COALESCE(EXCLUDED.workos_org_id, organizations.workos_org_id),
  updated_at = NOW();

-- 1) Find org and user IDs
SELECT id, name, slug FROM organizations WHERE deleted_at IS NULL ORDER BY created_at DESC;
SELECT id, workos_user_id, email FROM users WHERE workos_user_id = 'user_01YOUR_WORKOS_USER_ID';

-- 2) Add membership (or update existing role)
INSERT INTO org_memberships (
  id, org_id, user_id, role, created_at, updated_at
)
VALUES (
  '22222222-2222-2222-2222-222222222222',
  'ORG_UUID_HERE',
  'USER_UUID_HERE',
  'owner',   -- or 'member'
  NOW(),
  NOW()
)
ON CONFLICT (org_id, user_id) DO UPDATE SET
  role = EXCLUDED.role,
  updated_at = NOW();
```

Use any valid UUID for `id` (the value above is only a placeholder).

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
Only happens in WorkOS mode (`DevAuth:Enabled=false`). Either set `WorkOS:ClientId` or set `DevAuth:Enabled=true` to use dev auth instead.

**`DevAuth:SigningKey must be at least 32 characters` at startup**  
The signing key in `DevAuth:SigningKey` is too short. Provide any 32+ character string.

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
In dev auth mode (`VITE_DEV_AUTH=true`), only `VITE_API_BASE_URL` is required. In WorkOS mode, also set `VITE_WORKOS_CLIENT_ID` and `VITE_WORKOS_REDIRECT_URI`.

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
