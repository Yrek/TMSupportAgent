// ── Threat Modeling Agent — Azure Infrastructure ──────────────────────────────
// Deploys resources to an EU Azure region for data residency (spec §3).
// Default: Sweden Central — keeps costs within the Sweden MACC/credits plan.
//
// Azure Static Web Apps does not support swedencentral; its management plane
// is placed in westeurope (swaLocation). This is irrelevant to end users —
// SWA is a global CDN and file data never leaves the CDN edge.
//
// Usage:
//   az deployment sub create \
//     --location swedencentral \
//     --template-file infra/main.bicep \
//     --parameters infra/parameters/production.bicepparam
//
// Required parameters: see infra/parameters/production.bicepparam.example

targetScope = 'subscription'

// ── Parameters ────────────────────────────────────────────────────────────────

@description('Short environment name used in resource naming (e.g. prod, staging).')
@allowed(['prod', 'staging', 'dev'])
param environmentName string

@description('Azure region for compute, data, and messaging resources. Must be an EU region for data residency.')
@allowed(['swedencentral', 'westeurope', 'northeurope'])
param location string = 'swedencentral'

@description('Azure region for Azure Static Web Apps management plane. SWA does not support swedencentral; westeurope is the closest supported EU region. The CDN itself is global.')
@allowed(['westeurope', 'northeurope'])
param swaLocation string = 'westeurope'

@description('WorkOS Client ID — used to validate JWT audience in the API.')
@secure()
param workosClientId string

@description('WorkOS API key — used by the API to call WorkOS Management API (user deletion, invitations).')
@secure()
param workosApiKey string

@description('Anthropic API key for the worker LLM client.')
@secure()
param anthropicApiKey string

@description('Name of the Azure OpenAI resource (must exist in the same subscription).')
param azureOpenAiResourceName string

@description('Azure OpenAI deployment name for the strong model (e.g. gpt-4o).')
param azureOpenAiStrongModel string = 'gpt-4o'

@description('Azure OpenAI deployment name for the low-cost model (e.g. gpt-4o-mini).')
param azureOpenAiLowCostModel string = 'gpt-4o-mini'

@description('PostgreSQL administrator login.')
@secure()
param pgAdminLogin string

@description('PostgreSQL administrator password.')
@secure()
param pgAdminPassword string

// ── Naming ────────────────────────────────────────────────────────────────────

var prefix = 'tma-${environmentName}'
var rgName = '${prefix}-rg'
var tags = {
  application: 'ThreatModelingAgent'
  environment: environmentName
  managedBy: 'bicep'
}

// ── Resource Group ────────────────────────────────────────────────────────────

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: rgName
  location: location
  tags: tags
}

// ── Modules ───────────────────────────────────────────────────────────────────

module observability 'modules/observability.bicep' = {
  name: 'observability'
  scope: rg
  params: {
    prefix: prefix
    location: location
    tags: tags
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  scope: rg
  params: {
    prefix: prefix
    location: location
    tags: tags
    workosClientId: workosClientId
    workosApiKey: workosApiKey
    anthropicApiKey: anthropicApiKey
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  scope: rg
  params: {
    prefix: prefix
    location: location
    tags: tags
  }
}

module serviceBus 'modules/servicebus.bicep' = {
  name: 'servicebus'
  scope: rg
  params: {
    prefix: prefix
    location: location
    tags: tags
  }
}

module database 'modules/database.bicep' = {
  name: 'database'
  scope: rg
  params: {
    prefix: prefix
    location: location
    tags: tags
    adminLogin: pgAdminLogin
    adminPassword: pgAdminPassword
    logAnalyticsWorkspaceId: observability.outputs.logAnalyticsWorkspaceId
  }
}

module registry 'modules/registry.bicep' = {
  name: 'registry'
  scope: rg
  params: {
    prefix: prefix
    location: location
    tags: tags
  }
}

module staticWebApp 'modules/staticwebapp.bicep' = {
  name: 'staticwebapp'
  scope: rg
  params: {
    prefix: prefix
    location: swaLocation
    tags: tags
  }
}

module containerApps 'modules/containerapps.bicep' = {
  name: 'containerapps'
  scope: rg
  params: {
    prefix: prefix
    location: location
    tags: tags
    logAnalyticsWorkspaceId: observability.outputs.logAnalyticsWorkspaceId
    appInsightsConnectionString: observability.outputs.appInsightsConnectionString
    keyVaultName: keyVault.outputs.keyVaultName
    storageAccountName: storage.outputs.storageAccountName
    serviceBusNamespaceName: serviceBus.outputs.namespaceName
    serviceBusQueueName: serviceBus.outputs.queueName
    pgHost: database.outputs.fqdn
    pgDatabase: 'threatmodeling'
    registryLoginServer: registry.outputs.loginServer
    azureOpenAiResourceName: azureOpenAiResourceName
    azureOpenAiStrongModel: azureOpenAiStrongModel
    azureOpenAiLowCostModel: azureOpenAiLowCostModel
  }
}

// ── Role assignments (managed identity → Azure resources) ─────────────────────
// Defined here to avoid circular references between modules.

// API managed identity → Key Vault (secret reader)
module apiKvRole 'modules/roleassignment.bicep' = {
  name: 'api-kv-role'
  scope: rg
  params: {
    principalId: containerApps.outputs.apiIdentityPrincipalId
    roleDefinitionId: '4633458b-17de-408a-b874-0445c86b69e6' // Key Vault Secrets User
    resourceId: keyVault.outputs.keyVaultId
  }
}

// API managed identity → Storage (blob contributor on upload container)
module apiStorageRole 'modules/roleassignment.bicep' = {
  name: 'api-storage-role'
  scope: rg
  params: {
    principalId: containerApps.outputs.apiIdentityPrincipalId
    roleDefinitionId: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe' // Storage Blob Data Contributor
    resourceId: storage.outputs.storageAccountId
  }
}

// API managed identity → Service Bus (sender)
module apiSbRole 'modules/roleassignment.bicep' = {
  name: 'api-sb-role'
  scope: rg
  params: {
    principalId: containerApps.outputs.apiIdentityPrincipalId
    roleDefinitionId: '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39' // Azure Service Bus Data Sender
    resourceId: serviceBus.outputs.namespaceId
  }
}

// Worker managed identity → Key Vault (secret reader)
module workerKvRole 'modules/roleassignment.bicep' = {
  name: 'worker-kv-role'
  scope: rg
  params: {
    principalId: containerApps.outputs.workerIdentityPrincipalId
    roleDefinitionId: '4633458b-17de-408a-b874-0445c86b69e6' // Key Vault Secrets User
    resourceId: keyVault.outputs.keyVaultId
  }
}

// Worker managed identity → Storage (blob contributor)
module workerStorageRole 'modules/roleassignment.bicep' = {
  name: 'worker-storage-role'
  scope: rg
  params: {
    principalId: containerApps.outputs.workerIdentityPrincipalId
    roleDefinitionId: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe' // Storage Blob Data Contributor
    resourceId: storage.outputs.storageAccountId
  }
}

// Worker managed identity → Service Bus (receiver)
module workerSbRole 'modules/roleassignment.bicep' = {
  name: 'worker-sb-role'
  scope: rg
  params: {
    principalId: containerApps.outputs.workerIdentityPrincipalId
    roleDefinitionId: '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0' // Azure Service Bus Data Receiver
    resourceId: serviceBus.outputs.namespaceId
  }
}

// Worker managed identity → Azure OpenAI (Cognitive Services OpenAI User)
module workerOpenAiRole 'modules/roleassignment.bicep' = {
  name: 'worker-openai-role'
  scope: rg
  params: {
    principalId: containerApps.outputs.workerIdentityPrincipalId
    roleDefinitionId: '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd' // Cognitive Services OpenAI User
    resourceId: '/subscriptions/${subscription().subscriptionId}/resourceGroups/${azureOpenAiResourceName}-rg/providers/Microsoft.CognitiveServices/accounts/${azureOpenAiResourceName}'
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output apiUrl string = containerApps.outputs.apiUrl
output frontendUrl string = 'https://${staticWebApp.outputs.defaultHostname}'
output swaName string = staticWebApp.outputs.swaName
output registryLoginServer string = registry.outputs.loginServer
output keyVaultName string = keyVault.outputs.keyVaultName
output resourceGroupName string = rgName
