using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Connectors.CloudStorage;

/// <summary>
/// Production-ready MinIO connector (S3-compatible private cloud storage).
/// Issue #149 - MinIO private S3-compatible storage
/// </summary>
public class MinIOConnector : ICloudStorageConnector
{
    private readonly ILogger<MinIOConnector> _logger;
    private readonly AmazonS3Client _client;
    private readonly TransferUtility _transferUtility;

    public MinIOConnector(ILogger<MinIOConnector> logger, string endpoint, string accessKey, string secretKey, bool useSSL = true)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        var config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true, // Required for MinIO
            UseHttp = !useSSL
        };

        _client = new AmazonS3Client(accessKey, secretKey, config);
        _transferUtility = new TransferUtility(_client);

        _logger.LogInformation("MinIO connector initialized for endpoint {Endpoint}", endpoint);
    }

    public async Task<List<string>> ListBucketsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing MinIO buckets");
            var response = await _client.ListBucketsAsync(cancellationToken);
            
            var buckets = response.Buckets.Select(b => b.BucketName).ToList();
            _logger.LogInformation("Found {Count} MinIO buckets", buckets.Count);
            
            return buckets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list MinIO buckets");
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
            _logger.LogInformation("Uploading file {LocalPath} to MinIO bucket {Bucket}/{Key}", 
                localPath, bucket, key);

            var uploadRequest = new TransferUtilityUploadRequest
            {
                BucketName = bucket,
                FilePath = localPath,
                Key = key
            };

            await _transferUtility.UploadAsync(uploadRequest, cancellationToken);

            _logger.LogInformation("Successfully uploaded to MinIO: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file {Key} to MinIO", key);
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
            _logger.LogInformation("Downloading MinIO {Bucket}/{Key} to {LocalPath}", bucket, key, localPath);

            await _transferUtility.DownloadAsync(localPath, bucket, key, cancellationToken);

            _logger.LogInformation("Successfully downloaded from MinIO: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file {Key} from MinIO", key);
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
            _logger.LogDebug("Getting object stream for MinIO {Bucket}/{Key}", bucket, key);

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
            _logger.LogInformation("Deleting MinIO {Bucket}/{Key}", bucket, key);

            await _client.DeleteObjectAsync(bucket, key, cancellationToken);

            _logger.LogInformation("Successfully deleted object: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete object {Key}", key);
            throw;
        }
    }

    public async Task<bool> CreateBucketAsync(
        string bucketName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating MinIO bucket {Bucket}", bucketName);

            var request = new PutBucketRequest
            {
                BucketName = bucketName,
                UseClientRegion = true
            };

            await _client.PutBucketAsync(request, cancellationToken);

            _logger.LogInformation("Bucket created: {Bucket}", bucketName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create bucket {Bucket}", bucketName);
            throw;
        }
    }

    public async Task<bool> BucketExistsAsync(
        string bucketName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.GetBucketLocationAsync(bucketName, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _transferUtility?.Dispose();
        _client?.Dispose();
        _logger.LogDebug("MinIO connector disposed");
    }
}
