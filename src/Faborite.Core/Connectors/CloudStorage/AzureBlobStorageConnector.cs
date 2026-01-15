using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Connectors.CloudStorage;

/// <summary>
/// Production-ready Azure Blob Storage connector with hot/cool/archive tiers.
/// Issue #148 - Azure Blob Storage with hot/cool/archive access tiers
/// </summary>
public class AzureBlobStorageConnector : ICloudStorageConnector
{
    private readonly ILogger<AzureBlobStorageConnector> _logger;
    private readonly BlobServiceClient _serviceClient;

    public AzureBlobStorageConnector(ILogger<AzureBlobStorageConnector> logger, string connectionString)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceClient = new BlobServiceClient(connectionString);
        _logger.LogInformation("Azure Blob Storage connector initialized");
    }

    public async Task<List<string>> ListBucketsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing Azure Blob containers");
            var containers = new List<string>();
            
            await foreach (var container in _serviceClient.GetBlobContainersAsync(cancellationToken: cancellationToken))
            {
                containers.Add(container.Name);
            }
            
            _logger.LogInformation("Found {Count} containers", containers.Count);
            return containers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list containers");
            throw;
        }
    }

    public async Task<List<CloudObject>> ListObjectsAsync(
        string bucket,
        string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing blobs in container {Container} with prefix {Prefix}", bucket, prefix);

            var containerClient = _serviceClient.GetBlobContainerClient(bucket);
            var objects = new List<CloudObject>();
            
            await foreach (var blob in containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
            {
                objects.Add(new CloudObject(
                    blob.Name,
                    blob.Properties.ContentLength ?? 0,
                    blob.Properties.LastModified?.UtcDateTime ?? DateTime.UtcNow,
                    blob.Properties.AccessTier?.ToString() ?? "Hot",
                    blob.Properties.ETag.ToString(),
                    blob.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                ));
            }

            _logger.LogInformation("Found {Count} blobs in {Container}", objects.Count, bucket);
            return objects;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list blobs in container {Container}", bucket);
            throw;
        }
    }

    public async Task UploadFileAsync(
        string bucket,
        string key,
        string localPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Uploading file {LocalPath} to {Container}/{Blob}", localPath, bucket, key);

            var containerClient = _serviceClient.GetBlobContainerClient(bucket);
            var blobClient = containerClient.GetBlobClient(key);

            await blobClient.UploadAsync(localPath, overwrite: true, cancellationToken);

            _logger.LogInformation("Successfully uploaded to Azure Blob: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file {Key}", key);
            throw;
        }
    }

    public async Task DownloadFileAsync(
        string bucket,
        string key,
        string localPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Downloading {Container}/{Blob} to {LocalPath}", bucket, key, localPath);

            var containerClient = _serviceClient.GetBlobContainerClient(bucket);
            var blobClient = containerClient.GetBlobClient(key);

            await blobClient.DownloadToAsync(localPath, cancellationToken);

            _logger.LogInformation("Successfully downloaded from Azure Blob: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file {Key}", key);
            throw;
        }
    }

    public async Task<Stream> GetObjectStreamAsync(
        string bucket,
        string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting blob stream for {Container}/{Blob}", bucket, key);

            var containerClient = _serviceClient.GetBlobContainerClient(bucket);
            var blobClient = containerClient.GetBlobClient(key);

            var stream = new MemoryStream();
            await blobClient.DownloadToAsync(stream, cancellationToken);
            stream.Position = 0;
            
            return stream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get blob stream for {Key}", key);
            throw;
        }
    }

    public async Task DeleteObjectAsync(
        string bucket,
        string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting {Container}/{Blob}", bucket, key);

            var containerClient = _serviceClient.GetBlobContainerClient(bucket);
            var blobClient = containerClient.GetBlobClient(key);

            await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);

            _logger.LogInformation("Successfully deleted blob: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete blob {Key}", key);
            throw;
        }
    }

    public async Task SetAccessTierAsync(
        string bucket,
        string key,
        AccessTier tier,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Setting access tier to {Tier} for {Container}/{Blob}", 
                tier, bucket, key);

            var containerClient = _serviceClient.GetBlobContainerClient(bucket);
            var blobClient = containerClient.GetBlobClient(key);

            await blobClient.SetAccessTierAsync(tier, cancellationToken: cancellationToken);

            _logger.LogInformation("Access tier updated for {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set access tier for {Key}", key);
            throw;
        }
    }

    public async Task<BlobStorageMetrics> GetStorageMetricsAsync(
        string bucket,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting storage metrics for container {Container}", bucket);

            var objects = await ListObjectsAsync(bucket, cancellationToken: cancellationToken);

            var totalSize = objects.Sum(obj => obj.Size);
            var objectCount = objects.Count;

            var sizeByTier = objects
                .GroupBy(obj => obj.StorageClass)
                .ToDictionary(g => g.Key, g => g.Sum(obj => obj.Size));

            return new BlobStorageMetrics(totalSize, objectCount, sizeByTier);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get storage metrics for container {Container}", bucket);
            throw;
        }
    }

    public void Dispose()
    {
        _logger.LogDebug("Azure Blob Storage connector disposed");
    }
}

public record BlobStorageMetrics(
    long TotalSizeBytes,
    int BlobCount,
    Dictionary<string, long> SizeByTier
);
