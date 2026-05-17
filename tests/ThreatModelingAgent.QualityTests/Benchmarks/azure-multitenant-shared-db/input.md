# Azure Multi-Tenant SaaS With Shared Database

External users access a web frontend hosted on Azure Static Web Apps.

The frontend calls a REST API hosted on Azure App Service.

The API authenticates users via Microsoft Entra ID.

The system is multi-tenant. Each user belongs to a specific tenant (organisation).

All tenants share the same Azure SQL Database. There is no database-level row-level security configured.

The API does not describe whether tenant filtering is centrally enforced or distributed across individual query sites.

The API reads and writes tenant-specific business data including contracts, invoices, and customer records.

Users can upload files which are stored in a shared Azure Blob Storage container. Files from all tenants are stored under a folder path prefixed with the tenant ID.

The API uses a managed identity to access Azure SQL Database and Azure Blob Storage.

There is no description of what permissions the managed identity holds.

Audit logging is described as "enabled" but no detail is given on what events are captured or whether logs are tenant-scoped.
