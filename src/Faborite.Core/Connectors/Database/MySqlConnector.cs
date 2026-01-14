using Microsoft.Extensions.Logging;
using MySqlConnector;
using System.Data;

namespace Faborite.Core.Connectors.Database;

/// <summary>
/// Production-ready MySQL connector with full query and metadata support.
/// Issue #136 - MySQL direct connector
/// </summary>
public class MySqlConnector : IQueryableConnector
{
    private readonly ILogger<MySqlConnector> _logger;
    private readonly string _connectionString;

    public string Name => "MySQL";
    public string Version => "1.0.0";

    public MySqlConnector(ILogger<MySqlConnector> logger, string connectionString)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Testing MySQL connection");
        
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            await using var command = new MySqlCommand("SELECT VERSION()", connection);
            var version = await command.ExecuteScalarAsync(cancellationToken);
            
            _logger.LogInformation("MySQL connection successful: {Version}", version);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MySQL connection failed");
            return false;
        }
    }

    public async Task<ConnectorMetadata> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        var version = await GetServerVersionAsync(connection, cancellationToken);
        var isMariaDB = version.Contains("MariaDB", StringComparison.OrdinalIgnoreCase);
        
        return new ConnectorMetadata(
            isMariaDB ? "MariaDB" : "MySQL",
            version,
            new Dictionary<string, string>
            {
                ["ACID"] = "true",
                ["Transactions"] = "true",
                ["JSON"] = "true",
                ["FullTextSearch"] = "true",
                ["Replication"] = "true",
                ["Partitioning"] = "true"
            },
            new List<string> { "Query", "BulkLoad", "Transaction", "Streaming" }
        );
    }

    public async Task<QueryResult> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing MySQL query");
        var startTime = DateTime.UtcNow;
        
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        await using var command = new MySqlCommand(query, connection);
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
        _logger.LogInformation("Listing MySQL tables");
        
        const string query = @"
            SELECT 
                TABLE_SCHEMA,
                TABLE_NAME,
                TABLE_ROWS
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys')
                AND TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_SCHEMA, TABLE_NAME";
        
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        await using var command = new MySqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        
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

    public async Task<DataTransferResult> BulkLoadAsync(
        string targetTable,
        string csvFilePath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting LOAD DATA INFILE for {Table}", targetTable);
        var startTime = DateTime.UtcNow;
        
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            var query = $@"
                LOAD DATA LOCAL INFILE '{csvFilePath}'
                INTO TABLE {targetTable}
                FIELDS TERMINATED BY ','
                ENCLOSED BY '""'
                LINES TERMINATED BY '\n'
                IGNORE 1 ROWS";
            
            await using var command = new MySqlCommand(query, connection);
            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            
            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Bulk load completed: {Rows} rows in {Duration}ms", 
                rowsAffected, duration.TotalMilliseconds);
            
            return new DataTransferResult(rowsAffected, 0, duration, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk load failed");
            var duration = DateTime.UtcNow - startTime;
            return new DataTransferResult(0, 0, duration, false, ex.Message);
        }
    }

    public async Task<DataTransferResult> BulkInsertAsync(
        string targetTable,
        IEnumerable<Dictionary<string, object?>> data,
        int batchSize = 1000,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting bulk insert to {Table}", targetTable);
        var startTime = DateTime.UtcNow;
        long rowsTransferred = 0;
        
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            var dataList = data.ToList();
            if (dataList.Count == 0)
            {
                return new DataTransferResult(0, 0, TimeSpan.Zero, true);
            }
            
            var columns = dataList[0].Keys.ToList();
            var columnList = string.Join(", ", columns.Select(c => $"`{c}`"));
            
            for (int i = 0; i < dataList.Count; i += batchSize)
            {
                var batch = dataList.Skip(i).Take(batchSize).ToList();
                
                var values = new List<string>();
                var parameters = new List<MySqlParameter>();
                
                for (int j = 0; j < batch.Count; j++)
                {
                    var row = batch[j];
                    var paramNames = columns.Select(c => $"@p{i}_{j}_{c}").ToList();
                    values.Add($"({string.Join(", ", paramNames)})");
                    
                    foreach (var column in columns)
                    {
                        parameters.Add(new MySqlParameter($"@p{i}_{j}_{column}", row[column] ?? DBNull.Value));
                    }
                }
                
                var insertQuery = $"INSERT INTO {targetTable} ({columnList}) VALUES {string.Join(", ", values)}";
                
                await using var command = new MySqlCommand(insertQuery, connection);
                command.Parameters.AddRange(parameters.ToArray());
                
                rowsTransferred += await command.ExecuteNonQueryAsync(cancellationToken);
            }
            
            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Bulk insert completed: {Rows} rows in {Duration}ms", 
                rowsTransferred, duration.TotalMilliseconds);
            
            return new DataTransferResult(rowsTransferred, 0, duration, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk insert failed");
            var duration = DateTime.UtcNow - startTime;
            return new DataTransferResult(rowsTransferred, 0, duration, false, ex.Message);
        }
    }

    private static List<ColumnMetadata> GetColumnMetadata(MySqlDataReader reader)
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
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string query = @"
            SELECT 
                COLUMN_NAME,
                DATA_TYPE,
                IS_NULLABLE,
                CHARACTER_MAXIMUM_LENGTH
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @tableName
            ORDER BY ORDINAL_POSITION";
        
        await using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@tableName", tableName);
        
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
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

    private async Task<string> GetServerVersionAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand("SELECT VERSION()", connection);
        var version = await command.ExecuteScalarAsync(cancellationToken);
        return version?.ToString() ?? "Unknown";
    }
}
