using Microsoft.Extensions.Logging;

namespace Faborite.Core.Lineage;

/// <summary>
/// Tracks data lineage and transformation history.
/// Issue #44
/// </summary>
public class DataLineageTracker
{
    private readonly ILogger<DataLineageTracker> _logger;
    private readonly string _lineageDirectory;

    public DataLineageTracker(ILogger<DataLineageTracker> logger, string? lineageDirectory = null)
    {
        _logger = logger;
        _lineageDirectory = lineageDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "faborite", "lineage");
        Directory.CreateDirectory(_lineageDirectory);
    }

    /// <summary>
    /// Records a data lineage entry for a sync operation.
    /// </summary>
    public async Task<string> RecordLineageAsync(
        LineageEntry entry,
        CancellationToken cancellationToken = default)
    {
        var lineageId = Guid.NewGuid().ToString("N");
        entry = entry with { LineageId = lineageId };

        var filePath = Path.Combine(_lineageDirectory, $"{entry.TableName}_{lineageId}.json");
        var json = System.Text.Json.JsonSerializer.Serialize(entry, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        _logger.LogInformation("Recorded lineage {LineageId} for table {Table}", lineageId, entry.TableName);
        return lineageId;
    }

    /// <summary>
    /// Retrieves lineage history for a table.
    /// </summary>
    public async Task<List<LineageEntry>> GetLineageHistoryAsync(
        string tableName,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var pattern = $"{tableName}_*.json";
        var files = Directory.GetFiles(_lineageDirectory, pattern)
            .OrderByDescending(f => File.GetCreationTimeUtc(f))
            .Take(limit);

        var entries = new List<LineageEntry>();

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var entry = System.Text.Json.JsonSerializer.Deserialize<LineageEntry>(json);
                if (entry != null)
                    entries.Add(entry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load lineage file {File}", file);
            }
        }

        return entries;
    }

    /// <summary>
    /// Builds a complete lineage graph showing data flow.
    /// </summary>
    public async Task<LineageGraph> BuildLineageGraphAsync(
        string tableName,
        CancellationToken cancellationToken = default)
    {
        var entries = await GetLineageHistoryAsync(tableName, cancellationToken: cancellationToken);
        
        var nodes = new List<LineageNode>();
        var edges = new List<LineageEdge>();

        foreach (var entry in entries)
        {
            // Source node
            var sourceNode = new LineageNode(
                Id: $"{entry.SourceWorkspace}/{entry.SourceLakehouse}/{entry.TableName}",
                Type: NodeType.Source,
                Name: entry.TableName,
                Metadata: new Dictionary<string, object>
                {
                    ["workspace"] = entry.SourceWorkspace,
                    ["lakehouse"] = entry.SourceLakehouse
                }
            );
            nodes.Add(sourceNode);

            // Target node
            var targetNode = new LineageNode(
                Id: entry.TargetPath,
                Type: NodeType.Target,
                Name: Path.GetFileName(entry.TargetPath),
                Metadata: new Dictionary<string, object>
                {
                    ["path"] = entry.TargetPath
                }
            );
            nodes.Add(targetNode);

            // Edge representing the sync operation
            var edge = new LineageEdge(
                SourceId: sourceNode.Id,
                TargetId: targetNode.Id,
                OperationType: "sync",
                Timestamp: entry.SyncedAt,
                Metadata: new Dictionary<string, object>
                {
                    ["rowCount"] = entry.RowCount,
                    ["transformations"] = entry.Transformations.Count
                }
            );
            edges.Add(edge);

            // Add transformation nodes if any
            foreach (var transformation in entry.Transformations)
            {
                var transformNode = new LineageNode(
                    Id: $"{entry.LineageId}_{transformation.Name}",
                    Type: NodeType.Transformation,
                    Name: transformation.Name,
                    Metadata: new Dictionary<string, object>
                    {
                        ["type"] = transformation.Type,
                        ["columns"] = transformation.AffectedColumns
                    }
                );
                nodes.Add(transformNode);

                edges.Add(new LineageEdge(sourceNode.Id, transformNode.Id, "transform", entry.SyncedAt, null));
                edges.Add(new LineageEdge(transformNode.Id, targetNode.Id, "output", entry.SyncedAt, null));
            }
        }

        return new LineageGraph(
            TableName: tableName,
            Nodes: nodes.DistinctBy(n => n.Id).ToList(),
            Edges: edges,
            GeneratedAt: DateTime.UtcNow
        );
    }

    /// <summary>
    /// Generates a visual representation of lineage (Mermaid diagram).
    /// </summary>
    public string GenerateMermaidDiagram(LineageGraph graph)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("graph TD");

        foreach (var node in graph.Nodes)
        {
            var shape = node.Type switch
            {
                NodeType.Source => $"[({node.Name})]",
                NodeType.Target => $"[/{node.Name}/]",
                NodeType.Transformation => $"{{{{node.Name}}}}",
                _ => $"[{node.Name}]"
            };
            sb.AppendLine($"    {SanitizeId(node.Id)}{shape}");
        }

        foreach (var edge in graph.Edges)
        {
            sb.AppendLine($"    {SanitizeId(edge.SourceId)} -->|{edge.OperationType}| {SanitizeId(edge.TargetId)}");
        }

        return sb.ToString();
    }

    private string SanitizeId(string id)
    {
        return id.Replace("/", "_").Replace("\\", "_").Replace(" ", "_");
    }
}

public enum NodeType
{
    Source,
    Target,
    Transformation,
    Join
}

public record LineageEntry(
    string TableName,
    string SourceWorkspace,
    string SourceLakehouse,
    string TargetPath,
    long RowCount,
    List<TransformationInfo> Transformations,
    DateTime SyncedAt)
{
    public string? LineageId { get; init; }
}

public record TransformationInfo(
    string Name,
    string Type,
    List<string> AffectedColumns,
    Dictionary<string, object>? Parameters = null);

public record LineageNode(
    string Id,
    NodeType Type,
    string Name,
    Dictionary<string, object>? Metadata);

public record LineageEdge(
    string SourceId,
    string TargetId,
    string OperationType,
    DateTime Timestamp,
    Dictionary<string, object>? Metadata);

public record LineageGraph(
    string TableName,
    List<LineageNode> Nodes,
    List<LineageEdge> Edges,
    DateTime GeneratedAt);
