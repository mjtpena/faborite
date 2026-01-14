using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Faborite.Core.Connectors.NoSQL;

public record RedisConfig(
    string ConnectionString,
    int Database = 0,
    int ConnectTimeout = 5000,
    int SyncTimeout = 5000,
    bool AbortOnConnectFail = false);

/// <summary>
/// Production-ready Redis connector for caching, key-value storage, and pub/sub.
/// Supports Strings, Hashes, Lists, Sets, Sorted Sets, Streams, and Pub/Sub.
/// </summary>
public class RedisConnector : IDataConnector, IDisposable
{
    private readonly RedisConfig _config;
    private readonly ILogger<RedisConnector> _logger;
    private ConnectionMultiplexer? _connection;
    private IDatabase? _database;

    public string Name => "Redis";
    public string Version => "2.8.16";

    public RedisConnector(RedisConfig config, ILogger<RedisConnector> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var db = await GetDatabaseAsync();
            await db.PingAsync();
            
            _logger.LogInformation("Successfully connected to Redis database {Database}", _config.Database);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Redis");
            return false;
        }
    }

    public async Task<ConnectorMetadata> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        var db = await GetDatabaseAsync();
        var server = _connection!.GetServer(_connection.GetEndPoints().First());
        var info = await server.InfoAsync("Server");
        
        var infoFlat = info.SelectMany(g => g).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        var redisVersion = infoFlat.ContainsKey("redis_version") ? infoFlat["redis_version"] : "unknown";
        var uptime = infoFlat.ContainsKey("uptime_in_seconds") ? infoFlat["uptime_in_seconds"] : "0";
        
        var capabilities = new Dictionary<string, string>
        {
            ["RedisVersion"] = redisVersion,
            ["UptimeSeconds"] = uptime,
            ["Database"] = _config.Database.ToString(),
            ["SupportsStreams"] = "true",
            ["SupportsPubSub"] = "true"
        };

        var operations = new List<string> 
        { 
            "Get", "Set", "Delete", "Exists", "HashGet", "HashSet", "ListPush", "ListPop",
            "SetAdd", "SetMembers", "SortedSetAdd", "StreamAdd", "StreamRead", "Publish", "Subscribe"
        };

        return new ConnectorMetadata(
            Type: "NoSQL-KeyValue",
            Version: Version,
            Capabilities: capabilities,
            SupportedOperations: operations);
    }

    // String operations
    public async Task<string?> GetAsync(string key)
    {
        var db = await GetDatabaseAsync();
        var value = await db.StringGetAsync(key);
        return value.HasValue ? value.ToString() : null;
    }

    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        var json = await GetAsync(key);
        return json != null ? JsonSerializer.Deserialize<T>(json) : null;
    }

    public async Task<bool> SetAsync(string key, string value, TimeSpan? expiry = null)
    {
        var db = await GetDatabaseAsync();
        return await db.StringSetAsync(key, value, expiry);
    }

    public async Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class
    {
        var json = JsonSerializer.Serialize(value);
        return await SetAsync(key, json, expiry);
    }

    public async Task<bool> DeleteAsync(string key)
    {
        var db = await GetDatabaseAsync();
        return await db.KeyDeleteAsync(key);
    }

    public async Task<long> DeleteAsync(params string[] keys)
    {
        var db = await GetDatabaseAsync();
        var redisKeys = keys.Select(k => (RedisKey)k).ToArray();
        return await db.KeyDeleteAsync(redisKeys);
    }

    public async Task<bool> ExistsAsync(string key)
    {
        var db = await GetDatabaseAsync();
        return await db.KeyExistsAsync(key);
    }

    // Hash operations
    public async Task<Dictionary<string, string>> HashGetAllAsync(string key)
    {
        var db = await GetDatabaseAsync();
        var entries = await db.HashGetAllAsync(key);
        return entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
    }

    public async Task<string?> HashGetAsync(string key, string field)
    {
        var db = await GetDatabaseAsync();
        var value = await db.HashGetAsync(key, field);
        return value.HasValue ? value.ToString() : null;
    }

    public async Task<bool> HashSetAsync(string key, string field, string value)
    {
        var db = await GetDatabaseAsync();
        return await db.HashSetAsync(key, field, value);
    }

    public async Task HashSetAsync(string key, Dictionary<string, string> fields)
    {
        var db = await GetDatabaseAsync();
        var entries = fields.Select(kvp => new HashEntry(kvp.Key, kvp.Value)).ToArray();
        await db.HashSetAsync(key, entries);
    }

    // List operations
    public async Task<long> ListPushAsync(string key, string value, bool leftPush = true)
    {
        var db = await GetDatabaseAsync();
        return leftPush 
            ? await db.ListLeftPushAsync(key, value)
            : await db.ListRightPushAsync(key, value);
    }

    public async Task<string?> ListPopAsync(string key, bool leftPop = true)
    {
        var db = await GetDatabaseAsync();
        var value = leftPop
            ? await db.ListLeftPopAsync(key)
            : await db.ListRightPopAsync(key);
        return value.HasValue ? value.ToString() : null;
    }

    public async Task<List<string>> ListRangeAsync(string key, long start = 0, long stop = -1)
    {
        var db = await GetDatabaseAsync();
        var values = await db.ListRangeAsync(key, start, stop);
        return values.Select(v => v.ToString()).ToList();
    }

    // Set operations
    public async Task<bool> SetAddAsync(string key, string value)
    {
        var db = await GetDatabaseAsync();
        return await db.SetAddAsync(key, value);
    }

    public async Task<List<string>> SetMembersAsync(string key)
    {
        var db = await GetDatabaseAsync();
        var members = await db.SetMembersAsync(key);
        return members.Select(m => m.ToString()).ToList();
    }

    public async Task<bool> SetContainsAsync(string key, string value)
    {
        var db = await GetDatabaseAsync();
        return await db.SetContainsAsync(key, value);
    }

    // Sorted Set operations
    public async Task<bool> SortedSetAddAsync(string key, string member, double score)
    {
        var db = await GetDatabaseAsync();
        return await db.SortedSetAddAsync(key, member, score);
    }

    public async Task<List<(string Member, double Score)>> SortedSetRangeWithScoresAsync(
        string key, long start = 0, long stop = -1, bool descending = false)
    {
        var db = await GetDatabaseAsync();
        var order = descending ? Order.Descending : Order.Ascending;
        var values = await db.SortedSetRangeByRankWithScoresAsync(key, start, stop, order);
        return values.Select(v => (v.Element.ToString(), v.Score)).ToList();
    }

    // Stream operations (Redis 5.0+)
    public async Task<string> StreamAddAsync(string key, Dictionary<string, string> fields, string? messageId = null)
    {
        var db = await GetDatabaseAsync();
        var entries = fields.Select(kvp => new NameValueEntry(kvp.Key, kvp.Value)).ToArray();
        var id = await db.StreamAddAsync(key, entries, messageId == null ? "*" : messageId);
        return id.ToString();
    }

    public async Task<List<StreamEntry>> StreamReadAsync(string key, string position = "0-0", int count = 10)
    {
        var db = await GetDatabaseAsync();
        var messages = await db.StreamReadAsync(key, position, count);
        return messages.Select(m => new StreamEntry
        {
            Id = m.Id.ToString(),
            Values = m.Values.ToDictionary(v => v.Name.ToString(), v => v.Value.ToString())
        }).ToList();
    }

    // Pub/Sub operations
    public async Task PublishAsync(string channel, string message)
    {
        if (_connection == null)
            await GetDatabaseAsync();
        
        var subscriber = _connection!.GetSubscriber();
        await subscriber.PublishAsync(RedisChannel.Literal(channel), message);
        
        _logger.LogDebug("Published message to channel {Channel}", channel);
    }

    public async Task SubscribeAsync(string channel, Action<string, string> handler)
    {
        if (_connection == null)
            await GetDatabaseAsync();
        
        var subscriber = _connection!.GetSubscriber();
        await subscriber.SubscribeAsync(RedisChannel.Literal(channel), (ch, msg) =>
        {
            handler(ch.ToString(), msg.ToString());
        });
        
        _logger.LogInformation("Subscribed to channel {Channel}", channel);
    }

    private async Task<IDatabase> GetDatabaseAsync()
    {
        if (_database != null)
            return _database;

        var options = ConfigurationOptions.Parse(_config.ConnectionString);
        options.ConnectTimeout = _config.ConnectTimeout;
        options.SyncTimeout = _config.SyncTimeout;
        options.AbortOnConnectFail = _config.AbortOnConnectFail;

        _connection = await ConnectionMultiplexer.ConnectAsync(options);
        _database = _connection.GetDatabase(_config.Database);
        
        return _database;
    }

    public void Dispose()
    {
        _connection?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class StreamEntry
{
    public string Id { get; init; } = string.Empty;
    public Dictionary<string, string> Values { get; init; } = new();
}
