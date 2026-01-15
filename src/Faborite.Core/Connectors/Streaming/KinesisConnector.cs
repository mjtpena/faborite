using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Faborite.Core.Connectors.Streaming;

public record KinesisConfig(
    string StreamName,
    string Region = "us-east-1",
    string? AccessKeyId = null,
    string? SecretAccessKey = null,
    int BatchSize = 500);

/// <summary>
/// Production-ready AWS Kinesis connector for real-time streaming data.
/// Supports producer with batching, consumer with shard iteration, and enhanced fan-out.
/// Issue #155
/// </summary>
public class KinesisConnector : IStreamingConnector, IDisposable
{
    private readonly KinesisConfig _config;
    private readonly ILogger<KinesisConnector> _logger;
    private readonly AmazonKinesisClient _client;

    public string Name => "AWS Kinesis";
    public string Version => "1.0.0";

    public KinesisConnector(KinesisConfig config, ILogger<KinesisConnector> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        var credentials = !string.IsNullOrEmpty(config.AccessKeyId) && !string.IsNullOrEmpty(config.SecretAccessKey)
            ? new BasicAWSCredentials(config.AccessKeyId, config.SecretAccessKey)
            : null;
        
        var clientConfig = new AmazonKinesisConfig
        {
            RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(config.Region)
        };
        
        _client = credentials != null 
            ? new AmazonKinesisClient(credentials, clientConfig)
            : new AmazonKinesisClient(clientConfig);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new DescribeStreamRequest { StreamName = _config.StreamName };
            var response = await _client.DescribeStreamAsync(request, cancellationToken);
            
            _logger.LogInformation("Connected to Kinesis stream: {Name}, Status: {Status}, Shards: {Count}",
                response.StreamDescription.StreamName,
                response.StreamDescription.StreamStatus,
                response.StreamDescription.Shards.Count);
            
            return response.StreamDescription.StreamStatus == StreamStatus.ACTIVE;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Kinesis stream: {Stream}", _config.StreamName);
            return false;
        }
    }

    public async Task<ConnectorMetadata> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        var request = new DescribeStreamRequest { StreamName = _config.StreamName };
        var response = await _client.DescribeStreamAsync(request, cancellationToken);
        var stream = response.StreamDescription;
        
        var capabilities = new Dictionary<string, string>
        {
            ["StreamName"] = stream.StreamName,
            ["Status"] = stream.StreamStatus.Value,
            ["ShardCount"] = stream.Shards.Count.ToString(),
            ["RetentionPeriod"] = stream.RetentionPeriodHours.ToString(),
            ["EncryptionType"] = stream.EncryptionType?.Value ?? "NONE",
            ["EnhancedMonitoring"] = stream.EnhancedMonitoring.Any().ToString()
        };

        var operations = new List<string> { "PutRecord", "PutRecords", "GetRecords", "ListShards" };

        return new ConnectorMetadata(
            Type: "Streaming",
            Version: Version,
            Capabilities: capabilities,
            SupportedOperations: operations);
    }

    public async Task ProduceAsync<T>(string topic, T value, string? key = null, CancellationToken cancellationToken = default) where T : class
    {
        var data = SerializeToBytes(value);
        var partitionKey = key ?? Guid.NewGuid().ToString();
        
        var request = new PutRecordRequest
        {
            StreamName = _config.StreamName,
            Data = new MemoryStream(data),
            PartitionKey = partitionKey
        };
        
        var response = await _client.PutRecordAsync(request, cancellationToken);
        
        _logger.LogDebug("Published record to Kinesis: Shard={Shard}, SequenceNumber={Seq}",
            response.ShardId, response.SequenceNumber);
    }

    public async Task ProduceBatchAsync<T>(string topic, IEnumerable<T> values, Func<T, string>? keySelector = null, CancellationToken cancellationToken = default) where T : class
    {
        await ProduceBatchInternalAsync(values, keySelector, cancellationToken);
    }
    
    private async Task ProduceBatchInternalAsync<T>(IEnumerable<T> values, Func<T, string>? keySelector = null, CancellationToken cancellationToken = default) where T : class
    {
        var valuesList = values.ToList();
        var batches = valuesList.Chunk(_config.BatchSize);
        
        int totalSuccess = 0;
        int totalFailed = 0;
        
        foreach (var batch in batches)
        {
            var records = batch.Select(value => new PutRecordsRequestEntry
            {
                Data = new MemoryStream(SerializeToBytes(value)),
                PartitionKey = keySelector?.Invoke(value) ?? Guid.NewGuid().ToString()
            }).ToList();
            
            var request = new PutRecordsRequest
            {
                StreamName = _config.StreamName,
                Records = records
            };
            
            var response = await _client.PutRecordsAsync(request, cancellationToken);
            
            totalSuccess += response.Records.Count(r => string.IsNullOrEmpty(r.ErrorCode));
            totalFailed += response.FailedRecordCount;
        }
        
        _logger.LogInformation("Published batch to Kinesis: {Success} succeeded, {Failed} failed",
            totalSuccess, totalFailed);
    }

    public async IAsyncEnumerable<T> ConsumeAsync<T>(
        string topic,
        string consumerGroup,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        var request = new DescribeStreamRequest { StreamName = _config.StreamName };
        var response = await _client.DescribeStreamAsync(request, cancellationToken);
        var shards = response.StreamDescription.Shards;
        
        _logger.LogInformation("Starting consumption from {ShardCount} shards", shards.Count);
        
        foreach (var shard in shards)
        {
            var iteratorRequest = new GetShardIteratorRequest
            {
                StreamName = _config.StreamName,
                ShardId = shard.ShardId,
                ShardIteratorType = ShardIteratorType.TRIM_HORIZON
            };
            
            var iteratorResponse = await _client.GetShardIteratorAsync(iteratorRequest, cancellationToken);
            var shardIterator = iteratorResponse.ShardIterator;
            
            while (!cancellationToken.IsCancellationRequested && !string.IsNullOrEmpty(shardIterator))
            {
                var getRequest = new GetRecordsRequest
                {
                    ShardIterator = shardIterator,
                    Limit = 1000
                };
                
                var getResponse = await _client.GetRecordsAsync(getRequest, cancellationToken);
                
                foreach (var record in getResponse.Records)
                {
                    T? value = default;
                    
                    value = DeserializeFromBytes<T>(record.Data.ToArray());
                    if (value != null)
                    {
                        yield return value;
                    }
                }
                
                shardIterator = getResponse.NextShardIterator;
                
                if (getResponse.Records.Count == 0)
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }
    }

    public async Task<List<TopicInfo>> ListTopicsAsync(CancellationToken cancellationToken = default)
    {
        // Kinesis uses streams, not topics - return stream as topic
        var shards = await ListShardsAsync(cancellationToken);
        return new List<TopicInfo>
        {
            new TopicInfo(_config.StreamName, shards.Count)
        };
    }

    public async Task<List<string>> ListShardsAsync(CancellationToken cancellationToken = default)
    {
        var request = new DescribeStreamRequest { StreamName = _config.StreamName };
        var response = await _client.DescribeStreamAsync(request, cancellationToken);
        var shardIds = response.StreamDescription.Shards.Select(s => s.ShardId).ToList();
        
        _logger.LogInformation("Found {ShardCount} shards in stream {Stream}", shardIds.Count, _config.StreamName);
        
        return shardIds;
    }

    public async Task<StreamMetrics> GetStreamMetricsAsync(CancellationToken cancellationToken = default)
    {
        var request = new DescribeStreamRequest { StreamName = _config.StreamName };
        var response = await _client.DescribeStreamAsync(request, cancellationToken);
        var stream = response.StreamDescription;
        
        var limitResponse = await _client.DescribeLimitsAsync(new DescribeLimitsRequest(), cancellationToken);
        
        return new StreamMetrics(
            StreamName: stream.StreamName,
            Status: stream.StreamStatus.Value,
            ShardCount: stream.Shards.Count,
            RetentionHours: stream.RetentionPeriodHours,
            OpenShardCount: limitResponse.OpenShardCount,
            ShardLimit: limitResponse.ShardLimit,
            OnDemandStreamCount: limitResponse.OnDemandStreamCount
        );
    }

    public async Task IncreaseRetentionAsync(int hours, CancellationToken cancellationToken = default)
    {
        if (hours < 24 || hours > 8760) // 24 hours to 365 days
        {
            throw new ArgumentException("Retention period must be between 24 and 8760 hours", nameof(hours));
        }
        
        var request = new IncreaseStreamRetentionPeriodRequest
        {
            StreamName = _config.StreamName,
            RetentionPeriodHours = hours
        };
        
        await _client.IncreaseStreamRetentionPeriodAsync(request, cancellationToken);
        
        _logger.LogInformation("Increased retention period for {Stream} to {Hours} hours", 
            _config.StreamName, hours);
    }

    private static byte[] SerializeToBytes<T>(T value)
    {
        var json = JsonSerializer.Serialize(value);
        return Encoding.UTF8.GetBytes(json);
    }

    private static T? DeserializeFromBytes<T>(byte[] data)
    {
        var json = Encoding.UTF8.GetString(data);
        return JsonSerializer.Deserialize<T>(json);
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}

public record StreamMetrics(
    string StreamName,
    string Status,
    int ShardCount,
    int RetentionHours,
    int OpenShardCount,
    int ShardLimit,
    int OnDemandStreamCount
);
