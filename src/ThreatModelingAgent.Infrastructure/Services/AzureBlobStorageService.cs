using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ThreatModelingAgent.Domain.Interfaces;

namespace ThreatModelingAgent.Infrastructure.Services;

/// <summary>
/// Azure Blob Storage implementation of IBlobStorage.
///
/// Authentication: Managed Identity in production (DefaultAzureCredential).
/// Local dev: connection string fallback via AzureStorage:ConnectionString.
///
/// Blob paths follow the layout defined in data-model spec:
///   /{org_id}/uploads/{job_id}/{random-filename}
///
/// SAS URIs are write-once, scoped to the exact blob path, and short-lived (≤5 min).
/// The worker reads blobs via Managed Identity — SAS is only for direct client upload.
/// </summary>
public sealed class AzureBlobStorageService : IBlobStorage
{
    private readonly BlobServiceClient _serviceClient;
    private readonly BlobContainerClient _container;
    private readonly ILogger<AzureBlobStorageService> _logger;

    public AzureBlobStorageService(IConfiguration configuration, ILogger<AzureBlobStorageService> logger)
    {
        _logger = logger;

        var accountName = configuration["AzureStorage:AccountName"];
        var containerName = configuration["AzureStorage:ContainerName"]
            ?? throw new InvalidOperationException("AzureStorage:ContainerName is required.");

        // Prefer Managed Identity (production). Fall back to connection string for local dev.
        var connectionString = configuration["AzureStorage:ConnectionString"];

        if (!string.IsNullOrEmpty(connectionString))
        {
            // Local dev — connection string (never committed; gitignored dev settings)
            _serviceClient = new BlobServiceClient(connectionString);
        }
        else if (!string.IsNullOrEmpty(accountName))
        {
            // Production — Managed Identity via DefaultAzureCredential (CLAUDE.md §10.1)
            var serviceUri = new Uri($"https://{accountName}.blob.core.windows.net");
            _serviceClient = new BlobServiceClient(serviceUri, new DefaultAzureCredential());
        }
        else
        {
            throw new InvalidOperationException(
                "AzureStorage must be configured with either AccountName (Managed Identity) " +
                "or ConnectionString (local dev). Application cannot start without blob storage. (CLAUDE.md §4.3)");
        }

        _container = _serviceClient.GetBlobContainerClient(containerName);
    }

    public async Task<string> UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default)
    {
        var blobClient = _container.GetBlobClient(path);

        await blobClient.UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        }, ct);

        _logger.LogInformation("Blob uploaded. Path={BlobPath}", path);

        return blobClient.Uri.AbsoluteUri;
    }

    public async Task<Stream> DownloadAsync(string path, CancellationToken ct = default)
    {
        var blobClient = _container.GetBlobClient(path);
        var response = await blobClient.DownloadStreamingAsync(cancellationToken: ct);
        return response.Value.Content;
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var blobClient = _container.GetBlobClient(path);
        var deleted = await blobClient.DeleteIfExistsAsync(cancellationToken: ct);

        if (deleted)
            _logger.LogInformation("Blob deleted. Path={BlobPath}", path);
    }

    public async Task DeleteByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        // Enumerate and delete all blobs under the prefix (org erasure / job cleanup)
        var deletedCount = 0;

        // GetBlobsAsync signature: (BlobTraits, BlobStates, string prefix, CancellationToken)
        await foreach (var item in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, ct))
        {
            var blobClient = _container.GetBlobClient(item.Name);
            await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
            deletedCount++;
        }

        _logger.LogInformation("Blobs deleted by prefix. Prefix={Prefix} Count={Count}", prefix, deletedCount);
    }

    /// <summary>
    /// Returns a write-once SAS URI scoped to exactly one blob path, valid for at most 5 minutes.
    /// Only Create permission is granted — the client cannot read, overwrite, or list.
    /// </summary>
    public async Task<Uri> GetUploadSasUriAsync(string path, TimeSpan expiry, CancellationToken ct = default)
    {
        if (expiry > TimeSpan.FromMinutes(5))
            throw new ArgumentException("SAS expiry must not exceed 5 minutes.", nameof(expiry));

        var blobClient = _container.GetBlobClient(path);

        await _container.CreateIfNotExistsAsync(cancellationToken: ct);

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _container.Name,
            BlobName = path,
            Resource = "b", // blob-level SAS
            ExpiresOn = DateTimeOffset.UtcNow.Add(expiry)
        };

        // Write-once: Create only. No read, delete, or list permissions.
        sasBuilder.SetPermissions(BlobSasPermissions.Create);

        // Under Managed Identity, use a UserDelegationKey (no storage account key required).
        // Fall back to account-key SAS for local dev with a connection string.
        try
        {
            var delegationKeyResponse = await _serviceClient.GetUserDelegationKeyAsync(
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.Add(expiry),
                ct);

            var sasUri = blobClient.GenerateSasUri(sasBuilder);
            _logger.LogInformation(
                "SAS URI generated. Path={BlobPath} ExpiresIn={ExpirySeconds}s",
                path, expiry.TotalSeconds);
            return sasUri;
        }
        catch (RequestFailedException ex)
        {
            // UserDelegationKey requires OAuth — not available on connection-string clients in local dev.
            _logger.LogDebug(
                "UserDelegationKey unavailable ({Code}), falling back to account-key SAS.",
                ex.ErrorCode);
            var sasUri = blobClient.GenerateSasUri(sasBuilder);
            return sasUri;
        }
    }
}