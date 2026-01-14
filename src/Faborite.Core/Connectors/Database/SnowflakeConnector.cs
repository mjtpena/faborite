using Microsoft.Extensions.Logging;
using Snowflake.Data.Client;
using System.Data;

namespace Faborite.Core.Connectors.Database;

/// <summary>
/// Production-ready Snowflake connector with incremental sync and time travel support.
/// Issue #149 - Snowflake Cloud Data Warehouse Connector
/// </summary>
public class SnowflakeConnector : IQueryableConnector
{
    private readonly ILogger<SnowflakeConnector> _logger;
    private readonly SnowflakeConfig _config;
    private readonly string _connectionString;

    public string Name => "Snowflake";
    public string Version => "1.0.0";

    public SnowflakeConnector(ILogger<SnowflakeConnector> logger, SnowflakeConfig config)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _connectionString = BuildConnectionString(config);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Testing Snowflake connection to {Account}/{Warehouse}", 
            _config.Account, _config.Warehouse);
        
        try
        {
            using var connection = new SnowflakeDbConnection { ConnectionString = _connectionString };
            await connection.OpenAsync(cancellationToken);
            
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT CURRENT_VERSION(), CURRENT_WAREHOUSE(), CURRENT_DATABASE()";
            
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var version = reader.GetString(0);
                var warehouse = reader.GetString(1);
                var database = reader.GetString(2);
                
                _logger.LogInformation("Snowflake connection successful: Version={Version}, Warehouse={Warehouse}, Database={Database}",
                    version, warehouse, database);
                
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snowflake connection failed");
            return false;
        }
    }

    public async Task<ConnectorMetadata> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SnowflakeDbConnection { ConnectionString = _connectionString };
        await connection.OpenAsync(cancellationToken);
        
        var version = await GetSnowflakeVersionAsync(connection, cancellationToken);
        var edition = await GetSnowflakeEditionAsync(connection, cancellationToken);
        
        return new ConnectorMetadata(
            "Snowflake",
            $"{version} ({edition})",
            new Dictionary<string, string>
            {
                ["TimeTravel"] = "true",
                ["Clustering"] = "true",
                ["ChangeTracking"] = "true",
                ["ZeroCopyCloning"] = "true",
                ["ExternalTables"] = "true",
                ["MaterializedViews"] = "true",
                ["Streams"] = "true",
                ["Tasks"] = "true",
                ["MultiCluster"] = "true"
            },
            new List<string> { "Query", "COPY INTO", "PUT/GET", "Incremental", "TimeTravel" }
        );
    }

    public async Task<QueryResult> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing Snowflake query");
        var startTime = DateTime.UtcNow;
        
        using var connection = new SnowflakeDbConnection { ConnectionString = _connectionString };
        await connection.OpenAsync(cancellationToken);
        
        using var command = connection.CreateCommand();
        command.CommandText = query;
        
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

    public async Task<QueryResult> ExecuteTimeTravelQueryAsync(
        string query,
        DateTime asOfTimestamp,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing Snowflake time travel query at {Timestamp}", asOfTimestamp);
        
        // Modify query to include AT clause
        var timeTravelQuery = $"{query} AT(TIMESTAMP => '{asOfTimestamp:yyyy-MM-dd HH:mm:ss}'::TIMESTAMP)";
        
        return await ExecuteQueryAsync(timeTravelQuery, cancellationToken);
    }

    public async Task<List<TableInfo>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Listing Snowflake tables");
        
        const string query = @"
            SELECT 
                table_schema,
                table_name,
                row_count,
                bytes
            FROM information_schema.tables
            WHERE table_schema != 'INFORMATION_SCHEMA'
                AND table_type = 'BASE TABLE'
            ORDER BY table_schema, table_name";
        
        using var connection = new SnowflakeDbConnection { ConnectionString = _connectionString };
        await connection.OpenAsync(cancellationToken);
        
        using var command = connection.CreateCommand();
        command.CommandText = query;
        
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        
        var tables = new List<TableInfo>();
        
        while (await reader.ReadAsync(cancellationToken))
        {
            var schema = reader.GetString(0);
            var tableName = reader.GetString(1);
            var rowCount = reader.IsDBNull(2) ? 0L : reader.GetInt64(2);
            
            var columns = await GetTableColumnsAsync(schema, tableName, connection, cancellationToken);
            
            tables.Add(new TableInfo(tableName, schema, rowCount, columns));
        }
        
        _logger.LogInformation("Found {TableCount} tables", tables.Count);
        return tables;
    }

    public async Task<DataTransferResult> CopyIntoAsync(
        string targetTable,
        string stageName,
        string filePattern = "*.csv",
        bool purgeFiles = true,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting COPY INTO {Table} from stage {Stage}", targetTable, stageName);
        var startTime = DateTime.UtcNow;
        
        try
        {
            using var connection = new SnowflakeDbConnection { ConnectionString = _connectionString };
            await connection.OpenAsync(cancellationToken);
            
            var copyCommand = $@"
                COPY INTO {targetTable}
                FROM @{stageName}/{filePattern}
                FILE_FORMAT = (TYPE = 'CSV' FIELD_DELIMITER = ',' SKIP_HEADER = 1)
                ON_ERROR = 'CONTINUE'
                PURGE = {(purgeFiles ? "TRUE" : "FALSE")}";
            
            using var command = connection.CreateCommand();
            command.CommandText = copyCommand;
            
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            
            long rowsLoaded = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                // COPY INTO returns: file, status, rows_parsed, rows_loaded, error_limit, errors_seen, first_error, etc.
                rowsLoaded += reader.GetInt64(3); // rows_loaded column
            }
            
            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("COPY INTO completed: {Rows} rows in {Duration}ms", 
                rowsLoaded, duration.TotalMilliseconds);
            
            return new DataTransferResult(rowsLoaded, 0, duration, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "COPY INTO failed");
            var duration = DateTime.UtcNow - startTime;
            return new DataTransferResult(0, 0, duration, false, ex.Message);
        }
    }

    public async Task<DataTransferResult> IncrementalSyncAsync(
        string sourceTable,
        string targetTable,
        string? changeTrackingStream = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting incremental sync from {Source} to {Target}", sourceTable, targetTable);
        var startTime = DateTime.UtcNow;
        
        try
        {
            using var connection = new SnowflakeDbConnection { ConnectionString = _connectionString };
            await connection.OpenAsync(cancellationToken);
            
            // If no stream specified, create a temporary one
            if (string.IsNullOrEmpty(changeTrackingStream))
            {
                changeTrackingStream = $"TEMP_STREAM_{Guid.NewGuid():N}";
                
                using var createStreamCmd = connection.CreateCommand();
                createStreamCmd.CommandText = $"CREATE STREAM {changeTrackingStream} ON TABLE {sourceTable}";
                await createStreamCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            
            // Merge changes from stream into target
            var mergeCommand = $@"
                MERGE INTO {targetTable} AS target
                USING {changeTrackingStream} AS source
                ON target.id = source.id
                WHEN MATCHED AND source.METADATA$ACTION = 'DELETE' THEN DELETE
                WHEN MATCHED THEN UPDATE SET *
                WHEN NOT MATCHED AND source.METADATA$ACTION != 'DELETE' THEN INSERT *";
            
            using var command = connection.CreateCommand();
            command.CommandText = mergeCommand;
            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            
            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Incremental sync completed: {Rows} rows in {Duration}ms", 
                rowsAffected, duration.TotalMilliseconds);
            
            return new DataTransferResult(rowsAffected, 0, duration, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Incremental sync failed");
            var duration = DateTime.UtcNow - startTime;
            return new DataTransferResult(0, 0, duration, false, ex.Message);
        }
    }

    public async Task<string> CreateZeroCopyCloneAsync(
        string sourceTable,
        string cloneName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating zero-copy clone {Clone} from {Source}", cloneName, sourceTable);
        
        using var connection = new SnowflakeDbConnection { ConnectionString = _connectionString };
        await connection.OpenAsync(cancellationToken);
        
        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE {cloneName} CLONE {sourceTable}";
        await command.ExecuteNonQueryAsync(cancellationToken);
        
        _logger.LogInformation("Clone {Clone} created successfully", cloneName);
        return cloneName;
    }

    private static List<ColumnMetadata> GetColumnMetadata(IDataReader reader)
    {
        var columns = new List<ColumnMetadata>();
        var schemaTable = reader.GetSchemaTable();
        
        if (schemaTable != null)
        {
            foreach (DataRow row in schemaTable.Rows)
            {
                var columnName = row["ColumnName"].ToString() ?? "";
                var dataType = row["DataTypeName"]?.ToString() ?? row["DataType"]?.ToString() ?? "";
                var allowDBNull = row["AllowDBNull"] != DBNull.Value && (bool)row["AllowDBNull"];
                var columnSize = row["ColumnSize"] != DBNull.Value ? Convert.ToInt32(row["ColumnSize"]) : (int?)null;
                
                columns.Add(new ColumnMetadata(columnName, dataType, allowDBNull, columnSize));
            }
        }
        else
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(new ColumnMetadata(
                    reader.GetName(i),
                    reader.GetDataTypeName(i),
                    true
                ));
            }
        }
        
        return columns;
    }

    private async Task<List<ColumnMetadata>> GetTableColumnsAsync(
        string schema,
        string tableName,
        SnowflakeDbConnection connection,
        CancellationToken cancellationToken)
    {
        const string query = @"
            SELECT 
                column_name,
                data_type,
                is_nullable,
                character_maximum_length
            FROM information_schema.columns
            WHERE table_schema = ? AND table_name = ?
            ORDER BY ordinal_position";
        
        using var command = connection.CreateCommand();
        command.CommandText = query;
        command.Parameters.Add(new SnowflakeDbParameter { Value = schema });
        command.Parameters.Add(new SnowflakeDbParameter { Value = tableName });
        
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<ColumnMetadata>();
        
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new ColumnMetadata(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2) == "YES",
                reader.IsDBNull(3) ? null : Convert.ToInt32(reader.GetInt64(3))
            ));
        }
        
        return columns;
    }

    private async Task<string> GetSnowflakeVersionAsync(SnowflakeDbConnection connection, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CURRENT_VERSION()";
        var version = await command.ExecuteScalarAsync(cancellationToken);
        return version?.ToString() ?? "Unknown";
    }

    private async Task<string> GetSnowflakeEditionAsync(SnowflakeDbConnection connection, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CURRENT_EDITION()";
        var edition = await command.ExecuteScalarAsync(cancellationToken);
        return edition?.ToString() ?? "Unknown";
    }

    private static string BuildConnectionString(SnowflakeConfig config)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append($"account={config.Account};");
        builder.Append($"user={config.Username};");
        builder.Append($"password={config.Password};");
        builder.Append($"warehouse={config.Warehouse};");
        builder.Append($"db={config.Database};");
        builder.Append($"schema={config.Schema};");
        
        if (!string.IsNullOrEmpty(config.Role))
            builder.Append($"role={config.Role};");
        
        return builder.ToString();
    }
}

/// <summary>
/// Snowflake connector configuration
/// </summary>
public record SnowflakeConfig(
    string Account,
    string Username,
    string Password,
    string Warehouse,
    string Database,
    string Schema = "PUBLIC",
    string? Role = null
);
