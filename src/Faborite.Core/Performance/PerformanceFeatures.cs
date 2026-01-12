using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Distributed;

namespace Faborite.Core.Caching;

/// <summary>
/// Redis caching implementation. Issue #104
/// </summary>
public class RedisCacheManager
{
    private readonly ILogger<RedisCacheManager> _logger;
    private readonly IDistributedCache _cache;

    public RedisCacheManager(ILogger<RedisCacheManager> logger, IDistributedCache cache)
    {
        _logger = logger;
        _cache = cache;
    }

    public async Task<T?> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null)
    {
        var cached = await _cache.GetStringAsync(key);
        if (cached != null)
        {
            _logger.LogDebug("Cache hit: {Key}", key);
            return System.Text.Json.JsonSerializer.Deserialize<T>(cached);
        }

        _logger.LogDebug("Cache miss: {Key}", key);
        var value = await factory();
        var json = System.Text.Json.JsonSerializer.Serialize(value);
        
        await _cache.SetStringAsync(key, json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiry ?? TimeSpan.FromHours(1)
        });

        return value;
    }
}

/// <summary>
/// Batch processing engine. Issue #107
/// </summary>
public class BatchProcessor
{
    private readonly ILogger<BatchProcessor> _logger;

    public BatchProcessor(ILogger<BatchProcessor> logger)
    {
        _logger = logger;
    }

    public async Task<BatchResult> ProcessBatchAsync<T>(List<T> items, Func<T, Task> processor, int batchSize = 100)
    {
        _logger.LogInformation("Processing {Count} items in batches of {Size}", items.Count, batchSize);
        
        var batches = items.Chunk(batchSize);
        var processed = 0;
        var failed = 0;

        foreach (var batch in batches)
        {
            try
            {
                await Task.WhenAll(batch.Select(processor));
                processed += batch.Length;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch processing failed");
                failed += batch.Length;
            }
        }

        return new BatchResult(processed, failed);
    }
}

public record BatchResult(int Processed, int Failed);

/// <summary>
/// Memory optimization utilities. Issue #109
/// </summary>
public class MemoryOptimizer
{
    private readonly ILogger<MemoryOptimizer> _logger;

    public MemoryOptimizer(ILogger<MemoryOptimizer> logger)
    {
        _logger = logger;
    }

    public void ForceGarbageCollection()
    {
        _logger.LogInformation("Forcing garbage collection");
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
    }

    public MemoryStats GetMemoryStats()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var gcMemory = GC.GetTotalMemory(false);
        
        return new MemoryStats(
            WorkingSetMB: process.WorkingSet64 / 1024 / 1024,
            ManagedMemoryMB: gcMemory / 1024 / 1024,
            Gen0Collections: GC.CollectionCount(0),
            Gen1Collections: GC.CollectionCount(1),
            Gen2Collections: GC.CollectionCount(2)
        );
    }
}

public record MemoryStats(long WorkingSetMB, long ManagedMemoryMB, int Gen0Collections, int Gen1Collections, int Gen2Collections);

/// <summary>
/// Performance profiler. Issue #113
/// </summary>
public class PerformanceProfiler
{
    private readonly ILogger<PerformanceProfiler> _logger;
    private readonly List<ProfileEntry> _entries = new();

    public PerformanceProfiler(ILogger<PerformanceProfiler> logger)
    {
        _logger = logger;
    }

    public IDisposable Profile(string operation)
    {
        return new ProfilerScope(operation, this);
    }

    internal void RecordEntry(ProfileEntry entry)
    {
        _entries.Add(entry);
        _logger.LogDebug("Profile: {Operation} took {Duration}ms", entry.Operation, entry.Duration.TotalMilliseconds);
    }

    public ProfileReport GenerateReport()
    {
        var grouped = _entries.GroupBy(e => e.Operation);
        var stats = grouped.Select(g => new OperationStats(
            Operation: g.Key,
            Count: g.Count(),
            TotalDuration: TimeSpan.FromMilliseconds(g.Sum(e => e.Duration.TotalMilliseconds)),
            AvgDuration: TimeSpan.FromMilliseconds(g.Average(e => e.Duration.TotalMilliseconds)),
            MinDuration: g.Min(e => e.Duration),
            MaxDuration: g.Max(e => e.Duration)
        )).ToList();

        return new ProfileReport(stats);
    }

    private class ProfilerScope : IDisposable
    {
        private readonly string _operation;
        private readonly PerformanceProfiler _profiler;
        private readonly DateTime _start = DateTime.UtcNow;

        public ProfilerScope(string operation, PerformanceProfiler profiler)
        {
            _operation = operation;
            _profiler = profiler;
        }

        public void Dispose()
        {
            var duration = DateTime.UtcNow - _start;
            _profiler.RecordEntry(new ProfileEntry(_operation, duration));
        }
    }
}

public record ProfileEntry(string Operation, TimeSpan Duration);
public record OperationStats(string Operation, int Count, TimeSpan TotalDuration, TimeSpan AvgDuration, TimeSpan MinDuration, TimeSpan MaxDuration);
public record ProfileReport(List<OperationStats> Operations);

/// <summary>
/// Capacity planning and forecasting. Issue #115
/// </summary>
public class CapacityPlanner
{
    private readonly ILogger<CapacityPlanner> _logger;

    public CapacityPlanner(ILogger<CapacityPlanner> logger)
    {
        _logger = logger;
    }

    public CapacityForecast ForecastCapacity(List<UsageDataPoint> historicalData, int daysAhead = 30)
    {
        _logger.LogInformation("Forecasting capacity for {Days} days ahead", daysAhead);

        // Simple linear regression
        var avgGrowth = historicalData.Count > 1
            ? (historicalData[^1].Value - historicalData[0].Value) / historicalData.Count
            : 0;

        var currentCapacity = 1000.0; // GB
        var currentUsage = historicalData.LastOrDefault()?.Value ?? 0;
        var forecastedUsage = currentUsage + (avgGrowth * daysAhead);
        var utilizationPercent = (forecastedUsage / currentCapacity) * 100;

        var recommendation = utilizationPercent switch
        {
            > 80 => "Scale up immediately",
            > 60 => "Plan to scale up within 30 days",
            < 20 => "Consider scaling down to save costs",
            _ => "Current capacity is adequate"
        };

        return new CapacityForecast(
            CurrentUsage: currentUsage,
            ForecastedUsage: forecastedUsage,
            CurrentCapacity: currentCapacity,
            UtilizationPercent: utilizationPercent,
            Recommendation: recommendation
        );
    }
}

public record UsageDataPoint(DateTime Date, double Value);
public record CapacityForecast(double CurrentUsage, double ForecastedUsage, double CurrentCapacity, double UtilizationPercent, string Recommendation);
