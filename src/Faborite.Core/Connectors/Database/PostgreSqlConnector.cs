using Microsoft.Extensions.Logging;
using Npgsql;
using System.Data;

namespace Faborite.Core.Connectors.Database;

/// <summary>
/// Production-ready PostgreSQL connector with full query and metadata support.
/// Issue #136 - PostgreSQL direct connector
/// </summary>
public class PostgreSqlConnector : IQueryableConnector
{
    private readonly ILogger<PostgreSqlConnector> _logger;
    private readonly string _connectionString;

    public string Name => "PostgreSQL";
    public string Version => "1.0.0";

    public PostgreSqlConnector(ILogger<PostgreSqlConnector> logger, string connectionString)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Testing PostgreSQL connection");
        
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            await using var command = new NpgsqlCommand("SELECT version()", connection);
            var version = await command.ExecuteScalarAsync(cancellationToken);
            
            _logger.LogInformation("PostgreSQL connection successful: {Version}", version);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgreSQL connection failed");
            return false;
        }
    }

    public async Task<ConnectorMetadata> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        var version = await GetServerVersionAsync(connection, cancellationToken);
        
        return new ConnectorMetadata(
            "PostgreSQL",
            version,
            new Dictionary<string, string>
            {
                ["ACID"] = "true",
                ["Transactions"] = "true",
                ["JSONB"] = "true",
                ["FullTextSearch"] = "true",
                ["Partitioning"] = "true",
                ["Replication"] = "true"
            },
            new List<string> { "Query", "BulkCopy", "Transaction", "Streaming" }
        );
    }

    public async Task<QueryResult> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing PostgreSQL query");
        var startTime = DateTime.UtcNow;
        
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        await using var command = new NpgsqlCommand(query, connection);
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
        _logger.LogInformation("Listing PostgreSQL tables");
        
        const string query = @"
            SELECT 
                table_schema,
                table_name,
                (SELECT COUNT(*) FROM information_schema.columns c 
                 WHERE c.table_schema = t.table_schema AND c.table_name = t.table_name) as column_count
            FROM information_schema.tables t
            WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
                AND table_type = 'BASE TABLE'
            ORDER BY table_schema, table_name";
        
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        await using var command = new NpgsqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        
        var tables = new List<TableInfo>();
        
        while (await reader.ReadAsync(cancellationToken))
        {
            var schema = reader.GetString(0);
            var tableName = reader.GetString(1);
            var columnCount = reader.GetInt64(2);
            
            var columns = await GetTableColumnsAsync(schema, tableName, connection, cancellationToken);
            var rowCount = await GetTableRowCountAsync(schema, tableName, connection, cancellationToken);
            
            tables.Add(new TableInfo(tableName, schema, rowCount, columns));
        }
        
        _logger.LogInformation("Found {TableCount} tables", tables.Count);
        return tables;
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
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            var dataList = data.ToList();
            if (dataList.Count == 0)
            {
                return new DataTransferResult(0, 0, TimeSpan.Zero, true);
            }
            
            var columns = dataList[0].Keys.ToList();
            var copyCommand = $"COPY {targetTable} ({string.Join(", ", columns)}) FROM STDIN (FORMAT BINARY)";
            
            await using var writer = await connection.BeginBinaryImportAsync(copyCommand, cancellationToken);
            
            foreach (var row in dataList)
            {
                await writer.StartRowAsync(cancellationToken);
                foreach (var column in columns)
                {
                    await writer.WriteAsync(row[column], cancellationToken);
                }
                rowsTransferred++;
            }
            
            await writer.CompleteAsync(cancellationToken);
            
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

    private static List<ColumnMetadata> GetColumnMetadata(NpgsqlDataReader reader)
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
                
                columns.Add(new ColumnMetadata(columnName, dataType, allowDBNull));
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
            WHERE table_schema = @schema AND table_name = @tableName
            ORDER BY ordinal_position";
        
        await using var command = new NpgsqlCommand(query, connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("tableName", tableName);
        
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
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

    private async Task<long> GetTableRowCountAsync(
        string schema,
        string tableName,
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = $"SELECT COUNT(*) FROM \"{schema}\".\"{tableName}\"";
            await using var command = new NpgsqlCommand(query, connection);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(result);
        }
        catch
        {
            return 0; // Table might not be accessible or COUNT might fail
        }
    }

    private async Task<string> GetServerVersionAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SHOW server_version", connection);
        var version = await command.ExecuteScalarAsync(cancellationToken);
        return version?.ToString() ?? "Unknown";
    }
}
