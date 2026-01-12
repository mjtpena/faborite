using Faborite.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Preview;

/// <summary>
/// Dry-run mode for previewing sync operations without executing them.
/// Issue #39
/// </summary>
public class SyncPreviewEngine
{
    private readonly ILogger<SyncPreviewEngine> _logger;

    public SyncPreviewEngine(ILogger<SyncPreviewEngine> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generates a preview of what would be synced without actually syncing.
    /// </summary>
    public async Task<SyncPreview> GeneratePreviewAsync(
        FaboriteConfig config,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating sync preview for workspace {WorkspaceId}", config.WorkspaceId);

        var tables = new List<TablePreview>();
        var startTime = DateTime.UtcNow;

        // Mock: In real implementation, this would query OneLake API
        // For now, simulate table discovery
        var tableNames = new[] { "customers", "orders", "products", "events" };

        foreach (var tableName in tableNames)
        {
            if (ShouldSkipTable(tableName, config.Sync))
                continue;

            var preview = await PreviewTableAsync(tableName, config, cancellationToken);
            tables.Add(preview);
        }

        var duration = DateTime.UtcNow - startTime;
        var totalRows = tables.Sum(t => t.EstimatedRows);
        var totalSize = tables.Sum(t => t.EstimatedSizeBytes);

        return new SyncPreview(
            Tables: tables,
            TotalTables: tables.Count,
            TotalRows: totalRows,
            TotalSizeBytes: totalSize,
            EstimatedDuration: EstimateDuration(totalRows),
            GeneratedAt: DateTime.UtcNow,
            Duration: duration
        );
    }

    private async Task<TablePreview> PreviewTableAsync(
        string tableName,
        FaboriteConfig config,
        CancellationToken cancellationToken)
    {
        // Mock metadata - in real implementation, query OneLake
        var totalRows = Random.Shared.Next(1000, 1000000);
        var sizeBytes = totalRows * 100L; // Estimate 100 bytes per row

        var sampleConfig = config.Tables.TryGetValue(tableName, out var tableOverride)
            ? tableOverride.Sample ?? config.Sample
            : config.Sample;

        var rowsToSync = CalculateRowsToSync(totalRows, sampleConfig);
        var sizeToSync = (long)(sizeBytes * ((double)rowsToSync / totalRows));

        var columns = new List<ColumnPreview>
        {
            new("id", "int", false, 4),
            new("name", "string", true, 100),
            new("created_at", "datetime", false, 8)
        };

        return new TablePreview(
            TableName: tableName,
            TotalRows: totalRows,
            EstimatedRows: rowsToSync,
            TotalSizeBytes: sizeBytes,
            EstimatedSizeBytes: sizeToSync,
            Columns: columns,
            SamplingStrategy: sampleConfig.Strategy.ToString(),
            OutputPath: Path.Combine(config.Sync.LocalPath, $"{tableName}.parquet"),
            WillSync: true,
            SkipReason: null
        );
    }

    private long CalculateRowsToSync(long totalRows, SampleConfig config)
    {
        if (config.Strategy == SampleStrategy.Full || totalRows <= config.MaxFullTableRows)
            return totalRows;

        return Math.Min(config.Rows, totalRows);
    }

    private bool ShouldSkipTable(string tableName, SyncConfig config)
    {
        if (config.SkipTables.Contains(tableName))
            return true;

        if (config.IncludeTables != null && !config.IncludeTables.Contains(tableName))
            return true;

        return false;
    }

    private TimeSpan EstimateDuration(long totalRows)
    {
        // Rough estimate: 10,000 rows per second
        var seconds = totalRows / 10000.0;
        return TimeSpan.FromSeconds(Math.Max(1, seconds));
    }
}

public record SyncPreview(
    List<TablePreview> Tables,
    int TotalTables,
    long TotalRows,
    long TotalSizeBytes,
    TimeSpan EstimatedDuration,
    DateTime GeneratedAt,
    TimeSpan Duration);

public record TablePreview(
    string TableName,
    long TotalRows,
    long EstimatedRows,
    long TotalSizeBytes,
    long EstimatedSizeBytes,
    List<ColumnPreview> Columns,
    string SamplingStrategy,
    string OutputPath,
    bool WillSync,
    string? SkipReason);

public record ColumnPreview(
    string Name,
    string DataType,
    bool IsNullable,
    long SizeBytes);
