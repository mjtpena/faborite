using Microsoft.Extensions.Logging;

namespace Faborite.Core.Filtering;

/// <summary>
/// Handles selective column syncing for tables.
/// Issue #29
/// </summary>
public class ColumnSelector
{
    private readonly ILogger<ColumnSelector> _logger;

    public ColumnSelector(ILogger<ColumnSelector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Filters columns based on configuration.
    /// </summary>
    public List<string> SelectColumns(
        List<string> availableColumns,
        ColumnFilterConfig config)
    {
        if (config.Mode == ColumnFilterMode.All)
        {
            return availableColumns;
        }

        var selected = config.Mode switch
        {
            ColumnFilterMode.Include => FilterInclude(availableColumns, config.Columns),
            ColumnFilterMode.Exclude => FilterExclude(availableColumns, config.Columns),
            ColumnFilterMode.Pattern => FilterPattern(availableColumns, config.Patterns!),
            _ => availableColumns
        };

        _logger.LogDebug("Selected {Count} of {Total} columns", selected.Count, availableColumns.Count);
        return selected;
    }

    private List<string> FilterInclude(List<string> columns, List<string> include)
    {
        return columns.Where(c => include.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    private List<string> FilterExclude(List<string> columns, List<string> exclude)
    {
        return columns.Where(c => !exclude.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    private List<string> FilterPattern(List<string> columns, List<string> patterns)
    {
        var selected = new List<string>();
        
        foreach (var column in columns)
        {
            foreach (var pattern in patterns)
            {
                if (IsMatch(column, pattern))
                {
                    selected.Add(column);
                    break;
                }
            }
        }

        return selected;
    }

    private bool IsMatch(string column, string pattern)
    {
        // Simple wildcard matching (* and ?)
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        
        return System.Text.RegularExpressions.Regex.IsMatch(
            column, 
            regex, 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Generates SQL SELECT clause for filtered columns.
    /// </summary>
    public string GenerateSelectClause(List<string> columns, string? alias = null)
    {
        if (!columns.Any())
        {
            return alias != null ? $"{alias}.*" : "*";
        }

        var prefix = alias != null ? $"{alias}." : "";
        return string.Join(", ", columns.Select(c => $"{prefix}[{c}]"));
    }
}

public enum ColumnFilterMode
{
    All,
    Include,
    Exclude,
    Pattern
}

public record ColumnFilterConfig
{
    public ColumnFilterMode Mode { get; init; } = ColumnFilterMode.All;
    public List<string> Columns { get; init; } = new();
    public List<string>? Patterns { get; init; }
}
