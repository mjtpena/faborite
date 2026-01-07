using Azure.Identity;
using Azure.Storage.Files.DataLake;
using Azure.Storage.Files.DataLake.Models;
using Faborite.Core.Configuration;

namespace Faborite.Core.OneLake;

/// <summary>
/// Metadata about a lakehouse table.
/// </summary>
public record LakehouseTable(
    string Name,
    string Path,
    long? SizeBytes = null,
    DateTimeOffset? LastModified = null
);

/// <summary>
/// Metadata about a lakehouse file.
/// </summary>
public record LakehouseFile(
    string Name,
    string Path,
    long SizeBytes,
    DateTimeOffset? LastModified = null
);

/// <summary>
/// Client for interacting with OneLake (Fabric's storage layer).
/// OneLake uses ADLS Gen2 protocol.
/// </summary>
public class OneLakeClient : IOneLakeClient
{
    private const string OneLakeEndpoint = "https://onelake.dfs.fabric.microsoft.com";
    
    private readonly string _workspaceId;
    private readonly string _lakehouseId;
    private readonly DataLakeServiceClient _serviceClient;
    private DataLakeFileSystemClient? _fileSystemClient;

    public OneLakeClient(string workspaceId, string lakehouseId, AuthConfig? authConfig = null)
    {
        _workspaceId = workspaceId;
        _lakehouseId = lakehouseId;
        
        var credential = GetCredential(authConfig ?? new AuthConfig());
        _serviceClient = new DataLakeServiceClient(new Uri(OneLakeEndpoint), credential);
    }

    private static Azure.Core.TokenCredential GetCredential(AuthConfig config)
    {
        return config.Method switch
        {
            AuthMethod.AzureCli => new AzureCliCredential(),
            AuthMethod.ManagedIdentity => string.IsNullOrEmpty(config.ClientId)
                ? new ManagedIdentityCredential()
                : new ManagedIdentityCredential(config.ClientId),
            AuthMethod.ServicePrincipal when !string.IsNullOrEmpty(config.TenantId) 
                && !string.IsNullOrEmpty(config.ClientId) 
                && !string.IsNullOrEmpty(config.ClientSecret) 
                => new ClientSecretCredential(config.TenantId, config.ClientId, config.ClientSecret),
            _ => new ChainedTokenCredential(
                new AzureCliCredential(),
                new DefaultAzureCredential())
        };
    }

    /// <summary>
    /// Gets the file system client for the workspace.
    /// </summary>
    private DataLakeFileSystemClient FileSystem => 
        _fileSystemClient ??= _serviceClient.GetFileSystemClient(_workspaceId);

    /// <summary>
    /// Base path for this lakehouse.
    /// </summary>
    public string BasePath => _lakehouseId;

    /// <summary>
    /// Path to Tables folder.
    /// </summary>
    public string TablesPath => $"{BasePath}/Tables";

    /// <summary>
    /// Path to Files folder.
    /// </summary>
    public string FilesPath => $"{BasePath}/Files";

    /// <summary>
    /// Get the full OneLake URI for a table.
    /// </summary>
    public string GetTableUri(string tableName) =>
        $"abfss://{_workspaceId}@onelake.dfs.fabric.microsoft.com/{_lakehouseId}/Tables/{tableName}";

    /// <summary>
    /// Get the full path for a table.
    /// </summary>
    public string GetTablePath(string tableName) => $"{TablesPath}/{tableName}";

    /// <summary>
    /// List all tables in the lakehouse.
    /// </summary>
    public async Task<List<LakehouseTable>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        var tables = new List<LakehouseTable>();

        try
        {
            await foreach (var pathItem in FileSystem.GetPathsAsync(TablesPath, recursive: false, cancellationToken: cancellationToken))
            {
                if (pathItem.IsDirectory == true)
                {
                    var name = Path.GetFileName(pathItem.Name);
                    // Skip internal Delta Lake folders
                    if (!name.StartsWith("_"))
                    {
                        tables.Add(new LakehouseTable(
                            Name: name,
                            Path: pathItem.Name,
                            LastModified: pathItem.LastModified
                        ));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Could not list tables: {ex.Message}");
        }

        return tables;
    }

    /// <summary>
    /// List files in the Files folder.
    /// </summary>
    public async Task<List<LakehouseFile>> ListFilesAsync(string path = "", CancellationToken cancellationToken = default)
    {
        var files = new List<LakehouseFile>();
        var fullPath = string.IsNullOrEmpty(path) ? FilesPath : $"{FilesPath}/{path}";

        try
        {
            await foreach (var pathItem in FileSystem.GetPathsAsync(fullPath, recursive: true, cancellationToken: cancellationToken))
            {
                if (pathItem.IsDirectory != true)
                {
                    files.Add(new LakehouseFile(
                        Name: Path.GetFileName(pathItem.Name),
                        Path: pathItem.Name,
                        SizeBytes: pathItem.ContentLength ?? 0,
                        LastModified: pathItem.LastModified
                    ));
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Could not list files at {fullPath}: {ex.Message}");
        }

        return files;
    }

    /// <summary>
    /// Get all parquet files for a table.
    /// </summary>
    public async Task<List<string>> GetTableParquetFilesAsync(string tableName, CancellationToken cancellationToken = default)
    {
        var files = new List<string>();
        var tablePath = GetTablePath(tableName);

        await foreach (var pathItem in FileSystem.GetPathsAsync(tablePath, recursive: true, cancellationToken: cancellationToken))
        {
            if (pathItem.IsDirectory != true && pathItem.Name.EndsWith(".parquet"))
            {
                files.Add(pathItem.Name);
            }
        }

        return files;
    }

    /// <summary>
    /// Estimate table size in bytes.
    /// </summary>
    public async Task<long> GetTableSizeAsync(string tableName, CancellationToken cancellationToken = default)
    {
        long totalSize = 0;
        var tablePath = GetTablePath(tableName);

        try
        {
            await foreach (var pathItem in FileSystem.GetPathsAsync(tablePath, recursive: true, cancellationToken: cancellationToken))
            {
                if (pathItem.IsDirectory != true && pathItem.Name.EndsWith(".parquet"))
                {
                    totalSize += pathItem.ContentLength ?? 0;
                }
            }
        }
        catch
        {
            // Ignore errors
        }

        return totalSize;
    }

    /// <summary>
    /// Download a file from OneLake.
    /// </summary>
    public async Task DownloadFileAsync(string remotePath, string localPath, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var fileClient = FileSystem.GetFileClient(remotePath);
        
        await using var stream = File.Create(localPath);
        await fileClient.ReadToAsync(stream, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Download all parquet files for a table.
    /// </summary>
    public async Task DownloadTableAsync(string tableName, string localBasePath, IProgress<(int current, int total)>? progress = null, CancellationToken cancellationToken = default)
    {
        var files = await GetTableParquetFilesAsync(tableName, cancellationToken);
        var tablePath = GetTablePath(tableName);
        var localTablePath = Path.Combine(localBasePath, tableName);
        
        Directory.CreateDirectory(localTablePath);

        for (int i = 0; i < files.Count; i++)
        {
            var remotePath = files[i];
            var relativePath = remotePath.Substring(tablePath.Length).TrimStart('/');
            var localFilePath = Path.Combine(localTablePath, relativePath);

            await DownloadFileAsync(remotePath, localFilePath, cancellationToken);
            progress?.Report((i + 1, files.Count));
        }
    }

    /// <summary>
    /// Test connection to OneLake.
    /// </summary>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await foreach (var _ in FileSystem.GetPathsAsync(BasePath, recursive: false, cancellationToken: cancellationToken))
            {
                // Just try to list one item and return
                return true;
            }
            return true; // Empty listing is still valid
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        // DataLakeServiceClient doesn't implement IDisposable, but we keep this for future use
        GC.SuppressFinalize(this);
    }
}
