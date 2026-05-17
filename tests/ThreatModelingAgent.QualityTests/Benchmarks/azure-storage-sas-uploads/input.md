# Azure Blob Storage With SAS-Based Client Uploads

Users access a web application that allows them to upload and download documents.

The backend API is hosted on Azure App Service.

When a user initiates an upload, the API generates a pre-signed SAS URL and returns it to the browser. The browser then uploads the file directly to Azure Blob Storage using the SAS URL.

The SAS URL grants write access to a specific blob path. SAS tokens are generated using the storage account access key. Token expiry is set to 24 hours.

All users share the same storage account and the same blob container. Files are stored under a path prefixed with the user ID: uploads/{userId}/{filename}.

There is no container-per-user or account-per-user isolation.

The storage account access key is stored in the application configuration. It is not rotated on a defined schedule.

Users can upload any file type. Uploaded files are later downloaded and displayed in the application.

There is no description of malware scanning or content validation on uploaded files.
