// ── Azure Static Web Apps — Free tier ────────────────────────────────────────
// Hosts the React frontend (pre-built dist/ uploaded by CI using the deployment token).
//
// The deployment token is not exposed as a Bicep output because it is a secret.
// CI retrieves it at deploy time via:
//   az staticwebapp secrets list --name <swaName> --query "properties.apiKey" -o tsv
//
// SPA routing is handled by staticwebapp.config.json in the frontend directory.
// Security headers (CSP, HSTS, X-Frame-Options, etc.) are also defined there.

param prefix string
param location string
param tags object

resource swa 'Microsoft.Web/staticSites@2023-01-01' = {
  name: '${prefix}-swa'
  // SWA is a global service — location controls metadata placement only.
  // Allowed values differ from other resource types; westeurope and northeurope are supported.
  location: location
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    // Allow PR preview environments (staging environments per branch)
    stagingEnvironmentPolicy: 'Enabled'
    // Allow the staticwebapp.config.json in the repo to configure routing and headers
    allowConfigFileUpdates: true
    enterpriseGradeCdnStatus: 'Disabled'
  }
}

output swaName string = swa.name
output defaultHostname string = swa.properties.defaultHostname
output id string = swa.id
