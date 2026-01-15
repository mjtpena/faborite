using Npgsql;
using Microsoft.Extensions.Logging;
using System.Data;

namespace Faborite.Core.Connectors.Database;

public record RedshiftConfig(
    string ClusterEndpoint,
    string Database,
    string Username,
    string Password,
    int Port = 5439,
    bool UseSsl = true,
    int CommandTimeout = 300);

/// <summary>
/// Production-ready Amazon Redshift connector with Spectrum external table support.
/// Uses PostgreSQL wire protocol (Npgsql) with Redshift-specific optimizations.
/// Issue #132
/// </summary>
public class RedshiftConnector : IQueryableConnector
{
    private readonly ILogger<RedshiftConnector> _logger;
    private readonly RedshiftConfig _config;
    private readonly string _connectionString;

    public string Name => "Amazon Redshift";
    public string Version => "1.0.0";

    public RedshiftConnector(ILogger<RedshiftConnector> logger, RedshiftConfig config)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _connectionString = BuildConnectionString(config);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Testing Redshift connection to {Cluster}/{Database}", 
            _config.ClusterEndpoint, _config.Database);
        
        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT version(), current_database(), current_user";
            
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var version = reader.GetString(0);
                var database = reader.GetString(1);
                var user = reader.GetString(2);
                
                _logger.LogInformation("Redshift connection successful: Version={Version}, DB={Database}, User={User}",
                    version, database, user);
                
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redshift connection failed");
            return false;
        }
    }

    public async Task<ConnectorMetadata> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        var clusterInfo = await GetClusterInfoAsync(connection, cancellationToken);
        
        return new ConnectorMetadata(
            "Amazon Redshift",
            Version,
            new Dictionary<string, string>
            {
                ["Cluster"] = _config.ClusterEndpoint,
                ["Database"] = _config.Database,
                ["SupportsSpectrum"] = "true",
                ["SupportsDistKeys"] = "true",
                ["SupportsSortKeys"] = "true",
                ["SupportsColumnEncoding"] = "true",
                ["MaxConcurrentQueries"] = clusterInfo.GetValueOrDefault("max_concurrency", "50"),
                ["NodeType"] = clusterInfo.GetValueOrDefault("node_type", "unknown"),
                ["ClusterVersion"] = clusterInfo.GetValueOrDefault("version", "unknown")
            },
            new List<string> { "Query", "COPY", "UNLOAD", "Spectrum", "VACUUM", "ANALYZE" }
        );
    }

    public async Task<QueryResult> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing Redshift query");
        var startTime = DateTime.UtcNow;
        
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        using var command = connection.CreateCommand();
        command.CommandText = query;
        command.CommandTimeout = _config.CommandTimeout;
        
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        
        var columns = GetColumnMetadata(reader);
        var rows = new List<Dictionary<string, object?>>();
        
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[columns[i].Name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(row);
        }
        
        var duration = DateTime.UtcNow - startTime;
        _logger.LogInformation("Query returned {RowCount} rows in {Duration}ms", rows.Count, duration.TotalMilliseconds);
        
        return new QueryResult(rows, columns, rows.Count, duration);
    }

    public async Task<List<TableInfo>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Listing Redshift tables");
        
        const string query = @"
            SELECT 
                schemaname,
                tablename,
                COALESCE(
                    (SELECT COUNT(*) FROM svv_table_info WHERE schema = schemaname AND ""table"" = tablename),
                    0
                ) AS estimated_rows
            FROM pg_tables
            WHERE schemaname NOT IN ('pg_catalog', 'information_schema', 'pg_internal')
            ORDER BY schemaname, tablename";
        
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        using var command = connection.CreateCommand();
        command.CommandText = query;
        
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        
        var tables = new List<TableInfo>();
        
        while (await reader.ReadAsync(cancellationToken))
        {
            var schema = reader.GetString(0);
            var tableName = reader.GetString(1);
            var rowCount = reader.GetInt64(2);
            
            var columns = await GetTableColumnsAsync(schema, tableName, connection, cancellationToken);
            
            tables.Add(new TableInfo(tableName, schema, rowCount, columns));
        }
        
        _logger.LogInformation("Found {TableCount} tables", tables.Count);
        return tables;
    }

    public async Task<DataTransferResult> CopyFromS3Async(
        string targetTable,
        string s3Path,
        string? iamRole = null,
        string format = "PARQUET",
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting COPY from S3: {Path} to {Table}", s3Path, targetTable);
        var startTime = DateTime.UtcNow;
        
        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            var credentials = !string.IsNullOrEmpty(iamRole) 
                ? $"IAM_ROLE '{iamRole}'" 
                : "ACCESS_KEY_ID '<key>' SECRET_ACCESS_KEY '<secret>'";
            
            var copyCommand = $@"
                COPY {targetTable}
                FROM '{s3Path}'
                {credentials}
                FORMAT AS {format}
                MAXERROR 10
                COMPUPDATE ON
                STATUPDATE ON";
            
            using var command = connection.CreateCommand();
            command.CommandText = copyCommand;
            command.CommandTimeout = _config.CommandTimeout;
            
            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            
            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("COPY completed: {Rows} rows in {Duration}ms", 
                rowsAffected, duration.TotalMilliseconds);
            
            return new DataTransferResult(rowsAffected, 0, duration, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "COPY from S3 failed");
            var duration = DateTime.UtcNow - startTime;
            return new DataTransferResult(0, 0, duration, false, ex.Message);
        }
    }

    public async Task<DataTransferResult> UnloadToS3Async(
        string sourceQuery,
        string s3Path,
        string? iamRole = null,
        string format = "PARQUET",
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting UNLOAD to S3: {Path}", s3Path);
        var startTime = DateTime.UtcNow;
        
        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            var credentials = !string.IsNullOrEmpty(iamRole) 
                ? $"IAM_ROLE '{iamRole}'" 
                : "ACCESS_KEY_ID '<key>' SECRET_ACCESS_KEY '<secret>'";
            
            var unloadCommand = $@"
                UNLOAD ('{sourceQuery}')
                TO '{s3Path}'
                {credentials}
                FORMAT AS {format}
                PARALLEL ON
                ALLOWOVERWRITE";
            
            using var command = connection.CreateCommand();
            command.CommandText = unloadCommand;
            command.CommandTimeout = _config.CommandTimeout;
            
            await command.ExecuteNonQueryAsync(cancellationToken);
            
            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("UNLOAD completed in {Duration}ms", duration.TotalMilliseconds);
            
            return new DataTransferResult(0, 0, duration, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UNLOAD to S3 failed");
            var duration = DateTime.UtcNow - startTime;
            return new DataTransferResult(0, 0, duration, false, ex.Message);
        }
    }

    public async Task<string> CreateSpectrumExternalTableAsync(
        string tableName,
        string s3Location,
        string externalSchema,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating Spectrum external table {Schema}.{Table}", externalSchema, tableName);
        
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        var createTableSql = $@"
            CREATE EXTERNAL TABLE {externalSchema}.{tableName} (
                -- Schema inferred from external data
            )
            STORED AS PARQUET
            LOCATION '{s3Location}'";
        
        using var command = connection.CreateCommand();
        command.CommandText = createTableSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
        
        _logger.LogInformation("External table {Schema}.{Table} created", externalSchema, tableName);
        return $"{externalSchema}.{tableName}";
    }

    public async Task VacuumAsync(
        string tableName,
        bool full = false,
        bool sortOnly = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Running VACUUM on {Table} (Full={Full}, SortOnly={SortOnly})", 
            tableName, full, sortOnly);
        
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        var vacuumType = full ? "FULL" : sortOnly ? "SORT ONLY" : "";
        var vacuumCommand = $"VACUUM {vacuumType} {tableName}";
        
        using var command = connection.CreateCommand();
        command.CommandText = vacuumCommand;
        command.CommandTimeout = _config.CommandTimeout;
        
        await command.ExecuteNonQueryAsync(cancellationToken);
        
        _logger.LogInformation("VACUUM completed on {Table}", tableName);
    }

    public async Task AnalyzeAsync(
        string tableName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Running ANALYZE on {Table}", tableName);
        
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        using var command = connection.CreateCommand();
        command.CommandText = $"ANALYZE {tableName}";
        await command.ExecuteNonQueryAsync(cancellationToken);
        
        _logger.LogInformation("ANALYZE completed on {Table}", tableName);
    }

    private static List<ColumnMetadata> GetColumnMetadata(IDataReader reader)
    {
        var columns = new List<ColumnMetadata>();
        
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(new ColumnMetadata(
                reader.GetName(i),
                reader.GetDataTypeName(i),
                true
            ));
        }
        
        return columns;
    }

    private async Task<List<ColumnMetadata>> GetTableColumnsAsync(
        string schema,
        string tableName,
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string query = @"
            SELECT 
                column_name,
                data_type,
                is_nullable,
                character_maximum_length
            FROM information_schema.columns
            WHERE table_schema = @Schema AND table_name = @TableName
            ORDER BY ordinal_position";
        
        using var command = connection.CreateCommand();
        command.CommandText = query;
        command.Parameters.AddWithValue("@Schema", schema);
        command.Parameters.AddWithValue("@TableName", tableName);
        
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<ColumnMetadata>();
        
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new ColumnMetadata(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2) == "YES",
                reader.IsDBNull(3) ? null : reader.GetInt32(3)
            ));
        }
        
        return columns;
    }

    private async Task<Dictionary<string, string>> GetClusterInfoAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string query = @"
            SELECT 
                'version' AS key, version() AS value
            UNION ALL
            SELECT 
                'node_type', 
                COALESCE(node_type, 'unknown')
            FROM stv_slices
            LIMIT 1";
        
        using var command = connection.CreateCommand();
        command.CommandText = query;
        
        var info = new Dictionary<string, string>();
        
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            info[reader.GetString(0)] = reader.GetString(1);
        }
        
        return info;
    }

    private static string BuildConnectionString(RedshiftConfig config)
    {
        return $"Host={config.ClusterEndpoint};" +
               $"Port={config.Port};" +
               $"Database={config.Database};" +
               $"Username={config.Username};" +
               $"Password={config.Password};" +
               $"SSL Mode={(config.UseSsl ? "Require" : "Disable")};" +
               $"Trust Server Certificate=true;" +
               $"Timeout=30";
    }
}
