// Azure Container Apps — api and worker
// Both use system-assigned managed identity; no secrets in env vars.
// Secrets that require Key Vault references are wired at deploy time.

param prefix string
param location string
param tags object
param logAnalyticsWorkspaceId string
param appInsightsConnectionString string
param keyVaultName string
param storageAccountName string
param serviceBusNamespaceName string
param serviceBusQueueName string
param pgHost string
param pgDatabase string
param registryLoginServer string
param azureOpenAiResourceName string
param azureOpenAiStrongModel string
param azureOpenAiLowCostModel string

@description('Full image tag for the API container (e.g. myregistry.azurecr.io/api:sha-abc123).')
param apiImageTag string = '${registryLoginServer}/api:latest'

@description('Full image tag for the Worker container.')
param workerImageTag string = '${registryLoginServer}/worker:latest'

// ── Container Apps Environment ────────────────────────────────────────────────

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-cae'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: reference(logAnalyticsWorkspaceId, '2023-09-01').customerId
        sharedKey: listKeys(logAnalyticsWorkspaceId, '2023-09-01').primarySharedKey
      }
    }
  }
}

// ── API Container App ─────────────────────────────────────────────────────────

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${prefix}-api'
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
      }
      registries: [
        {
          server: registryLoginServer
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImageTag
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ConnectionStrings__DefaultConnection'
              // Connection string wired via Key Vault reference or direct param post-MVP
              // For MVP: set via az containerapp update after DB provisioning
              value: 'Host=${pgHost};Database=${pgDatabase};Username=api_user;SSL Mode=Require'
            }
            {
              name: 'WorkOS__Issuer'
              value: 'https://api.workos.com'
            }
            {
              name: 'WorkOS__JwksUri'
              value: 'https://api.workos.com/.well-known/jwks.json'
            }
            {
              name: 'AzureStorage__AccountName'
              value: storageAccountName
            }
            {
              name: 'AzureStorage__ContainerName'
              value: 'architectures'
            }
            {
              name: 'ApplicationInsights__ConnectionString'
              value: appInsightsConnectionString
            }
            // WorkOS:ClientId is loaded from Key Vault via managed identity at startup.
            // Set AZURE_CLIENT_ID is not needed — system-assigned identity is auto-discovered.
            {
              name: 'KeyVault__Name'
              value: keyVaultName
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '20'
              }
            }
          }
        ]
      }
    }
  }
}

// ── Worker Container App ──────────────────────────────────────────────────────

resource workerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${prefix}-worker'
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      // No ingress — worker has no public endpoint
      registries: [
        {
          server: registryLoginServer
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: workerImageTag
          resources: {
            cpu: json('1.0')
            memory: '2Gi'
          }
          env: [
            {
              name: 'DOTNET_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ConnectionStrings__DefaultConnection'
              value: 'Host=${pgHost};Database=${pgDatabase};Username=worker_user;SSL Mode=Require'
            }
            {
              name: 'AzureServiceBus__NamespaceFQDN'
              value: '${serviceBusNamespaceName}.servicebus.windows.net'
            }
            {
              name: 'AzureServiceBus__QueueName'
              value: serviceBusQueueName
            }
            {
              name: 'AzureStorage__AccountName'
              value: storageAccountName
            }
            {
              name: 'AzureStorage__ContainerName'
              value: 'architectures'
            }
            {
              name: 'AzureOpenAI__ResourceName'
              value: azureOpenAiResourceName
            }
            {
              name: 'LlmRouting__StrongModel'
              value: azureOpenAiStrongModel
            }
            {
              name: 'LlmRouting__LowCostModel'
              value: azureOpenAiLowCostModel
            }
            {
              name: 'ApplicationInsights__ConnectionString'
              value: appInsightsConnectionString
            }
            {
              name: 'KeyVault__Name'
              value: keyVaultName
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 10
        rules: [
          {
            name: 'servicebus-scaling'
            custom: {
              type: 'azure-servicebus'
              // Use system-assigned managed identity — no connection string needed.
              // Worker identity is granted AzureServiceBusDataReceiver in main.bicep.
              identity: 'system'
              metadata: {
                namespace: '${serviceBusNamespaceName}.servicebus.windows.net'
                queueName: serviceBusQueueName
                messageCount: '5'
                activationMessageCount: '0'
              }
            }
          }
        ]
      }
    }
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output apiUrl string = 'https://${apiApp.properties.configuration.ingress.fqdn}'
output apiIdentityPrincipalId string = apiApp.identity.principalId
output workerIdentityPrincipalId string = workerApp.identity.principalId
