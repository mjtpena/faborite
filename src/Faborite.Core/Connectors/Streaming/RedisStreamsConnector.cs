using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Faborite.Core.Connectors.Streaming;

/// <summary>
/// Production-ready Redis Streams connector for high-velocity data ingestion.
/// Issue #144 - Redis Streams for high-velocity data
/// </summary>
public class RedisStreamsConnector : IDisposable
{
    private readonly ILogger<RedisStreamsConnector> _logger;
    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _database;

    public RedisStreamsConnector(
        ILogger<RedisStreamsConnector> logger,
        string connectionString,
        int database = 0)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _redis = ConnectionMultiplexer.Connect(connectionString);
        _database = _redis.GetDatabase(database);

        _logger.LogInformation("Redis Streams connector initialized for database {Database}", database);
    }

    public async Task<string> AddMessageAsync(
        string streamKey,
        Dictionary<string, string> fields,
        string? messageId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Adding message to stream {Stream}", streamKey);

            var nameValues = fields.Select(kvp => new NameValueEntry(kvp.Key, kvp.Value)).ToArray();
            
            var id = await _database.StreamAddAsync(
                streamKey,
                nameValues,
                messageId,
                maxLength: null,
                useApproximateMaxLength: false);

            _logger.LogDebug("Message added to stream {Stream} with ID {Id}", streamKey, id);
            return id.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add message to stream {Stream}", streamKey);
            throw;
        }
    }

    public async Task<int> AddBatchAsync(
        string streamKey,
        List<Dictionary<string, string>> messages,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Adding batch of {Count} messages to stream {Stream}", 
                messages.Count, streamKey);

            var batch = _database.CreateBatch();
            var tasks = new List<Task<RedisValue>>();

            foreach (var message in messages)
            {
                var nameValues = message.Select(kvp => new NameValueEntry(kvp.Key, kvp.Value)).ToArray();
                tasks.Add(batch.StreamAddAsync(streamKey, nameValues));
            }

            batch.Execute();
            await Task.WhenAll(tasks);

            _logger.LogInformation("Batch added: {Count} messages", messages.Count);
            return messages.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add batch to stream {Stream}", streamKey);
            throw;
        }
    }

    public async IAsyncEnumerable<StreamMessage> ReadStreamAsync(
        string streamKey,
        string consumerId = "0",
        int count = 10)
    {
        _logger.LogDebug("Reading stream {Stream} from {Id}", streamKey, consumerId);

        while (true)
        {
            var entries = await _database.StreamReadAsync(
                streamKey,
                consumerId,
                count);

            if (entries.Length == 0)
            {
                await Task.Delay(100); // Brief pause before retrying
                continue;
            }

            foreach (var entry in entries)
            {
                var fields = entry.Values.ToDictionary(
                    nv => nv.Name.ToString(),
                    nv => nv.Value.ToString());

                yield return new StreamMessage(
                    entry.Id.ToString(),
                    streamKey,
                    fields,
                    DateTime.UtcNow
                );
            }
        }
    }

    public async Task<ConsumerGroupInfo> CreateConsumerGroupAsync(
        string streamKey,
        string groupName,
        string startPosition = "0",
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating consumer group {Group} for stream {Stream}", 
                groupName, streamKey);

            var created = await _database.StreamCreateConsumerGroupAsync(
                streamKey,
                groupName,
                startPosition);

            _logger.LogInformation("Consumer group created: {Group}", groupName);
            return new ConsumerGroupInfo(groupName, streamKey, 0, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create consumer group {Group}", groupName);
            throw;
        }
    }

    public async IAsyncEnumerable<StreamMessage> ReadGroupAsync(
        string streamKey,
        string groupName,
        string consumerName,
        int count = 10)
    {
        _logger.LogDebug("Reading stream {Stream} as consumer {Consumer} in group {Group}",
            streamKey, consumerName, groupName);

        while (true)
        {
            var entries = await _database.StreamReadGroupAsync(
                streamKey,
                groupName,
                consumerName,
                ">",
                count);

            if (entries.Length == 0)
            {
                await Task.Delay(100);
                continue;
            }

            foreach (var entry in entries)
            {
                var fields = entry.Values.ToDictionary(
                    nv => nv.Name.ToString(),
                    nv => nv.Value.ToString());

                yield return new StreamMessage(
                    entry.Id.ToString(),
                    streamKey,
                    fields,
                    DateTime.UtcNow
                );
            }
        }
    }

    public async Task AcknowledgeAsync(
        string streamKey,
        string groupName,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _database.StreamAcknowledgeAsync(streamKey, groupName, messageId);
            _logger.LogDebug("Acknowledged message {Id} in group {Group}", messageId, groupName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acknowledge message {Id}", messageId);
            throw;
        }
    }

    public async Task<StreamInfo> GetStreamInfoAsync(
        string streamKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await _database.StreamInfoAsync(streamKey);
            
            return new StreamInfo(
                streamKey,
                (int)info.Length,
                info.FirstEntry.Id.ToString(),
                info.LastEntry.Id.ToString(),
                info.RadixTreeKeys,
                info.RadixTreeNodes
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get stream info for {Stream}", streamKey);
            throw;
        }
    }

    public async Task<long> TrimStreamAsync(
        string streamKey,
        int maxLength,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Trimming stream {Stream} to {MaxLength}", streamKey, maxLength);

            var trimmed = await _database.StreamTrimAsync(streamKey, maxLength);

            _logger.LogInformation("Trimmed {Count} messages from stream {Stream}", trimmed, streamKey);
            return trimmed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trim stream {Stream}", streamKey);
            throw;
        }
    }

    public void Dispose()
    {
        _redis?.Dispose();
        _logger.LogDebug("Redis Streams connector disposed");
    }
}

public record StreamMessage(
    string Id,
    string StreamKey,
    Dictionary<string, string> Fields,
    DateTime Timestamp
);

public record ConsumerGroupInfo(
    string GroupName,
    string StreamKey,
    int Consumers,
    int PendingMessages
);

public record StreamInfo(
    string Key,
    int Length,
    string FirstEntryId,
    string LastEntryId,
    long RadixTreeKeys,
    long RadixTreeNodes
);
