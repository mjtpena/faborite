using Faborite.Core.Configuration;
using Faborite.Core.Sampling;
using FluentAssertions;
using Xunit;

namespace Faborite.Core.Tests.Sampling;

public class DataSamplerTests : IDisposable
{
    private readonly DataSampler _sampler;
    private readonly string _testDir;
    private readonly List<string> _createdFiles = new();

    public DataSamplerTests()
    {
        _sampler = new DataSampler();
        _testDir = Path.Combine(Path.GetTempPath(), $"faborite_sampler_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        _sampler.Dispose();
        foreach (var file in _createdFiles)
        {
            if (File.Exists(file)) File.Delete(file);
        }
        if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true);
    }

    private string CreateTestParquet(int rowCount = 100)
    {
        var path = Path.Combine(_testDir, $"test_{Guid.NewGuid():N}.parquet");
        _createdFiles.Add(path);
        
        // Use DuckDB to create a test parquet file
        using var conn = new DuckDB.NET.Data.DuckDBConnection("DataSource=:memory:");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            COPY (
                SELECT 
                    i as id,
                    'name_' || i as name,
                    CAST(i * 10.5 as DOUBLE) as value,
                    DATE '2024-01-01' + INTERVAL (i % 365) DAY as created_date
                FROM generate_series(1, {rowCount}) t(i)
            ) TO '{path.Replace("\\", "/")}' (FORMAT PARQUET)";
        cmd.ExecuteNonQuery();
        
        return path;
    }

    [Fact]
    public void Constructor_InitializesSuccessfully()
    {
        // Arrange & Act
        using var sampler = new DataSampler();

        // Assert - no exception means success
        sampler.Should().NotBeNull();
    }

    [Fact]
    public void SampleFromLocalParquet_WithRandomStrategy_ReturnsCorrectRowCount()
    {
        // Arrange
        var sourcePath = CreateTestParquet(1000);
        var outputPath = Path.Combine(_testDir, "output_random.parquet");
        _createdFiles.Add(outputPath);
        
        var config = new SampleConfig
        {
            Strategy = SampleStrategy.Random,
            Rows = 100,
            Seed = 42
        };

        // Act
        var result = _sampler.SampleFromLocalParquet(sourcePath, "test_table", outputPath, config);

        // Assert
        result.Should().NotBeNull();
        result.TableName.Should().Be("test_table");
        result.RowCount.Should().BeLessOrEqualTo(100);
        result.LocalParquetPath.Should().Be(outputPath);
        File.Exists(outputPath).Should().BeTrue();
    }

    [Fact]
    public void SampleFromLocalParquet_WithHeadStrategy_ReturnsFirstRows()
    {
        // Arrange
        var sourcePath = CreateTestParquet(1000);
        var outputPath = Path.Combine(_testDir, "output_head.parquet");
        _createdFiles.Add(outputPath);
        
        var config = new SampleConfig
        {
            Strategy = SampleStrategy.Head,
            Rows = 50
        };

        // Act
        var result = _sampler.SampleFromLocalParquet(sourcePath, "test_table", outputPath, config);

        // Assert
        result.RowCount.Should().Be(50);
        
        // Verify the output contains the first rows
        using var conn = new DuckDB.NET.Data.DuckDBConnection("DataSource=:memory:");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT MIN(id), MAX(id) FROM '{outputPath.Replace("\\", "/")}'";
        using var reader = cmd.ExecuteReader();
        reader.Read();
        reader.GetInt32(0).Should().Be(1); // First row should be id=1
    }

    [Fact]
    public void SampleFromLocalParquet_WithTailStrategy_ReturnsLastRows()
    {
        // Arrange
        var sourcePath = CreateTestParquet(1000);
        var outputPath = Path.Combine(_testDir, "output_tail.parquet");
        _createdFiles.Add(outputPath);
        
        var config = new SampleConfig
        {
            Strategy = SampleStrategy.Tail,
            Rows = 50
        };

        // Act
        var result = _sampler.SampleFromLocalParquet(sourcePath, "test_table", outputPath, config);

        // Assert
        result.RowCount.Should().Be(50);
        
        // Verify the output contains the last rows
        using var conn = new DuckDB.NET.Data.DuckDBConnection("DataSource=:memory:");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT MAX(id) FROM '{outputPath.Replace("\\", "/")}'";
        using var reader = cmd.ExecuteReader();
        reader.Read();
        reader.GetInt32(0).Should().Be(1000); // Last row should be id=1000
    }

    [Fact]
    public void SampleFromLocalParquet_WithFullStrategy_ReturnsAllRows()
    {
        // Arrange
        var sourcePath = CreateTestParquet(500);
        var outputPath = Path.Combine(_testDir, "output_full.parquet");
        _createdFiles.Add(outputPath);
        
        var config = new SampleConfig
        {
            Strategy = SampleStrategy.Full,
            Rows = 100 // Should be ignored for Full strategy
        };

        // Act
        var result = _sampler.SampleFromLocalParquet(sourcePath, "test_table", outputPath, config);

        // Assert
        result.RowCount.Should().Be(500);
    }

    [Fact]
    public void SampleFromLocalParquet_WithRecentStrategy_ReturnsMostRecentRows()
    {
        // Arrange
        var sourcePath = CreateTestParquet(1000);
        var outputPath = Path.Combine(_testDir, "output_recent.parquet");
        _createdFiles.Add(outputPath);
        
        var config = new SampleConfig
        {
            Strategy = SampleStrategy.Recent,
            Rows = 100,
            DateColumn = "created_date"
        };

        // Act
        var result = _sampler.SampleFromLocalParquet(sourcePath, "test_table", outputPath, config);

        // Assert
        result.RowCount.Should().Be(100);
    }

    [Fact]
    public void SampleFromLocalParquet_WithQueryStrategy_AppliesWhereClause()
    {
        // Arrange
        var sourcePath = CreateTestParquet(1000);
        var outputPath = Path.Combine(_testDir, "output_query.parquet");
        _createdFiles.Add(outputPath);
        
        var config = new SampleConfig
        {
            Strategy = SampleStrategy.Query,
            Rows = 1000,
            WhereClause = "id <= 100"
        };

        // Act
        var result = _sampler.SampleFromLocalParquet(sourcePath, "test_table", outputPath, config);

        // Assert
        result.RowCount.Should().Be(100);
    }

    [Fact]
    public void SampleFromLocalParquet_WithSmallTable_ReturnsAllRows()
    {
        // Arrange
        var sourcePath = CreateTestParquet(50); // Small table
        var outputPath = Path.Combine(_testDir, "output_small.parquet");
        _createdFiles.Add(outputPath);
        
        var config = new SampleConfig
        {
            Strategy = SampleStrategy.Random,
            Rows = 10000,
            MaxFullTableRows = 100 // Table is smaller than this
        };

        // Act
        var result = _sampler.SampleFromLocalParquet(sourcePath, "test_table", outputPath, config);

        // Assert
        result.RowCount.Should().Be(50); // All rows returned
        result.SourceRowCount.Should().Be(50);
    }

    [Fact]
    public void SampleFromLocalParquet_CreatesOutputDirectory()
    {
        // Arrange
        var sourcePath = CreateTestParquet(100);
        var outputDir = Path.Combine(_testDir, "new_subdir", "nested");
        var outputPath = Path.Combine(outputDir, "output.parquet");
        _createdFiles.Add(outputPath);
        
        var config = new SampleConfig
        {
            Strategy = SampleStrategy.Head,
            Rows = 10
        };

        // Act
        var result = _sampler.SampleFromLocalParquet(sourcePath, "test_table", outputPath, config);

        // Assert
        Directory.Exists(outputDir).Should().BeTrue();
        File.Exists(outputPath).Should().BeTrue();
    }

    [Fact]
    public void SampleResult_ContainsCorrectMetadata()
    {
        // Arrange
        var sourcePath = CreateTestParquet(500);
        var outputPath = Path.Combine(_testDir, "output_metadata.parquet");
        _createdFiles.Add(outputPath);
        
        var config = new SampleConfig
        {
            Strategy = SampleStrategy.Random,
            Rows = 100
        };

        // Act
        var result = _sampler.SampleFromLocalParquet(sourcePath, "my_table", outputPath, config);

        // Assert
        result.TableName.Should().Be("my_table");
        result.LocalParquetPath.Should().Be(outputPath);
        result.SourceRowCount.Should().Be(500);
        result.RowCount.Should().BeLessOrEqualTo(100);
    }

    [Theory]
    [InlineData(SampleStrategy.Random)]
    [InlineData(SampleStrategy.Head)]
    [InlineData(SampleStrategy.Tail)]
    [InlineData(SampleStrategy.Full)]
    public void SampleFromLocalParquet_AllStrategies_ProduceValidOutput(SampleStrategy strategy)
    {
        // Arrange
        var sourcePath = CreateTestParquet(200);
        var outputPath = Path.Combine(_testDir, $"output_{strategy}.parquet");
        _createdFiles.Add(outputPath);
        
        var config = new SampleConfig
        {
            Strategy = strategy,
            Rows = 50,
            MaxFullTableRows = 10 // Force sampling even for small tables
        };

        // Act
        var result = _sampler.SampleFromLocalParquet(sourcePath, "test", outputPath, config);

        // Assert
        File.Exists(outputPath).Should().BeTrue();
        result.RowCount.Should().BeGreaterThan(0);
    }
}
