using Microsoft.Extensions.Logging;

namespace Faborite.Core.Transformation;

/// <summary>
/// Production-ready pivot and unpivot transformations for data reshaping.
/// Issue #52 - Pivot/unpivot operations
/// </summary>
public class PivotEngine
{
    private readonly ILogger<PivotEngine> _logger;

    public PivotEngine(ILogger<PivotEngine> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Pivot: Transform rows into columns based on unique values in a column.
    /// Example: Transform sales data by month into columns for each month.
    /// </summary>
    public List<Dictionary<string, object?>> Pivot(
        List<Dictionary<string, object?>> data,
        string[] groupByColumns,
        string pivotColumn,
        string valueColumn,
        AggregateFunction aggregateFunction = AggregateFunction.Sum)
    {
        _logger.LogInformation("Pivoting {Rows} rows on column {Pivot}", data.Count, pivotColumn);

        if (data.Count == 0) return data;

        // Get unique pivot values
        var pivotValues = data
            .Select(row => row.GetValueOrDefault(pivotColumn)?.ToString() ?? "NULL")
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        _logger.LogDebug("Found {Count} unique pivot values", pivotValues.Count);

        // Group by specified columns
        var groups = data.GroupBy(row =>
        {
            var key = string.Join("|", groupByColumns.Select(col => row.GetValueOrDefault(col)?.ToString() ?? ""));
            return key;
        });

        var result = new List<Dictionary<string, object?>>();

        foreach (var group in groups)
        {
            var pivotedRow = new Dictionary<string, object?>();

            // Add group by columns
            var firstRow = group.First();
            foreach (var col in groupByColumns)
            {
                pivotedRow[col] = firstRow.GetValueOrDefault(col);
            }

            // Add pivoted columns
            foreach (var pivotValue in pivotValues)
            {
                var matchingRows = group.Where(row =>
                    (row.GetValueOrDefault(pivotColumn)?.ToString() ?? "NULL") == pivotValue
                ).ToList();

                if (matchingRows.Any())
                {
                    var values = matchingRows
                        .Select(row => row.GetValueOrDefault(valueColumn))
                        .Where(v => v != null)
                        .Select(v => Convert.ToDouble(v))
                        .ToList();

                    pivotedRow[pivotValue] = values.Any() ? Aggregate(values, aggregateFunction) : null;
                }
                else
                {
                    pivotedRow[pivotValue] = null;
                }
            }

            result.Add(pivotedRow);
        }

        _logger.LogInformation("Pivot complete: {ResultRows} rows with {PivotCols} pivot columns",
            result.Count, pivotValues.Count);

        return result;
    }

    /// <summary>
    /// Unpivot: Transform columns into rows.
    /// Example: Transform columns Jan, Feb, Mar into Month and Value rows.
    /// </summary>
    public List<Dictionary<string, object?>> Unpivot(
        List<Dictionary<string, object?>> data,
        string[] keepColumns,
        string[] unpivotColumns,
        string keyColumnName = "Attribute",
        string valueColumnName = "Value")
    {
        _logger.LogInformation("Unpivoting {Rows} rows with {UnpivotCols} columns",
            data.Count, unpivotColumns.Length);

        var result = new List<Dictionary<string, object?>>();

        foreach (var row in data)
        {
            foreach (var unpivotCol in unpivotColumns)
            {
                var unpivotedRow = new Dictionary<string, object?>();

                // Copy keep columns
                foreach (var keepCol in keepColumns)
                {
                    unpivotedRow[keepCol] = row.GetValueOrDefault(keepCol);
                }

                // Add key and value
                unpivotedRow[keyColumnName] = unpivotCol;
                unpivotedRow[valueColumnName] = row.GetValueOrDefault(unpivotCol);

                result.Add(unpivotedRow);
            }
        }

        _logger.LogInformation("Unpivot complete: {ResultRows} rows created", result.Count);

        return result;
    }

    /// <summary>
    /// Cross-tabulation: Create a contingency table with counts.
    /// </summary>
    public List<Dictionary<string, object?>> CrossTab(
        List<Dictionary<string, object?>> data,
        string rowColumn,
        string columnColumn,
        string? valueColumn = null,
        AggregateFunction aggregateFunction = AggregateFunction.Count)
    {
        _logger.LogInformation("Creating cross-tab for {Row} x {Col}", rowColumn, columnColumn);

        if (data.Count == 0) return data;

        var pivotColumn = columnColumn;
        var groupByColumns = new[] { rowColumn };
        var aggValueColumn = valueColumn ?? rowColumn; // Use row column for counting if no value specified

        return Pivot(data, groupByColumns, pivotColumn, aggValueColumn, aggregateFunction);
    }

    /// <summary>
    /// Transpose: Swap rows and columns completely.
    /// </summary>
    public List<Dictionary<string, object?>> Transpose(List<Dictionary<string, object?>> data)
    {
        _logger.LogInformation("Transposing {Rows} rows", data.Count);

        if (data.Count == 0) return data;

        var columns = data.First().Keys.ToList();
        var result = new List<Dictionary<string, object?>>();

        foreach (var column in columns)
        {
            var transposedRow = new Dictionary<string, object?>
            {
                ["Column"] = column
            };

            for (int i = 0; i < data.Count; i++)
            {
                transposedRow[$"Row{i}"] = data[i].GetValueOrDefault(column);
            }

            result.Add(transposedRow);
        }

        _logger.LogInformation("Transpose complete: {ResultRows} rows", result.Count);

        return result;
    }

    /// <summary>
    /// Multi-value pivot: Pivot multiple value columns simultaneously.
    /// </summary>
    public List<Dictionary<string, object?>> MultiValuePivot(
        List<Dictionary<string, object?>> data,
        string[] groupByColumns,
        string pivotColumn,
        Dictionary<string, AggregateFunction> valueColumnsWithAggregates)
    {
        _logger.LogInformation("Multi-value pivoting {Count} value columns", valueColumnsWithAggregates.Count);

        if (data.Count == 0) return data;

        var pivotValues = data
            .Select(row => row.GetValueOrDefault(pivotColumn)?.ToString() ?? "NULL")
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var groups = data.GroupBy(row =>
        {
            var key = string.Join("|", groupByColumns.Select(col => row.GetValueOrDefault(col)?.ToString() ?? ""));
            return key;
        });

        var result = new List<Dictionary<string, object?>>();

        foreach (var group in groups)
        {
            var pivotedRow = new Dictionary<string, object?>();

            var firstRow = group.First();
            foreach (var col in groupByColumns)
            {
                pivotedRow[col] = firstRow.GetValueOrDefault(col);
            }

            foreach (var pivotValue in pivotValues)
            {
                var matchingRows = group.Where(row =>
                    (row.GetValueOrDefault(pivotColumn)?.ToString() ?? "NULL") == pivotValue
                ).ToList();

                foreach (var (valueColumn, aggregateFunc) in valueColumnsWithAggregates)
                {
                    var columnName = $"{pivotValue}_{valueColumn}";

                    if (matchingRows.Any())
                    {
                        var values = matchingRows
                            .Select(row => row.GetValueOrDefault(valueColumn))
                            .Where(v => v != null)
                            .Select(v => Convert.ToDouble(v))
                            .ToList();

                        pivotedRow[columnName] = values.Any() ? Aggregate(values, aggregateFunc) : null;
                    }
                    else
                    {
                        pivotedRow[columnName] = null;
                    }
                }
            }

            result.Add(pivotedRow);
        }

        _logger.LogInformation("Multi-value pivot complete: {Rows} rows", result.Count);

        return result;
    }

    private double Aggregate(List<double> values, AggregateFunction function)
    {
        return function switch
        {
            AggregateFunction.Sum => values.Sum(),
            AggregateFunction.Average => values.Average(),
            AggregateFunction.Min => values.Min(),
            AggregateFunction.Max => values.Max(),
            AggregateFunction.Count => values.Count,
            AggregateFunction.First => values.First(),
            AggregateFunction.Last => values.Last(),
            AggregateFunction.Median => CalculateMedian(values),
            AggregateFunction.StdDev => CalculateStdDev(values),
            _ => values.Sum()
        };
    }

    private double CalculateMedian(List<double> values)
    {
        var sorted = values.OrderBy(x => x).ToList();
        int count = sorted.Count;
        
        if (count % 2 == 0)
        {
            return (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0;
        }
        else
        {
            return sorted[count / 2];
        }
    }

    private double CalculateStdDev(List<double> values)
    {
        var mean = values.Average();
        var variance = values.Sum(x => Math.Pow(x - mean, 2)) / values.Count;
        return Math.Sqrt(variance);
    }
}

public enum AggregateFunction
{
    Sum,
    Average,
    Min,
    Max,
    Count,
    First,
    Last,
    Median,
    StdDev
}
