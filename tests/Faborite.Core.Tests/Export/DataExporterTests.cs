using Faborite.Core.Configuration;
using Faborite.Core.Export;
using FluentAssertions;
using Xunit;

namespace Faborite.Core.Tests.Export;

public class DataExporterTests : IDisposable
{
    private readonly DataExporter _exporter;
    private readonly string _testDir;
    private readonly List<string> _createdFiles = new();

    public DataExporterTests()
    {
        _exporter = new DataExporter();
        _testDir = Path.Combine(Path.GetTempPath(), $"faborite_export_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        _exporter.Dispose();
        foreach (var file in _createdFiles)
        {
            if (File.Exists(file)) File.Delete(file);
        }
        if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true);
    }

    private string CreateTestParquet(int rowCount = 100)
    {
        var path = Path.Combine(_testDir, $"source_{Guid.NewGuid():N}.parquet");
        _createdFiles.Add(path);
        
        using var conn = new DuckDB.NET.Data.DuckDBConnection("DataSource=:memory:");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            COPY (
                SELECT 
                    i as id,
                    'name_' || i as name,
                    CAST(i * 10.5 as DOUBLE) as value
                FROM generate_series(1, {rowCount}) t(i)
            ) TO '{path.Replace("\\", "/")}' (FORMAT PARQUET)";
        cmd.ExecuteNonQuery();
        
        return path;
    }

    [Fact]
    public void Export_ToParquet_CreatesValidFile()
    {
        // Arrange
        var sourcePath = CreateTestParquet(100);
        var outputDir = Path.Combine(_testDir, "parquet_output");
        _createdFiles.Add(Path.Combine(outputDir, "test_table", "test_table.parquet"));
        var config = new FormatConfig { Format = OutputFormat.Parquet };

        // Act
        var result = _exporter.Export(sourcePath, "test_table", outputDir, config);

        // Assert
        File.Exists(result).Should().BeTrue();
        new FileInfo(result).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Export_ToCsv_CreatesValidFile()
    {
        // Arrange
        var sourcePath = CreateTestParquet(50);
        var outputDir = Path.Combine(_testDir, "csv_output");
        var config = new FormatConfig { Format = OutputFormat.Csv };

        // Act
        var result = _exporter.Export(sourcePath, "test_table", outputDir, config);

        // Assert
        File.Exists(result).Should().BeTrue();
        var content = File.ReadAllText(result);
        content.Should().Contain("id");
        content.Should().Contain("name");
        content.Should().Contain("value");
        content.Split('\n').Length.Should().BeGreaterThan(1); // Header + data
    }

    [Fact]
    public void Export_ToJson_CreatesValidFile()
    {
        // Arrange
        var sourcePath = CreateTestParquet(50);
        var outputDir = Path.Combine(_testDir, "json_output");
        var config = new FormatConfig { Format = OutputFormat.Json };

        // Act
        var result = _exporter.Export(sourcePath, "test_table", outputDir, config);

        // Assert
        File.Exists(result).Should().BeTrue();
        var content = File.ReadAllText(result);
        content.Should().Contain("\"id\"");
        content.Should().Contain("\"name\"");
    }

    [Fact]
    public void Export_ToDuckDb_CreatesValidDatabase()
    {
        // Arrange
        var sourcePath = CreateTestParquet(100);
        var outputDir = Path.Combine(_testDir, "duckdb_output");
        var config = new FormatConfig { Format = OutputFormat.DuckDb };

        // Act
        var result = _exporter.Export(sourcePath, "test_table", outputDir, config);

        // Assert
        File.Exists(result).Should().BeTrue();
        
        // Verify table exists
        using var conn = new DuckDB.NET.Data.DuckDBConnection($"DataSource={result}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM test_table";
        var count = (long)cmd.ExecuteScalar()!;
        count.Should().Be(100);
    }

    [Fact]
    public void Export_CreatesOutputDirectory()
    {
        // Arrange
        var sourcePath = CreateTestParquet(10);
        var outputDir = Path.Combine(_testDir, "nested", "output", "dir");
        var config = new FormatConfig { Format = OutputFormat.Parquet };

        // Act
        var result = _exporter.Export(sourcePath, "data", outputDir, config);

        // Assert
        Directory.Exists(outputDir).Should().BeTrue();
        File.Exists(result).Should().BeTrue();
    }

    [Fact]
    public void ExportSchema_CreatesValidJsonFile()
    {
        // Arrange
        var sourcePath = CreateTestParquet(10);
        var schemaPath = Path.Combine(_testDir, "schema.json");
        _createdFiles.Add(schemaPath);

        // Act
        _exporter.ExportSchema(sourcePath, schemaPath, "test_table");

        // Assert
        File.Exists(schemaPath).Should().BeTrue();
        var content = File.ReadAllText(schemaPath);
        content.Should().Contain("\"tableName\"");
        content.Should().Contain("test_table");
        content.Should().Contain("\"columns\"");
        
        // Should be valid JSON
        var action = () => System.Text.Json.JsonDocument.Parse(content);
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(OutputFormat.Parquet, ".parquet")]
    [InlineData(OutputFormat.Csv, ".csv")]
    [InlineData(OutputFormat.Json, ".json")]
    [InlineData(OutputFormat.DuckDb, ".duckdb")]
    public void Export_AllFormats_CreateFiles(OutputFormat format, string expectedExtension)
    {
        // Arrange
        var sourcePath = CreateTestParquet(50);
        var outputDir = Path.Combine(_testDir, $"format_test_{format}");
        var config = new FormatConfig { Format = format };

        // Act
        var result = _exporter.Export(sourcePath, "data", outputDir, config);

        // Assert
        File.Exists(result).Should().BeTrue();
    }

    [Fact]
    public void Export_WithCompression_CreatesCompressedFile()
    {
        // Arrange
        var sourcePath = CreateTestParquet(1000);
        var outputDirSnappy = Path.Combine(_testDir, "compression_snappy");
        var outputDirGzip = Path.Combine(_testDir, "compression_gzip");

        // Act
        var snappyResult = _exporter.Export(sourcePath, "data", outputDirSnappy, new FormatConfig { Format = OutputFormat.Parquet, Compression = "snappy" });
        var gzipResult = _exporter.Export(sourcePath, "data", outputDirGzip, new FormatConfig { Format = OutputFormat.Parquet, Compression = "gzip" });

        // Assert
        File.Exists(snappyResult).Should().BeTrue();
        File.Exists(gzipResult).Should().BeTrue();
        // Both should have data
        new FileInfo(snappyResult).Length.Should().BeGreaterThan(0);
        new FileInfo(gzipResult).Length.Should().BeGreaterThan(0);
    }
}
