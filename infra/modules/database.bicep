// Azure Database for PostgreSQL Flexible Server
// Row-Level Security is applied by EF migrations in the application.

param prefix string
param location string
param tags object
param logAnalyticsWorkspaceId string

@secure()
param adminLogin string

@secure()
param adminPassword string

resource pgServer 'Microsoft.DBforPostgreSQL/flexibleServers@2023-12-01-preview' = {
  name: '${prefix}-pg'
  location: location
  tags: tags
  sku: {
    name: 'Standard_B2s'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: adminLogin
    administratorLoginPassword: adminPassword
    storage: {
      storageSizeGB: 32
      autoGrow: 'Enabled'
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'       // Enable for DR (post-MVP)
    }
    highAvailability: {
      mode: 'Disabled'                     // Enable standby for GA
    }
    authConfig: {
      activeDirectoryAuth: 'Disabled'
      passwordAuth: 'Enabled'
    }
    network: {
      publicNetworkAccess: 'Enabled'       // Tighten to private endpoint post-MVP
    }
  }
}

resource threatModelingDb 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2023-12-01-preview' = {
  parent: pgServer
  name: 'threatmodeling'
  properties: {
    charset: 'UTF8'
    collation: 'en_US.UTF8'
  }
}

// Diagnostic settings — ship logs to Log Analytics
resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'pg-diagnostics'
  scope: pgServer
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        category: 'PostgreSQLLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

output fqdn string = pgServer.properties.fullyQualifiedDomainName
output serverId string = pgServer.id
