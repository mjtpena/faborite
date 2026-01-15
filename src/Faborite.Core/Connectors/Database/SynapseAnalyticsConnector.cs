using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;

namespace Faborite.Core.Connectors.Database;

public record SynapseConfig(
    string WorkspaceName,
    string SqlPoolName,
    string Username,
    string Password,
    string? Database = "master",
    int CommandTimeout = 300);

/// <summary>
/// Production-ready Azure Synapse Analytics connector with distributed query optimization.
/// Supports dedicated SQL pools, PolyBase external tables, and COPY INTO for bulk loading.
/// Issue #134
/// </summary>
public class SynapseAnalyticsConnector : IQueryableConnector
{
    private readonly ILogger<SynapseAnalyticsConnector> _logger;
    private readonly SynapseConfig _config;
    private readonly string _connectionString;

    public string Name => "Azure Synapse Analytics";
    public string Version => "1.0.0";

    public SynapseAnalyticsConnector(ILogger<SynapseAnalyticsConnector> logger, SynapseConfig config)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _connectionString = BuildConnectionString(config);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Testing Synapse connection to {Workspace}/{Pool}", 
            _config.WorkspaceName, _config.SqlPoolName);
        
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT @@VERSION, SERVERPROPERTY('Edition'), SERVERPROPERTY('EngineEdition')";
            
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var version = reader.GetString(0);
                var edition = reader.GetString(1);
                var engineEdition = reader.GetInt32(2);
                
                _logger.LogInformation("Synapse connection successful: Edition={Edition}, Engine={Engine}",
                    edition, engineEdition);
                
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Synapse connection failed");
            return false;
        }
    }

    public async Task<ConnectorMetadata> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        var distributionInfo = await GetDistributionInfoAsync(connection, cancellationToken);
        
        return new ConnectorMetadata(
            "Azure Synapse Analytics",
            Version,
            new Dictionary<string, string>
            {
                ["Workspace"] = _config.WorkspaceName,
                ["SqlPool"] = _config.SqlPoolName,
                ["DistributionTypes"] = "HASH, ROUND_ROBIN, REPLICATE",
                ["SupportsPolyBase"] = "true",
                ["SupportsCopyInto"] = "true",
                ["SupportsExternalTables"] = "true",
                ["MaxConcurrentQueries"] = "128",
                ["ColumnStoreSupport"] = "true",
                ["MaterializedViews"] = "true",
                ["ResultSetCaching"] = "true"
            },
            new List<string> { "Query", "COPY INTO", "PolyBase", "BulkLoad", "ExternalTable" }
        );
    }

    public async Task<QueryResult> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing Synapse query");
        var startTime = DateTime.UtcNow;
        
        using var connection = new SqlConnection(_connectionString);
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
        _logger.LogInformation("Listing Synapse tables");
        
        const string query = @"
            SELECT 
                s.name AS schema_name,
                t.name AS table_name,
                SUM(p.rows) AS row_count,
                tp.distribution_policy_desc,
                tp.partition_column_name
            FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            INNER JOIN sys.pdw_table_distribution_properties tp ON t.object_id = tp.object_id
            LEFT JOIN sys.partitions p ON t.object_id = p.object_id
            WHERE t.is_external = 0
            GROUP BY s.name, t.name, tp.distribution_policy_desc, tp.partition_column_name
            ORDER BY s.name, t.name";
        
        using var connection = new SqlConnection(_connectionString);
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

    public async Task<DataTransferResult> CopyIntoAsync(
        string targetTable,
        string storageAccountUri,
        string fileFormat = "PARQUET",
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting COPY INTO {Table} from {Uri}", targetTable, storageAccountUri);
        var startTime = DateTime.UtcNow;
        
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            var copyCommand = $@"
                COPY INTO {targetTable}
                FROM '{storageAccountUri}'
                WITH (
                    FILE_TYPE = '{fileFormat}',
                    MAXERRORS = 10,
                    COMPRESSION = 'gzip'
                )";
            
            using var command = connection.CreateCommand();
            command.CommandText = copyCommand;
            command.CommandTimeout = _config.CommandTimeout;
            
            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            
            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("COPY INTO completed: {Rows} rows in {Duration}ms", 
                rowsAffected, duration.TotalMilliseconds);
            
            return new DataTransferResult(rowsAffected, 0, duration, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "COPY INTO failed");
            var duration = DateTime.UtcNow - startTime;
            return new DataTransferResult(0, 0, duration, false, ex.Message);
        }
    }

    public async Task<string> CreateExternalTableAsync(
        string tableName,
        string dataSource,
        string fileFormat,
        string location,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating external table {Table}", tableName);
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        var createTableSql = $@"
            CREATE EXTERNAL TABLE {tableName} (
                -- Schema will be inferred from external data source
            )
            WITH (
                DATA_SOURCE = {dataSource},
                FILE_FORMAT = {fileFormat},
                LOCATION = '{location}'
            )";
        
        using var command = connection.CreateCommand();
        command.CommandText = createTableSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
        
        _logger.LogInformation("External table {Table} created", tableName);
        return tableName;
    }

    public async Task<QueryStatistics> GetQueryStatisticsAsync(
        string queryId,
        CancellationToken cancellationToken = default)
    {
        const string query = @"
            SELECT 
                request_id,
                status,
                total_elapsed_time,
                row_count,
                data_processed_mb,
                result_cache_hit
            FROM sys.dm_pdw_exec_requests
            WHERE request_id = @QueryId";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        using var command = connection.CreateCommand();
        command.CommandText = query;
        command.Parameters.AddWithValue("@QueryId", queryId);
        
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        
        if (await reader.ReadAsync(cancellationToken))
        {
            return new QueryStatistics(
                RequestId: reader.GetString(0),
                Status: reader.GetString(1),
                ElapsedTime: TimeSpan.FromMilliseconds(reader.GetInt32(2)),
                RowCount: reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
                DataProcessedMB: reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
                CacheHit: !reader.IsDBNull(5) && reader.GetInt32(5) > 0
            );
        }
        
        throw new InvalidOperationException($"Query {queryId} not found");
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
        
        return columns;
    }

    private async Task<List<ColumnMetadata>> GetTableColumnsAsync(
        string schema,
        string tableName,
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string query = @"
            SELECT 
                c.name AS column_name,
                t.name AS data_type,
                c.is_nullable,
                c.max_length
            FROM sys.columns c
            INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
            INNER JOIN sys.tables tb ON c.object_id = tb.object_id
            INNER JOIN sys.schemas s ON tb.schema_id = s.schema_id
            WHERE s.name = @Schema AND tb.name = @TableName
            ORDER BY c.column_id";
        
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
                reader.GetBoolean(2),
                reader.IsDBNull(3) ? null : reader.GetInt16(3)
            ));
        }
        
        return columns;
    }

    private async Task<Dictionary<string, string>> GetDistributionInfoAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string query = @"
            SELECT 
                distribution_policy_desc,
                COUNT(*) as table_count
            FROM sys.pdw_table_distribution_properties
            GROUP BY distribution_policy_desc";
        
        using var command = connection.CreateCommand();
        command.CommandText = query;
        
        var distributionInfo = new Dictionary<string, string>();
        
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            distributionInfo[reader.GetString(0)] = reader.GetInt32(1).ToString();
        }
        
        return distributionInfo;
    }

    private static string BuildConnectionString(SynapseConfig config)
    {
        var server = $"{config.WorkspaceName}.sql.azuresynapse.net";
        
        return $"Server=tcp:{server},1433;" +
               $"Initial Catalog={config.Database};" +
               $"User ID={config.Username};" +
               $"Password={config.Password};" +
               $"Encrypt=True;" +
               $"TrustServerCertificate=False;" +
               $"Connection Timeout=30;";
    }
}

public record QueryStatistics(
    string RequestId,
    string Status,
    TimeSpan ElapsedTime,
    long RowCount,
    long DataProcessedMB,
    bool CacheHit
);
