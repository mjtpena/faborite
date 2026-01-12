using System.Text.Json;

namespace Faborite.Api.Endpoints;

public static class LocalDataEndpoints
{
    public static void MapLocalDataEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/local").WithTags("Local Data");

        group.MapGet("/", (string? path) =>
        {
            var localPath = path ?? "./local_lakehouse";
            
            if (!Directory.Exists(localPath))
            {
                return Results.Ok(new
                {
                    Exists = false,
                    Path = Path.GetFullPath(localPath),
                    Tables = Array.Empty<object>()
                });
            }

            var tables = Directory.GetDirectories(localPath)
                .Select(dir =>
                {
                    var tableName = Path.GetFileName(dir);
                    var files = Directory.GetFiles(dir);
                    var schemaFile = Path.Combine(dir, "_schema.json");
                    
                    return new LocalTableInfo
                    {
                        Name = tableName,
                        Path = dir,
                        Files = files.Select(f => new LocalFileInfo
                        {
                            Name = Path.GetFileName(f),
                            Path = f,
                            SizeBytes = new FileInfo(f).Length,
                            Extension = Path.GetExtension(f)
                        }).ToList(),
                        HasSchema = File.Exists(schemaFile),
                        TotalSizeBytes = files.Sum(f => new FileInfo(f).Length)
                    };
                })
                .ToList();

            return Results.Ok(new
            {
                Exists = true,
                Path = Path.GetFullPath(localPath),
                Tables = tables,
                TotalTables = tables.Count,
                TotalSizeBytes = tables.Sum(t => t.TotalSizeBytes)
            });
        })
        .WithName("ListLocalTables")
        ;

        group.MapGet("/{tableName}/schema", (string tableName, string? path) =>
        {
            var localPath = path ?? "./local_lakehouse";
            var schemaPath = Path.Combine(localPath, tableName, "_schema.json");

            if (!File.Exists(schemaPath))
                return Results.NotFound(new { Error = $"Schema not found for table '{tableName}'" });

            try
            {
                var json = File.ReadAllText(schemaPath);
                var schema = JsonSerializer.Deserialize<JsonElement>(json);
                return Results.Ok(schema);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = $"Failed to read schema: {ex.Message}" });
            }
        })
        .WithName("GetLocalTableSchema")
        ;

        group.MapGet("/{tableName}/files", (string tableName, string? path) =>
        {
            var localPath = path ?? "./local_lakehouse";
            var tablePath = Path.Combine(localPath, tableName);

            if (!Directory.Exists(tablePath))
                return Results.NotFound(new { Error = $"Table '{tableName}' not found locally" });

            var files = Directory.GetFiles(tablePath)
                .Select(f => new LocalFileInfo
                {
                    Name = Path.GetFileName(f),
                    Path = f,
                    SizeBytes = new FileInfo(f).Length,
                    Extension = Path.GetExtension(f),
                    LastModified = File.GetLastWriteTimeUtc(f)
                })
                .ToList();

            return Results.Ok(files);
        })
        .WithName("GetLocalTableFiles")
        ;

        group.MapDelete("/{tableName}", (string tableName, string? path) =>
        {
            var localPath = path ?? "./local_lakehouse";
            var tablePath = Path.Combine(localPath, tableName);

            if (!Directory.Exists(tablePath))
                return Results.NotFound(new { Error = $"Table '{tableName}' not found locally" });

            try
            {
                Directory.Delete(tablePath, recursive: true);
                return Results.Ok(new { Message = $"Table '{tableName}' deleted" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = $"Failed to delete: {ex.Message}" });
            }
        })
        .WithName("DeleteLocalTable")
        ;

        group.MapDelete("/", (string? path) =>
        {
            var localPath = path ?? "./local_lakehouse";

            if (!Directory.Exists(localPath))
                return Results.Ok(new { Message = "Nothing to delete" });

            try
            {
                var tables = Directory.GetDirectories(localPath);
                foreach (var table in tables)
                {
                    Directory.Delete(table, recursive: true);
                }
                return Results.Ok(new { Message = $"Deleted {tables.Length} tables" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = $"Failed to delete: {ex.Message}" });
            }
        })
        .WithName("DeleteAllLocalTables")
        ;
    }
}

public class LocalTableInfo
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public List<LocalFileInfo> Files { get; set; } = new();
    public bool HasSchema { get; set; }
    public long TotalSizeBytes { get; set; }
}

public class LocalFileInfo
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Extension { get; set; } = "";
    public DateTime? LastModified { get; set; }
}
