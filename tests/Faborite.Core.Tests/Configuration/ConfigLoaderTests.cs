using Faborite.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace Faborite.Core.Tests.Configuration;

public class ConfigLoaderTests : IDisposable
{
    private readonly string _testDir;
    private readonly List<string> _createdFiles = new();

    public ConfigLoaderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"faborite_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        foreach (var file in _createdFiles)
        {
            if (File.Exists(file)) File.Delete(file);
        }
        if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true);
    }

    private string CreateTempFile(string content, string extension = ".json")
    {
        var path = Path.Combine(_testDir, $"config_{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, content);
        _createdFiles.Add(path);
        return path;
    }

    [Fact]
    public void Load_WithValidJsonFile_ReturnsConfig()
    {
        // Arrange
        var json = """
        {
            "workspaceId": "test-workspace-id",
            "lakehouseId": "test-lakehouse-id",
            "sample": {
                "rows": 5000,
                "strategy": "recent"
            }
        }
        """;
        var path = CreateTempFile(json);

        // Act
        var config = ConfigLoader.Load(path);

        // Assert
        config.Should().NotBeNull();
        config.WorkspaceId.Should().Be("test-workspace-id");
        config.LakehouseId.Should().Be("test-lakehouse-id");
        config.Sample.Rows.Should().Be(5000);
        config.Sample.Strategy.Should().Be(SampleStrategy.Recent);
    }

    [Fact]
    public void Load_WithMissingFile_ReturnsDefaultConfig()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDir, "nonexistent.json");

        // Act
        var config = ConfigLoader.Load(nonExistentPath);

        // Assert
        config.Should().NotBeNull();
        config.Sample.Rows.Should().Be(10000); // Default value
        config.Sample.Strategy.Should().Be(SampleStrategy.Random); // Default value
    }

    [Fact]
    public void Load_WithPartialConfig_MergesWithDefaults()
    {
        // Arrange
        var json = """
        {
            "workspaceId": "my-workspace",
            "sample": {
                "rows": 1000
            }
        }
        """;
        var path = CreateTempFile(json);

        // Act
        var config = ConfigLoader.Load(path);

        // Assert
        config.WorkspaceId.Should().Be("my-workspace");
        config.Sample.Rows.Should().Be(1000);
        config.Sample.Strategy.Should().Be(SampleStrategy.Random); // Default
        config.Sync.ParallelTables.Should().Be(4); // Default
    }

    [Fact]
    public void Load_WithTableOverrides_LoadsOverrides()
    {
        // Arrange
        var json = """
        {
            "workspaceId": "test",
            "tables": {
                "events": {
                    "sample": {
                        "strategy": "recent",
                        "rows": 500,
                        "dateColumn": "created_at"
                    }
                }
            }
        }
        """;
        var path = CreateTempFile(json);

        // Act
        var config = ConfigLoader.Load(path);

        // Assert
        config.Tables.Should().ContainKey("events");
        config.Tables["events"].Sample.Should().NotBeNull();
        config.Tables["events"].Sample!.Strategy.Should().Be(SampleStrategy.Recent);
        config.Tables["events"].Sample!.Rows.Should().Be(500);
        config.Tables["events"].Sample!.DateColumn.Should().Be("created_at");
    }

    [Fact]
    public void Save_WritesValidJson()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            WorkspaceId = "save-test-workspace",
            LakehouseId = "save-test-lakehouse",
            Sample = new SampleConfig
            {
                Rows = 2500,
                Strategy = SampleStrategy.Stratified,
                StratifyColumn = "region"
            }
        };
        var path = Path.Combine(_testDir, "saved_config.json");
        _createdFiles.Add(path);

        // Act
        ConfigLoader.Save(config, path);
        var loadedConfig = ConfigLoader.Load(path);

        // Assert
        loadedConfig.WorkspaceId.Should().Be("save-test-workspace");
        loadedConfig.Sample.Rows.Should().Be(2500);
        loadedConfig.Sample.Strategy.Should().Be(SampleStrategy.Stratified);
        loadedConfig.Sample.StratifyColumn.Should().Be("region");
    }

    [Fact]
    public void GenerateExample_ReturnsValidJson()
    {
        // Act
        var example = ConfigLoader.GenerateExample();

        // Assert
        example.Should().NotBeNullOrWhiteSpace();
        example.Should().Contain("workspaceId");
        example.Should().Contain("lakehouseId");
        example.Should().Contain("sample");
        
        // Should be parseable
        var action = () => System.Text.Json.JsonDocument.Parse(example);
        action.Should().NotThrow();
    }

    [Fact]
    public void GetTableConfig_WithOverride_ReturnsMergedConfig()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            Sample = new SampleConfig
            {
                Rows = 10000,
                Strategy = SampleStrategy.Random
            },
            Tables = new Dictionary<string, TableOverride>
            {
                ["events"] = new TableOverride
                {
                    Sample = new SampleConfig
                    {
                        Rows = 1000,
                        Strategy = SampleStrategy.Recent,
                        DateColumn = "event_time"
                    }
                }
            }
        };

        // Act
        var (sampleConfig, formatConfig) = ConfigLoader.GetTableConfig(config, "events");

        // Assert
        sampleConfig.Rows.Should().Be(1000);
        sampleConfig.Strategy.Should().Be(SampleStrategy.Recent);
        sampleConfig.DateColumn.Should().Be("event_time");
    }

    [Fact]
    public void GetTableConfig_WithoutOverride_ReturnsDefaultConfig()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            Sample = new SampleConfig
            {
                Rows = 10000,
                Strategy = SampleStrategy.Random
            }
        };

        // Act
        var (sampleConfig, formatConfig) = ConfigLoader.GetTableConfig(config, "some_table");

        // Assert
        sampleConfig.Rows.Should().Be(10000);
        sampleConfig.Strategy.Should().Be(SampleStrategy.Random);
    }

    [Theory]
    [InlineData("random", SampleStrategy.Random)]
    [InlineData("recent", SampleStrategy.Recent)]
    [InlineData("head", SampleStrategy.Head)]
    [InlineData("tail", SampleStrategy.Tail)]
    [InlineData("stratified", SampleStrategy.Stratified)]
    [InlineData("query", SampleStrategy.Query)]
    [InlineData("full", SampleStrategy.Full)]
    public void Load_WithStrategyString_ParsesCorrectly(string strategyStr, SampleStrategy expected)
    {
        // Arrange
        var json = $$"""
        {
            "sample": {
                "strategy": "{{strategyStr}}"
            }
        }
        """;
        var path = CreateTempFile(json);

        // Act
        var config = ConfigLoader.Load(path);

        // Assert
        config.Sample.Strategy.Should().Be(expected);
    }

    [Theory]
    [InlineData("parquet", OutputFormat.Parquet)]
    [InlineData("csv", OutputFormat.Csv)]
    [InlineData("json", OutputFormat.Json)]
    [InlineData("duckdb", OutputFormat.DuckDb)]
    public void Load_WithFormatString_ParsesCorrectly(string formatStr, OutputFormat expected)
    {
        // Arrange
        var json = $$"""
        {
            "format": {
                "format": "{{formatStr}}"
            }
        }
        """;
        var path = CreateTempFile(json);

        // Act
        var config = ConfigLoader.Load(path);

        // Assert
        config.Format.Format.Should().Be(expected);
    }
}
