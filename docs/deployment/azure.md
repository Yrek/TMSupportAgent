# Azure Deployment

Deploys the Threat Modeling Agent with primary workloads in Sweden Central (EU data residency — spec §3). Services that are unavailable in Sweden Central (for example SWA management) are placed in West Europe.

---

## Architecture

```
Internet (HTTPS)
    │
    ▼
Container Apps Environment (Sweden Central)
    ├── api            (public ingress, always-on)
    └── worker         (no ingress, scales to zero, KEDA queue trigger)
        │
        ├── Azure PostgreSQL Flexible Server (B2s)
        ├── Azure Blob Storage (LRS, Hot)
        ├── Azure Service Bus Standard (analysis-jobs queue)
        ├── Azure Key Vault (secrets)
        └── Azure Application Insights
```

All service-to-Azure communication uses **managed identity** — no connection strings or API keys in environment variables.

Estimated cost: ~€100–140/month at near-zero usage (spec §5.1).

---

## Prerequisites

| Tool | Install |
|---|---|
| Azure CLI 2.57+ | [learn.microsoft.com](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) |
| Bicep CLI (bundled with az) | `az bicep install` |
| Docker | [docker.com](https://www.docker.com/products/docker-desktop) |
| .NET SDK 10.0+ | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) |
| EF Core CLI | `dotnet tool install --global dotnet-ef` |

You also need:
- An Azure subscription with Contributor access
- A WorkOS account **or** an Azure App Registration (Entra ID mode — see [Entra ID auth mode](#entra-id-auth-mode))
- An Anthropic API key
- An Azure OpenAI resource pre-provisioned in a supported EU region (`swedencentral` preferred; use `westeurope` if required by quota/availability) with `gpt-4o` and `gpt-4o-mini` deployments

---

## First-time setup

### 1. Log in to Azure

```bash
az login
az account set --subscription YOUR_SUBSCRIPTION_ID
```

### 2. Pre-provision Azure OpenAI

Azure OpenAI cannot be provisioned via standard Bicep — it requires quota approval.

1. Go to the Azure portal → Create a resource → Azure OpenAI
2. Choose **Sweden Central** when available in your subscription/quota; otherwise use **West Europe**
3. Deploy `gpt-4o` and `gpt-4o-mini` in that resource
4. Note the resource name — you will need it as `azureOpenAiResourceName`

### 3. Create the parameter file

```bash
cp infra/parameters/production.bicepparam.example \
   infra/parameters/production.bicepparam
```

Edit `infra/parameters/production.bicepparam` with your values. This file is git-ignored.

### 4. Deploy infrastructure

```bash
az deployment sub create \
  --location swedencentral \
  --template-file infra/main.bicep \
  --parameters infra/parameters/production.bicepparam \
  --name tma-prod-$(date +%Y%m%d)
```

This creates a resource group `tma-prod-rg` and all resources inside it. The deployment is **idempotent** — safe to re-run.

`infra/main.bicep` keeps primary resources in `swedencentral` (`location`) and places Static Web Apps management in `westeurope` (`swaLocation`), because SWA does not currently support `swedencentral`.

Output values (save these):
- `apiUrl` — public HTTPS URL for the API
- `registryLoginServer` — ACR login server for pushing images
- `keyVaultName` — Key Vault name

### 5. Build and push container images

```bash
# Log in to the registry
az acr login --name $(az deployment sub show \
  --name tma-prod-latest \
  --query properties.outputs.registryLoginServer.value -o tsv | cut -d. -f1)

# Build and push
ACR=$(az deployment sub show --name tma-prod-latest \
  --query properties.outputs.registryLoginServer.value -o tsv)

docker build -t $ACR/api:latest -f src/ThreatModelingAgent.Api/Dockerfile .
docker push $ACR/api:latest

docker build -t $ACR/worker:latest -f src/ThreatModelingAgent.Worker/Dockerfile .
docker push $ACR/worker:latest
```

### 6. Grant ACR pull access to Container Apps

```bash
RG=tma-prod-rg
ACR_ID=$(az acr show --name <acr-name> --resource-group $RG --query id -o tsv)
API_ID=$(az containerapp show --name tma-prod-api --resource-group $RG \
  --query identity.principalId -o tsv)
WORKER_ID=$(az containerapp show --name tma-prod-worker --resource-group $RG \
  --query identity.principalId -o tsv)

# AcrPull role for both identities
az role assignment create --assignee $API_ID \
  --role AcrPull --scope $ACR_ID
az role assignment create --assignee $WORKER_ID \
  --role AcrPull --scope $ACR_ID
```

### 7. Run database migrations

```bash
# Get the PostgreSQL connection string
PG_HOST=$(az deployment sub show --name tma-prod-latest \
  --query "properties.outputs" -o json | jq -r '.pgFqdn.value // empty')

# If not in outputs, find it:
# az postgres flexible-server show --name tma-prod-pg --resource-group tma-prod-rg \
#   --query fullyQualifiedDomainName -o tsv

dotnet ef database update \
  --project src/ThreatModelingAgent.Infrastructure \
  --startup-project src/ThreatModelingAgent.Api
```

Set the connection string via the environment or User Secrets before running:

```bash
export ConnectionStrings__DefaultConnection="Host=$PG_HOST;Database=threatmodeling;Username=pgadmin;Password=YOUR_PG_PASSWORD;SSL Mode=Require"
dotnet ef database update \
  --project src/ThreatModelingAgent.Infrastructure \
  --startup-project src/ThreatModelingAgent.Api
```

> For production, create a dedicated migration user rather than using the admin account.

### 8. Update Container Apps with the image

```bash
az containerapp update \
  --name tma-prod-api \
  --resource-group tma-prod-rg \
  --image $ACR/api:latest

az containerapp update \
  --name tma-prod-worker \
  --resource-group tma-prod-rg \
  --image $ACR/worker:latest
```

---

## Subsequent deployments

For day-to-day deployments, use the GitHub Actions [deploy workflow](../../.github/workflows/deploy.yml), which handles build, push, migration, and Container App update in sequence.

Frontend deployment is handled by [frontend-ci workflow](../../.github/workflows/frontend-ci.yml):
- Push to `main` deploys frontend to `staging` SWA (`tma-staging-swa`)
- Manual dispatch can deploy to either `staging` or `prod`

For a manual deploy:

```bash
TAG=sha-$(git rev-parse --short HEAD)

docker build -t $ACR/api:$TAG -f src/ThreatModelingAgent.Api/Dockerfile .
docker build -t $ACR/worker:$TAG -f src/ThreatModelingAgent.Worker/Dockerfile .
docker push $ACR/api:$TAG
docker push $ACR/worker:$TAG

az containerapp update --name tma-prod-api --resource-group tma-prod-rg --image $ACR/api:$TAG
az containerapp update --name tma-prod-worker --resource-group tma-prod-rg --image $ACR/worker:$TAG
```

---

## GitHub Actions setup

The [deploy workflow](../../.github/workflows/deploy.yml) uses OIDC federation — no stored Azure credentials.

### Required GitHub secrets (per environment)

| Secret | Value |
|---|---|
| `AZURE_CLIENT_ID` | App registration client ID (federated identity) |
| `AZURE_TENANT_ID` | Azure AD tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID |
| `WORKOS_CLIENT_ID` | WorkOS application client ID |
| `WORKOS_API_KEY` | WorkOS API key (for invitations and user deletion) |
| `ANTHROPIC_API_KEY` | Anthropic API key |
| `PG_ADMIN_LOGIN` | PostgreSQL admin login |
| `PG_ADMIN_PASSWORD` | PostgreSQL admin password |
| `DATABASE_CONNECTION_STRING` | Full connection string for migration job |
| `SENTRY_AUTH_TOKEN` | Optional: Sentry source-map upload token (frontend build) |

### Required GitHub variables (per environment)

| Variable | Value |
|---|---|
| `ACR_NAME` | ACR name (without `.azurecr.io`) |
| `ACR_LOGIN_SERVER` | Full ACR login server (e.g. `tmaprodc.azurecr.io`) |
| `AZURE_OPENAI_RESOURCE_NAME` | Azure OpenAI resource name |
| `VITE_API_BASE_URL` | Frontend API base URL (for Vite build, e.g. `https://api-host/v1`) |
| `VITE_WORKOS_CLIENT_ID` | WorkOS client id used by frontend AuthKit |
| `VITE_WORKOS_REDIRECT_URI` | Frontend callback URL (`https://<swa-host>/auth/callback`) |
| `VITE_SENTRY_DSN` | Optional frontend Sentry DSN |
| `VITE_APPINSIGHTS_CONNECTION_STRING` | Optional frontend App Insights connection string |
| `SENTRY_ORG` | Optional Sentry org slug (if source maps enabled) |
| `SENTRY_PROJECT` | Optional Sentry project slug (if source maps enabled) |

### Setting up OIDC federation

```bash
# Create an app registration
az ad app create --display-name tma-github-actions
APP_ID=$(az ad app list --display-name tma-github-actions --query '[0].appId' -o tsv)
az ad sp create --id $APP_ID

# Add federated credential for main branch
az ad app federated-credential create --id $APP_ID --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:YOUR_ORG/YOUR_REPO:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'

# Assign Contributor at subscription scope (narrow to RG post-MVP)
az role assignment create \
  --assignee $APP_ID \
  --role Contributor \
  --scope /subscriptions/YOUR_SUBSCRIPTION_ID
```

---

## Verifying the deployment

```bash
# Check API is responding (expect 401 — auth required, not 502/504)
API_URL=$(az containerapp show --name tma-prod-api --resource-group tma-prod-rg \
  --query properties.configuration.ingress.fqdn -o tsv)

curl -i https://$API_URL/orgs

# Check Container App logs
az containerapp logs show --name tma-prod-api --resource-group tma-prod-rg --tail 50
az containerapp logs show --name tma-prod-worker --resource-group tma-prod-rg --tail 50
```

---

## Bootstrap first platform admin (manual DB + WorkOS)

Use this once to create your first platform admin so they can access `/v1/admin/*`.

Important:
- Platform-admin authorization is based on JWT role claim `platform:admin`.
- A DB insert alone is not enough; the user must also be assigned `platform:admin` in WorkOS.

Steps:

1. Sign in once with the target account through the deployed frontend.
2. Get the WorkOS user id (`sub`, usually like `user_01...`) from the access token.
3. In WorkOS dashboard (same environment as your `WORKOS_CLIENT_ID`), assign role `platform:admin` to that user.
4. Insert (or upsert) the user directly in PostgreSQL:

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

Use any valid UUID for `id` (the value above is only a placeholder).

5. Verify with that user token:

```bash
curl -H "Authorization: Bearer <token>" https://$API_URL/v1/auth/session
curl -H "Authorization: Bearer <token>" https://$API_URL/v1/admin/stats
```

Expected:
- `/v1/auth/session` contains `"isPlatformAdmin": true`
- `/v1/admin/stats` returns `200 OK`

Notes:
- Platform admins are intentionally rejected on org-scoped routes.
- If this user should also work inside a specific org, add an `org_memberships` row separately (`owner` or `member`):

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

## Entra ID auth mode

If your organisation uses Azure AD / Microsoft 365 and you want users to sign in with their Microsoft accounts instead of WorkOS, deploy in Entra ID mode.

### App Registration

Follow the [local setup guide](local.md#entra-id-auth-mode) steps 1–2 to create the App Registration and org row. For production, add the production redirect URI to the App Registration:

- Under **Authentication → Redirect URIs (SPA)**: add `https://<your-frontend-domain>/auth/callback`

### GitHub Actions secrets (Entra ID mode)

Replace the `WORKOS_*` secrets with:

| Secret | Value |
|---|---|
| `ENTRA_TENANT_ID` | Directory (tenant) ID from the App Registration |
| `ENTRA_CLIENT_ID` | Application (client) ID from the App Registration |
| `ENTRA_DEFAULT_ORG_ID` | UUID of the org all users are provisioned into |
| `ENTRA_ADMIN_OIDS` | Comma-separated Entra Object IDs that receive Owner role |

### GitHub Actions variables (Entra ID mode)

Replace the `VITE_WORKOS_*` variables with:

| Variable | Value |
|---|---|
| `VITE_AUTH_MODE` | `entra` |
| `VITE_ENTRA_TENANT_ID` | Directory (tenant) ID |
| `VITE_ENTRA_CLIENT_ID` | Application (client) ID |

Remove `VITE_WORKOS_CLIENT_ID` and `VITE_WORKOS_REDIRECT_URI` — they are not used in Entra mode.

### Container App environment variables (Entra ID mode)

Set these on the API Container App (via Key Vault references or directly):

```
EntraId__Enabled=true
EntraId__TenantId=<tenant-id>
EntraId__ClientId=<client-id>
EntraId__DefaultOrgId=<org-uuid>
EntraId__AdminOids=<oid1>,<oid2>
DevAuth__Enabled=false
```

The WorkOS variables (`WorkOS__ClientId`, `WorkOS__ApiKey`) are not required and can be left empty or omitted.

---

## Security post-MVP hardening

These steps are deferred from day-one MVP but **MUST** be done before GA:

- [ ] Enable private endpoints for PostgreSQL, Service Bus, Key Vault, and Blob Storage
- [ ] Restrict Key Vault and Storage network ACLs to the Container Apps VNet only
- [ ] Enable Azure PIM for operator access (spec §6.4)
- [ ] Enable geo-redundant backup on PostgreSQL
- [ ] Configure Azure Front Door WAF (spec OD-6)
- [ ] Set HSTS `max-age` to full value (63072000) after domain is stable
- [ ] Enable Azure Defender for Containers and PostgreSQL

---

## Teardown

```bash
# Delete the entire resource group (irreversible — deletes all data)
az group delete --name tma-prod-rg --yes
```
