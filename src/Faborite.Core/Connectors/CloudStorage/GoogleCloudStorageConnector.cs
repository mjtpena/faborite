using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Connectors.CloudStorage;

/// <summary>
/// Production-ready Google Cloud Storage connector with storage class management.
/// Issue #147 - Google Cloud Storage with nearline/coldline/archive tiers
/// </summary>
public class GoogleCloudStorageConnector : ICloudStorageConnector
{
    private readonly ILogger<GoogleCloudStorageConnector> _logger;
    private readonly StorageClient _client;

    public GoogleCloudStorageConnector(ILogger<GoogleCloudStorageConnector> logger, string credentialsJson)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _client = StorageClient.Create();
        _logger.LogInformation("Google Cloud Storage connector initialized");
    }

    public async Task<List<string>> ListBucketsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing GCS buckets");
            var buckets = new List<string>();
            
            await foreach (var bucket in _client.ListBucketsAsync(System.Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")))
            {
                buckets.Add(bucket.Name);
            }
            
            _logger.LogInformation("Found {Count} GCS buckets", buckets.Count);
            return buckets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list GCS buckets");
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
            _logger.LogDebug("Listing objects in bucket {Bucket} with prefix {Prefix}", bucket, prefix);

            var objects = new List<CloudObject>();
            await foreach (var obj in _client.ListObjectsAsync(bucket, prefix))
            {
                objects.Add(new CloudObject(
                    obj.Name,
                    (long)(obj.Size ?? 0),
                    obj.UpdatedDateTimeOffset?.UtcDateTime ?? DateTime.UtcNow,
                    obj.StorageClass ?? "STANDARD",
                    obj.ETag,
                    obj.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                ));
            }

            _logger.LogInformation("Found {Count} objects in {Bucket}", objects.Count, bucket);
            return objects;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list objects in bucket {Bucket}", bucket);
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
            _logger.LogInformation("Uploading file {LocalPath} to gs://{Bucket}/{Key}", localPath, bucket, key);

            using var fileStream = File.OpenRead(localPath);
            await _client.UploadObjectAsync(bucket, key, null, fileStream, cancellationToken: cancellationToken);

            _logger.LogInformation("Successfully uploaded to GCS: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file {Key} to GCS", key);
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
            _logger.LogInformation("Downloading gs://{Bucket}/{Key} to {LocalPath}", bucket, key, localPath);

            using var fileStream = File.Create(localPath);
            await _client.DownloadObjectAsync(bucket, key, fileStream, cancellationToken: cancellationToken);

            _logger.LogInformation("Successfully downloaded from GCS: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file {Key} from GCS", key);
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
            _logger.LogDebug("Getting object stream for gs://{Bucket}/{Key}", bucket, key);

            var stream = new MemoryStream();
            await _client.DownloadObjectAsync(bucket, key, stream, cancellationToken: cancellationToken);
            stream.Position = 0;
            
            return stream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get object stream for {Key}", key);
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
            _logger.LogInformation("Deleting gs://{Bucket}/{Key}", bucket, key);

            await _client.DeleteObjectAsync(bucket, key, cancellationToken: cancellationToken);

            _logger.LogInformation("Successfully deleted object: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete object {Key}", key);
            throw;
        }
    }

    public async Task SetStorageClassAsync(
        string bucket,
        string key,
        string storageClass,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Setting storage class to {Class} for gs://{Bucket}/{Key}", 
                storageClass, bucket, key);

            var obj = await _client.GetObjectAsync(bucket, key, cancellationToken: cancellationToken);
            obj.StorageClass = storageClass;
            
            await _client.UpdateObjectAsync(obj, cancellationToken: cancellationToken);

            _logger.LogInformation("Storage class updated for {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set storage class for {Key}", key);
            throw;
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
        _logger.LogDebug("GCS connector disposed");
    }
}
