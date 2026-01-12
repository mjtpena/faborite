using Faborite.Core.Common;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Analytics;

/// <summary>
/// Custom aggregation functions for data analysis.
/// Issue #50
/// </summary>
public class AggregationEngine
{
    private readonly ILogger<AggregationEngine> _logger;

    public AggregationEngine(ILogger<AggregationEngine> logger)
    {
        _logger = logger;
    }

    public async Task<AggregationResult> AggregateAsync(
        TableData data,
        AggregationQuery query,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing aggregation query on {Table}", query.SourceTable);

        var results = new Dictionary<string, object>();

        foreach (var agg in query.Aggregations)
        {
            var value = ExecuteAggregation(data, agg);
            results[agg.Name] = value;
        }

        // Group by if specified
        if (query.GroupBy.Any())
        {
            var grouped = GroupData(data, query.GroupBy);
            var groupResults = new List<Dictionary<string, object>>();

            foreach (var (groupKey, groupData) in grouped)
            {
                var groupResult = new Dictionary<string, object>();
                
                // Add group key columns
                var keyParts = groupKey.Split('|');
                for (int i = 0; i < query.GroupBy.Count; i++)
                {
                    groupResult[query.GroupBy[i]] = keyParts[i];
                }

                // Add aggregations
                foreach (var agg in query.Aggregations)
                {
                    groupResult[agg.Name] = ExecuteAggregation(groupData, agg);
                }

                groupResults.Add(groupResult);
            }

            return new AggregationResult(
                Query: query,
                ScalarResults: results,
                GroupedResults: groupResults,
                ExecutedAt: DateTime.UtcNow
            );
        }

        return new AggregationResult(
            Query: query,
            ScalarResults: results,
            GroupedResults: null,
            ExecutedAt: DateTime.UtcNow
        );
    }

    private object ExecuteAggregation(TableData data, Aggregation agg)
    {
        var values = data.GetColumnValues(agg.Column).Where(v => v != null).ToList();

        return agg.Function switch
        {
            AggFunction.Count => values.Count,
            AggFunction.Sum => values.Sum(v => Convert.ToDouble(v)),
            AggFunction.Avg => values.Average(v => Convert.ToDouble(v)),
            AggFunction.Min => values.Min(v => Convert.ToDouble(v)),
            AggFunction.Max => values.Max(v => Convert.ToDouble(v)),
            AggFunction.Median => CalculateMedian(values),
            AggFunction.StdDev => CalculateStdDev(values),
            AggFunction.Variance => CalculateVariance(values),
            AggFunction.Percentile => CalculatePercentile(values, agg.Parameters?.GetValueOrDefault("percentile", 0.5) ?? 0.5),
            AggFunction.Mode => CalculateMode(values),
            AggFunction.DistinctCount => values.Distinct().Count(),
            _ => throw new ArgumentException($"Unknown function: {agg.Function}")
        };
    }

    private Dictionary<string, TableData> GroupData(TableData data, List<string> groupBy)
    {
        var groups = new Dictionary<string, TableData>();

        foreach (var row in data.Rows)
        {
            var key = string.Join("|", groupBy.Select(col => row.GetValueOrDefault(col)?.ToString() ?? "NULL"));
            
            if (!groups.ContainsKey(key))
            {
                groups[key] = new TableData { Columns = data.Columns };
            }
            
            groups[key].Rows.Add(row);
        }

        return groups;
    }

    private double CalculateMedian(List<object> values)
    {
        var sorted = values.Select(v => Convert.ToDouble(v)).OrderBy(x => x).ToList();
        return sorted[sorted.Count / 2];
    }

    private double CalculateStdDev(List<object> values)
    {
        var numbers = values.Select(v => Convert.ToDouble(v)).ToList();
        var mean = numbers.Average();
        var variance = numbers.Sum(n => Math.Pow(n - mean, 2)) / numbers.Count;
        return Math.Sqrt(variance);
    }

    private double CalculateVariance(List<object> values)
    {
        var numbers = values.Select(v => Convert.ToDouble(v)).ToList();
        var mean = numbers.Average();
        return numbers.Sum(n => Math.Pow(n - mean, 2)) / numbers.Count;
    }

    private double CalculatePercentile(List<object> values, double percentile)
    {
        var sorted = values.Select(v => Convert.ToDouble(v)).OrderBy(x => x).ToList();
        var index = (int)(percentile * sorted.Count);
        return sorted[Math.Min(index, sorted.Count - 1)];
    }

    private object CalculateMode(List<object> values)
    {
        return values.GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key ?? 0;
    }
}

public record AggregationQuery(
    string SourceTable,
    List<Aggregation> Aggregations,
    List<string> GroupBy,
    string? Filter = null);

public record Aggregation(
    string Name,
    string Column,
    AggFunction Function,
    Dictionary<string, double>? Parameters = null);

public enum AggFunction
{
    Count,
    Sum,
    Avg,
    Min,
    Max,
    Median,
    StdDev,
    Variance,
    Percentile,
    Mode,
    DistinctCount
}

public record AggregationResult(
    AggregationQuery Query,
    Dictionary<string, object> ScalarResults,
    List<Dictionary<string, object>>? GroupedResults,
    DateTime ExecutedAt);
