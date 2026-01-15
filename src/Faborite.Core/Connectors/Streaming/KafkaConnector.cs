using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Connectors.Streaming;

/// <summary>
/// Production-ready Apache Kafka connector for distributed streaming.
/// Issue #143 - Apache Kafka connector
/// </summary>
public class KafkaConnector : IAsyncDisposable
{
    private readonly ILogger<KafkaConnector> _logger;
    private readonly ProducerConfig _producerConfig;
    private readonly ConsumerConfig _consumerConfig;
    private IProducer<string, string>? _producer;
    private IConsumer<string, string>? _consumer;

    public KafkaConnector(
        ILogger<KafkaConnector> logger,
        string bootstrapServers,
        string? groupId = null,
        string? saslUsername = null,
        string? saslPassword = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MaxInFlight = 5,
            CompressionType = CompressionType.Snappy,
            LingerMs = 10,
            BatchSize = 16384,
            MessageTimeoutMs = 300000
        };

        _consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId ?? $"faborite-{Guid.NewGuid()}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            MaxPollIntervalMs = 300000
        };

        if (!string.IsNullOrEmpty(saslUsername) && !string.IsNullOrEmpty(saslPassword))
        {
            _producerConfig.SecurityProtocol = SecurityProtocol.SaslSsl;
            _producerConfig.SaslMechanism = SaslMechanism.Plain;
            _producerConfig.SaslUsername = saslUsername;
            _producerConfig.SaslPassword = saslPassword;

            _consumerConfig.SecurityProtocol = SecurityProtocol.SaslSsl;
            _consumerConfig.SaslMechanism = SaslMechanism.Plain;
            _consumerConfig.SaslUsername = saslUsername;
            _consumerConfig.SaslPassword = saslPassword;
        }

        _logger.LogInformation("Kafka connector initialized for {Servers}", bootstrapServers);
    }

    public async Task<DeliveryResult<string, string>> ProduceAsync(
        string topic,
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _producer ??= new ProducerBuilder<string, string>(_producerConfig)
                .SetErrorHandler((_, e) => _logger.LogError("Kafka producer error: {Reason}", e.Reason))
                .Build();

            var message = new Message<string, string>
            {
                Key = key,
                Value = value,
                Timestamp = Timestamp.Default
            };

            var result = await _producer.ProduceAsync(topic, message, cancellationToken);

            _logger.LogDebug("Produced message to {Topic} partition {Partition} offset {Offset}",
                topic, result.Partition.Value, result.Offset.Value);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to produce message to {Topic}", topic);
            throw;
        }
    }

    public async Task<int> ProduceBatchAsync(
        string topic,
        List<(string Key, string Value)> messages,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Producing batch of {Count} messages to {Topic}", messages.Count, topic);

            _producer ??= new ProducerBuilder<string, string>(_producerConfig)
                .SetErrorHandler((_, e) => _logger.LogError("Kafka producer error: {Reason}", e.Reason))
                .Build();

            var tasks = messages.Select(msg =>
                _producer.ProduceAsync(topic, new Message<string, string>
                {
                    Key = msg.Key,
                    Value = msg.Value
                }, cancellationToken)
            ).ToList();

            await Task.WhenAll(tasks);

            _logger.LogInformation("Produced {Count} messages to {Topic}", messages.Count, topic);
            return messages.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to produce batch to {Topic}", topic);
            throw;
        }
    }

    public async IAsyncEnumerable<ConsumeResult<string, string>> ConsumeAsync(
        string topic,
        CancellationToken cancellationToken = default)
    {
        _consumer ??= new ConsumerBuilder<string, string>(_consumerConfig)
            .SetErrorHandler((_, e) => _logger.LogError("Kafka consumer error: {Reason}", e.Reason))
            .Build();

        _consumer.Subscribe(topic);

        _logger.LogInformation("Started consuming from topic {Topic}", topic);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result = null;

                try
                {
                    result = _consumer.Consume(TimeSpan.FromSeconds(1));

                    if (result != null && !result.IsPartitionEOF)
                    {
                        yield return result;

                        // Manually commit offset after processing
                        _consumer.StoreOffset(result);
                        _consumer.Commit(result);
                    }
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Consume error");
                }
            }
        }
        finally
        {
            _consumer.Close();
            _logger.LogInformation("Stopped consuming from topic {Topic}", topic);
        }
    }

    public async Task<List<string>> ListTopicsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing Kafka topics");

            using var adminClient = new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = _producerConfig.BootstrapServers
            }).Build();

            var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(10));
            var topics = metadata.Topics
                .Where(t => !t.Topic.StartsWith("__"))
                .Select(t => t.Topic)
                .ToList();

            _logger.LogInformation("Found {Count} topics", topics.Count);
            return await Task.FromResult(topics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list topics");
            throw;
        }
    }

    public async Task CreateTopicAsync(
        string topicName,
        int numPartitions = 3,
        short replicationFactor = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating topic {Topic} with {Partitions} partitions",
                topicName, numPartitions);

            using var adminClient = new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = _producerConfig.BootstrapServers
            }).Build();

            await adminClient.CreateTopicsAsync(new[]
            {
                new TopicSpecification
                {
                    Name = topicName,
                    NumPartitions = numPartitions,
                    ReplicationFactor = replicationFactor
                }
            });

            _logger.LogInformation("Topic {Topic} created successfully", topicName);
        }
        catch (CreateTopicsException ex)
        {
            _logger.LogError(ex, "Failed to create topic {Topic}", topicName);
            throw;
        }
    }

    public async Task DeleteTopicAsync(string topicName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting topic {Topic}", topicName);

            using var adminClient = new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = _producerConfig.BootstrapServers
            }).Build();

            await adminClient.DeleteTopicsAsync(new[] { topicName });

            _logger.LogInformation("Topic {Topic} deleted successfully", topicName);
        }
        catch (DeleteTopicsException ex)
        {
            _logger.LogError(ex, "Failed to delete topic {Topic}", topicName);
            throw;
        }
    }

    public async Task<KafkaTopicInfo> GetTopicInfoAsync(
        string topicName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var adminClient = new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = _producerConfig.BootstrapServers
            }).Build();

            var metadata = adminClient.GetMetadata(topicName, TimeSpan.FromSeconds(10));
            var topic = metadata.Topics.FirstOrDefault(t => t.Topic == topicName);

            if (topic == null)
                throw new InvalidOperationException($"Topic {topicName} not found");

            var info = new KafkaTopicInfo(
                topic.Topic,
                topic.Partitions.Count,
                topic.Partitions.Select(p => new PartitionInfo(
                    p.PartitionId,
                    p.Leader,
                    p.Replicas.Length
                )).ToList()
            );

            return await Task.FromResult(info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get topic info for {Topic}", topicName);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _producer?.Dispose();
        _consumer?.Dispose();
        await Task.CompletedTask;
        _logger.LogDebug("Kafka connector disposed");
    }
}

public record KafkaTopicInfo(
    string TopicName,
    int PartitionCount,
    List<PartitionInfo> Partitions
);

public record PartitionInfo(
    int PartitionId,
    int Leader,
    int ReplicaCount
);
