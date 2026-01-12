using DuckDB.NET.Data;

namespace Faborite.Api.Endpoints;

public static class QueryEndpoints
{
    public static void MapQueryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/query").WithTags("Query");

        group.MapPost("/", async (QueryRequest request) =>
        {
            var localPath = request.LocalPath ?? "./local_lakehouse";

            if (!Directory.Exists(localPath))
                return Results.BadRequest(new { Error = "Local data path does not exist" });

            try
            {
                using var connection = new DuckDBConnection("DataSource=:memory:");
                await connection.OpenAsync();

                // Register all parquet files as views
                var tables = Directory.GetDirectories(localPath);
                foreach (var tableDir in tables)
                {
                    var tableName = Path.GetFileName(tableDir);
                    var parquetFiles = Directory.GetFiles(tableDir, "*.parquet");
                    
                    if (parquetFiles.Length > 0)
                    {
                        var parquetPath = parquetFiles[0].Replace("\\", "/");
                        using var cmd = connection.CreateCommand();
                        cmd.CommandText = $"CREATE VIEW \"{tableName}\" AS SELECT * FROM read_parquet('{parquetPath}')";
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                // Execute query
                using var queryCmd = connection.CreateCommand();
                queryCmd.CommandText = request.Sql;
                
                using var reader = await queryCmd.ExecuteReaderAsync();
                
                var columns = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columns.Add(reader.GetName(i));
                }

                var rows = new List<Dictionary<string, object?>>();
                var maxRows = request.MaxRows ?? 1000;
                var rowCount = 0;

                while (await reader.ReadAsync() && rowCount < maxRows)
                {
                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    rows.Add(row);
                    rowCount++;
                }

                return Results.Ok(new QueryResult
                {
                    Columns = columns,
                    Rows = rows,
                    RowCount = rows.Count,
                    Truncated = rowCount >= maxRows
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        })
        .WithName("ExecuteQuery")
        ;

        group.MapGet("/tables", (string? localPath) =>
        {
            var path = localPath ?? "./local_lakehouse";

            if (!Directory.Exists(path))
                return Results.Ok(new { Tables = Array.Empty<string>() });

            var tables = Directory.GetDirectories(path)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();

            return Results.Ok(new { Tables = tables });
        })
        .WithName("GetQueryableTables")
        ;

        group.MapPost("/preview/{tableName}", async (string tableName, int? rows, string? localPath) =>
        {
            var path = localPath ?? "./local_lakehouse";
            var tablePath = Path.Combine(path, tableName);

            if (!Directory.Exists(tablePath))
                return Results.NotFound(new { Error = $"Table '{tableName}' not found" });

            var parquetFiles = Directory.GetFiles(tablePath, "*.parquet");
            if (parquetFiles.Length == 0)
                return Results.NotFound(new { Error = $"No parquet files found for table '{tableName}'" });

            try
            {
                using var connection = new DuckDBConnection("DataSource=:memory:");
                await connection.OpenAsync();

                var parquetPath = parquetFiles[0].Replace("\\", "/");
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT * FROM read_parquet('{parquetPath}') LIMIT {rows ?? 100}";
                
                using var reader = await cmd.ExecuteReaderAsync();
                
                var columns = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columns.Add(reader.GetName(i));
                }

                var dataRows = new List<Dictionary<string, object?>>();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    dataRows.Add(row);
                }

                return Results.Ok(new QueryResult
                {
                    Columns = columns,
                    Rows = dataRows,
                    RowCount = dataRows.Count,
                    Truncated = false
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        })
        .WithName("PreviewLocalTable")
        ;
    }
}

public record QueryRequest(
    string Sql,
    string? LocalPath = null,
    int? MaxRows = 1000
);

public class QueryResult
{
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public int RowCount { get; set; }
    public bool Truncated { get; set; }
}
