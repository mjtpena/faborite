using Microsoft.Extensions.Logging;

namespace Faborite.Api.Analytics;

/// <summary>
/// API usage analytics and metrics collection.
/// Issue #65
/// </summary>
public class ApiAnalyticsCollector
{
    private readonly ILogger<ApiAnalyticsCollector> _logger;
    private readonly List<ApiCallRecord> _records = new();

    public ApiAnalyticsCollector(ILogger<ApiAnalyticsCollector> logger)
    {
        _logger = logger;
    }

    public void RecordApiCall(ApiCallRecord record)
    {
        _records.Add(record);
        _logger.LogDebug("Recorded API call: {Method} {Path} - {Status} in {Duration}ms",
            record.Method, record.Path, record.StatusCode, record.Duration.TotalMilliseconds);
    }

    public ApiAnalytics GetAnalytics(DateTime startDate, DateTime endDate)
    {
        var filtered = _records.Where(r => r.Timestamp >= startDate && r.Timestamp <= endDate).ToList();

        var totalCalls = filtered.Count;
        var successfulCalls = filtered.Count(r => r.StatusCode < 400);
        var failedCalls = totalCalls - successfulCalls;
        var avgDuration = filtered.Any() ? filtered.Average(r => r.Duration.TotalMilliseconds) : 0;

        var topEndpoints = filtered
            .GroupBy(r => $"{r.Method} {r.Path}")
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new EndpointStats(g.Key, g.Count(), g.Average(r => r.Duration.TotalMilliseconds)))
            .ToList();

        var userStats = filtered
            .GroupBy(r => r.UserId ?? "anonymous")
            .Select(g => new UserStats(g.Key, g.Count(), g.Average(r => r.Duration.TotalMilliseconds)))
            .OrderByDescending(u => u.CallCount)
            .Take(10)
            .ToList();

        return new ApiAnalytics(
            TotalCalls: totalCalls,
            SuccessfulCalls: successfulCalls,
            FailedCalls: failedCalls,
            AverageDuration: avgDuration,
            TopEndpoints: topEndpoints,
            TopUsers: userStats,
            StartDate: startDate,
            EndDate: endDate
        );
    }
}

public record ApiCallRecord(
    string Method,
    string Path,
    int StatusCode,
    TimeSpan Duration,
    DateTime Timestamp,
    string? UserId = null,
    string? TenantId = null);

public record ApiAnalytics(
    int TotalCalls,
    int SuccessfulCalls,
    int FailedCalls,
    double AverageDuration,
    List<EndpointStats> TopEndpoints,
    List<UserStats> TopUsers,
    DateTime StartDate,
    DateTime EndDate);

public record EndpointStats(string Endpoint, int Count, double AvgDuration);
public record UserStats(string UserId, int CallCount, double AvgDuration);

/// <summary>
/// Async long-running operations with status tracking.
/// Issue #67
/// </summary>
public class AsyncOperationManager
{
    private readonly ILogger<AsyncOperationManager> _logger;
    private readonly Dictionary<string, AsyncOperation> _operations = new();

    public AsyncOperationManager(ILogger<AsyncOperationManager> logger)
    {
        _logger = logger;
    }

    public string StartOperation(string operationType, Func<CancellationToken, Task<object>> operation)
    {
        var operationId = Guid.NewGuid().ToString("N");
        var asyncOp = new AsyncOperation(operationId, operationType, operation);
        
        _operations[operationId] = asyncOp;
        
        _ = ExecuteOperationAsync(asyncOp);

        _logger.LogInformation("Started async operation {Id} of type {Type}", operationId, operationType);
        return operationId;
    }

    private async Task ExecuteOperationAsync(AsyncOperation operation)
    {
        try
        {
            operation.Status = OperationStatus.Running;
            operation.Result = await operation.Operation(CancellationToken.None);
            operation.Status = OperationStatus.Completed;
            operation.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            operation.Status = OperationStatus.Failed;
            operation.Error = ex.Message;
            operation.CompletedAt = DateTime.UtcNow;
            _logger.LogError(ex, "Async operation {Id} failed", operation.Id);
        }
    }

    public AsyncOperation? GetOperation(string operationId)
    {
        return _operations.GetValueOrDefault(operationId);
    }

    public List<AsyncOperation> GetAllOperations()
    {
        return _operations.Values.ToList();
    }
}

public enum OperationStatus { Pending, Running, Completed, Failed, Cancelled }

public class AsyncOperation
{
    public string Id { get; }
    public string Type { get; }
    public Func<CancellationToken, Task<object>> Operation { get; }
    public OperationStatus Status { get; set; } = OperationStatus.Pending;
    public object? Result { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public AsyncOperation(string id, string type, Func<CancellationToken, Task<object>> operation)
    {
        Id = id;
        Type = type;
        Operation = operation;
    }
}
