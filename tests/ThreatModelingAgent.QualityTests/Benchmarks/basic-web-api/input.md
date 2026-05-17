# Basic Single-Tenant Web API

A React single-page application is served from Azure Static Web Apps.

The frontend calls a REST API hosted on Azure App Service.

The API authenticates users with Microsoft Entra ID (OIDC).

The system is single-tenant. All users belong to the same organisation.

The API uses Azure SQL Database for persistence. The database is not configured with row-level security.

Users can upload profile pictures and documents. Uploaded files are stored in Azure Blob Storage.

The API accesses the database and blob storage using a managed identity. The exact permissions granted to the managed identity are not described.

The Azure App Service is internet-facing. There is no API gateway, WAF, or CDN in front of it.

The Azure SQL Database and Azure Blob Storage account are not described as having private endpoints configured.

There is no description of rate limiting on any API endpoints.

Secrets and API keys are stored in Azure Key Vault. The Key Vault is not described as having a private endpoint.
