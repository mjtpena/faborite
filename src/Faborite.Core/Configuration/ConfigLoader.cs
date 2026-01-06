using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace Faborite.Core.Configuration;

/// <summary>
/// Loads and saves Faborite configuration.
/// </summary>
public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Load configuration from file and environment variables.
    /// </summary>
    /// <param name="configPath">Path to config file (optional).</param>
    /// <returns>Loaded configuration.</returns>
    public static FaboriteConfig Load(string? configPath = null)
    {
        var builder = new ConfigurationBuilder();
        
        // Try to find config file
        var possiblePaths = new[]
        {
            configPath,
            "faborite.json",
            ".faborite.json",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".faborite", "config.json")
        };

        foreach (var path in possiblePaths.Where(p => p != null))
        {
            if (File.Exists(path))
            {
                builder.AddJsonFile(path, optional: true);
                break;
            }
        }
        
        // Add environment variables with FABORITE_ prefix
        builder.AddEnvironmentVariables("FABORITE_");
        
        var configuration = builder.Build();
        var config = new FaboriteConfig();
        configuration.Bind(config);
        
        return config;
    }

    /// <summary>
    /// Load configuration from JSON string.
    /// </summary>
    public static FaboriteConfig LoadFromJson(string json)
    {
        return JsonSerializer.Deserialize<FaboriteConfig>(json, JsonOptions) 
            ?? new FaboriteConfig();
    }

    /// <summary>
    /// Save configuration to file.
    /// </summary>
    public static void Save(FaboriteConfig config, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Generate example configuration file content.
    /// </summary>
    public static string GenerateExample()
    {
        var example = new FaboriteConfig
        {
            WorkspaceId = "your-workspace-guid",
            LakehouseId = "your-lakehouse-guid",
            Sample = new SampleConfig
            {
                Strategy = SampleStrategy.Random,
                Rows = 10000,
                Seed = 42,
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
                ParallelTables = 4,
                SkipTables = new List<string> { "_staging", "_temp" }
            },
            Tables = new Dictionary<string, TableOverride>
            {
                ["large_events"] = new TableOverride
                {
                    Sample = new SampleConfig
                    {
                        Strategy = SampleStrategy.Recent,
                        Rows = 5000,
                        DateColumn = "event_timestamp"
                    }
                },
                ["dim_products"] = new TableOverride
                {
                    Sample = new SampleConfig
                    {
                        Strategy = SampleStrategy.Full
                    }
                }
            }
        };

        return JsonSerializer.Serialize(example, JsonOptions);
    }
    
    /// <summary>
    /// Get effective configuration for a specific table.
    /// </summary>
    public static (SampleConfig Sample, FormatConfig Format) GetTableConfig(
        FaboriteConfig config, 
        string tableName)
    {
        var sampleConfig = config.Sample;
        var formatConfig = config.Format;

        if (config.Tables.TryGetValue(tableName, out var tableOverride))
        {
            if (tableOverride.Sample != null)
            {
                sampleConfig = MergeSampleConfig(config.Sample, tableOverride.Sample);
            }
            if (tableOverride.Format != null)
            {
                formatConfig = MergeFormatConfig(config.Format, tableOverride.Format);
            }
        }

        return (sampleConfig, formatConfig);
    }

    private static SampleConfig MergeSampleConfig(SampleConfig baseConfig, SampleConfig overrideConfig)
    {
        return new SampleConfig
        {
            Strategy = overrideConfig.Strategy,
            Rows = overrideConfig.Rows > 0 ? overrideConfig.Rows : baseConfig.Rows,
            DateColumn = overrideConfig.DateColumn ?? baseConfig.DateColumn,
            StratifyColumn = overrideConfig.StratifyColumn ?? baseConfig.StratifyColumn,
            WhereClause = overrideConfig.WhereClause ?? baseConfig.WhereClause,
            Seed = overrideConfig.Seed != 0 ? overrideConfig.Seed : baseConfig.Seed,
            AutoDetectDate = overrideConfig.AutoDetectDate,
            MaxFullTableRows = overrideConfig.MaxFullTableRows > 0 ? overrideConfig.MaxFullTableRows : baseConfig.MaxFullTableRows
        };
    }

    private static FormatConfig MergeFormatConfig(FormatConfig baseConfig, FormatConfig overrideConfig)
    {
        return new FormatConfig
        {
            Format = overrideConfig.Format,
            Compression = !string.IsNullOrEmpty(overrideConfig.Compression) ? overrideConfig.Compression : baseConfig.Compression,
            PartitionBy = overrideConfig.PartitionBy ?? baseConfig.PartitionBy,
            SingleFile = overrideConfig.SingleFile
        };
    }
}
