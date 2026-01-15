using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Connectors.CloudStorage;

/// <summary>
/// Production-ready Wasabi connector (S3-compatible hot cloud storage).
/// Issue #152 - Wasabi hot cloud storage integration
/// </summary>
public class WasabiConnector : ICloudStorageConnector
{
    private readonly ILogger<WasabiConnector> _logger;
    private readonly AmazonS3Client _client;
    private readonly TransferUtility _transferUtility;

    public WasabiConnector(
        ILogger<WasabiConnector> logger,
        string accessKey,
        string secretKey,
        string region = "us-east-1")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        var endpoint = $"https://s3.{region}.wasabisys.com";
        
        var config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = false,
            SignatureVersion = "4"
        };

        _client = new AmazonS3Client(accessKey, secretKey, config);
        _transferUtility = new TransferUtility(_client);

        _logger.LogInformation("Wasabi connector initialized for region {Region}", region);
    }

    public async Task<List<string>> ListBucketsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing Wasabi buckets");
            var response = await _client.ListBucketsAsync(cancellationToken);
            
            var buckets = response.Buckets.Select(b => b.BucketName).ToList();
            _logger.LogInformation("Found {Count} Wasabi buckets", buckets.Count);
            
            return buckets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Wasabi buckets");
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
                "STANDARD",
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
            _logger.LogInformation("Uploading file {LocalPath} to Wasabi {Bucket}/{Key}", 
                localPath, bucket, key);

            var uploadRequest = new TransferUtilityUploadRequest
            {
                BucketName = bucket,
                FilePath = localPath,
                Key = key
            };

            await _transferUtility.UploadAsync(uploadRequest, cancellationToken);

            _logger.LogInformation("Successfully uploaded to Wasabi (hot storage): {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file {Key} to Wasabi", key);
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
            _logger.LogInformation("Downloading Wasabi {Bucket}/{Key} to {LocalPath}", bucket, key, localPath);

            await _transferUtility.DownloadAsync(localPath, bucket, key, cancellationToken);

            _logger.LogInformation("Successfully downloaded from Wasabi: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file {Key} from Wasabi", key);
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
            _logger.LogDebug("Getting object stream for Wasabi {Bucket}/{Key}", bucket, key);

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
            _logger.LogInformation("Deleting Wasabi {Bucket}/{Key}", bucket, key);

            await _client.DeleteObjectAsync(bucket, key, cancellationToken);

            _logger.LogInformation("Successfully deleted object: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete object {Key}", key);
            throw;
        }
    }

    public void Dispose()
    {
        _transferUtility?.Dispose();
        _client?.Dispose();
        _logger.LogDebug("Wasabi connector disposed");
    }
}
