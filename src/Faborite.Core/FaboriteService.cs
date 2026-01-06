using Faborite.Core.Configuration;
using Faborite.Core.Export;
using Faborite.Core.OneLake;
using Faborite.Core.Sampling;

namespace Faborite.Core;

/// <summary>
/// Result of syncing a single table.
/// </summary>
public record TableSyncResult(
    string TableName,
    bool Success,
    long RowsSynced = 0,
    long? SourceRows = null,
    string? OutputPath = null,
    string? Error = null,
    TimeSpan Duration = default
);

/// <summary>
/// Summary of a full sync operation.
/// </summary>
public record SyncSummary(
    string WorkspaceId,
    string LakehouseId,
    List<TableSyncResult> Tables,
    long TotalRows,
    DateTime StartTime,
    DateTime EndTime
)
{
    public int SuccessfulTables => Tables.Count(t => t.Success);
    public int FailedTables => Tables.Count(t => !t.Success);
    public TimeSpan Duration => EndTime - StartTime;
}

/// <summary>
/// Main orchestrator for syncing Fabric lakehouse data locally.
/// </summary>
public class FaboriteService : IDisposable
{
    private readonly FaboriteConfig _config;
    private readonly OneLakeClient _oneLakeClient;
    private readonly DataSampler _sampler;
    private readonly DataExporter _exporter;

    public FaboriteService(FaboriteConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        
        ValidateConfig(config);

        _oneLakeClient = new OneLakeClient(
            config.WorkspaceId!,
            config.LakehouseId!,
            config.Auth);
        
        _sampler = new DataSampler();
        _exporter = new DataExporter();
    }

    private static void ValidateConfig(FaboriteConfig config)
    {
        if (string.IsNullOrEmpty(config.WorkspaceId) && string.IsNullOrEmpty(config.WorkspaceName))
            throw new ArgumentException("Either WorkspaceId or WorkspaceName must be set");
        
        if (string.IsNullOrEmpty(config.LakehouseId) && string.IsNullOrEmpty(config.LakehouseName))
            throw new ArgumentException("Either LakehouseId or LakehouseName must be set");

        if (string.IsNullOrEmpty(config.WorkspaceId))
            throw new NotImplementedException("Resolving WorkspaceName to ID is not yet implemented. Please provide WorkspaceId.");
        
        if (string.IsNullOrEmpty(config.LakehouseId))
            throw new NotImplementedException("Resolving LakehouseName to ID is not yet implemented. Please provide LakehouseId.");
    }

    /// <summary>
    /// Test connection to OneLake.
    /// </summary>
    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        => _oneLakeClient.TestConnectionAsync(cancellationToken);

    /// <summary>
    /// List all tables in the lakehouse.
    /// </summary>
    public Task<List<LakehouseTable>> ListTablesAsync(CancellationToken cancellationToken = default)
        => _oneLakeClient.ListTablesAsync(cancellationToken);

    /// <summary>
    /// Sync lakehouse data to local storage.
    /// </summary>
    public async Task<SyncSummary> SyncAsync(
        IEnumerable<string>? tables = null,
        SampleConfig? sampleOverride = null,
        FormatConfig? formatOverride = null,
        IProgress<(string tableName, int current, int total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var results = new List<TableSyncResult>();

        // Get list of tables to sync
        var allTables = await _oneLakeClient.ListTablesAsync(cancellationToken);
        
        var tablesToSync = FilterTables(allTables, tables?.ToList());

        if (tablesToSync.Count == 0)
        {
            return new SyncSummary(
                _config.WorkspaceId!,
                _config.LakehouseId!,
                results,
                0,
                startTime,
                DateTime.UtcNow);
        }

        var outputPath = _config.Sync.LocalPath;
        Directory.CreateDirectory(outputPath);

        // Sync tables
        if (_config.Sync.ParallelTables > 1)
        {
            results = await SyncParallelAsync(tablesToSync, outputPath, sampleOverride, formatOverride, progress, cancellationToken);
        }
        else
        {
            results = await SyncSequentialAsync(tablesToSync, outputPath, sampleOverride, formatOverride, progress, cancellationToken);
        }

        return new SyncSummary(
            _config.WorkspaceId!,
            _config.LakehouseId!,
            results,
            results.Sum(r => r.RowsSynced),
            startTime,
            DateTime.UtcNow);
    }

    /// <summary>
    /// Sync a single table.
    /// </summary>
    public async Task<TableSyncResult> SyncTableAsync(
        string tableName,
        string outputPath,
        SampleConfig? sampleConfig = null,
        FormatConfig? formatConfig = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Get effective config for this table
        var (defaultSample, defaultFormat) = ConfigLoader.GetTableConfig(_config, tableName);
        var effectiveSample = sampleConfig ?? defaultSample;
        var effectiveFormat = formatConfig ?? defaultFormat;

        try
        {
            // Create temp directory for downloaded parquet files
            var tempDir = Path.Combine(Path.GetTempPath(), "faborite", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Download table parquet files from OneLake
                await _oneLakeClient.DownloadTableAsync(tableName, tempDir, cancellationToken: cancellationToken);

                // Sample the data
                var sourceParquetPath = Path.Combine(tempDir, tableName, "*.parquet");
                var sampledParquetPath = Path.Combine(tempDir, $"{tableName}_sampled.parquet");
                
                var sampleResult = _sampler.SampleFromLocalParquet(
                    sourceParquetPath,
                    tableName,
                    sampledParquetPath,
                    effectiveSample);

                // Export to final format
                var finalPath = _exporter.Export(
                    sampledParquetPath,
                    tableName,
                    outputPath,
                    effectiveFormat);

                // Export schema if configured
                if (_config.Sync.IncludeSchema)
                {
                    _exporter.ExportSchema(sampledParquetPath, tableName, outputPath);
                }

                stopwatch.Stop();

                return new TableSyncResult(
                    TableName: tableName,
                    Success: true,
                    RowsSynced: sampleResult.RowCount,
                    SourceRows: sampleResult.SourceRowCount,
                    OutputPath: finalPath,
                    Duration: stopwatch.Elapsed);
            }
            finally
            {
                // Clean up temp directory
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new TableSyncResult(
                TableName: tableName,
                Success: false,
                Error: ex.Message,
                Duration: stopwatch.Elapsed);
        }
    }

    private List<LakehouseTable> FilterTables(List<LakehouseTable> allTables, List<string>? requestedTables)
    {
        IEnumerable<LakehouseTable> filtered = allTables;

        // Filter by requested tables
        if (requestedTables != null && requestedTables.Count > 0)
        {
            var requestedSet = new HashSet<string>(requestedTables, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(t => requestedSet.Contains(t.Name));
        }
        // Or use include list from config
        else if (_config.Sync.IncludeTables != null && _config.Sync.IncludeTables.Count > 0)
        {
            var includeSet = new HashSet<string>(_config.Sync.IncludeTables, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(t => includeSet.Contains(t.Name));
        }

        // Apply skip list
        if (_config.Sync.SkipTables.Count > 0)
        {
            var skipSet = new HashSet<string>(_config.Sync.SkipTables, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(t => !skipSet.Contains(t.Name));
        }

        return filtered.ToList();
    }

    private async Task<List<TableSyncResult>> SyncSequentialAsync(
        List<LakehouseTable> tables,
        string outputPath,
        SampleConfig? sampleOverride,
        FormatConfig? formatOverride,
        IProgress<(string tableName, int current, int total)>? progress,
        CancellationToken cancellationToken)
    {
        var results = new List<TableSyncResult>();

        for (int i = 0; i < tables.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var table = tables[i];
            progress?.Report((table.Name, i, tables.Count));

            var result = await SyncTableAsync(table.Name, outputPath, sampleOverride, formatOverride, cancellationToken);
            results.Add(result);
        }

        progress?.Report(("Done", tables.Count, tables.Count));
        return results;
    }

    private async Task<List<TableSyncResult>> SyncParallelAsync(
        List<LakehouseTable> tables,
        string outputPath,
        SampleConfig? sampleOverride,
        FormatConfig? formatOverride,
        IProgress<(string tableName, int current, int total)>? progress,
        CancellationToken cancellationToken)
    {
        var results = new List<TableSyncResult>();
        var completed = 0;
        var lockObj = new object();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _config.Sync.ParallelTables,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(tables, options, async (table, ct) =>
        {
            var result = await SyncTableAsync(table.Name, outputPath, sampleOverride, formatOverride, ct);
            
            lock (lockObj)
            {
                results.Add(result);
                completed++;
                progress?.Report((table.Name, completed, tables.Count));
            }
        });

        return results;
    }

    public void Dispose()
    {
        _oneLakeClient.Dispose();
        _sampler.Dispose();
        _exporter.Dispose();
        GC.SuppressFinalize(this);
    }
}
