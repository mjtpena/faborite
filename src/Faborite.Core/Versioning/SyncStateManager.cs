using Microsoft.Extensions.Logging;

namespace Faborite.Core.Versioning;

/// <summary>
/// Manages sync state snapshots for rollback/restore functionality.
/// Issue #40
/// </summary>
public class SyncStateManager
{
    private readonly ILogger<SyncStateManager> _logger;
    private readonly string _stateDirectory;

    public SyncStateManager(ILogger<SyncStateManager> logger, string? stateDirectory = null)
    {
        _logger = logger;
        _stateDirectory = stateDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "faborite", "states");
        Directory.CreateDirectory(_stateDirectory);
    }

    /// <summary>
    /// Saves current sync state as a snapshot.
    /// </summary>
    public async Task<string> SaveSnapshotAsync(
        string workspaceId,
        string lakehouseId,
        List<TableSyncInfo> tables,
        CancellationToken cancellationToken = default)
    {
        var snapshotId = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
        var snapshot = new SyncSnapshot(
            SnapshotId: snapshotId,
            WorkspaceId: workspaceId,
            LakehouseId: lakehouseId,
            CreatedAt: DateTime.UtcNow,
            Tables: tables
        );

        var path = Path.Combine(_stateDirectory, $"{snapshotId}.json");
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(path, json, cancellationToken);

        _logger.LogInformation("Saved sync snapshot {SnapshotId} with {Count} tables", snapshotId, tables.Count);
        return snapshotId;
    }

    /// <summary>
    /// Loads a snapshot by ID.
    /// </summary>
    public async Task<SyncSnapshot?> LoadSnapshotAsync(
        string snapshotId,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_stateDirectory, $"{snapshotId}.json");
        
        if (!File.Exists(path))
        {
            _logger.LogWarning("Snapshot {SnapshotId} not found", snapshotId);
            return null;
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<SyncSnapshot>(json);

        _logger.LogInformation("Loaded snapshot {SnapshotId}", snapshotId);
        return snapshot;
    }

    /// <summary>
    /// Restores local data to a previous snapshot state.
    /// </summary>
    public async Task<RestoreResult> RestoreSnapshotAsync(
        string snapshotId,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await LoadSnapshotAsync(snapshotId, cancellationToken);
        if (snapshot == null)
        {
            return new RestoreResult(false, 0, "Snapshot not found");
        }

        _logger.LogInformation("Restoring snapshot {SnapshotId} to {Path}", snapshotId, targetPath);

        var restored = 0;
        var errors = new List<string>();

        foreach (var table in snapshot.Tables)
        {
            try
            {
                var sourcePath = table.FilePath;
                var targetFilePath = Path.Combine(targetPath, Path.GetFileName(sourcePath));

                if (File.Exists(sourcePath))
                {
                    // Create backup of current file
                    if (File.Exists(targetFilePath))
                    {
                        var backupPath = $"{targetFilePath}.backup_{DateTime.UtcNow:yyyyMMddHHmmss}";
                        File.Move(targetFilePath, backupPath);
                    }

                    // Restore from snapshot
                    File.Copy(sourcePath, targetFilePath, overwrite: true);
                    restored++;

                    _logger.LogDebug("Restored table {Table} from snapshot", table.TableName);
                }
                else
                {
                    var error = $"Source file not found for table {table.TableName}: {sourcePath}";
                    errors.Add(error);
                    _logger.LogWarning(error);
                }
            }
            catch (Exception ex)
            {
                var error = $"Failed to restore table {table.TableName}: {ex.Message}";
                errors.Add(error);
                _logger.LogError(ex, error);
            }
        }

        var success = errors.Count == 0;
        var message = success
            ? $"Successfully restored {restored} tables"
            : $"Restored {restored} tables with {errors.Count} errors";

        return new RestoreResult(success, restored, message, errors.Any() ? errors : null);
    }

    /// <summary>
    /// Lists all available snapshots.
    /// </summary>
    public List<SnapshotInfo> ListSnapshots()
    {
        var files = Directory.GetFiles(_stateDirectory, "*.json");
        var snapshots = new List<SnapshotInfo>();

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var snapshot = System.Text.Json.JsonSerializer.Deserialize<SyncSnapshot>(json);
                
                if (snapshot != null)
                {
                    snapshots.Add(new SnapshotInfo(
                        snapshot.SnapshotId,
                        snapshot.CreatedAt,
                        snapshot.Tables.Count,
                        new FileInfo(file).Length
                    ));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load snapshot from {File}", file);
            }
        }

        return snapshots.OrderByDescending(s => s.CreatedAt).ToList();
    }

    /// <summary>
    /// Deletes a snapshot.
    /// </summary>
    public bool DeleteSnapshot(string snapshotId)
    {
        var path = Path.Combine(_stateDirectory, $"{snapshotId}.json");
        
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogInformation("Deleted snapshot {SnapshotId}", snapshotId);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Cleans up old snapshots keeping only the most recent N.
    /// </summary>
    public int CleanupOldSnapshots(int keepCount = 10)
    {
        var snapshots = ListSnapshots();
        var toDelete = snapshots.Skip(keepCount).ToList();

        foreach (var snapshot in toDelete)
        {
            DeleteSnapshot(snapshot.SnapshotId);
        }

        _logger.LogInformation("Cleaned up {Count} old snapshots", toDelete.Count);
        return toDelete.Count;
    }
}

public record SyncSnapshot(
    string SnapshotId,
    string WorkspaceId,
    string LakehouseId,
    DateTime CreatedAt,
    List<TableSyncInfo> Tables);

public record TableSyncInfo(
    string TableName,
    long RowCount,
    long SizeBytes,
    string FilePath,
    DateTime SyncedAt);

public record SnapshotInfo(
    string SnapshotId,
    DateTime CreatedAt,
    int TableCount,
    long SizeBytes);

public record RestoreResult(
    bool Success,
    int TablesRestored,
    string Message,
    List<string>? Errors = null);
