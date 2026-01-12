using Microsoft.Extensions.Logging;

namespace Faborite.Core.Connectors;

/// <summary>
/// Base interface for all data source connectors.
/// Phase 9: Advanced Data Integration
/// </summary>
public interface IDataConnector
{
    string Name { get; }
    string Version { get; }
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<ConnectorMetadata> GetMetadataAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Metadata about a data connector
/// </summary>
public record ConnectorMetadata(
    string Type,
    string Version,
    Dictionary<string, string> Capabilities,
    List<string> SupportedOperations
);

/// <summary>
/// Connection configuration base
/// </summary>
public record ConnectionConfig(
    string ConnectionString,
    Dictionary<string, string> Properties,
    int TimeoutSeconds = 30
);

/// <summary>
/// Data transfer result
/// </summary>
public record DataTransferResult(
    long RowsTransferred,
    long BytesTransferred,
    TimeSpan Duration,
    bool Success,
    string? ErrorMessage = null
);

/// <summary>
/// Interface for queryable data sources
/// </summary>
public interface IQueryableConnector : IDataConnector
{
    Task<QueryResult> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default);
    Task<List<TableInfo>> ListTablesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Query execution result
/// </summary>
public record QueryResult(
    List<Dictionary<string, object?>> Rows,
    List<ColumnMetadata> Columns,
    long RowCount,
    TimeSpan ExecutionTime
);

/// <summary>
/// Column metadata
/// </summary>
public record ColumnMetadata(
    string Name,
    string DataType,
    bool IsNullable,
    int? MaxLength = null
);

/// <summary>
/// Table information
/// </summary>
public record TableInfo(
    string Name,
    string Schema,
    long RowCount,
    List<ColumnMetadata> Columns
);
