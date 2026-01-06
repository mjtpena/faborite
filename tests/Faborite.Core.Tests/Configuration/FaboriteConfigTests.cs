using Faborite.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace Faborite.Core.Tests.Configuration;

public class FaboriteConfigTests
{
    [Fact]
    public void FaboriteConfig_DefaultValues_AreCorrect()
    {
        // Act
        var config = new FaboriteConfig();

        // Assert
        config.WorkspaceId.Should().BeNull();
        config.LakehouseId.Should().BeNull();
        config.Sample.Should().NotBeNull();
        config.Format.Should().NotBeNull();
        config.Sync.Should().NotBeNull();
        config.Auth.Should().NotBeNull();
        config.Tables.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void SampleConfig_DefaultValues_AreCorrect()
    {
        // Act
        var config = new SampleConfig();

        // Assert
        config.Strategy.Should().Be(SampleStrategy.Random);
        config.Rows.Should().Be(10000);
        config.Seed.Should().Be(42);
        config.AutoDetectDate.Should().BeTrue();
        config.MaxFullTableRows.Should().Be(50000);
        config.DateColumn.Should().BeNull();
        config.StratifyColumn.Should().BeNull();
        config.WhereClause.Should().BeNull();
    }

    [Fact]
    public void FormatConfig_DefaultValues_AreCorrect()
    {
        // Act
        var config = new FormatConfig();

        // Assert
        config.Format.Should().Be(OutputFormat.Parquet);
        config.Compression.Should().Be("snappy");
        config.SingleFile.Should().BeTrue();
        config.PartitionBy.Should().BeNull();
    }

    [Fact]
    public void SyncConfig_DefaultValues_AreCorrect()
    {
        // Act
        var config = new SyncConfig();

        // Assert
        config.LocalPath.Should().Be("./local_lakehouse");
        config.Overwrite.Should().BeTrue();
        config.IncludeSchema.Should().BeTrue();
        config.ParallelTables.Should().Be(4);
        config.SkipTables.Should().NotBeNull();
        config.IncludeTables.Should().BeNull();
    }

    [Fact]
    public void AuthConfig_DefaultValues_AreCorrect()
    {
        // Act
        var config = new AuthConfig();

        // Assert
        config.Method.Should().Be(AuthMethod.Default);
        config.TenantId.Should().BeNull();
        config.ClientId.Should().BeNull();
        config.ClientSecret.Should().BeNull();
    }

    [Fact]
    public void SampleStrategy_HasExpectedValues()
    {
        // Assert
        Enum.GetValues<SampleStrategy>().Should().HaveCount(7);
        Enum.IsDefined(SampleStrategy.Random).Should().BeTrue();
        Enum.IsDefined(SampleStrategy.Recent).Should().BeTrue();
        Enum.IsDefined(SampleStrategy.Head).Should().BeTrue();
        Enum.IsDefined(SampleStrategy.Tail).Should().BeTrue();
        Enum.IsDefined(SampleStrategy.Stratified).Should().BeTrue();
        Enum.IsDefined(SampleStrategy.Query).Should().BeTrue();
        Enum.IsDefined(SampleStrategy.Full).Should().BeTrue();
    }

    [Fact]
    public void OutputFormat_HasExpectedValues()
    {
        // Assert
        Enum.GetValues<OutputFormat>().Should().HaveCount(5);
        Enum.IsDefined(OutputFormat.Parquet).Should().BeTrue();
        Enum.IsDefined(OutputFormat.Delta).Should().BeTrue();
        Enum.IsDefined(OutputFormat.Csv).Should().BeTrue();
        Enum.IsDefined(OutputFormat.Json).Should().BeTrue();
        Enum.IsDefined(OutputFormat.DuckDb).Should().BeTrue();
    }

    [Fact]
    public void AuthMethod_HasExpectedValues()
    {
        // Assert
        Enum.GetValues<AuthMethod>().Should().HaveCount(4);
        Enum.IsDefined(AuthMethod.Default).Should().BeTrue();
        Enum.IsDefined(AuthMethod.AzureCli).Should().BeTrue();
        Enum.IsDefined(AuthMethod.ServicePrincipal).Should().BeTrue();
        Enum.IsDefined(AuthMethod.ManagedIdentity).Should().BeTrue();
    }

    [Fact]
    public void TableOverride_CanBeCreated()
    {
        // Arrange & Act
        var tableOverride = new TableOverride
        {
            Sample = new SampleConfig
            {
                Rows = 500,
                Strategy = SampleStrategy.Recent,
                DateColumn = "created_at"
            },
            Format = new FormatConfig
            {
                Format = OutputFormat.Csv
            }
        };

        // Assert
        tableOverride.Sample.Should().NotBeNull();
        tableOverride.Sample!.Rows.Should().Be(500);
        tableOverride.Format.Should().NotBeNull();
        tableOverride.Format!.Format.Should().Be(OutputFormat.Csv);
    }
}
