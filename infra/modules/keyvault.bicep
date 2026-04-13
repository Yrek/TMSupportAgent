// Azure Key Vault — secrets storage
// Managed identity access granted in main.bicep role assignments.

param prefix string
param location string
param tags object

@secure()
param workosClientId string

@secure()
param workosApiKey string

@secure()
param anthropicApiKey string

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${prefix}-kv'
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true          // Use RBAC, not access policies
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'         // Restrict via private endpoint post-MVP
    networkAcls: {
      defaultAction: 'Allow'               // Tighten to private endpoint post-MVP
      bypass: 'AzureServices'
    }
  }
}

resource secretWorkosClientId 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'WorkOS--ClientId'
  properties: {
    value: workosClientId
    attributes: {
      enabled: true
    }
  }
}

resource secretWorkosApiKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'WorkOS--ApiKey'
  properties: {
    value: workosApiKey
    attributes: {
      enabled: true
    }
  }
}

resource secretAnthropicApiKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Anthropic--ApiKey'
  properties: {
    value: anthropicApiKey
    attributes: {
      enabled: true
    }
  }
}

output keyVaultName string = keyVault.name
output keyVaultId string = keyVault.id
output keyVaultUri string = keyVault.properties.vaultUri
