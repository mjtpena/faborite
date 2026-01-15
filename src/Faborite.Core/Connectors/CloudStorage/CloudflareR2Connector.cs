using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Connectors.CloudStorage;

/// <summary>
/// Production-ready Cloudflare R2 connector (S3-compatible with zero egress fees).
/// Issue #150 - Cloudflare R2 zero-egress storage
/// </summary>
public class CloudflareR2Connector : ICloudStorageConnector
{
    private readonly ILogger<CloudflareR2Connector> _logger;
    private readonly AmazonS3Client _client;
    private readonly TransferUtility _transferUtility;

    public CloudflareR2Connector(
        ILogger<CloudflareR2Connector> logger,
        string accountId,
        string accessKey,
        string secretKey)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        var endpoint = $"https://{accountId}.r2.cloudflarestorage.com";
        
        var config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true,
            SignatureVersion = "4"
        };

        _client = new AmazonS3Client(accessKey, secretKey, config);
        _transferUtility = new TransferUtility(_client);

        _logger.LogInformation("Cloudflare R2 connector initialized for account {AccountId}", accountId);
    }

    public async Task<List<string>> ListBucketsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing R2 buckets");
            var response = await _client.ListBucketsAsync(cancellationToken);
            
            var buckets = response.Buckets.Select(b => b.BucketName).ToList();
            _logger.LogInformation("Found {Count} R2 buckets", buckets.Count);
            
            return buckets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list R2 buckets");
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

            var request = new ListObjectsV2Request
            {
                BucketName = bucket,
                Prefix = prefix ?? ""
            };

            var response = await _client.ListObjectsV2Async(request, cancellationToken);
            
            var objects = response.S3Objects.Select(obj => new CloudObject(
                obj.Key,
                obj.Size,
                obj.LastModified,
                obj.StorageClass?.Value ?? "STANDARD",
                obj.ETag,
                new Dictionary<string, string>()
            )).ToList();

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
            _logger.LogInformation("Uploading file {LocalPath} to R2 {Bucket}/{Key}", 
                localPath, bucket, key);

            var uploadRequest = new TransferUtilityUploadRequest
            {
                BucketName = bucket,
                FilePath = localPath,
                Key = key
            };

            await _transferUtility.UploadAsync(uploadRequest, cancellationToken);

            _logger.LogInformation("Successfully uploaded to R2 (zero egress): {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file {Key} to R2", key);
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
            _logger.LogInformation("Downloading R2 {Bucket}/{Key} to {LocalPath} (zero egress)", 
                bucket, key, localPath);

            await _transferUtility.DownloadAsync(localPath, bucket, key, cancellationToken);

            _logger.LogInformation("Successfully downloaded from R2: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file {Key} from R2", key);
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
            _logger.LogDebug("Getting object stream for R2 {Bucket}/{Key}", bucket, key);

            var response = await _client.GetObjectAsync(bucket, key, cancellationToken);
            return response.ResponseStream;
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
            _logger.LogInformation("Deleting R2 {Bucket}/{Key}", bucket, key);

            await _client.DeleteObjectAsync(bucket, key, cancellationToken);

            _logger.LogInformation("Successfully deleted object: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete object {Key}", key);
            throw;
        }
    }

    public async Task<R2StorageMetrics> GetStorageMetricsAsync(
        string bucket,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting R2 storage metrics for bucket {Bucket}", bucket);

            var objects = await ListObjectsAsync(bucket, cancellationToken: cancellationToken);

            var totalSize = objects.Sum(obj => obj.Size);
            var objectCount = objects.Count;

            // R2 has zero egress fees - highlight this benefit
            _logger.LogInformation("R2 Metrics - Bucket: {Bucket}, Objects: {Count}, Size: {Size} bytes (ZERO egress fees!)",
                bucket, objectCount, totalSize);

            return new R2StorageMetrics(totalSize, objectCount, 0); // Zero egress cost
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get storage metrics for bucket {Bucket}", bucket);
            throw;
        }
    }

    public void Dispose()
    {
        _transferUtility?.Dispose();
        _client?.Dispose();
        _logger.LogDebug("Cloudflare R2 connector disposed");
    }
}

public record R2StorageMetrics(
    long TotalSizeBytes,
    int ObjectCount,
    decimal EgressCost  // Always 0 for R2!
);
