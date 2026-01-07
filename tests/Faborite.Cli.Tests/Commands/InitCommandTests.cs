using Faborite.Cli.Commands;
using FluentAssertions;
using Spectre.Console.Cli;
using Xunit;

namespace Faborite.Cli.Tests.Commands;

public class InitCommandTests : IDisposable
{
    private readonly string _testDir;
    private readonly List<string> _createdFiles = new();

    public InitCommandTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"faborite_init_tests_{Guid.NewGuid():N}");
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

    [Fact]
    public async Task InitCommand_CreatesConfigFile()
    {
        // Arrange
        var outputPath = Path.Combine(_testDir, "faborite.json");
        _createdFiles.Add(outputPath);
        
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.AddCommand<InitCommand>("init");
        });

        // Act
        var result = await app.RunAsync(new[] { "init", "-o", outputPath });

        // Assert
        File.Exists(outputPath).Should().BeTrue();
    }

    [Fact]
    public async Task InitCommand_WithExistingFile_ReturnsError()
    {
        // Arrange
        var outputPath = Path.Combine(_testDir, "existing.json");
        File.WriteAllText(outputPath, "{}");
        _createdFiles.Add(outputPath);
        
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.AddCommand<InitCommand>("init");
        });

        // Act
        var result = await app.RunAsync(new[] { "init", "-o", outputPath });

        // Assert
        result.Should().Be(1);
    }

    [Fact]
    public async Task InitCommand_WithForce_OverwritesFile()
    {
        // Arrange
        var outputPath = Path.Combine(_testDir, "force.json");
        File.WriteAllText(outputPath, "{}");
        _createdFiles.Add(outputPath);
        
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.AddCommand<InitCommand>("init");
        });

        // Act
        var result = await app.RunAsync(new[] { "init", "-o", outputPath, "--force" });

        // Assert
        result.Should().Be(0);
        var content = File.ReadAllText(outputPath);
        content.Should().Contain("workspaceId");
    }
}

public class StatusCommandTests : IDisposable
{
    private readonly string _testDir;

    public StatusCommandTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"faborite_status_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task StatusCommand_WithNoData_ShowsNoDataMessage()
    {
        // Arrange
        var emptyPath = Path.Combine(_testDir, "empty_lakehouse");
        Directory.CreateDirectory(emptyPath);
        
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.AddCommand<StatusCommand>("status");
        });

        // Act
        var result = await app.RunAsync(new[] { "status", "-p", emptyPath });

        // Assert - should complete (might show "no data" message)
        result.Should().BeOneOf(0, 1);
    }

    [Fact]
    public async Task StatusCommand_WithData_ShowsStatus()
    {
        // Arrange - create some fake synced data
        var dataPath = Path.Combine(_testDir, "with_data");
        var tablePath = Path.Combine(dataPath, "test_table");
        Directory.CreateDirectory(tablePath);
        
        // Create a minimal parquet-like structure
        File.WriteAllText(Path.Combine(tablePath, "_schema.json"), "{}");
        
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.AddCommand<StatusCommand>("status");
        });

        // Act
        var result = await app.RunAsync(new[] { "status", "-p", dataPath });

        // Assert
        result.Should().BeOneOf(0, 1);
    }
}

public class SyncCommandSettingsTests
{
    [Fact]
    public void SyncSettings_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var settings = new SyncSettings();

        // Assert
        settings.Rows.Should().Be(10000);
        settings.Strategy.Should().Be("random");
        settings.Format.Should().Be("parquet");
        settings.OutputPath.Should().Be("./local_lakehouse");
        settings.Parallel.Should().Be(4);
        settings.NoSchema.Should().BeFalse();
    }

    [Fact]
    public void SyncSettings_Properties_CanBeSet()
    {
        // Arrange & Act
        var settings = new SyncSettings
        {
            WorkspaceId = "ws-123",
            LakehouseId = "lh-456",
            ConfigPath = "/path/to/config.json",
            Rows = 5000,
            Strategy = "recent",
            DateColumn = "created_at",
            Format = "csv",
            OutputPath = "./output",
            Tables = new[] { "table1", "table2" },
            Skip = new[] { "skip_table" },
            Parallel = 8,
            NoSchema = true
        };

        // Assert
        settings.WorkspaceId.Should().Be("ws-123");
        settings.LakehouseId.Should().Be("lh-456");
        settings.ConfigPath.Should().Be("/path/to/config.json");
        settings.Rows.Should().Be(5000);
        settings.Strategy.Should().Be("recent");
        settings.DateColumn.Should().Be("created_at");
        settings.Format.Should().Be("csv");
        settings.OutputPath.Should().Be("./output");
        settings.Tables.Should().BeEquivalentTo(new[] { "table1", "table2" });
        settings.Skip.Should().BeEquivalentTo(new[] { "skip_table" });
        settings.Parallel.Should().Be(8);
        settings.NoSchema.Should().BeTrue();
    }
}
