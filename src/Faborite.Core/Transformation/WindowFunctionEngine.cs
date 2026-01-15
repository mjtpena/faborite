using Microsoft.Extensions.Logging;

namespace Faborite.Core.Transformation;

/// <summary>
/// Production-ready window function engine supporting SQL-like operations.
/// Issue #51 - Window functions (ROW_NUMBER, RANK, LAG/LEAD, etc.)
/// </summary>
public class WindowFunctionEngine
{
    private readonly ILogger<WindowFunctionEngine> _logger;

    public WindowFunctionEngine(ILogger<WindowFunctionEngine> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public List<Dictionary<string, object?>> ApplyWindowFunction(
        List<Dictionary<string, object?>> data,
        WindowFunction function,
        string outputColumn,
        string[]? partitionBy = null,
        string[]? orderBy = null,
        bool[]? orderDescending = null)
    {
        _logger.LogInformation("Applying {Function} window function over {Rows} rows", 
            function, data.Count);

        if (data.Count == 0) return data;

        // Partition the data
        var partitions = PartitionData(data, partitionBy);

        // Process each partition
        var result = new List<Dictionary<string, object?>>();
        foreach (var partition in partitions)
        {
            // Sort within partition
            var sortedPartition = SortPartition(partition, orderBy, orderDescending);
            
            // Apply window function
            var processedPartition = function switch
            {
                WindowFunction.RowNumber => ApplyRowNumber(sortedPartition, outputColumn),
                WindowFunction.Rank => ApplyRank(sortedPartition, outputColumn, orderBy),
                WindowFunction.DenseRank => ApplyDenseRank(sortedPartition, outputColumn, orderBy),
                WindowFunction.NTile => ApplyNTile(sortedPartition, outputColumn, 4),
                WindowFunction.Lag => ApplyLag(sortedPartition, outputColumn, orderBy?[0] ?? "", 1),
                WindowFunction.Lead => ApplyLead(sortedPartition, outputColumn, orderBy?[0] ?? "", 1),
                WindowFunction.FirstValue => ApplyFirstValue(sortedPartition, outputColumn, orderBy?[0] ?? ""),
                WindowFunction.LastValue => ApplyLastValue(sortedPartition, outputColumn, orderBy?[0] ?? ""),
                WindowFunction.CumulativeSum => ApplyCumulativeSum(sortedPartition, outputColumn, orderBy?[0] ?? ""),
                WindowFunction.MovingAverage => ApplyMovingAverage(sortedPartition, outputColumn, orderBy?[0] ?? "", 3),
                WindowFunction.PercentRank => ApplyPercentRank(sortedPartition, outputColumn, orderBy),
                WindowFunction.CumeDist => ApplyCumeDist(sortedPartition, outputColumn, orderBy),
                _ => sortedPartition
            };

            result.AddRange(processedPartition);
        }

        _logger.LogDebug("Window function applied: {OutputColumn} added to {Rows} rows", 
            outputColumn, result.Count);

        return result;
    }

    private List<List<Dictionary<string, object?>>> PartitionData(
        List<Dictionary<string, object?>> data,
        string[]? partitionBy)
    {
        if (partitionBy == null || partitionBy.Length == 0)
            return new List<List<Dictionary<string, object?>>> { data };

        var groups = data.GroupBy(row =>
        {
            var key = string.Join("|", partitionBy.Select(col => row.GetValueOrDefault(col)?.ToString() ?? ""));
            return key;
        });

        return groups.Select(g => g.ToList()).ToList();
    }

    private List<Dictionary<string, object?>> SortPartition(
        List<Dictionary<string, object?>> partition,
        string[]? orderBy,
        bool[]? orderDescending)
    {
        if (orderBy == null || orderBy.Length == 0)
            return partition;

        var sorted = partition.AsEnumerable();
        for (int i = 0; i < orderBy.Length; i++)
        {
            var column = orderBy[i];
            var descending = orderDescending != null && i < orderDescending.Length && orderDescending[i];

            sorted = i == 0
                ? (descending 
                    ? sorted.OrderByDescending(r => r.GetValueOrDefault(column))
                    : sorted.OrderBy(r => r.GetValueOrDefault(column)))
                : (descending
                    ? ((IOrderedEnumerable<Dictionary<string, object?>>)sorted).ThenByDescending(r => r.GetValueOrDefault(column))
                    : ((IOrderedEnumerable<Dictionary<string, object?>>)sorted).ThenBy(r => r.GetValueOrDefault(column)));
        }

        return sorted.ToList();
    }

    private List<Dictionary<string, object?>> ApplyRowNumber(
        List<Dictionary<string, object?>> data,
        string outputColumn)
    {
        for (int i = 0; i < data.Count; i++)
        {
            data[i][outputColumn] = i + 1;
        }
        return data;
    }

    private List<Dictionary<string, object?>> ApplyRank(
        List<Dictionary<string, object?>> data,
        string outputColumn,
        string[]? orderBy)
    {
        if (orderBy == null || orderBy.Length == 0)
            return ApplyRowNumber(data, outputColumn);

        int rank = 1;
        object? previousValue = null;

        for (int i = 0; i < data.Count; i++)
        {
            var currentValue = data[i].GetValueOrDefault(orderBy[0]);
            
            if (i > 0 && !Equals(currentValue, previousValue))
            {
                rank = i + 1;
            }

            data[i][outputColumn] = rank;
            previousValue = currentValue;
        }

        return data;
    }

    private List<Dictionary<string, object?>> ApplyDenseRank(
        List<Dictionary<string, object?>> data,
        string outputColumn,
        string[]? orderBy)
    {
        if (orderBy == null || orderBy.Length == 0)
            return ApplyRowNumber(data, outputColumn);

        int rank = 1;
        object? previousValue = null;

        for (int i = 0; i < data.Count; i++)
        {
            var currentValue = data[i].GetValueOrDefault(orderBy[0]);
            
            if (i > 0 && !Equals(currentValue, previousValue))
            {
                rank++;
            }

            data[i][outputColumn] = rank;
            previousValue = currentValue;
        }

        return data;
    }

    private List<Dictionary<string, object?>> ApplyNTile(
        List<Dictionary<string, object?>> data,
        string outputColumn,
        int buckets)
    {
        int bucketSize = (int)Math.Ceiling((double)data.Count / buckets);

        for (int i = 0; i < data.Count; i++)
        {
            data[i][outputColumn] = Math.Min((i / bucketSize) + 1, buckets);
        }

        return data;
    }

    private List<Dictionary<string, object?>> ApplyLag(
        List<Dictionary<string, object?>> data,
        string outputColumn,
        string sourceColumn,
        int offset)
    {
        for (int i = 0; i < data.Count; i++)
        {
            data[i][outputColumn] = i >= offset 
                ? data[i - offset].GetValueOrDefault(sourceColumn) 
                : null;
        }
        return data;
    }

    private List<Dictionary<string, object?>> ApplyLead(
        List<Dictionary<string, object?>> data,
        string outputColumn,
        string sourceColumn,
        int offset)
    {
        for (int i = 0; i < data.Count; i++)
        {
            data[i][outputColumn] = i < data.Count - offset 
                ? data[i + offset].GetValueOrDefault(sourceColumn) 
                : null;
        }
        return data;
    }

    private List<Dictionary<string, object?>> ApplyFirstValue(
        List<Dictionary<string, object?>> data,
        string outputColumn,
        string sourceColumn)
    {
        var firstValue = data.FirstOrDefault()?.GetValueOrDefault(sourceColumn);
        foreach (var row in data)
        {
            row[outputColumn] = firstValue;
        }
        return data;
    }

    private List<Dictionary<string, object?>> ApplyLastValue(
        List<Dictionary<string, object?>> data,
        string outputColumn,
        string sourceColumn)
    {
        var lastValue = data.LastOrDefault()?.GetValueOrDefault(sourceColumn);
        foreach (var row in data)
        {
            row[outputColumn] = lastValue;
        }
        return data;
    }

    private List<Dictionary<string, object?>> ApplyCumulativeSum(
        List<Dictionary<string, object?>> data,
        string outputColumn,
        string sourceColumn)
    {
        double sum = 0;
        foreach (var row in data)
        {
            if (row.TryGetValue(sourceColumn, out var value) && value != null)
            {
                sum += Convert.ToDouble(value);
            }
            row[outputColumn] = sum;
        }
        return data;
    }

    private List<Dictionary<string, object?>> ApplyMovingAverage(
        List<Dictionary<string, object?>> data,
        string outputColumn,
        string sourceColumn,
        int windowSize)
    {
        for (int i = 0; i < data.Count; i++)
        {
            int start = Math.Max(0, i - windowSize + 1);
            int count = i - start + 1;
            
            double sum = 0;
            for (int j = start; j <= i; j++)
            {
                if (data[j].TryGetValue(sourceColumn, out var value) && value != null)
                {
                    sum += Convert.ToDouble(value);
                }
            }

            data[i][outputColumn] = sum / count;
        }

        return data;
    }

    private List<Dictionary<string, object?>> ApplyPercentRank(
        List<Dictionary<string, object?>> data,
        string outputColumn,
        string[]? orderBy)
    {
        for (int i = 0; i < data.Count; i++)
        {
            data[i][outputColumn] = data.Count > 1 
                ? (double)i / (data.Count - 1) 
                : 0.0;
        }
        return data;
    }

    private List<Dictionary<string, object?>> ApplyCumeDist(
        List<Dictionary<string, object?>> data,
        string outputColumn,
        string[]? orderBy)
    {
        for (int i = 0; i < data.Count; i++)
        {
            data[i][outputColumn] = (double)(i + 1) / data.Count;
        }
        return data;
    }
}

public enum WindowFunction
{
    RowNumber,        // Assigns sequential number
    Rank,             // Rank with gaps
    DenseRank,        // Rank without gaps
    NTile,            // Divides into N buckets
    Lag,              // Access previous row
    Lead,             // Access next row
    FirstValue,       // First value in window
    LastValue,        // Last value in window
    CumulativeSum,    // Running total
    MovingAverage,    // Sliding window average
    PercentRank,      // Relative rank (0-1)
    CumeDist          // Cumulative distribution (0-1)
}
