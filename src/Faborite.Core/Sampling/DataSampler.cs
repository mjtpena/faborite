using DuckDB.NET.Data;
using Faborite.Core.Configuration;

namespace Faborite.Core.Sampling;

/// <summary>
/// Result of a sampling operation.
/// </summary>
public record SampleResult(
    string TableName,
    string LocalParquetPath,
    long RowCount,
    long? SourceRowCount = null
);

/// <summary>
/// Samples data from Delta tables using DuckDB.
/// </summary>
public class DataSampler : IDataSampler
{
    private readonly DuckDBConnection _connection;
    private bool _deltaExtensionLoaded;

    public DataSampler()
    {
        _connection = new DuckDBConnection("DataSource=:memory:");
        _connection.Open();
        
        // Install and load required extensions
        ExecuteNonQuery("INSTALL httpfs;");
        ExecuteNonQuery("LOAD httpfs;");
    }

    /// <summary>
    /// Ensure Delta extension is loaded (lazy loading since it's slower).
    /// </summary>
    private void EnsureDeltaExtension()
    {
        if (_deltaExtensionLoaded) return;
        
        ExecuteNonQuery("INSTALL delta;");
        ExecuteNonQuery("LOAD delta;");
        _deltaExtensionLoaded = true;
    }

    /// <summary>
    /// Sample data from parquet files.
    /// </summary>
    public async Task<SampleResult> SampleFromParquetAsync(
        string parquetPath,
        string tableName,
        string outputPath,
        SampleConfig config,
        CancellationToken cancellationToken = default)
    {
        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // Build the sample query based on strategy
        var query = BuildSampleQuery(parquetPath, config, isParquet: true);
        
        // Get source row count
        long? sourceRowCount = null;
        try
        {
            sourceRowCount = ExecuteScalar<long>($"SELECT COUNT(*) FROM '{parquetPath}'");
        }
        catch
        {
            // Ignore count errors
        }

        // Check if table is small enough to pull in full
        if (sourceRowCount.HasValue && sourceRowCount.Value <= config.MaxFullTableRows && config.Strategy != SampleStrategy.Full)
        {
            query = $"SELECT * FROM '{parquetPath}'";
        }

        // Execute sample and write to parquet
        var outputQuery = $"COPY ({query}) TO '{outputPath}' (FORMAT PARQUET, COMPRESSION 'snappy')";
        ExecuteNonQuery(outputQuery);

        // Get sampled row count
        var rowCount = ExecuteScalar<long>($"SELECT COUNT(*) FROM '{outputPath}'");

        return new SampleResult(
            TableName: tableName,
            LocalParquetPath: outputPath,
            RowCount: rowCount,
            SourceRowCount: sourceRowCount
        );
    }

    /// <summary>
    /// Sample data from a Delta table URI.
    /// </summary>
    public async Task<SampleResult> SampleFromDeltaAsync(
        string deltaUri,
        string tableName,
        string outputPath,
        SampleConfig config,
        CancellationToken cancellationToken = default)
    {
        EnsureDeltaExtension();
        
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var query = BuildSampleQuery(deltaUri, config, isParquet: false);

        // Get source row count
        long? sourceRowCount = null;
        try
        {
            sourceRowCount = ExecuteScalar<long>($"SELECT COUNT(*) FROM delta_scan('{deltaUri}')");
        }
        catch
        {
            // Ignore count errors
        }

        // Check if table is small enough to pull in full
        if (sourceRowCount.HasValue && sourceRowCount.Value <= config.MaxFullTableRows && config.Strategy != SampleStrategy.Full)
        {
            query = $"SELECT * FROM delta_scan('{deltaUri}')";
        }

        // Execute sample and write to parquet
        var outputQuery = $"COPY ({query}) TO '{outputPath}' (FORMAT PARQUET, COMPRESSION 'snappy')";
        ExecuteNonQuery(outputQuery);

        // Get sampled row count
        var rowCount = ExecuteScalar<long>($"SELECT COUNT(*) FROM '{outputPath}'");

        return new SampleResult(
            TableName: tableName,
            LocalParquetPath: outputPath,
            RowCount: rowCount,
            SourceRowCount: sourceRowCount
        );
    }

    /// <summary>
    /// Sample from local parquet files (already downloaded).
    /// </summary>
    public SampleResult SampleFromLocalParquet(
        string localParquetPath,
        string tableName,
        string outputPath,
        SampleConfig config)
    {
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // Handle glob pattern for multiple files
        var sourcePath = localParquetPath.EndsWith(".parquet") 
            ? localParquetPath 
            : Path.Combine(localParquetPath, "*.parquet");

        var query = BuildSampleQuery(sourcePath, config, isParquet: true);

        // Get source row count
        long? sourceRowCount = null;
        try
        {
            sourceRowCount = ExecuteScalar<long>($"SELECT COUNT(*) FROM '{sourcePath}'");
        }
        catch
        {
            // Ignore count errors
        }

        // Check if table is small enough to pull in full
        if (sourceRowCount.HasValue && sourceRowCount.Value <= config.MaxFullTableRows && config.Strategy != SampleStrategy.Full)
        {
            query = $"SELECT * FROM '{sourcePath}'";
        }

        // Execute sample and write to parquet
        var outputQuery = $"COPY ({query}) TO '{outputPath}' (FORMAT PARQUET, COMPRESSION 'snappy')";
        ExecuteNonQuery(outputQuery);

        // Get sampled row count
        var rowCount = ExecuteScalar<long>($"SELECT COUNT(*) FROM '{outputPath}'");

        return new SampleResult(
            TableName: tableName,
            LocalParquetPath: outputPath,
            RowCount: rowCount,
            SourceRowCount: sourceRowCount
        );
    }

    private string BuildSampleQuery(string source, SampleConfig config, bool isParquet)
    {
        var fromClause = isParquet ? $"'{source}'" : $"delta_scan('{source}')";
        var n = config.Rows;
        var seed = config.Seed;

        return config.Strategy switch
        {
            SampleStrategy.Random => 
                $"SELECT * FROM {fromClause} USING SAMPLE {n}",
            
            SampleStrategy.Recent when !string.IsNullOrEmpty(config.DateColumn) =>
                $"SELECT * FROM {fromClause} ORDER BY \"{config.DateColumn}\" DESC LIMIT {n}",
            
            SampleStrategy.Recent => // Auto-detect date column not implemented in query, fall back to random
                $"SELECT * FROM {fromClause} USING SAMPLE {n}",
            
            SampleStrategy.Head =>
                $"SELECT * FROM {fromClause} LIMIT {n}",
            
            SampleStrategy.Tail =>
                $"SELECT * FROM (SELECT *, ROW_NUMBER() OVER () as _rn FROM {fromClause}) sub WHERE _rn > (SELECT COUNT(*) - {n} FROM {fromClause})",
            
            SampleStrategy.Stratified when !string.IsNullOrEmpty(config.StratifyColumn) =>
                $@"WITH ranked AS (
                    SELECT *,
                        ROW_NUMBER() OVER (PARTITION BY ""{config.StratifyColumn}"" ORDER BY RANDOM()) as _rank,
                        COUNT(*) OVER (PARTITION BY ""{config.StratifyColumn}"") as _group_count,
                        COUNT(*) OVER () as _total_count
                    FROM {fromClause}
                )
                SELECT * EXCLUDE (_rank, _group_count, _total_count)
                FROM ranked
                WHERE _rank <= CEIL({n}::DOUBLE * _group_count / _total_count)",
            
            SampleStrategy.Query when !string.IsNullOrEmpty(config.WhereClause) =>
                $"SELECT * FROM {fromClause} WHERE {config.WhereClause} LIMIT {n}",
            
            SampleStrategy.Full =>
                $"SELECT * FROM {fromClause}",
            
            _ => $"SELECT * FROM {fromClause} USING SAMPLE {n}"
        };
    }

    private static string GetCompressionCodec(string? compression) => 
        compression?.ToLowerInvariant() switch
        {
            "snappy" => "SNAPPY",
            "gzip" => "GZIP",
            "zstd" => "ZSTD",
            "lz4" => "LZ4",
            "none" => "UNCOMPRESSED",
            _ => "SNAPPY"
        };

    private void ExecuteNonQuery(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private T ExecuteScalar<T>(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        var result = command.ExecuteScalar();
        return (T)Convert.ChangeType(result!, typeof(T));
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
