using Microsoft.Extensions.Logging;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using System.Text.Json;

namespace Faborite.Core.Connectors.Streaming;

/// <summary>
/// Production-ready Apache Kafka connector for real-time streaming data ingestion.
/// Issue #141 - Apache Kafka streaming data ingestion
/// </summary>
public class KafkaConnector : IStreamingConnector
{
    private readonly ILogger<KafkaConnector> _logger;
    private readonly KafkaConfig _config;
    private IConsumer<string, string>? _consumer;
    private IProducer<string, string>? _producer;

    public string Name => "Apache Kafka";
    public string Version => "1.0.0";

    public KafkaConnector(ILogger<KafkaConnector> logger, KafkaConfig config)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Testing Kafka connection to {BootstrapServers}", _config.BootstrapServers);
        
        try
        {
            var producerConfig = new ProducerConfig
            {
                BootstrapServers = _config.BootstrapServers,
                ClientId = _config.ClientId,
                SecurityProtocol = _config.SecurityProtocol,
                SaslMechanism = _config.SaslMechanism,
                SaslUsername = _config.SaslUsername,
                SaslPassword = _config.SaslPassword
            };

            using var adminClient = new AdminClientBuilder(producerConfig).Build();
            
            // Just test the connection by getting metadata
            var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(5));
            
            _logger.LogInformation("Kafka connection successful. Found {BrokerCount} brokers, {TopicCount} topics",
                metadata.Brokers.Count, metadata.Topics.Count);
            
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kafka connection failed");
            return Task.FromResult(false);
        }
    }

    public Task<ConnectorMetadata> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ConnectorMetadata(
            "Apache Kafka",
            Version,
            new Dictionary<string, string>
            {
                ["Streaming"] = "true",
                ["Partitioning"] = "true",
                ["Replication"] = "true",
                ["ExactlyOnce"] = "true",
                ["ConsumerGroups"] = "true"
            },
            new List<string> { "Produce", "Consume", "StreamProcessing" }
        ));
    }

    public async Task ProduceAsync<T>(
        string topic,
        T value,
        string? key = null,
        CancellationToken cancellationToken = default) where T : class
    {
        _logger.LogInformation("Producing message to topic {Topic}", topic);
        
        _producer ??= CreateProducer();
        
        var message = new Message<string, string>
        {
            Key = key ?? Guid.NewGuid().ToString(),
            Value = JsonSerializer.Serialize(value)
        };

        try
        {
            var deliveryResult = await _producer.ProduceAsync(topic, message, cancellationToken);
            
            _logger.LogInformation("Message delivered to {Topic} partition {Partition} at offset {Offset}",
                deliveryResult.Topic, deliveryResult.Partition.Value, deliveryResult.Offset.Value);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "Failed to produce message to {Topic}", topic);
            throw;
        }
    }

    public async Task ProduceBatchAsync<T>(
        string topic,
        IEnumerable<T> values,
        Func<T, string>? keySelector = null,
        CancellationToken cancellationToken = default) where T : class
    {
        _logger.LogInformation("Producing batch to topic {Topic}", topic);
        
        _producer ??= CreateProducer();
        
        var tasks = new List<Task<DeliveryResult<string, string>>>();
        
        foreach (var value in values)
        {
            var message = new Message<string, string>
            {
                Key = keySelector?.Invoke(value) ?? Guid.NewGuid().ToString(),
                Value = JsonSerializer.Serialize(value)
            };

            tasks.Add(_producer.ProduceAsync(topic, message, cancellationToken));
        }

        await Task.WhenAll(tasks);
        
        _logger.LogInformation("Batch of {Count} messages delivered to {Topic}", tasks.Count, topic);
    }

    public async IAsyncEnumerable<T> ConsumeAsync<T>(
        string topic,
        string consumerGroup,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        _logger.LogInformation("Starting to consume from topic {Topic} with group {Group}", topic, consumerGroup);
        
        var consumer = CreateConsumer(consumerGroup);
        consumer.Subscribe(topic);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var consumeResult = consumer.Consume(cancellationToken);
                
                if (consumeResult == null)
                    continue;

                _logger.LogDebug("Consumed message from {Topic} partition {Partition} at offset {Offset}",
                    consumeResult.Topic, consumeResult.Partition.Value, consumeResult.Offset.Value);

                T? message = null;
                try
                {
                    message = JsonSerializer.Deserialize<T>(consumeResult.Message.Value);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to deserialize message from {Topic}", topic);
                    continue;
                }

                if (message != null)
                {
                    yield return message;
                }

                // Commit offset after successful processing
                if (_config.EnableAutoCommit == false)
                {
                    consumer.Commit(consumeResult);
                }
            }
        }
        finally
        {
            consumer.Close();
            consumer.Dispose();
        }
    }

    public async Task<List<TopicInfo>> ListTopicsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Listing Kafka topics");
        
        var adminConfig = new AdminClientConfig
        {
            BootstrapServers = _config.BootstrapServers
        };
        
        using var adminClient = new AdminClientBuilder(adminConfig).Build();
        var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(5));
        
        return await Task.FromResult(metadata.Topics.Select(t => new TopicInfo(
            t.Topic,
            t.Partitions.Count,
            t.Error.IsError ? t.Error.Reason : null
        )).ToList());
    }

    private IProducer<string, string> CreateProducer()
    {
        var config = new ProducerConfig
        {
            BootstrapServers = _config.BootstrapServers,
            ClientId = _config.ClientId,
            SecurityProtocol = _config.SecurityProtocol,
            SaslMechanism = _config.SaslMechanism,
            SaslUsername = _config.SaslUsername,
            SaslPassword = _config.SaslPassword,
            Acks = Acks.All, // Wait for all replicas
            EnableIdempotence = true,
            MaxInFlight = 5,
            MessageTimeoutMs = 30000,
            CompressionType = CompressionType.Snappy
        };

        return new ProducerBuilder<string, string>(config)
            .SetErrorHandler((_, error) =>
            {
                _logger.LogError("Kafka producer error: {Reason}", error.Reason);
            })
            .Build();
    }

    private IConsumer<string, string> CreateConsumer(string groupId)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _config.BootstrapServers,
            GroupId = groupId,
            ClientId = _config.ClientId,
            SecurityProtocol = _config.SecurityProtocol,
            SaslMechanism = _config.SaslMechanism,
            SaslUsername = _config.SaslUsername,
            SaslPassword = _config.SaslPassword,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = _config.EnableAutoCommit,
            EnableAutoOffsetStore = false,
            SessionTimeoutMs = 30000,
            HeartbeatIntervalMs = 3000
        };

        return new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, error) =>
            {
                _logger.LogError("Kafka consumer error: {Reason}", error.Reason);
            })
            .SetPartitionsAssignedHandler((_, partitions) =>
            {
                _logger.LogInformation("Partitions assigned: {Partitions}",
                    string.Join(", ", partitions.Select(p => $"{p.Topic}[{p.Partition.Value}]")));
            })
            .Build();
    }

    public void Dispose()
    {
        _producer?.Dispose();
        _consumer?.Dispose();
    }
}

/// <summary>
/// Kafka connector configuration
/// </summary>
public record KafkaConfig(
    string BootstrapServers,
    string? ClientId = null,
    SecurityProtocol SecurityProtocol = SecurityProtocol.Plaintext,
    SaslMechanism? SaslMechanism = null,
    string? SaslUsername = null,
    string? SaslPassword = null,
    bool EnableAutoCommit = false
);

/// <summary>
/// Topic information
/// </summary>
public record TopicInfo(
    string Name,
    int PartitionCount,
    string? Error = null
);

/// <summary>
/// Interface for streaming data connectors
/// </summary>
public interface IStreamingConnector : IDataConnector, IDisposable
{
    Task ProduceAsync<T>(string topic, T value, string? key = null, CancellationToken cancellationToken = default) where T : class;
    Task ProduceBatchAsync<T>(string topic, IEnumerable<T> values, Func<T, string>? keySelector = null, CancellationToken cancellationToken = default) where T : class;
    IAsyncEnumerable<T> ConsumeAsync<T>(string topic, string consumerGroup, CancellationToken cancellationToken = default) where T : class;
    Task<List<TopicInfo>> ListTopicsAsync(CancellationToken cancellationToken = default);
}
