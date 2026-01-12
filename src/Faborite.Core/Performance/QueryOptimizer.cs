using Microsoft.Extensions.Logging;

namespace Faborite.Core.Performance;

/// <summary>
/// Performance optimization and query execution planning.
/// Issue #105
/// </summary>
public class QueryOptimizer
{
    private readonly ILogger<QueryOptimizer> _logger;

    public QueryOptimizer(ILogger<QueryOptimizer> logger)
    {
        _logger = logger;
    }

    public OptimizedQuery Optimize(string query, QueryHints hints)
    {
        _logger.LogInformation("Optimizing query");

        var plan = AnalyzeQuery(query);
        var optimizations = ApplyOptimizations(plan, hints);

        return new OptimizedQuery(
            OriginalQuery: query,
            OptimizedSql: optimizations.Query,
            EstimatedCost: optimizations.Cost,
            Optimizations: optimizations.Applied
        );
    }

    private QueryPlan AnalyzeQuery(string query)
    {
        return new QueryPlan(
            Operations: new List<string> { "scan", "filter", "aggregate" },
            EstimatedRows: 100000,
            EstimatedCost: 1000
        );
    }

    private (string Query, double Cost, List<string> Applied) ApplyOptimizations(QueryPlan plan, QueryHints hints)
    {
        var optimizations = new List<string>();
        var query = "OPTIMIZED: " + plan.Operations.First();
        var cost = plan.EstimatedCost;

        if (hints.UseIndexes)
        {
            optimizations.Add("index_scan");
            cost *= 0.1;
        }

        if (hints.PushDownFilters)
        {
            optimizations.Add("filter_pushdown");
            cost *= 0.5;
        }

        if (hints.ParallelExecution)
        {
            optimizations.Add("parallel_execution");
            cost *= 0.3;
        }

        return (query, cost, optimizations);
    }
}

public record QueryHints(
    bool UseIndexes = true,
    bool PushDownFilters = true,
    bool ParallelExecution = true);

public record QueryPlan(List<string> Operations, long EstimatedRows, double EstimatedCost);
public record OptimizedQuery(string OriginalQuery, string OptimizedSql, double EstimatedCost, List<string> Optimizations);

/// <summary>
/// Connection pooling for database connections.
/// Issue #106
/// </summary>
public class ConnectionPool
{
    private readonly ILogger<ConnectionPool> _logger;
    private readonly Queue<PooledConnection> _available = new();
    private readonly HashSet<PooledConnection> _inUse = new();
    private readonly int _maxSize;
    private readonly SemaphoreSlim _semaphore;

    public ConnectionPool(ILogger<ConnectionPool> logger, int maxSize = 100)
    {
        _logger = logger;
        _maxSize = maxSize;
        _semaphore = new SemaphoreSlim(maxSize, maxSize);
    }

    public async Task<PooledConnection> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);

        lock (_available)
        {
            if (_available.TryDequeue(out var conn))
            {
                _inUse.Add(conn);
                _logger.LogDebug("Reused connection from pool");
                return conn;
            }
        }

        var newConn = new PooledConnection(Guid.NewGuid().ToString("N"));
        lock (_inUse)
        {
            _inUse.Add(newConn);
        }

        _logger.LogDebug("Created new connection");
        return newConn;
    }

    public void Release(PooledConnection connection)
    {
        lock (_inUse)
        {
            _inUse.Remove(connection);
        }

        lock (_available)
        {
            _available.Enqueue(connection);
        }

        _semaphore.Release();
        _logger.LogDebug("Released connection to pool");
    }

    public PoolStatistics GetStatistics()
    {
        return new PoolStatistics(
            TotalConnections: _inUse.Count + _available.Count,
            AvailableConnections: _available.Count,
            InUseConnections: _inUse.Count,
            MaxSize: _maxSize
        );
    }
}

public record PooledConnection(string Id);
public record PoolStatistics(int TotalConnections, int AvailableConnections, int InUseConnections, int MaxSize);

/// <summary>
/// Auto-scaling coordinator for distributed workloads.
/// Issue #112
/// </summary>
public class AutoScaler
{
    private readonly ILogger<AutoScaler> _logger;
    private int _currentWorkers = 1;
    private readonly int _minWorkers = 1;
    private readonly int _maxWorkers = 10;

    public AutoScaler(ILogger<AutoScaler> logger)
    {
        _logger = logger;
    }

    public async Task<ScalingDecision> EvaluateAsync(WorkloadMetrics metrics, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Evaluating auto-scaling for workload");

        var targetWorkers = CalculateTargetWorkers(metrics);
        
        if (targetWorkers > _currentWorkers)
        {
            return new ScalingDecision(ScalingAction.ScaleUp, targetWorkers - _currentWorkers, $"Scale up to {targetWorkers} workers");
        }
        else if (targetWorkers < _currentWorkers)
        {
            return new ScalingDecision(ScalingAction.ScaleDown, _currentWorkers - targetWorkers, $"Scale down to {targetWorkers} workers");
        }

        return new ScalingDecision(ScalingAction.NoChange, 0, "Workload within optimal range");
    }

    private int CalculateTargetWorkers(WorkloadMetrics metrics)
    {
        // Simple CPU-based scaling
        if (metrics.CpuUtilization > 80)
            return Math.Min(_currentWorkers + 2, _maxWorkers);
        
        if (metrics.CpuUtilization < 20)
            return Math.Max(_currentWorkers - 1, _minWorkers);

        return _currentWorkers;
    }
}

public enum ScalingAction { ScaleUp, ScaleDown, NoChange }

public record WorkloadMetrics(double CpuUtilization, double MemoryUtilization, int QueueDepth);
public record ScalingDecision(ScalingAction Action, int WorkerDelta, string Reason);
