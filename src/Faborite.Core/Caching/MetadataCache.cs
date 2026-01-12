using Microsoft.Extensions.Logging;

namespace Faborite.Core.Caching;

/// <summary>
/// Caches table metadata to reduce API calls.
/// Issue #38
/// </summary>
public class MetadataCache
{
    private readonly ILogger<MetadataCache> _logger;
    private readonly Dictionary<string, CachedMetadata> _cache = new();
    private readonly TimeSpan _defaultTtl;

    public MetadataCache(ILogger<MetadataCache> logger, TimeSpan? ttl = null)
    {
        _logger = logger;
        _defaultTtl = ttl ?? TimeSpan.FromHours(24);
    }

    /// <summary>
    /// Gets cached metadata or fetches if not available/expired.
    /// </summary>
    public async Task<TableSchema> GetOrFetchAsync(
        string tableName,
        Func<Task<TableSchema>> fetchFunc,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = tableName.ToLowerInvariant();

        if (_cache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired())
        {
            _logger.LogDebug("Cache hit for table {Table}", tableName);
            return cached.Schema;
        }

        _logger.LogDebug("Cache miss for table {Table}, fetching metadata", tableName);
        var schema = await fetchFunc();

        _cache[cacheKey] = new CachedMetadata(schema, DateTime.UtcNow.Add(_defaultTtl));
        return schema;
    }

    /// <summary>
    /// Invalidates cache entry for a table.
    /// </summary>
    public void Invalidate(string tableName)
    {
        var cacheKey = tableName.ToLowerInvariant();
        if (_cache.Remove(cacheKey))
        {
            _logger.LogDebug("Invalidated cache for table {Table}", tableName);
        }
    }

    /// <summary>
    /// Clears all cached metadata.
    /// </summary>
    public void Clear()
    {
        var count = _cache.Count;
        _cache.Clear();
        _logger.LogInformation("Cleared {Count} cached metadata entries", count);
    }

    /// <summary>
    /// Removes expired entries from cache.
    /// </summary>
    public void PurgeExpired()
    {
        var expired = _cache.Where(kvp => kvp.Value.IsExpired()).Select(kvp => kvp.Key).ToList();
        
        foreach (var key in expired)
        {
            _cache.Remove(key);
        }

        if (expired.Any())
        {
            _logger.LogDebug("Purged {Count} expired cache entries", expired.Count);
        }
    }

    public CacheStatistics GetStatistics()
    {
        var now = DateTime.UtcNow;
        var valid = _cache.Count(kvp => !kvp.Value.IsExpired());
        var expired = _cache.Count - valid;

        return new CacheStatistics(
            TotalEntries: _cache.Count,
            ValidEntries: valid,
            ExpiredEntries: expired,
            OldestEntry: _cache.Values.Any() ? _cache.Values.Min(v => v.ExpiresAt) : null,
            NewestEntry: _cache.Values.Any() ? _cache.Values.Max(v => v.ExpiresAt) : null
        );
    }
}

internal record CachedMetadata(TableSchema Schema, DateTime ExpiresAt)
{
    public bool IsExpired() => DateTime.UtcNow >= ExpiresAt;
}

public record TableSchema(
    string TableName,
    List<ColumnSchema> Columns,
    List<string> PrimaryKeys,
    long EstimatedRows,
    DateTime LastModified);

public record ColumnSchema(
    string Name,
    string DataType,
    bool IsNullable,
    int? MaxLength);

public record CacheStatistics(
    int TotalEntries,
    int ValidEntries,
    int ExpiredEntries,
    DateTime? OldestEntry,
    DateTime? NewestEntry);
