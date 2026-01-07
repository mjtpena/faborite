using Faborite.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace Faborite.Core.Tests;

public class FaboriteServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly List<string> _createdDirs = new();

    public FaboriteServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"faborite_service_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _createdDirs.Add(_testDir);
    }

    public void Dispose()
    {
        foreach (var dir in _createdDirs)
        {
            if (Directory.Exists(dir))
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }

    [Fact]
    public void Constructor_WithNullConfig_ThrowsArgumentNullException()
    {
        // Act
        var action = () => new FaboriteService(null!);

        // Assert
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("config");
    }

    [Fact]
    public void Constructor_WithMissingWorkspaceId_ThrowsArgumentException()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            WorkspaceId = null,
            WorkspaceName = null,
            LakehouseId = "test-lakehouse"
        };

        // Act
        var action = () => new FaboriteService(config);

        // Assert
        action.Should().Throw<ArgumentException>()
            .WithMessage("*WorkspaceId*WorkspaceName*");
    }

    [Fact]
    public void Constructor_WithMissingLakehouseId_ThrowsArgumentException()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            WorkspaceId = "test-workspace",
            LakehouseId = null,
            LakehouseName = null
        };

        // Act
        var action = () => new FaboriteService(config);

        // Assert
        action.Should().Throw<ArgumentException>()
            .WithMessage("*LakehouseId*LakehouseName*");
    }

    [Fact]
    public void Constructor_WithWorkspaceNameOnly_ThrowsNotImplementedException()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            WorkspaceId = null,
            WorkspaceName = "My Workspace",
            LakehouseId = "test-lakehouse"
        };

        // Act
        var action = () => new FaboriteService(config);

        // Assert
        action.Should().Throw<NotImplementedException>()
            .WithMessage("*WorkspaceName*");
    }

    [Fact]
    public void Constructor_WithLakehouseNameOnly_ThrowsNotImplementedException()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            WorkspaceId = "test-workspace",
            LakehouseId = null,
            LakehouseName = "My Lakehouse"
        };

        // Act
        var action = () => new FaboriteService(config);

        // Assert
        action.Should().Throw<NotImplementedException>()
            .WithMessage("*LakehouseName*");
    }

    [Fact]
    public void Constructor_WithValidConfig_CreatesService()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            WorkspaceId = "test-workspace",
            LakehouseId = "test-lakehouse"
        };

        // Act
        using var service = new FaboriteService(config);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var config = new FaboriteConfig
        {
            WorkspaceId = "test-workspace",
            LakehouseId = "test-lakehouse"
        };
        var service = new FaboriteService(config);

        // Act & Assert - should not throw
        service.Dispose();
        service.Dispose();
    }
}
