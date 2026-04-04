// Azure Container Registry — stores api and worker images

param prefix string
param location string
param tags object

// Registry names: 5-50 chars, alphanumeric only
var registryName = replace(replace('${prefix}cr', '-', ''), '_', '')

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: length(registryName) > 50 ? substring(registryName, 0, 50) : registryName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false                // Managed identity only
    publicNetworkAccess: 'Enabled'
    zoneRedundancy: 'Disabled'
  }
}

output loginServer string = registry.loginServer
output registryId string = registry.id
output registryName string = registry.name
