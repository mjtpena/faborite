using Faborite.Api.Services;
using Faborite.Core.OneLake;

namespace Faborite.Api.Endpoints;

public static class TablesEndpoints
{
    public static void MapTablesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/tables").WithTags("Tables");

        group.MapGet("/", async (FaboriteApiService service) =>
        {
            try
            {
                var tables = await service.ListTablesAsync();
                return Results.Ok(tables.Select(t => new TableDto
                {
                    Name = t.Name,
                    Path = t.Path,
                    Format = "delta",
                    RowCount = null,
                    SizeBytes = t.SizeBytes,
                    LastModified = t.LastModified?.DateTime,
                    Columns = null
                }));
            }
            catch (InvalidOperationException)
            {
                return Results.BadRequest(new { Error = "Not connected. Connect first." });
            }
        })
        .WithName("ListTables");

        group.MapGet("/{tableName}/preview", async (string tableName, int? rows, FaboriteApiService service) =>
        {
            try
            {
                var tables = await service.ListTablesAsync();
                var table = tables.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                
                if (table == null)
                    return Results.NotFound(new { Error = $"Table '{tableName}' not found" });

                // For preview, we'll return table metadata for now
                // Full preview would require reading the data
                return Results.Ok(new
                {
                    TableName = table.Name,
                    RowCount = (long?)null,
                    Columns = (object?)null,
                    PreviewRows = rows ?? 100,
                    Message = "Data preview requires sync first"
                });
            }
            catch (InvalidOperationException)
            {
                return Results.BadRequest(new { Error = "Not connected. Connect first." });
            }
        })
        .WithName("PreviewTable");

        group.MapGet("/{tableName}/schema", async (string tableName, FaboriteApiService service) =>
        {
            try
            {
                var tables = await service.ListTablesAsync();
                var table = tables.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                
                if (table == null)
                    return Results.NotFound(new { Error = $"Table '{tableName}' not found" });

                return Results.Ok(new
                {
                    TableName = table.Name,
                    Columns = (object?)null
                });
            }
            catch (InvalidOperationException)
            {
                return Results.BadRequest(new { Error = "Not connected. Connect first." });
            }
        })
        .WithName("GetTableSchema");
    }
}

public class TableDto
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Format { get; set; } = "";
    public long? RowCount { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime? LastModified { get; set; }
    public List<ColumnDto>? Columns { get; set; }
}

public class ColumnDto
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Nullable { get; set; }
}
