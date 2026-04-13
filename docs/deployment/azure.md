# Azure Deployment

Deploys the Threat Modeling Agent to Azure Container Apps in West Europe (EU data residency — spec §3).

---

## Architecture

```
Internet (HTTPS)
    │
    ▼
Container Apps Environment (West Europe)
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
- A WorkOS account with an application configured
- An Anthropic API key
- An Azure OpenAI resource pre-provisioned in West Europe with `gpt-4o` and `gpt-4o-mini` deployments

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
2. Choose **West Europe** as the region
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
  --location westeurope \
  --template-file infra/main.bicep \
  --parameters infra/parameters/production.bicepparam \
  --name tma-prod-$(date +%Y%m%d)
```

This creates a resource group `tma-prod-rg` and all resources inside it. The deployment is **idempotent** — safe to re-run.

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

### Required GitHub variables (per environment)

| Variable | Value |
|---|---|
| `ACR_NAME` | ACR name (without `.azurecr.io`) |
| `ACR_LOGIN_SERVER` | Full ACR login server (e.g. `tmaprodc.azurecr.io`) |
| `AZURE_OPENAI_RESOURCE_NAME` | Azure OpenAI resource name |

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
