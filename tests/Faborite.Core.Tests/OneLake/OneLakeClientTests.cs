using Faborite.Core.OneLake;
using Faborite.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace Faborite.Core.Tests.OneLake;

public class OneLakeClientTests
{
    private const string TestWorkspaceId = "test-workspace-id";
    private const string TestLakehouseId = "test-lakehouse-id";

    [Fact]
    public void Constructor_WithValidIds_CreatesClient()
    {
        // Act
        using var client = new OneLakeClient(TestWorkspaceId, TestLakehouseId);

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithAuthConfig_CreatesClient()
    {
        // Arrange
        var authConfig = new AuthConfig
        {
            Method = AuthMethod.Default
        };

        // Act
        using var client = new OneLakeClient(TestWorkspaceId, TestLakehouseId, authConfig);

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithAzureCliAuth_CreatesClient()
    {
        // Arrange
        var authConfig = new AuthConfig
        {
            Method = AuthMethod.AzureCli
        };

        // Act
        using var client = new OneLakeClient(TestWorkspaceId, TestLakehouseId, authConfig);

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithManagedIdentityAuth_CreatesClient()
    {
        // Arrange
        var authConfig = new AuthConfig
        {
            Method = AuthMethod.ManagedIdentity
        };

        // Act
        using var client = new OneLakeClient(TestWorkspaceId, TestLakehouseId, authConfig);

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithManagedIdentityAndClientId_CreatesClient()
    {
        // Arrange
        var authConfig = new AuthConfig
        {
            Method = AuthMethod.ManagedIdentity,
            ClientId = "test-client-id"
        };

        // Act
        using var client = new OneLakeClient(TestWorkspaceId, TestLakehouseId, authConfig);

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithServicePrincipalAuth_CreatesClient()
    {
        // Arrange
        var authConfig = new AuthConfig
        {
            Method = AuthMethod.ServicePrincipal,
            TenantId = "test-tenant-id",
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret"
        };

        // Act
        using var client = new OneLakeClient(TestWorkspaceId, TestLakehouseId, authConfig);

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void BasePath_ReturnsLakehouseId()
    {
        // Arrange
        using var client = new OneLakeClient(TestWorkspaceId, TestLakehouseId);

        // Act
        var basePath = client.BasePath;

        // Assert
        basePath.Should().Be(TestLakehouseId);
    }

    [Fact]
    public void TablesPath_ReturnsCorrectPath()
    {
        // Arrange
        using var client = new OneLakeClient(TestWorkspaceId, TestLakehouseId);

        // Act
        var tablesPath = client.TablesPath;

        // Assert
        tablesPath.Should().Be($"{TestLakehouseId}/Tables");
    }

    [Fact]
    public void FilesPath_ReturnsCorrectPath()
    {
        // Arrange
        using var client = new OneLakeClient(TestWorkspaceId, TestLakehouseId);

        // Act
        var filesPath = client.FilesPath;

        // Assert
        filesPath.Should().Be($"{TestLakehouseId}/Files");
    }

    [Fact]
    public void GetTableUri_ReturnsCorrectUri()
    {
        // Arrange
        using var client = new OneLakeClient(TestWorkspaceId, TestLakehouseId);

        // Act
        var tableUri = client.GetTableUri("my_table");

        // Assert
        tableUri.Should().Be($"abfss://{TestWorkspaceId}@onelake.dfs.fabric.microsoft.com/{TestLakehouseId}/Tables/my_table");
    }

    [Fact]
    public void GetTablePath_ReturnsCorrectPath()
    {
        // Arrange
        using var client = new OneLakeClient(TestWorkspaceId, TestLakehouseId);

        // Act
        var tablePath = client.GetTablePath("my_table");

        // Assert
        tablePath.Should().Be($"{TestLakehouseId}/Tables/my_table");
    }

    [Fact]
    public async Task TestConnectionAsync_WithInvalidCredentials_ReturnsFalse()
    {
        // Arrange
        using var client = new OneLakeClient("invalid-workspace", "invalid-lakehouse");

        // Act
        var result = await client.TestConnectionAsync();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ListTablesAsync_WithInvalidCredentials_ReturnsEmptyList()
    {
        // Arrange
        using var client = new OneLakeClient("invalid-workspace", "invalid-lakehouse");

        // Act
        var tables = await client.ListTablesAsync();

        // Assert
        tables.Should().BeEmpty();
    }

    [Fact]
    public async Task ListFilesAsync_WithInvalidCredentials_ReturnsEmptyList()
    {
        // Arrange
        using var client = new OneLakeClient("invalid-workspace", "invalid-lakehouse");

        // Act
        var files = await client.ListFilesAsync();

        // Assert
        files.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTableSizeAsync_WithInvalidCredentials_ReturnsZero()
    {
        // Arrange
        using var client = new OneLakeClient("invalid-workspace", "invalid-lakehouse");

        // Act
        var size = await client.GetTableSizeAsync("test_table");

        // Assert
        size.Should().Be(0);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var client = new OneLakeClient(TestWorkspaceId, TestLakehouseId);

        // Act & Assert - should not throw
        client.Dispose();
        client.Dispose();
    }
}

public class LakehouseTableTests
{
    [Fact]
    public void LakehouseTable_RecordProperties_AreCorrect()
    {
        // Arrange & Act
        var table = new LakehouseTable(
            Name: "test_table",
            Path: "/Tables/test_table",
            SizeBytes: 1024,
            LastModified: DateTimeOffset.UtcNow
        );

        // Assert
        table.Name.Should().Be("test_table");
        table.Path.Should().Be("/Tables/test_table");
        table.SizeBytes.Should().Be(1024);
        table.LastModified.Should().NotBeNull();
    }

    [Fact]
    public void LakehouseTable_WithMinimalProperties_Works()
    {
        // Arrange & Act
        var table = new LakehouseTable(
            Name: "minimal_table",
            Path: "/path"
        );

        // Assert
        table.Name.Should().Be("minimal_table");
        table.SizeBytes.Should().BeNull();
        table.LastModified.Should().BeNull();
    }
}

public class LakehouseFileTests
{
    [Fact]
    public void LakehouseFile_RecordProperties_AreCorrect()
    {
        // Arrange & Act
        var file = new LakehouseFile(
            Name: "data.parquet",
            Path: "/Files/data.parquet",
            SizeBytes: 2048,
            LastModified: DateTimeOffset.UtcNow
        );

        // Assert
        file.Name.Should().Be("data.parquet");
        file.Path.Should().Be("/Files/data.parquet");
        file.SizeBytes.Should().Be(2048);
        file.LastModified.Should().NotBeNull();
    }
}
