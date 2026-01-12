using Microsoft.Extensions.Logging;

namespace Faborite.Core.SchemaManagement;

/// <summary>
/// Manages schema versions and drift detection.
/// Issue #45
/// </summary>
public class SchemaVersionManager
{
    private readonly ILogger<SchemaVersionManager> _logger;
    private readonly string _schemaDirectory;

    public SchemaVersionManager(ILogger<SchemaVersionManager> logger, string? schemaDirectory = null)
    {
        _logger = logger;
        _schemaDirectory = schemaDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "faborite", "schemas");
        Directory.CreateDirectory(_schemaDirectory);
    }

    /// <summary>
    /// Saves current schema as a new version.
    /// </summary>
    public async Task<SchemaVersion> SaveSchemaVersionAsync(
        string tableName,
        TableSchema schema,
        CancellationToken cancellationToken = default)
    {
        var version = new SchemaVersion(
            VersionId: Guid.NewGuid().ToString("N"),
            TableName: tableName,
            Schema: schema,
            CreatedAt: DateTime.UtcNow,
            Hash: ComputeSchemaHash(schema)
        );

        var filePath = Path.Combine(_schemaDirectory, $"{tableName}_{version.VersionId}.json");
        var json = System.Text.Json.JsonSerializer.Serialize(version, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        _logger.LogInformation("Saved schema version {VersionId} for table {Table}", version.VersionId, tableName);
        return version;
    }

    /// <summary>
    /// Detects schema drift between two versions.
    /// </summary>
    public async Task<SchemaDrift> DetectDriftAsync(
        string tableName,
        TableSchema currentSchema,
        CancellationToken cancellationToken = default)
    {
        var lastVersion = await GetLatestSchemaVersionAsync(tableName, cancellationToken);
        
        if (lastVersion == null)
        {
            _logger.LogInformation("No previous schema found for {Table}, treating as new table", tableName);
            return new SchemaDrift(
                HasDrift: false,
                AddedColumns: currentSchema.Columns.Select(c => c.Name).ToList(),
                RemovedColumns: new List<string>(),
                ModifiedColumns: new List<ColumnModification>(),
                PreviousVersion: null,
                CurrentHash: ComputeSchemaHash(currentSchema)
            );
        }

        var previousSchema = lastVersion.Schema;
        var addedColumns = new List<string>();
        var removedColumns = new List<string>();
        var modifiedColumns = new List<ColumnModification>();

        // Detect added columns
        foreach (var col in currentSchema.Columns)
        {
            if (!previousSchema.Columns.Any(c => c.Name == col.Name))
            {
                addedColumns.Add(col.Name);
            }
        }

        // Detect removed columns
        foreach (var col in previousSchema.Columns)
        {
            if (!currentSchema.Columns.Any(c => c.Name == col.Name))
            {
                removedColumns.Add(col.Name);
            }
        }

        // Detect modified columns
        foreach (var currentCol in currentSchema.Columns)
        {
            var previousCol = previousSchema.Columns.FirstOrDefault(c => c.Name == currentCol.Name);
            if (previousCol != null && !AreColumnsEqual(currentCol, previousCol))
            {
                modifiedColumns.Add(new ColumnModification(
                    ColumnName: currentCol.Name,
                    PreviousType: previousCol.DataType,
                    CurrentType: currentCol.DataType,
                    PreviousNullable: previousCol.IsNullable,
                    CurrentNullable: currentCol.IsNullable,
                    PreviousMaxLength: previousCol.MaxLength,
                    CurrentMaxLength: currentCol.MaxLength
                ));
            }
        }

        var hasDrift = addedColumns.Any() || removedColumns.Any() || modifiedColumns.Any();

        if (hasDrift)
        {
            _logger.LogWarning("Schema drift detected for {Table}: +{Added} -{Removed} ~{Modified}",
                tableName, addedColumns.Count, removedColumns.Count, modifiedColumns.Count);
        }

        return new SchemaDrift(
            HasDrift: hasDrift,
            AddedColumns: addedColumns,
            RemovedColumns: removedColumns,
            ModifiedColumns: modifiedColumns,
            PreviousVersion: lastVersion.VersionId,
            CurrentHash: ComputeSchemaHash(currentSchema)
        );
    }

    private bool AreColumnsEqual(ColumnInfo col1, ColumnInfo col2)
    {
        return col1.DataType == col2.DataType &&
               col1.IsNullable == col2.IsNullable &&
               col1.MaxLength == col2.MaxLength;
    }

    /// <summary>
    /// Gets all schema versions for a table.
    /// </summary>
    public async Task<List<SchemaVersion>> GetSchemaHistoryAsync(
        string tableName,
        CancellationToken cancellationToken = default)
    {
        var pattern = $"{tableName}_*.json";
        var files = Directory.GetFiles(_schemaDirectory, pattern)
            .OrderByDescending(f => File.GetCreationTimeUtc(f));

        var versions = new List<SchemaVersion>();

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var version = System.Text.Json.JsonSerializer.Deserialize<SchemaVersion>(json);
                if (version != null)
                    versions.Add(version);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load schema version from {File}", file);
            }
        }

        return versions;
    }

    private async Task<SchemaVersion?> GetLatestSchemaVersionAsync(
        string tableName,
        CancellationToken cancellationToken)
    {
        var history = await GetSchemaHistoryAsync(tableName, cancellationToken);
        return history.FirstOrDefault();
    }

    private string ComputeSchemaHash(TableSchema schema)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(schema);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Generates migration script for schema changes.
    /// </summary>
    public string GenerateMigrationScript(SchemaDrift drift, string tableName)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"-- Schema migration for {tableName}");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        foreach (var col in drift.AddedColumns)
        {
            sb.AppendLine($"ALTER TABLE [{tableName}] ADD [{col}] NVARCHAR(MAX) NULL;");
        }

        foreach (var col in drift.RemovedColumns)
        {
            sb.AppendLine($"-- DROP COLUMN [{col}] -- Uncomment to execute");
        }

        foreach (var mod in drift.ModifiedColumns)
        {
            sb.AppendLine($"-- Column [{mod.ColumnName}] changed from {mod.PreviousType} to {mod.CurrentType}");
            sb.AppendLine($"-- ALTER TABLE [{tableName}] ALTER COLUMN [{mod.ColumnName}] {mod.CurrentType};");
        }

        return sb.ToString();
    }
}

public record SchemaVersion(
    string VersionId,
    string TableName,
    TableSchema Schema,
    DateTime CreatedAt,
    string Hash);

public record TableSchema(
    List<ColumnInfo> Columns,
    List<string> PrimaryKeys);

public record ColumnInfo(
    string Name,
    string DataType,
    bool IsNullable,
    int? MaxLength);

public record SchemaDrift(
    bool HasDrift,
    List<string> AddedColumns,
    List<string> RemovedColumns,
    List<ColumnModification> ModifiedColumns,
    string? PreviousVersion,
    string CurrentHash);

public record ColumnModification(
    string ColumnName,
    string PreviousType,
    string CurrentType,
    bool PreviousNullable,
    bool CurrentNullable,
    int? PreviousMaxLength,
    int? CurrentMaxLength);
