// Azure Service Bus Standard — async job queue with dead-letter support

param prefix string
param location string
param tags object

resource namespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: '${prefix}-sb'
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    minimumTlsVersion: '1.2'
    disableLocalAuth: true                 // Managed identity only; no SAS keys in prod
    publicNetworkAccess: 'Enabled'         // Tighten to private endpoint post-MVP
  }
}

resource analysisJobsQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: namespace
  name: 'analysis-jobs'
  properties: {
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: 'PT2H'
    lockDuration: 'PT5M'                   // Worker needs time for LLM pipeline
    maxDeliveryCount: 5
    requiresDuplicateDetection: false
    requiresSession: false
  }
}

output namespaceName string = namespace.name
output namespaceId string = namespace.id
output queueName string = analysisJobsQueue.name
