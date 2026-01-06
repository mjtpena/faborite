using Faborite.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace Faborite.Core.Tests.Configuration;

public class ConfigValidatorTests
{
    [Fact]
    public void Validate_WithValidConfig_ReturnsNoErrors()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            WorkspaceId = "12345678-1234-1234-1234-123456789012",
            LakehouseId = "12345678-1234-1234-1234-123456789013"
        };

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithMissingWorkspaceId_ReturnsError()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            LakehouseId = "12345678-1234-1234-1234-123456789013"
        };

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("WorkspaceId") || e.Contains("workspace"));
    }

    [Fact]
    public void Validate_WithMissingLakehouseId_ReturnsError()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            WorkspaceId = "12345678-1234-1234-1234-123456789012"
        };

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("LakehouseId") || e.Contains("lakehouse"));
    }

    [Fact]
    public void Validate_WithInvalidGuid_ReturnsError()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            WorkspaceId = "not-a-valid-guid",
            LakehouseId = "also-not-valid"
        };

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void Validate_WithNegativeRows_ReturnsError()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            WorkspaceId = "12345678-1234-1234-1234-123456789012",
            LakehouseId = "12345678-1234-1234-1234-123456789013",
            Sample = new SampleConfig { Rows = -100 }
        };

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Rows") || e.Contains("positive"));
    }

    [Fact]
    public void Validate_WithZeroRows_ReturnsError()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            WorkspaceId = "12345678-1234-1234-1234-123456789012",
            LakehouseId = "12345678-1234-1234-1234-123456789013",
            Sample = new SampleConfig { Rows = 0 }
        };

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithRecentStrategyButNoDateColumn_ReturnsWarning()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            WorkspaceId = "12345678-1234-1234-1234-123456789012",
            LakehouseId = "12345678-1234-1234-1234-123456789013",
            Sample = new SampleConfig 
            { 
                Strategy = SampleStrategy.Recent,
                DateColumn = null,
                AutoDetectDate = false
            }
        };

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        result.Warnings.Should().Contain(w => w.Contains("DateColumn") || w.Contains("date"));
    }

    [Fact]
    public void Validate_WithStratifiedButNoColumn_ReturnsError()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            WorkspaceId = "12345678-1234-1234-1234-123456789012",
            LakehouseId = "12345678-1234-1234-1234-123456789013",
            Sample = new SampleConfig 
            { 
                Strategy = SampleStrategy.Stratified,
                StratifyColumn = null
            }
        };

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("StratifyColumn") || e.Contains("stratify"));
    }

    [Fact]
    public void Validate_WithQueryButNoWhereClause_ReturnsError()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            WorkspaceId = "12345678-1234-1234-1234-123456789012",
            LakehouseId = "12345678-1234-1234-1234-123456789013",
            Sample = new SampleConfig 
            { 
                Strategy = SampleStrategy.Query,
                WhereClause = null
            }
        };

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("WhereClause") || e.Contains("query"));
    }

    [Fact]
    public void Validate_WithInvalidParallelCount_ReturnsError()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            WorkspaceId = "12345678-1234-1234-1234-123456789012",
            LakehouseId = "12345678-1234-1234-1234-123456789013",
            Sync = new SyncConfig { ParallelTables = -1 }
        };

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithServicePrincipalButMissingCredentials_ReturnsError()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            WorkspaceId = "12345678-1234-1234-1234-123456789012",
            LakehouseId = "12345678-1234-1234-1234-123456789013",
            Auth = new AuthConfig
            {
                Method = AuthMethod.ServicePrincipal,
                TenantId = null,
                ClientId = null,
                ClientSecret = null
            }
        };

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Validate_WithWorkspaceName_AllowsMissingWorkspaceId()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            WorkspaceName = "my-workspace",
            LakehouseId = "12345678-1234-1234-1234-123456789013"
        };

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        result.Errors.Should().NotContain(e => e.Contains("WorkspaceId"));
    }

    [Fact]
    public void ValidationResult_AccumulatesMultipleErrors()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            // Missing both IDs
            Sample = new SampleConfig 
            { 
                Rows = -1,
                Strategy = SampleStrategy.Stratified,
                StratifyColumn = null
            },
            Sync = new SyncConfig { ParallelTables = 0 }
        };

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThan(2);
    }
}
