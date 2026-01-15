using Faborite.Core.Connectors.Database;
using Microsoft.Extensions.Logging;
using Xunit;
using Moq;
using FaboriteMySqlConnector = Faborite.Core.Connectors.Database.MySqlConnector;

namespace Faborite.Core.Tests.Connectors.Database;

public class PostgreSqlConnectorTests
{
    private readonly Mock<ILogger<PostgreSqlConnector>> _mockLogger;

    public PostgreSqlConnectorTests()
    {
        _mockLogger = new Mock<ILogger<PostgreSqlConnector>>();
    }

    [Fact]
    public void Constructor_WithValidConnectionString_ShouldInitialize()
    {
        // Arrange
        var connectionString = "Host=localhost;Database=test;Username=user;Password=pass";

        // Act
        var connector = new PostgreSqlConnector(_mockLogger.Object, connectionString);

        // Assert
        Assert.NotNull(connector);
        Assert.Equal("PostgreSQL", connector.Name);
        Assert.NotNull(connector.Version);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new PostgreSqlConnector(null!, "connection string"));
    }

    [Fact]
    public void Constructor_WithNullConnectionString_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new PostgreSqlConnector(_mockLogger.Object, null!));
    }

    [Fact(Skip = "Requires PostgreSQL instance")]
    public async Task TestConnectionAsync_WithValidConnection_ShouldReturnTrue()
    {
        // Arrange
        var connectionString = "Host=localhost;Database=postgres;Username=postgres;Password=postgres";
        var connector = new PostgreSqlConnector(_mockLogger.Object, connectionString);

        // Act
        var result = await connector.TestConnectionAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TestConnectionAsync_WithInvalidConnection_ShouldReturnFalse()
    {
        // Arrange
        var connectionString = "Host=invalid-host;Database=test;Username=user;Password=wrong";
        var connector = new PostgreSqlConnector(_mockLogger.Object, connectionString);

        // Act
        var result = await connector.TestConnectionAsync();

        // Assert
        Assert.False(result);
    }
}

public class MySqlConnectorTests
{
    private readonly Mock<ILogger<FaboriteMySqlConnector>> _mockLogger;

    public MySqlConnectorTests()
    {
        _mockLogger = new Mock<ILogger<FaboriteMySqlConnector>>();
    }

    [Fact]
    public void Constructor_WithValidConnectionString_ShouldInitialize()
    {
        // Arrange
        var connectionString = "Server=localhost;Database=test;User=root;Password=pass";

        // Act
        var connector = new FaboriteMySqlConnector(_mockLogger.Object, connectionString);

        // Assert
        Assert.NotNull(connector);
        Assert.Equal("MySQL", connector.Name);
        Assert.NotNull(connector.Version);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new FaboriteMySqlConnector(null!, "connection string"));
    }

    [Fact]
    public async Task TestConnectionAsync_WithInvalidConnection_ShouldReturnFalse()
    {
        // Arrange
        var connectionString = "Server=invalid-host;Database=test;User=root;Password=wrong";
        var connector = new FaboriteMySqlConnector(_mockLogger.Object, connectionString);

        // Act
        var result = await connector.TestConnectionAsync();

        // Assert
        Assert.False(result);
    }
}

public class SqlServerConnectorTests
{
    private readonly Mock<ILogger<SqlServerConnector>> _mockLogger;

    public SqlServerConnectorTests()
    {
        _mockLogger = new Mock<ILogger<SqlServerConnector>>();
    }

    [Fact]
    public void Constructor_WithValidConnectionString_ShouldInitialize()
    {
        // Arrange
        var connectionString = "Server=localhost;Database=test;User Id=sa;Password=Pass@123";

        // Act
        var connector = new SqlServerConnector(_mockLogger.Object, connectionString);

        // Assert
        Assert.NotNull(connector);
        Assert.Equal("SQL Server", connector.Name);
        Assert.NotNull(connector.Version);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new SqlServerConnector(null!, "connection string"));
    }

    [Fact]
    public async Task TestConnectionAsync_WithInvalidConnection_ShouldReturnFalse()
    {
        // Arrange
        var connectionString = "Server=invalid-host;Database=test;User Id=sa;Password=wrong";
        var connector = new SqlServerConnector(_mockLogger.Object, connectionString);

        // Act
        var result = await connector.TestConnectionAsync();

        // Assert
        Assert.False(result);
    }
}

public class SnowflakeConnectorTests
{
    private readonly Mock<ILogger<SnowflakeConnector>> _mockLogger;

    public SnowflakeConnectorTests()
    {
        _mockLogger = new Mock<ILogger<SnowflakeConnector>>();
    }

    [Fact]
    public void Constructor_WithValidConfig_ShouldInitialize()
    {
        // Arrange
        var config = new SnowflakeConfig(
            Account: "test-account",
            Username: "user",
            Password: "pass",
            Warehouse: "compute_wh",
            Database: "testdb"
        );

        // Act
        var connector = new SnowflakeConnector(_mockLogger.Object, config);

        // Assert
        Assert.NotNull(connector);
        Assert.Equal("Snowflake", connector.Name);
        Assert.Equal("1.0.0", connector.Version);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange
        var config = new SnowflakeConfig("account", "user", "pass", "wh", "db");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new SnowflakeConnector(null!, config));
    }

    [Fact]
    public void Constructor_WithNullConfig_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new SnowflakeConnector(_mockLogger.Object, null!));
    }
}

public class BigQueryConnectorTests
{
    private readonly Mock<ILogger<BigQueryConnector>> _mockLogger;

    public BigQueryConnectorTests()
    {
        _mockLogger = new Mock<ILogger<BigQueryConnector>>();
    }

    [Fact(Skip = "Requires Google Cloud credentials")]
    public void Constructor_WithValidConfig_ShouldInitialize()
    {
        // Arrange
        var config = new BigQueryConfig(ProjectId: "test-project", DatasetId: "test-dataset");

        // Act
        var connector = new BigQueryConnector(_mockLogger.Object, config);

        // Assert
        Assert.NotNull(connector);
        Assert.Equal("BigQuery", connector.Name);
        Assert.Equal("3.11.0", connector.Version);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange
        var config = new BigQueryConfig(ProjectId: "test-project");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new BigQueryConnector(null!, config));
    }

    [Fact]
    public void Constructor_WithNullConfig_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new BigQueryConnector(_mockLogger.Object, null!));
    }
}
