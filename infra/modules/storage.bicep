// Azure Blob Storage — uploaded diagrams and analysis outputs
// org_id-prefixed path isolation enforced in application code.

param prefix string
param location string
param tags object

// Storage account names: 3-24 chars, lowercase letters and numbers only
var storageAccountName = replace(replace('${prefix}st', '-', ''), '_', '')

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: length(storageAccountName) > 24 ? substring(storageAccountName, 0, 24) : storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false            // No anonymous blob access
    allowSharedKeyAccess: false             // Managed identity only
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    networkAcls: {
      defaultAction: 'Allow'               // Tighten to private endpoint post-MVP
      bypass: 'AzureServices'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource architecturesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'architectures'
  properties: {
    publicAccess: 'None'
  }
}

output storageAccountName string = storageAccount.name
output storageAccountId string = storageAccount.id
