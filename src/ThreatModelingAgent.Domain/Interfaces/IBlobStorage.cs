namespace ThreatModelingAgent.Domain.Interfaces;

public interface IBlobStorage
{
    Task<string> UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string path, CancellationToken ct = default);
    Task DeleteAsync(string path, CancellationToken ct = default);
    Task DeleteByPrefixAsync(string prefix, CancellationToken ct = default);

    /// <summary>
    /// Returns a short-lived (max 5 min) write-once SAS URI scoped to the exact blob path.
    /// Used for direct upload from the API — the worker reads via managed identity, not SAS.
    /// </summary>
    Task<Uri> GetUploadSasUriAsync(string path, TimeSpan expiry, CancellationToken ct = default);
}
