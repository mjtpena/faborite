namespace Faborite.Core.OneLake;

/// <summary>
/// Interface for OneLake client operations.
/// </summary>
public interface IOneLakeClient : IDisposable
{
    /// <summary>
    /// Base path for this lakehouse.
    /// </summary>
    string BasePath { get; }
    
    /// <summary>
    /// Path to Tables folder.
    /// </summary>
    string TablesPath { get; }
    
    /// <summary>
    /// Path to Files folder.
    /// </summary>
    string FilesPath { get; }

    /// <summary>
    /// Get the full OneLake URI for a table.
    /// </summary>
    string GetTableUri(string tableName);

    /// <summary>
    /// Get the full path for a table.
    /// </summary>
    string GetTablePath(string tableName);

    /// <summary>
    /// List all tables in the lakehouse.
    /// </summary>
    Task<List<LakehouseTable>> ListTablesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// List files in the Files folder.
    /// </summary>
    Task<List<LakehouseFile>> ListFilesAsync(string path = "", CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all parquet files for a table.
    /// </summary>
    Task<List<string>> GetTableParquetFilesAsync(string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimate table size in bytes.
    /// </summary>
    Task<long> GetTableSizeAsync(string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Download a file from OneLake.
    /// </summary>
    Task DownloadFileAsync(string remotePath, string localPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Download all parquet files for a table.
    /// </summary>
    Task DownloadTableAsync(string tableName, string localBasePath, IProgress<(int current, int total)>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Test connection to OneLake.
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
