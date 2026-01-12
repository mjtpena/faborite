using Faborite.Core.Common;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Analytics;

/// <summary>
/// Window functions for advanced analytics.
/// Issue #51
/// </summary>
public class WindowFunctionEngine
{
    private readonly ILogger<WindowFunctionEngine> _logger;

    public WindowFunctionEngine(ILogger<WindowFunctionEngine> logger)
    {
        _logger = logger;
    }

    public TableData ApplyWindowFunction(
        TableData data,
        WindowFunction function)
    {
        _logger.LogInformation("Applying window function {Function}", function.Type);

        var result = new TableData
        {
            Columns = new List<string>(data.Columns) { function.OutputColumn },
            Rows = new List<Dictionary<string, object?>>()
        };

        // Partition data
        var partitions = PartitionData(data, function.PartitionBy);

        foreach (var (key, partition) in partitions)
        {
            // Sort within partition
            var sorted = SortPartition(partition, function.OrderBy);

            // Apply function
            var windowedRows = function.Type switch
            {
                WindowFunctionType.RowNumber => ApplyRowNumber(sorted, function),
                WindowFunctionType.Rank => ApplyRank(sorted, function),
                WindowFunctionType.DenseRank => ApplyDenseRank(sorted, function),
                WindowFunctionType.Lead => ApplyLead(sorted, function),
                WindowFunctionType.Lag => ApplyLag(sorted, function),
                WindowFunctionType.FirstValue => ApplyFirstValue(sorted, function),
                WindowFunctionType.LastValue => ApplyLastValue(sorted, function),
                WindowFunctionType.RunningSum => ApplyRunningSum(sorted, function),
                WindowFunctionType.RunningAvg => ApplyRunningAvg(sorted, function),
                WindowFunctionType.PercentRank => ApplyPercentRank(sorted, function),
                _ => throw new ArgumentException($"Unknown window function: {function.Type}")
            };

            result.Rows.AddRange(windowedRows);
        }

        return result;
    }

    private Dictionary<string, List<Dictionary<string, object?>>> PartitionData(
        TableData data,
        List<string> partitionBy)
    {
        if (!partitionBy.Any())
        {
            return new Dictionary<string, List<Dictionary<string, object?>>>
            {
                ["__all__"] = data.Rows
            };
        }

        var partitions = new Dictionary<string, List<Dictionary<string, object?>>>();
        foreach (var row in data.Rows)
        {
            var key = string.Join("|", partitionBy.Select(col => row.GetValueOrDefault(col)?.ToString() ?? "NULL"));
            if (!partitions.ContainsKey(key))
                partitions[key] = new List<Dictionary<string, object?>>();
            partitions[key].Add(row);
        }

        return partitions;
    }

    private List<Dictionary<string, object?>> SortPartition(
        List<Dictionary<string, object?>> partition,
        List<(string Column, bool Ascending)> orderBy)
    {
        if (!orderBy.Any())
            return partition;

        IOrderedEnumerable<Dictionary<string, object?>>? ordered = null;

        foreach (var (col, asc) in orderBy)
        {
            if (ordered == null)
            {
                ordered = asc
                    ? partition.OrderBy(row => row.GetValueOrDefault(col))
                    : partition.OrderByDescending(row => row.GetValueOrDefault(col));
            }
            else
            {
                ordered = asc
                    ? ordered.ThenBy(row => row.GetValueOrDefault(col))
                    : ordered.ThenByDescending(row => row.GetValueOrDefault(col));
            }
        }

        return ordered?.ToList() ?? partition;
    }

    private List<Dictionary<string, object?>> ApplyRowNumber(List<Dictionary<string, object?>> rows, WindowFunction function)
    {
        var result = new List<Dictionary<string, object?>>();
        for (int i = 0; i < rows.Count; i++)
        {
            var row = new Dictionary<string, object?>(rows[i])
            {
                [function.OutputColumn] = i + 1
            };
            result.Add(row);
        }
        return result;
    }

    private List<Dictionary<string, object?>> ApplyRank(List<Dictionary<string, object?>> rows, WindowFunction function)
    {
        var result = new List<Dictionary<string, object?>>();
        var rank = 1;
        object? previousValue = null;
        var sameRankCount = 0;

        foreach (var row in rows)
        {
            var currentValue = row.GetValueOrDefault(function.ValueColumn);
            
            if (previousValue != null && !Equals(currentValue, previousValue))
            {
                rank += sameRankCount;
                sameRankCount = 0;
            }
            
            sameRankCount++;
            var newRow = new Dictionary<string, object?>(row)
            {
                [function.OutputColumn] = rank
            };
            result.Add(newRow);
            previousValue = currentValue;
        }

        return result;
    }

    private List<Dictionary<string, object?>> ApplyDenseRank(List<Dictionary<string, object?>> rows, WindowFunction function)
    {
        var result = new List<Dictionary<string, object?>>();
        var rank = 1;
        object? previousValue = null;

        foreach (var row in rows)
        {
            var currentValue = row.GetValueOrDefault(function.ValueColumn);
            
            if (previousValue != null && !Equals(currentValue, previousValue))
            {
                rank++;
            }
            
            var newRow = new Dictionary<string, object?>(row)
            {
                [function.OutputColumn] = rank
            };
            result.Add(newRow);
            previousValue = currentValue;
        }

        return result;
    }

    private List<Dictionary<string, object?>> ApplyLead(List<Dictionary<string, object?>> rows, WindowFunction function)
    {
        var offset = function.Offset ?? 1;
        var result = new List<Dictionary<string, object?>>();

        for (int i = 0; i < rows.Count; i++)
        {
            var row = new Dictionary<string, object?>(rows[i]);
            row[function.OutputColumn] = i + offset < rows.Count
                ? rows[i + offset].GetValueOrDefault(function.ValueColumn)
                : null;
            result.Add(row);
        }

        return result;
    }

    private List<Dictionary<string, object?>> ApplyLag(List<Dictionary<string, object?>> rows, WindowFunction function)
    {
        var offset = function.Offset ?? 1;
        var result = new List<Dictionary<string, object?>>();

        for (int i = 0; i < rows.Count; i++)
        {
            var row = new Dictionary<string, object?>(rows[i]);
            row[function.OutputColumn] = i - offset >= 0
                ? rows[i - offset].GetValueOrDefault(function.ValueColumn)
                : null;
            result.Add(row);
        }

        return result;
    }

    private List<Dictionary<string, object?>> ApplyFirstValue(List<Dictionary<string, object?>> rows, WindowFunction function)
    {
        var firstValue = rows.FirstOrDefault()?.GetValueOrDefault(function.ValueColumn);
        return rows.Select(row => new Dictionary<string, object?>(row)
        {
            [function.OutputColumn] = firstValue
        }).ToList();
    }

    private List<Dictionary<string, object?>> ApplyLastValue(List<Dictionary<string, object?>> rows, WindowFunction function)
    {
        var lastValue = rows.LastOrDefault()?.GetValueOrDefault(function.ValueColumn);
        return rows.Select(row => new Dictionary<string, object?>(row)
        {
            [function.OutputColumn] = lastValue
        }).ToList();
    }

    private List<Dictionary<string, object?>> ApplyRunningSum(List<Dictionary<string, object?>> rows, WindowFunction function)
    {
        var result = new List<Dictionary<string, object?>>();
        double sum = 0;

        foreach (var row in rows)
        {
            sum += Convert.ToDouble(row.GetValueOrDefault(function.ValueColumn) ?? 0);
            var newRow = new Dictionary<string, object?>(row)
            {
                [function.OutputColumn] = sum
            };
            result.Add(newRow);
        }

        return result;
    }

    private List<Dictionary<string, object?>> ApplyRunningAvg(List<Dictionary<string, object?>> rows, WindowFunction function)
    {
        var result = new List<Dictionary<string, object?>>();
        double sum = 0;
        int count = 0;

        foreach (var row in rows)
        {
            sum += Convert.ToDouble(row.GetValueOrDefault(function.ValueColumn) ?? 0);
            count++;
            var newRow = new Dictionary<string, object?>(row)
            {
                [function.OutputColumn] = sum / count
            };
            result.Add(newRow);
        }

        return result;
    }

    private List<Dictionary<string, object?>> ApplyPercentRank(List<Dictionary<string, object?>> rows, WindowFunction function)
    {
        var result = new List<Dictionary<string, object?>>();
        var totalRows = rows.Count;

        for (int i = 0; i < rows.Count; i++)
        {
            var percentRank = totalRows > 1 ? (double)i / (totalRows - 1) : 0;
            var row = new Dictionary<string, object?>(rows[i])
            {
                [function.OutputColumn] = percentRank
            };
            result.Add(row);
        }

        return result;
    }
}

public enum WindowFunctionType
{
    RowNumber,
    Rank,
    DenseRank,
    Lead,
    Lag,
    FirstValue,
    LastValue,
    RunningSum,
    RunningAvg,
    PercentRank
}

public record WindowFunction(
    WindowFunctionType Type,
    string OutputColumn,
    string ValueColumn,
    List<string> PartitionBy,
    List<(string Column, bool Ascending)> OrderBy,
    int? Offset = null);
