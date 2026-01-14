using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using System.Data;

namespace Faborite.Core.Connectors.Database;

/// <summary>
/// Production-ready SQL Server connector with full query and metadata support.
/// Issue #136 - SQL Server direct connector
/// </summary>
public class SqlServerConnector : IQueryableConnector
{
    private readonly ILogger<SqlServerConnector> _logger;
    private readonly string _connectionString;

    public string Name => "SQL Server";
    public string Version => "1.0.0";

    public SqlServerConnector(ILogger<SqlServerConnector> logger, string connectionString)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Testing SQL Server connection");
        
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            await using var command = new SqlCommand("SELECT @@VERSION", connection);
            var version = await command.ExecuteScalarAsync(cancellationToken);
            
            _logger.LogInformation("SQL Server connection successful: {Version}", version);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL Server connection failed");
            return false;
        }
    }

    public async Task<ConnectorMetadata> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        var version = await GetServerVersionAsync(connection, cancellationToken);
        var edition = await GetServerEditionAsync(connection, cancellationToken);
        
        return new ConnectorMetadata(
            "SQL Server",
            $"{version} ({edition})",
            new Dictionary<string, string>
            {
                ["ACID"] = "true",
                ["Transactions"] = "true",
                ["JSON"] = "true",
                ["XML"] = "true",
                ["FullTextSearch"] = "true",
                ["ColumnStore"] = "true",
                ["InMemory"] = "true",
                ["TemporalTables"] = "true",
                ["Replication"] = "true"
            },
            new List<string> { "Query", "BulkCopy", "Transaction", "Streaming", "MERGE" }
        );
    }

    public async Task<QueryResult> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing SQL Server query");
        var startTime = DateTime.UtcNow;
        
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        await using var command = new SqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        
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
        _logger.LogInformation("Listing SQL Server tables");
        
        const string query = @"
            SELECT 
                s.name AS schema_name,
                t.name AS table_name,
                p.rows AS row_count
            FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            INNER JOIN sys.partitions p ON t.object_id = p.object_id
            WHERE p.index_id IN (0, 1)
                AND s.name NOT IN ('sys')
            ORDER BY s.name, t.name";
        
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        await using var command = new SqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        
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

    public async Task<DataTransferResult> BulkCopyAsync(
        string targetTable,
        DataTable data,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting bulk copy to {Table}", targetTable);
        var startTime = DateTime.UtcNow;
        
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            using var bulkCopy = new SqlBulkCopy(connection)
            {
                DestinationTableName = targetTable,
                BatchSize = 5000,
                BulkCopyTimeout = 600
            };
            
            await bulkCopy.WriteToServerAsync(data, cancellationToken);
            
            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Bulk copy completed: {Rows} rows in {Duration}ms", 
                data.Rows.Count, duration.TotalMilliseconds);
            
            return new DataTransferResult(data.Rows.Count, 0, duration, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk copy failed");
            var duration = DateTime.UtcNow - startTime;
            return new DataTransferResult(0, 0, duration, false, ex.Message);
        }
    }

    public async Task<DataTransferResult> BulkCopyAsync(
        string targetTable,
        IEnumerable<Dictionary<string, object?>> data,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting bulk copy to {Table}", targetTable);
        var startTime = DateTime.UtcNow;
        long rowsTransferred = 0;
        
        try
        {
            var dataList = data.ToList();
            if (dataList.Count == 0)
            {
                return new DataTransferResult(0, 0, TimeSpan.Zero, true);
            }
            
            // Convert to DataTable
            var dataTable = new DataTable();
            var columns = dataList[0].Keys.ToList();
            
            foreach (var column in columns)
            {
                dataTable.Columns.Add(column);
            }
            
            foreach (var row in dataList)
            {
                var dataRow = dataTable.NewRow();
                foreach (var column in columns)
                {
                    dataRow[column] = row[column] ?? DBNull.Value;
                }
                dataTable.Rows.Add(dataRow);
            }
            
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            using var bulkCopy = new SqlBulkCopy(connection)
            {
                DestinationTableName = targetTable,
                BatchSize = 5000,
                BulkCopyTimeout = 600
            };
            
            await bulkCopy.WriteToServerAsync(dataTable, cancellationToken);
            rowsTransferred = dataTable.Rows.Count;
            
            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Bulk copy completed: {Rows} rows in {Duration}ms", 
                rowsTransferred, duration.TotalMilliseconds);
            
            return new DataTransferResult(rowsTransferred, 0, duration, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk copy failed");
            var duration = DateTime.UtcNow - startTime;
            return new DataTransferResult(rowsTransferred, 0, duration, false, ex.Message);
        }
    }

    public async Task<DataTransferResult> MergeAsync(
        string targetTable,
        IEnumerable<Dictionary<string, object?>> data,
        List<string> keyColumns,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting MERGE operation on {Table}", targetTable);
        var startTime = DateTime.UtcNow;
        
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            var dataList = data.ToList();
            if (dataList.Count == 0)
            {
                return new DataTransferResult(0, 0, TimeSpan.Zero, true);
            }
            
            var columns = dataList[0].Keys.ToList();
            var updateColumns = columns.Except(keyColumns).ToList();
            
            var mergeQuery = $@"
                MERGE {targetTable} AS target
                USING (VALUES {string.Join(",", dataList.Select((_, i) => $"({string.Join(",", columns.Select(c => $"@{c}{i}"))})"))} ) 
                    AS source ({string.Join(",", columns)})
                ON {string.Join(" AND ", keyColumns.Select(k => $"target.{k} = source.{k}"))}
                WHEN MATCHED THEN
                    UPDATE SET {string.Join(", ", updateColumns.Select(c => $"{c} = source.{c}"))}
                WHEN NOT MATCHED THEN
                    INSERT ({string.Join(",", columns)})
                    VALUES ({string.Join(",", columns.Select(c => $"source.{c}"))});";
            
            await using var command = new SqlCommand(mergeQuery, connection);
            
            for (int i = 0; i < dataList.Count; i++)
            {
                foreach (var column in columns)
                {
                    command.Parameters.AddWithValue($"@{column}{i}", dataList[i][column] ?? DBNull.Value);
                }
            }
            
            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            
            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("MERGE completed: {Rows} rows affected in {Duration}ms", 
                rowsAffected, duration.TotalMilliseconds);
            
            return new DataTransferResult(rowsAffected, 0, duration, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MERGE failed");
            var duration = DateTime.UtcNow - startTime;
            return new DataTransferResult(0, 0, duration, false, ex.Message);
        }
    }

    private static List<ColumnMetadata> GetColumnMetadata(SqlDataReader reader)
    {
        var columns = new List<ColumnMetadata>();
        var schemaTable = reader.GetSchemaTable();
        
        if (schemaTable != null)
        {
            foreach (DataRow row in schemaTable.Rows)
            {
                var columnName = row["ColumnName"].ToString() ?? "";
                var dataType = row["DataTypeName"].ToString() ?? "";
                var allowDBNull = (bool)(row["AllowDBNull"] ?? true);
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
            WHERE c.object_id = OBJECT_ID(@schemaTable)
            ORDER BY c.column_id";
        
        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@schemaTable", $"{schema}.{tableName}");
        
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
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

    private async Task<string> GetServerVersionAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("SELECT SERVERPROPERTY('ProductVersion')", connection);
        var version = await command.ExecuteScalarAsync(cancellationToken);
        return version?.ToString() ?? "Unknown";
    }

    private async Task<string> GetServerEditionAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("SELECT SERVERPROPERTY('Edition')", connection);
        var edition = await command.ExecuteScalarAsync(cancellationToken);
        return edition?.ToString() ?? "Unknown";
    }
}
