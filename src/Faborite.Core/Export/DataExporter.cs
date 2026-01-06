using System.Text.Json;
using DuckDB.NET.Data;
using Faborite.Core.Configuration;

namespace Faborite.Core.Export;

/// <summary>
/// Exports data to various local formats.
/// </summary>
public class DataExporter : IDisposable
{
    private readonly DuckDBConnection _connection;

    public DataExporter()
    {
        _connection = new DuckDBConnection("DataSource=:memory:");
        _connection.Open();
    }

    /// <summary>
    /// Export parquet file to the specified format.
    /// </summary>
    public string Export(
        string sourceParquetPath,
        string tableName,
        string outputDir,
        FormatConfig config)
    {
        Directory.CreateDirectory(outputDir);
        var tableOutputDir = Path.Combine(outputDir, tableName);
        Directory.CreateDirectory(tableOutputDir);

        return config.Format switch
        {
            OutputFormat.Parquet => ExportToParquet(sourceParquetPath, tableName, tableOutputDir, config),
            OutputFormat.Csv => ExportToCsv(sourceParquetPath, tableName, tableOutputDir, config),
            OutputFormat.Json => ExportToJson(sourceParquetPath, tableName, tableOutputDir, config),
            OutputFormat.DuckDb => ExportToDuckDb(sourceParquetPath, tableName, outputDir, config),
            OutputFormat.Delta => ExportToDelta(sourceParquetPath, tableName, tableOutputDir, config),
            _ => throw new ArgumentException($"Unsupported format: {config.Format}")
        };
    }

    private string ExportToParquet(string source, string tableName, string outputDir, FormatConfig config)
    {
        var outputPath = Path.Combine(outputDir, $"{tableName}.parquet");
        var compression = GetCompressionCodec(config.Compression);
        
        ExecuteNonQuery($"COPY (SELECT * FROM '{source}') TO '{outputPath}' (FORMAT PARQUET, COMPRESSION '{compression}')");
        
        return outputPath;
    }

    private string ExportToCsv(string source, string tableName, string outputDir, FormatConfig config)
    {
        var outputPath = Path.Combine(outputDir, $"{tableName}.csv");
        
        ExecuteNonQuery($"COPY (SELECT * FROM '{source}') TO '{outputPath}' (FORMAT CSV, HEADER TRUE)");
        
        return outputPath;
    }

    private string ExportToJson(string source, string tableName, string outputDir, FormatConfig config)
    {
        var outputPath = Path.Combine(outputDir, $"{tableName}.jsonl");
        
        // DuckDB JSON export
        ExecuteNonQuery($"COPY (SELECT * FROM '{source}') TO '{outputPath}' (FORMAT JSON)");
        
        return outputPath;
    }

    private string ExportToDuckDb(string source, string tableName, string outputDir, FormatConfig config)
    {
        var dbPath = Path.Combine(outputDir, "lakehouse.duckdb");
        
        // Create or open the database and create/replace the table
        using var dbConnection = new DuckDBConnection($"DataSource={dbPath}");
        dbConnection.Open();
        
        using var command = dbConnection.CreateCommand();
        command.CommandText = $"CREATE OR REPLACE TABLE \"{tableName}\" AS SELECT * FROM '{source}'";
        command.ExecuteNonQuery();
        
        return dbPath;
    }

    private string ExportToDelta(string source, string tableName, string outputDir, FormatConfig config)
    {
        // For now, just copy as parquet - Delta write support in DuckDB is limited
        // In production, you'd use delta-rs or similar
        var outputPath = Path.Combine(outputDir, $"{tableName}.parquet");
        var compression = GetCompressionCodec(config.Compression);
        
        ExecuteNonQuery($"COPY (SELECT * FROM '{source}') TO '{outputPath}' (FORMAT PARQUET, COMPRESSION '{compression}')");
        
        // Create a simple _delta_log to indicate it's a "delta-like" table
        var deltaLogDir = Path.Combine(outputDir, "_delta_log");
        Directory.CreateDirectory(deltaLogDir);
        
        var commitInfo = new
        {
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            operation = "WRITE",
            operationParameters = new { mode = "Overwrite" }
        };
        File.WriteAllText(
            Path.Combine(deltaLogDir, "00000000000000000000.json"),
            JsonSerializer.Serialize(commitInfo));
        
        return outputDir;
    }

    /// <summary>
    /// Export schema to JSON file.
    /// </summary>
    public void ExportSchema(string sourceParquetPath, string tableName, string outputDir)
    {
        var tableOutputDir = Path.Combine(outputDir, tableName);
        Directory.CreateDirectory(tableOutputDir);
        var schemaPath = Path.Combine(tableOutputDir, "_schema.json");

        // Get schema from DuckDB
        using var command = _connection.CreateCommand();
        command.CommandText = $"DESCRIBE SELECT * FROM '{sourceParquetPath}'";
        using var reader = command.ExecuteReader();

        var fields = new List<object>();
        while (reader.Read())
        {
            fields.Add(new
            {
                name = reader.GetString(0),
                type = reader.GetString(1),
                nullable = reader.GetString(2) == "YES"
            });
        }

        var schema = new
        {
            tableName,
            fields,
            exportedAt = DateTime.UtcNow.ToString("O")
        };

        var json = JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(schemaPath, json);
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

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
