using Faborite.Core.Common;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Analytics;

/// <summary>
/// Pivot and unpivot transformations for data reshaping.
/// Issue #52
/// </summary>
public class PivotEngine
{
    private readonly ILogger<PivotEngine> _logger;

    public PivotEngine(ILogger<PivotEngine> logger)
    {
        _logger = logger;
    }

    public TableData Pivot(TableData data, PivotConfig config)
    {
        _logger.LogInformation("Pivoting data on {Index} with values from {Values}", config.IndexColumn, config.ValuesColumn);

        var pivoted = new TableData();
        
        // Get unique values for pivot columns
        var pivotValues = data.Rows
            .Select(r => r.GetValueOrDefault(config.PivotColumn)?.ToString())
            .Where(v => v != null)
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        // Build new columns
        pivoted.Columns = new List<string>(config.GroupByColumns) { config.IndexColumn };
        pivoted.Columns.AddRange(pivotValues!);

        // Group by index and group columns
        var groups = data.Rows
            .GroupBy(r =>
            {
                var keys = config.GroupByColumns.Select(col => r.GetValueOrDefault(col)?.ToString() ?? "").ToList();
                keys.Add(r.GetValueOrDefault(config.IndexColumn)?.ToString() ?? "");
                return string.Join("|", keys);
            });

        foreach (var group in groups)
        {
            var row = new Dictionary<string, object?>();
            var firstRow = group.First();
            
            // Add group by values
            foreach (var col in config.GroupByColumns)
            {
                row[col] = firstRow.GetValueOrDefault(col);
            }
            row[config.IndexColumn] = firstRow.GetValueOrDefault(config.IndexColumn);

            // Add pivoted values
            foreach (var pivotValue in pivotValues)
            {
                var matchingRow = group.FirstOrDefault(r =>
                    r.GetValueOrDefault(config.PivotColumn)?.ToString() == pivotValue);
                
                row[pivotValue!] = matchingRow?.GetValueOrDefault(config.ValuesColumn);
            }

            pivoted.Rows.Add(row);
        }

        return pivoted;
    }

    public TableData Unpivot(TableData data, UnpivotConfig config)
    {
        _logger.LogInformation("Unpivoting {Count} columns", config.ValueColumns.Count);

        var unpivoted = new TableData
        {
            Columns = new List<string>(config.IdColumns) { config.VariableColumn, config.ValueColumn }
        };

        foreach (var row in data.Rows)
        {
            foreach (var valueCol in config.ValueColumns)
            {
                var newRow = new Dictionary<string, object?>();
                
                // Copy ID columns
                foreach (var idCol in config.IdColumns)
                {
                    newRow[idCol] = row.GetValueOrDefault(idCol);
                }

                // Add variable and value
                newRow[config.VariableColumn] = valueCol;
                newRow[config.ValueColumn] = row.GetValueOrDefault(valueCol);

                unpivoted.Rows.Add(newRow);
            }
        }

        return unpivoted;
    }
}

public record PivotConfig(
    string IndexColumn,
    string PivotColumn,
    string ValuesColumn,
    List<string> GroupByColumns);

public record UnpivotConfig(
    List<string> IdColumns,
    List<string> ValueColumns,
    string VariableColumn,
    string ValueColumn);
