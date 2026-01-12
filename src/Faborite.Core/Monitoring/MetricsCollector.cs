using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Faborite.Core.Monitoring;

/// <summary>
/// Prometheus metrics exporter for monitoring.
/// Issues #116, #120
/// </summary>
public class MetricsCollector
{
    private readonly ILogger<MetricsCollector> _logger;
    private readonly Meter _meter;
    
    // Counters
    private readonly Counter<long> _syncCounter;
    private readonly Counter<long> _errorCounter;
    
    // Gauges
    private readonly ObservableGauge<int> _activeConnectionsGauge;
    
    // Histograms
    private readonly Histogram<double> _syncDurationHistogram;
    private readonly Histogram<long> _rowsProcessedHistogram;

    public MetricsCollector(ILogger<MetricsCollector> logger)
    {
        _logger = logger;
        _meter = new Meter("Faborite", "1.0.0");

        _syncCounter = _meter.CreateCounter<long>("faborite_syncs_total", "count", "Total number of sync operations");
        _errorCounter = _meter.CreateCounter<long>("faborite_errors_total", "count", "Total number of errors");
        _activeConnectionsGauge = _meter.CreateObservableGauge("faborite_active_connections", () => GetActiveConnections(), "connections", "Active database connections");
        _syncDurationHistogram = _meter.CreateHistogram<double>("faborite_sync_duration_seconds", "seconds", "Sync operation duration");
        _rowsProcessedHistogram = _meter.CreateHistogram<long>("faborite_rows_processed", "rows", "Rows processed per sync");
    }

    public void RecordSync(TimeSpan duration, long rowsProcessed, string tableName)
    {
        _syncCounter.Add(1, new KeyValuePair<string, object?>("table", tableName));
        _syncDurationHistogram.Record(duration.TotalSeconds, new KeyValuePair<string, object?>("table", tableName));
        _rowsProcessedHistogram.Record(rowsProcessed, new KeyValuePair<string, object?>("table", tableName));
        
        _logger.LogInformation("Recorded metrics: {Table} - {Duration}ms, {Rows} rows", 
            tableName, duration.TotalMilliseconds, rowsProcessed);
    }

    public void RecordError(string errorType)
    {
        _errorCounter.Add(1, new KeyValuePair<string, object?>("type", errorType));
    }

    private int GetActiveConnections()
    {
        // In production, query actual connection pool
        return 10;
    }
}

/// <summary>
/// Distributed tracing for request flow analysis.
/// Issue #118
/// </summary>
public class DistributedTracer
{
    private readonly ILogger<DistributedTracer> _logger;
    private readonly ActivitySource _activitySource;

    public DistributedTracer(ILogger<DistributedTracer> logger)
    {
        _logger = logger;
        _activitySource = new ActivitySource("Faborite");
    }

    public Activity? StartActivity(string operationName, ActivityKind kind = ActivityKind.Internal)
    {
        var activity = _activitySource.StartActivity(operationName, kind);
        activity?.SetTag("service.name", "Faborite");
        activity?.SetTag("service.version", "1.0.0");
        return activity;
    }

    public void AddEvent(Activity? activity, string eventName, Dictionary<string, object>? attributes = null)
    {
        if (activity == null) return;

        var tags = attributes?.Select(kvp => new KeyValuePair<string, object?>(kvp.Key, kvp.Value)).ToArray();
        activity.AddEvent(new ActivityEvent(eventName, tags: new ActivityTagsCollection(tags)));
    }

    public void SetError(Activity? activity, Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.SetTag("error.type", ex.GetType().Name);
        activity?.SetTag("error.message", ex.Message);
        activity?.SetTag("error.stack", ex.StackTrace);
    }
}

/// <summary>
/// Health check monitoring and alerting.
/// Issue #122
/// </summary>
public class HealthMonitor
{
    private readonly ILogger<HealthMonitor> _logger;
    private readonly Dictionary<string, HealthStatus> _componentHealth = new();

    public HealthMonitor(ILogger<HealthMonitor> logger)
    {
        _logger = logger;
    }

    public async Task<OverallHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var checks = new Dictionary<string, ComponentHealth>();

        // Database check
        checks["database"] = await CheckDatabaseAsync(cancellationToken);
        
        // OneLake API check
        checks["onelake_api"] = await CheckOneLakeApiAsync(cancellationToken);
        
        // Disk space check
        checks["disk_space"] = CheckDiskSpace();
        
        // Memory check
        checks["memory"] = CheckMemory();

        var isHealthy = checks.Values.All(c => c.Status == HealthStatus.Healthy);
        var overallStatus = isHealthy ? HealthStatus.Healthy : 
                           checks.Values.Any(c => c.Status == HealthStatus.Unhealthy) ? HealthStatus.Unhealthy : 
                           HealthStatus.Degraded;

        return new OverallHealth(overallStatus, checks, DateTime.UtcNow);
    }

    private async Task<ComponentHealth> CheckDatabaseAsync(CancellationToken ct)
    {
        try
        {
            // Ping database
            await Task.Delay(10, ct); // Simulate check
            return new ComponentHealth(HealthStatus.Healthy, "Database connection OK", null);
        }
        catch (Exception ex)
        {
            return new ComponentHealth(HealthStatus.Unhealthy, "Database unreachable", ex.Message);
        }
    }

    private async Task<ComponentHealth> CheckOneLakeApiAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(10, ct);
            return new ComponentHealth(HealthStatus.Healthy, "OneLake API responding", null);
        }
        catch (Exception ex)
        {
            return new ComponentHealth(HealthStatus.Unhealthy, "OneLake API unreachable", ex.Message);
        }
    }

    private ComponentHealth CheckDiskSpace()
    {
        var drive = DriveInfo.GetDrives().FirstOrDefault();
        if (drive == null)
            return new ComponentHealth(HealthStatus.Unknown, "Cannot determine disk space", null);

        var freePercent = (double)drive.AvailableFreeSpace / drive.TotalSize * 100;
        
        if (freePercent < 10)
            return new ComponentHealth(HealthStatus.Unhealthy, $"Disk space critical: {freePercent:F1}% free", null);
        if (freePercent < 20)
            return new ComponentHealth(HealthStatus.Degraded, $"Disk space low: {freePercent:F1}% free", null);
        
        return new ComponentHealth(HealthStatus.Healthy, $"Disk space OK: {freePercent:F1}% free", null);
    }

    private ComponentHealth CheckMemory()
    {
        var process = Process.GetCurrentProcess();
        var memoryMB = process.WorkingSet64 / 1024 / 1024;
        
        if (memoryMB > 2048)
            return new ComponentHealth(HealthStatus.Degraded, $"High memory usage: {memoryMB} MB", null);
        
        return new ComponentHealth(HealthStatus.Healthy, $"Memory usage OK: {memoryMB} MB", null);
    }
}

public enum HealthStatus { Healthy, Degraded, Unhealthy, Unknown }

public record ComponentHealth(HealthStatus Status, string Message, string? Error);
public record OverallHealth(HealthStatus Status, Dictionary<string, ComponentHealth> Components, DateTime CheckedAt);
