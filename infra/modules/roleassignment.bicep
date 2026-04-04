// Generic role assignment helper — called from main.bicep

param principalId string
param roleDefinitionId string
param resourceId string

resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(principalId, roleDefinitionId, resourceId)
  properties: {
    principalId: principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleDefinitionId)
    principalType: 'ServicePrincipal'
  }
}
