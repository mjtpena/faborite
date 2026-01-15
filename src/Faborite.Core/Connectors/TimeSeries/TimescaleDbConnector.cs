using Npgsql;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Connectors.TimeSeries;

/// <summary>
/// Production-ready TimescaleDB connector (PostgreSQL extension for time series).
/// Issue #154 - TimescaleDB connector
/// </summary>
public class TimescaleDbConnector : IAsyncDisposable
{
    private readonly ILogger<TimescaleDbConnector> _logger;
    private readonly string _connectionString;

    public TimescaleDbConnector(
        ILogger<TimescaleDbConnector> logger,
        string host,
        int port,
        string database,
        string username,
        string password)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _connectionString = $"Host={host};Port={port};Database={database};Username={username};Password={password}";

        _logger.LogInformation("TimescaleDB connector initialized for {Host}:{Port}/{Database}",
            host, port, database);
    }

    public async Task CreateHypertableAsync(
        string tableName,
        string timeColumn,
        int? chunkTimeInterval = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating hypertable {Table} with time column {Column}",
                tableName, timeColumn);

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = chunkTimeInterval.HasValue
                ? $"SELECT create_hypertable('{tableName}', '{timeColumn}', chunk_time_interval => INTERVAL '{chunkTimeInterval} days');"
                : $"SELECT create_hypertable('{tableName}', '{timeColumn}');";

            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Hypertable {Table} created successfully", tableName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create hypertable");
            throw;
        }
    }

    public async Task<int> InsertTimeSeriesAsync(
        string tableName,
        Dictionary<string, object?> data,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var columns = string.Join(", ", data.Keys);
            var parameters = string.Join(", ", data.Keys.Select((_, i) => $"@p{i}"));

            var sql = $"INSERT INTO {tableName} ({columns}) VALUES ({parameters})";

            await using var command = new NpgsqlCommand(sql, connection);

            int index = 0;
            foreach (var value in data.Values)
            {
                command.Parameters.AddWithValue($"@p{index}", value ?? DBNull.Value);
                index++;
            }

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogDebug("Inserted {Rows} rows into {Table}", rowsAffected, tableName);
            return rowsAffected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert time series data");
            throw;
        }
    }

    public async Task<int> BulkInsertAsync(
        string tableName,
        List<Dictionary<string, object?>> rows,
        CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0)
            return 0;

        try
        {
            _logger.LogInformation("Bulk inserting {Count} rows into {Table}", rows.Count, tableName);

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var columns = string.Join(", ", rows.First().Keys);
            var totalRows = 0;

            // Batch in groups of 1000 for performance
            const int batchSize = 1000;

            for (int i = 0; i < rows.Count; i += batchSize)
            {
                var batch = rows.Skip(i).Take(batchSize).ToList();

                var valuesSql = new List<string>();
                var command = new NpgsqlCommand { Connection = connection };

                for (int j = 0; j < batch.Count; j++)
                {
                    var row = batch[j];
                    var paramPlaceholders = new List<string>();

                    int colIndex = 0;
                    foreach (var value in row.Values)
                    {
                        var paramName = $"@p{j}_{colIndex}";
                        command.Parameters.AddWithValue(paramName, value ?? DBNull.Value);
                        paramPlaceholders.Add(paramName);
                        colIndex++;
                    }

                    valuesSql.Add($"({string.Join(", ", paramPlaceholders)})");
                }

                command.CommandText = $"INSERT INTO {tableName} ({columns}) VALUES {string.Join(", ", valuesSql)}";

                totalRows += await command.ExecuteNonQueryAsync(cancellationToken);
            }

            _logger.LogInformation("Bulk inserted {Rows} rows into {Table}", totalRows, tableName);
            return totalRows;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bulk insert");
            throw;
        }
    }

    public async Task<List<Dictionary<string, object?>>> QueryAsync(
        string sql,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Executing query");

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var results = new List<Dictionary<string, object?>>();

            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>();

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }

                results.Add(row);
            }

            _logger.LogInformation("Query returned {Count} rows", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute query");
            throw;
        }
    }

    public async Task<List<Dictionary<string, object?>>> TimeWeightedAverageAsync(
        string tableName,
        string timeColumn,
        string valueColumn,
        DateTime start,
        DateTime end,
        string? groupBy = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sql = groupBy != null
                ? $@"
                    SELECT {groupBy}, 
                           time_bucket('1 hour', {timeColumn}) AS bucket,
                           time_weight('Linear', {timeColumn}, {valueColumn}) AS twa
                    FROM {tableName}
                    WHERE {timeColumn} BETWEEN @start AND @end
                    GROUP BY {groupBy}, bucket
                    ORDER BY bucket"
                : $@"
                    SELECT time_bucket('1 hour', {timeColumn}) AS bucket,
                           time_weight('Linear', {timeColumn}, {valueColumn}) AS twa
                    FROM {tableName}
                    WHERE {timeColumn} BETWEEN @start AND @end
                    GROUP BY bucket
                    ORDER BY bucket";

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("@start", start);
            command.Parameters.AddWithValue("@end", end);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var results = new List<Dictionary<string, object?>>();

            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>();

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }

                results.Add(row);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate time-weighted average");
            throw;
        }
    }

    public async Task<List<Dictionary<string, object?>>> DownsampleAsync(
        string tableName,
        string timeColumn,
        string intervalStr,
        List<string> aggregations,
        DateTime start,
        DateTime end,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var aggClauses = string.Join(", ", aggregations);

            var sql = $@"
                SELECT time_bucket('{intervalStr}', {timeColumn}) AS bucket,
                       {aggClauses}
                FROM {tableName}
                WHERE {timeColumn} BETWEEN @start AND @end
                GROUP BY bucket
                ORDER BY bucket";

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("@start", start);
            command.Parameters.AddWithValue("@end", end);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var results = new List<Dictionary<string, object?>>();

            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>();

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }

                results.Add(row);
            }

            _logger.LogInformation("Downsampled {Count} buckets", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to downsample");
            throw;
        }
    }

    public async Task AddCompressionPolicyAsync(
        string tableName,
        int olderThanDays,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Adding compression policy to {Table} for data older than {Days} days",
                tableName, olderThanDays);

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = $@"
                SELECT add_compression_policy('{tableName}', 
                    INTERVAL '{olderThanDays} days');";

            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Compression policy added successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add compression policy");
            throw;
        }
    }

    public async Task AddRetentionPolicyAsync(
        string tableName,
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Adding retention policy to {Table} for {Days} days",
                tableName, retentionDays);

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = $@"
                SELECT add_retention_policy('{tableName}', 
                    INTERVAL '{retentionDays} days');";

            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Retention policy added successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add retention policy");
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        _logger.LogDebug("TimescaleDB connector disposed");
    }
}
