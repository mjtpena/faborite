using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Azure.Identity;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;
using Azure.Messaging.EventHubs.Processor;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Connectors.Streaming;

public record EventHubsConfig(
    string ConnectionString,
    string EventHubName,
    string ConsumerGroup = "$Default",
    string? BlobStorageConnectionString = null,
    string? BlobContainerName = "checkpoints",
    bool UseAzureIdentity = false,
    string? FullyQualifiedNamespace = null);

/// <summary>
/// Production-ready Azure Event Hubs connector for real-time streaming.
/// Implements producer, consumer with checkpointing, and partition management.
/// </summary>
public class EventHubsConnector : IStreamingConnector, IAsyncDisposable
{
    private readonly EventHubsConfig _config;
    private readonly ILogger<EventHubsConnector> _logger;
    private EventHubProducerClient? _producer;
    private EventProcessorClient? _processor;
    private BlobContainerClient? _checkpointStore;

    public string Name => "Azure Event Hubs";
    public string Version => "5.11.5";

    public EventHubsConnector(EventHubsConfig config, ILogger<EventHubsConnector> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var client = CreateProducerClient();
            var properties = await client.GetEventHubPropertiesAsync(cancellationToken);
            
            _logger.LogInformation("Connected to Event Hub: {Name}, Partitions: {Count}", 
                properties.Name, properties.PartitionIds.Length);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Event Hub: {Hub}", _config.EventHubName);
            return false;
        }
    }

    public async Task<ConnectorMetadata> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        await using var client = CreateProducerClient();
        var properties = await client.GetEventHubPropertiesAsync(cancellationToken);
        
        var capabilities = new Dictionary<string, string>
        {
            ["PartitionCount"] = properties.PartitionIds.Length.ToString(),
            ["CreatedOn"] = properties.CreatedOn.ToString("O"),
            ["SupportsCheckpointing"] = (!string.IsNullOrEmpty(_config.BlobStorageConnectionString)).ToString()
        };

        var operations = new List<string> { "Produce", "ProduceBatch", "Consume", "ListTopics" };

        return new ConnectorMetadata(
            Type: "Streaming",
            Version: Version,
            Capabilities: capabilities,
            SupportedOperations: operations);
    }

    public async Task ProduceAsync<T>(string topic, T value, string? key = null, CancellationToken cancellationToken = default) where T : class
    {
        _producer ??= CreateProducerClient();
        
        var eventData = SerializeToEventData(value);
        if (!string.IsNullOrEmpty(key))
        {
            eventData.Properties["PartitionKey"] = key;
        }
        
        await _producer.SendAsync(new[] { eventData }, cancellationToken);
        
        _logger.LogDebug("Published message to Event Hub: {Hub}", _config.EventHubName);
    }

    public async Task ProduceBatchAsync<T>(string topic, IEnumerable<T> values, Func<T, string>? keySelector = null, CancellationToken cancellationToken = default) where T : class
    {
        _producer ??= CreateProducerClient();
        
        using var eventBatch = await _producer.CreateBatchAsync(cancellationToken);
        
        int successCount = 0;
        int failedCount = 0;
        EventDataBatch? currentBatch = eventBatch;

        foreach (var value in values)
        {
            var eventData = SerializeToEventData(value);
            
            if (keySelector != null)
            {
                eventData.Properties["PartitionKey"] = keySelector(value);
            }
            
            if (!currentBatch.TryAdd(eventData))
            {
                await _producer.SendAsync(currentBatch, cancellationToken);
                successCount += currentBatch.Count;
                
                currentBatch = await _producer.CreateBatchAsync(cancellationToken);
                
                if (!currentBatch.TryAdd(eventData))
                {
                    _logger.LogWarning("Message too large for batch, skipping");
                    failedCount++;
                }
            }
        }

        if (currentBatch.Count > 0)
        {
            await _producer.SendAsync(currentBatch, cancellationToken);
            successCount += currentBatch.Count;
        }

        _logger.LogInformation("Published batch to Event Hub: {Success} succeeded, {Failed} failed", 
            successCount, failedCount);
    }

    public async IAsyncEnumerable<T> ConsumeAsync<T>(
        string topic, 
        string consumerGroup,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        if (_config.BlobStorageConnectionString == null)
        {
            _logger.LogError("BlobStorageConnectionString required for consumption with checkpointing");
            yield break;
        }

        _checkpointStore = new BlobContainerClient(_config.BlobStorageConnectionString, _config.BlobContainerName);
        await _checkpointStore.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        _processor = CreateProcessorClient(consumerGroup);

        var channel = System.Threading.Channels.Channel.CreateUnbounded<T>();

        _processor.ProcessEventAsync += async args =>
        {
            if (args.Data == null) return;

            try
            {
                var message = DeserializeFromEventData<T>(args.Data);
                await channel.Writer.WriteAsync(message, args.CancellationToken);
                
                if (args.Data.SequenceNumber % 10 == 0)
                {
                    await args.UpdateCheckpointAsync(args.CancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing event from partition {Partition}", 
                    args.Partition.PartitionId);
            }
        };

        _processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "Error on partition {Partition}, operation: {Operation}", 
                args.PartitionId, args.Operation);
            return Task.CompletedTask;
        };

        await _processor.StartProcessingAsync(cancellationToken);

        await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return message;
        }
    }

    public async Task<List<TopicInfo>> ListTopicsAsync(CancellationToken cancellationToken = default)
    {
        await using var client = CreateProducerClient();
        var properties = await client.GetEventHubPropertiesAsync(cancellationToken);
        
        return new List<TopicInfo>
        {
            new TopicInfo(properties.Name, properties.PartitionIds.Length)
        };
    }

    private EventHubProducerClient CreateProducerClient()
    {
        if (_config.UseAzureIdentity && !string.IsNullOrEmpty(_config.FullyQualifiedNamespace))
        {
            return new EventHubProducerClient(
                _config.FullyQualifiedNamespace,
                _config.EventHubName,
                new DefaultAzureCredential());
        }

        return new EventHubProducerClient(_config.ConnectionString, _config.EventHubName);
    }

    private EventProcessorClient CreateProcessorClient(string consumerGroup)
    {
        if (_checkpointStore == null)
            throw new InvalidOperationException("Checkpoint store not initialized");

        if (_config.UseAzureIdentity && !string.IsNullOrEmpty(_config.FullyQualifiedNamespace))
        {
            return new EventProcessorClient(
                _checkpointStore,
                consumerGroup,
                _config.FullyQualifiedNamespace,
                _config.EventHubName,
                new DefaultAzureCredential());
        }

        return new EventProcessorClient(
            _checkpointStore,
            consumerGroup,
            _config.ConnectionString,
            _config.EventHubName);
    }

    private static EventData SerializeToEventData<T>(T message)
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        
        var eventData = new EventData(bytes);
        eventData.Properties["MessageType"] = typeof(T).Name;
        eventData.Properties["Timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        return eventData;
    }

    private static T DeserializeFromEventData<T>(EventData eventData)
    {
        var json = Encoding.UTF8.GetString(eventData.Body.ToArray());
        return JsonSerializer.Deserialize<T>(json) 
            ?? throw new InvalidOperationException("Failed to deserialize message");
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_processor != null)
        {
            await _processor.StopProcessingAsync();
            _processor = null;
        }

        if (_producer != null)
        {
            await _producer.DisposeAsync();
            _producer = null;
        }

        GC.SuppressFinalize(this);
    }
}
