using Microsoft.Extensions.Logging;

namespace Faborite.Core.Scaling;

/// <summary>
/// Distributed worker coordinator for parallel processing.
/// Issue #103
/// </summary>
public class DistributedWorkerCoordinator
{
    private readonly ILogger<DistributedWorkerCoordinator> _logger;
    private readonly List<WorkerNode> _workers = new();
    private readonly Queue<WorkItem> _workQueue = new();

    public DistributedWorkerCoordinator(ILogger<DistributedWorkerCoordinator> logger)
    {
        _logger = logger;
    }

    public void RegisterWorker(WorkerNode worker)
    {
        _workers.Add(worker);
        _logger.LogInformation("Registered worker: {Id} at {Endpoint}", worker.Id, worker.Endpoint);
    }

    public async Task<WorkResult> DistributeWorkAsync(
        List<WorkItem> items,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Distributing {Count} work items across {Workers} workers", 
            items.Count, _workers.Count);

        var tasks = items.Select(item => AssignWorkToWorkerAsync(item, cancellationToken)).ToList();
        var results = await Task.WhenAll(tasks);

        return new WorkResult(
            TotalItems: items.Count,
            SuccessfulItems: results.Count(r => r.Success),
            FailedItems: results.Count(r => !r.Success),
            Duration: results.Max(r => r.Duration)
        );
    }

    private async Task<WorkItemResult> AssignWorkToWorkerAsync(WorkItem item, CancellationToken ct)
    {
        var worker = SelectWorker();
        if (worker == null)
        {
            return new WorkItemResult(item.Id, false, TimeSpan.Zero, "No available workers");
        }

        var startTime = DateTime.UtcNow;
        
        try
        {
            // In production, would make HTTP/gRPC call to worker
            await Task.Delay(100, ct); // Simulate work
            
            var duration = DateTime.UtcNow - startTime;
            return new WorkItemResult(item.Id, true, duration, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Work item {Id} failed on worker {Worker}", item.Id, worker.Id);
            return new WorkItemResult(item.Id, false, TimeSpan.Zero, ex.Message);
        }
    }

    private WorkerNode? SelectWorker()
    {
        // Round-robin selection
        return _workers.OrderBy(w => w.CurrentLoad).FirstOrDefault();
    }

    public List<WorkerStatus> GetWorkerStatus()
    {
        return _workers.Select(w => new WorkerStatus(
            w.Id,
            w.Endpoint,
            w.IsHealthy,
            w.CurrentLoad,
            w.LastHeartbeat
        )).ToList();
    }
}

public record WorkerNode(string Id, string Endpoint)
{
    public bool IsHealthy { get; set; } = true;
    public int CurrentLoad { get; set; } = 0;
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
}

public record WorkItem(string Id, string Type, object Data);

public record WorkItemResult(string ItemId, bool Success, TimeSpan Duration, string? Error);

public record WorkResult(int TotalItems, int SuccessfulItems, int FailedItems, TimeSpan Duration);

public record WorkerStatus(string Id, string Endpoint, bool IsHealthy, int CurrentLoad, DateTime LastHeartbeat);

/// <summary>
/// Load balancer for distributing requests.
/// Issue #102
/// </summary>
public class LoadBalancer
{
    private readonly ILogger<LoadBalancer> _logger;
    private readonly List<ServerNode> _servers = new();
    private int _currentIndex = 0;

    public LoadBalancer(ILogger<LoadBalancer> logger)
    {
        _logger = logger;
    }

    public void AddServer(ServerNode server)
    {
        _servers.Add(server);
        _logger.LogInformation("Added server to load balancer: {Id}", server.Id);
    }

    public ServerNode? GetNextServer(LoadBalancingStrategy strategy = LoadBalancingStrategy.RoundRobin)
    {
        if (!_servers.Any())
            return null;

        return strategy switch
        {
            LoadBalancingStrategy.RoundRobin => GetRoundRobinServer(),
            LoadBalancingStrategy.LeastConnections => GetLeastConnectionsServer(),
            LoadBalancingStrategy.Random => GetRandomServer(),
            _ => GetRoundRobinServer()
        };
    }

    private ServerNode GetRoundRobinServer()
    {
        var server = _servers[_currentIndex % _servers.Count];
        _currentIndex = (_currentIndex + 1) % _servers.Count;
        return server;
    }

    private ServerNode GetLeastConnectionsServer()
    {
        return _servers.OrderBy(s => s.ActiveConnections).First();
    }

    private ServerNode GetRandomServer()
    {
        return _servers[Random.Shared.Next(_servers.Count)];
    }
}

public enum LoadBalancingStrategy { RoundRobin, LeastConnections, Random }

public record ServerNode(string Id, string Endpoint)
{
    public int ActiveConnections { get; set; } = 0;
    public bool IsHealthy { get; set; } = true;
}
