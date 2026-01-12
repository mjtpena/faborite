using System.Text.Json;
using Faborite.Core.Configuration;

namespace Faborite.Api.Endpoints;

public static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/config").WithTags("Configuration");

        group.MapGet("/", (string? path) =>
        {
            var configPath = path ?? "faborite.json";
            
            if (!File.Exists(configPath))
            {
                return Results.Ok(new
                {
                    Exists = false,
                    Path = Path.GetFullPath(configPath),
                    Config = GetDefaultConfig()
                });
            }

            try
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<FaboriteConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return Results.Ok(new
                {
                    Exists = true,
                    Path = Path.GetFullPath(configPath),
                    Config = config
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = $"Failed to parse config: {ex.Message}" });
            }
        })
        .WithName("GetConfig")
        ;

        group.MapPost("/", (SaveConfigRequest request) =>
        {
            var configPath = request.Path ?? "faborite.json";

            try
            {
                var json = JsonSerializer.Serialize(request.Config, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                File.WriteAllText(configPath, json);

                return Results.Ok(new
                {
                    Success = true,
                    Path = Path.GetFullPath(configPath),
                    Message = "Configuration saved"
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = $"Failed to save config: {ex.Message}" });
            }
        })
        .WithName("SaveConfig")
        ;

        group.MapPost("/validate", (FaboriteConfig config) =>
        {
            var errors = new List<string>();

            if (string.IsNullOrEmpty(config.WorkspaceId) && string.IsNullOrEmpty(config.WorkspaceName))
                errors.Add("Either WorkspaceId or WorkspaceName must be set");

            if (string.IsNullOrEmpty(config.LakehouseId) && string.IsNullOrEmpty(config.LakehouseName))
                errors.Add("Either LakehouseId or LakehouseName must be set");

            if (config.Sample.Rows <= 0)
                errors.Add("Sample rows must be greater than 0");

            if (config.Sync.ParallelTables <= 0)
                errors.Add("Parallel tables must be greater than 0");

            return Results.Ok(new
            {
                IsValid = errors.Count == 0,
                Errors = errors
            });
        })
        .WithName("ValidateConfig")
        ;

        group.MapGet("/default", () =>
        {
            return Results.Ok(GetDefaultConfig());
        })
        .WithName("GetDefaultConfig")
        ;

        group.MapGet("/schema", () =>
        {
            // Return JSON schema for config
            return Results.Ok(new
            {
                Type = "object",
                Properties = new
                {
                    WorkspaceId = new { Type = "string", Description = "Fabric workspace ID (GUID)" },
                    LakehouseId = new { Type = "string", Description = "Lakehouse ID (GUID)" },
                    Sample = new
                    {
                        Type = "object",
                        Properties = new
                        {
                            Strategy = new { Type = "string", Enum = new[] { "random", "recent", "head", "tail", "stratified", "query", "full" } },
                            Rows = new { Type = "integer", Default = 10000 }
                        }
                    }
                }
            });
        })
        .WithName("GetConfigSchema")
        ;
    }

    private static FaboriteConfig GetDefaultConfig()
    {
        return new FaboriteConfig
        {
            WorkspaceId = "your-workspace-guid",
            LakehouseId = "your-lakehouse-guid",
            Sample = new SampleConfig
            {
                Strategy = SampleStrategy.Random,
                Rows = 10000,
                Seed = 42,
                AutoDetectDate = true,
                MaxFullTableRows = 50000
            },
            Format = new FormatConfig
            {
                Format = OutputFormat.Parquet,
                Compression = "snappy",
                SingleFile = true
            },
            Sync = new SyncConfig
            {
                LocalPath = "./local_lakehouse",
                Overwrite = true,
                IncludeSchema = true,
                ParallelTables = 4
            },
            Auth = new AuthConfig
            {
                Method = AuthMethod.Default
            }
        };
    }
}

public record SaveConfigRequest(
    FaboriteConfig Config,
    string? Path = null
);
