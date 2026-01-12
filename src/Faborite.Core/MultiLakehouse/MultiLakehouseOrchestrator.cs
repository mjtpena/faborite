using Faborite.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.MultiLakehouse;

/// <summary>
/// Configuration for multi-lakehouse sync operations.
/// Enables syncing from multiple lakehouses in a single operation.
/// Issue #26
/// </summary>
public record MultiLakehouseConfig
{
    public List<LakehouseTarget> Lakehouses { get; init; } = new();
    public bool ParallelSync { get; init; } = true;
    public int MaxParallelLakehouses { get; init; } = 3;
    public string OutputPath { get; init; } = "./local_lakehouse";
}

public record LakehouseTarget
{
    public required string WorkspaceId { get; init; }
    public required string LakehouseId { get; init; }
    public string? Name { get; init; }
    public List<string>? Tables { get; init; }
    public SampleConfig? SampleOverride { get; init; }
}

/// <summary>
/// Orchestrates sync operations across multiple lakehouses.
/// </summary>
public class MultiLakehouseOrchestrator
{
    private readonly ILogger<MultiLakehouseOrchestrator> _logger;

    public MultiLakehouseOrchestrator(ILogger<MultiLakehouseOrchestrator> logger)
    {
        _logger = logger;
    }

    public async Task<MultiLakehouseSyncResult> SyncAsync(
        MultiLakehouseConfig config,
        IProgress<MultiLakehouseProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting multi-lakehouse sync for {Count} lakehouses", config.Lakehouses.Count);

        var results = new List<LakehouseSyncResult>();
        var startTime = DateTime.UtcNow;

        if (config.ParallelSync)
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = config.MaxParallelLakehouses,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(config.Lakehouses, options, async (lakehouse, ct) =>
            {
                var result = await SyncLakehouseAsync(lakehouse, config.OutputPath, progress, ct);
                lock (results) { results.Add(result); }
            });
        }
        else
        {
            foreach (var lakehouse in config.Lakehouses)
            {
                var result = await SyncLakehouseAsync(lakehouse, config.OutputPath, progress, cancellationToken);
                results.Add(result);
            }
        }

        var duration = DateTime.UtcNow - startTime;
        return new MultiLakehouseSyncResult(results, duration);
    }

    private async Task<LakehouseSyncResult> SyncLakehouseAsync(
        LakehouseTarget target,
        string basePath,
        IProgress<MultiLakehouseProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Syncing lakehouse {Name} ({Id})", target.Name ?? "Unnamed", target.LakehouseId);

            var config = new Configuration.FaboriteConfig
            {
                WorkspaceId = target.WorkspaceId,
                LakehouseId = target.LakehouseId,
                Sync = new Configuration.SyncConfig
                {
                    LocalPath = Path.Combine(basePath, target.Name ?? target.LakehouseId)
                }
            };

            // Create service and sync
            using var service = new FaboriteService(config);
            var summary = await service.SyncAsync(target.Tables, target.SampleOverride, null, null, cancellationToken);

            progress?.Report(new MultiLakehouseProgress(target.Name ?? target.LakehouseId, summary.SuccessfulTables, summary.Tables.Count));

            return new LakehouseSyncResult(target.LakehouseId, true, summary.TotalRows, summary.SuccessfulTables, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync lakehouse {Id}", target.LakehouseId);
            return new LakehouseSyncResult(target.LakehouseId, false, 0, 0, ex.Message);
        }
    }
}

public record MultiLakehouseProgress(string LakehouseName, int CompletedTables, int TotalTables);

public record LakehouseSyncResult(string LakehouseId, bool Success, long TotalRows, int TablesSynced, string? Error);

public record MultiLakehouseSyncResult(List<LakehouseSyncResult> Results, TimeSpan Duration)
{
    public int SuccessfulLakehouses => Results.Count(r => r.Success);
    public int FailedLakehouses => Results.Count(r => !r.Success);
    public long TotalRows => Results.Sum(r => r.TotalRows);
    public int TotalTables => Results.Sum(r => r.TablesSynced);
}
