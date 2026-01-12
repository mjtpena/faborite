using Microsoft.Extensions.Logging;

namespace Faborite.Core.DataQuality;

/// <summary>
/// Handles data deduplication during sync operations.
/// Issue #31
/// </summary>
public class DeduplicationEngine
{
    private readonly ILogger<DeduplicationEngine> _logger;

    public DeduplicationEngine(ILogger<DeduplicationEngine> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Removes duplicate rows based on configuration.
    /// </summary>
    public async Task<DeduplicationResult> DeduplicateAsync(
        DataTable data,
        DeduplicationConfig config,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var originalCount = data.Rows.Count;

        var deduplicated = config.Strategy switch
        {
            DeduplicationStrategy.ExactMatch => DeduplicateExact(data, config.KeyColumns),
            DeduplicationStrategy.FirstOccurrence => DeduplicateFirst(data, config.KeyColumns),
            DeduplicationStrategy.LastOccurrence => DeduplicateLast(data, config.KeyColumns),
            DeduplicationStrategy.Custom => DeduplicateCustom(data, config),
            _ => data.Rows
        };

        data.Rows = deduplicated;
        var duplicatesRemoved = originalCount - data.Rows.Count;
        var duration = DateTime.UtcNow - startTime;

        _logger.LogInformation("Removed {Count} duplicates from {Total} rows in {Duration}ms", 
            duplicatesRemoved, originalCount, duration.TotalMilliseconds);

        return new DeduplicationResult(originalCount, data.Rows.Count, duplicatesRemoved, duration);
    }

    private List<DataRow> DeduplicateExact(DataTable data, List<string> keyColumns)
    {
        var seen = new HashSet<string>();
        var deduplicated = new List<DataRow>();

        foreach (var row in data.Rows)
        {
            var key = GenerateKey(row, keyColumns);
            if (seen.Add(key))
            {
                deduplicated.Add(row);
            }
        }

        return deduplicated;
    }

    private List<DataRow> DeduplicateFirst(DataTable data, List<string> keyColumns)
    {
        var grouped = data.Rows
            .GroupBy(row => GenerateKey(row, keyColumns))
            .Select(g => g.First())
            .ToList();

        return grouped;
    }

    private List<DataRow> DeduplicateLast(DataTable data, List<string> keyColumns)
    {
        var grouped = data.Rows
            .GroupBy(row => GenerateKey(row, keyColumns))
            .Select(g => g.Last())
            .ToList();

        return grouped;
    }

    private List<DataRow> DeduplicateCustom(DataTable data, DeduplicationConfig config)
    {
        if (config.RankingColumn == null)
        {
            _logger.LogWarning("Custom deduplication requires RankingColumn, falling back to FirstOccurrence");
            return DeduplicateFirst(data, config.KeyColumns);
        }

        var grouped = data.Rows
            .GroupBy(row => GenerateKey(row, config.KeyColumns))
            .Select(g => config.RankingOrder == RankingOrder.Descending
                ? g.OrderByDescending(r => r[config.RankingColumn]).First()
                : g.OrderBy(r => r[config.RankingColumn]).First())
            .ToList();

        return grouped;
    }

    private string GenerateKey(DataRow row, List<string> columns)
    {
        var values = columns.Select(col => row[col]?.ToString() ?? "NULL");
        return string.Join("|", values);
    }

    /// <summary>
    /// Generates SQL expression for deduplication using window functions.
    /// </summary>
    public string GenerateDeduplicationSql(
        string tableName,
        DeduplicationConfig config)
    {
        var partitionBy = string.Join(", ", config.KeyColumns.Select(c => $"[{c}]"));
        
        var orderBy = config.Strategy switch
        {
            DeduplicationStrategy.FirstOccurrence => "(SELECT NULL)",
            DeduplicationStrategy.LastOccurrence => "(SELECT NULL) DESC",
            DeduplicationStrategy.Custom when config.RankingColumn != null 
                => config.RankingOrder == RankingOrder.Descending 
                    ? $"[{config.RankingColumn}] DESC" 
                    : $"[{config.RankingColumn}]",
            _ => "(SELECT NULL)"
        };

        return $@"
WITH Ranked AS (
    SELECT *,
           ROW_NUMBER() OVER (PARTITION BY {partitionBy} ORDER BY {orderBy}) AS rn
    FROM [{tableName}]
)
SELECT * FROM Ranked WHERE rn = 1";
    }
}

public enum DeduplicationStrategy
{
    ExactMatch,
    FirstOccurrence,
    LastOccurrence,
    Custom
}

public enum RankingOrder
{
    Ascending,
    Descending
}

public record DeduplicationConfig
{
    public DeduplicationStrategy Strategy { get; init; } = DeduplicationStrategy.ExactMatch;
    public List<string> KeyColumns { get; init; } = new();
    public string? RankingColumn { get; init; }
    public RankingOrder RankingOrder { get; init; } = RankingOrder.Descending;
}

public record DeduplicationResult(
    int OriginalCount,
    int FinalCount,
    int DuplicatesRemoved,
    TimeSpan Duration);

public class DataTable
{
    public List<string> Columns { get; set; } = new();
    public List<DataRow> Rows { get; set; } = new();
}

public class DataRow
{
    private readonly Dictionary<string, object?> _values = new();

    public object? this[string column]
    {
        get => _values.TryGetValue(column, out var value) ? value : null;
        set => _values[column] = value;
    }
}
