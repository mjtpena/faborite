using Microsoft.Extensions.Logging;
using System.Linq.Expressions;

namespace Faborite.Core.Transformation;

/// <summary>
/// Production-ready custom aggregation engine for complex data summarization.
/// Issue #50 - Custom aggregations (percentiles, mode, variance, etc.)
/// </summary>
public class AggregationEngine
{
    private readonly ILogger<AggregationEngine> _logger;

    public AggregationEngine(ILogger<AggregationEngine> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Dictionary<string, object?> Aggregate(
        List<Dictionary<string, object?>> data,
        List<AggregationSpec> aggregations,
        string[]? groupBy = null)
    {
        _logger.LogInformation("Aggregating {Rows} rows with {AggCount} aggregations", 
            data.Count, aggregations.Count);

        if (data.Count == 0)
            return new Dictionary<string, object?>();

        var result = new Dictionary<string, object?>();

        foreach (var spec in aggregations)
        {
            var values = ExtractNumericValues(data, spec.Column);
            var aggregatedValue = ComputeAggregation(values, spec.Function, spec.Parameters);
            result[spec.OutputName ?? $"{spec.Function}_{spec.Column}"] = aggregatedValue;
        }

        return result;
    }

    public List<Dictionary<string, object?>> GroupByAggregate(
        List<Dictionary<string, object?>> data,
        string[] groupByColumns,
        List<AggregationSpec> aggregations)
    {
        _logger.LogInformation("Group by aggregation on {Cols} columns with {AggCount} aggregations",
            groupByColumns.Length, aggregations.Count);

        if (data.Count == 0)
            return new List<Dictionary<string, object?>>();

        var groups = data.GroupBy(row =>
        {
            var key = string.Join("|", groupByColumns.Select(col => row.GetValueOrDefault(col)?.ToString() ?? "NULL"));
            return key;
        });

        var results = new List<Dictionary<string, object?>>();

        foreach (var group in groups)
        {
            var result = new Dictionary<string, object?>();

            // Add group by columns
            var firstRow = group.First();
            foreach (var col in groupByColumns)
            {
                result[col] = firstRow.GetValueOrDefault(col);
            }

            // Add aggregations
            foreach (var spec in aggregations)
            {
                var values = ExtractNumericValues(group.ToList(), spec.Column);
                var aggregatedValue = ComputeAggregation(values, spec.Function, spec.Parameters);
                result[spec.OutputName ?? $"{spec.Function}_{spec.Column}"] = aggregatedValue;
            }

            results.Add(result);
        }

        _logger.LogInformation("Created {GroupCount} groups", results.Count);
        return results;
    }

    private object? ComputeAggregation(List<double> values, AggregationFunction function, Dictionary<string, object>? parameters)
    {
        if (values.Count == 0)
            return null;

        return function switch
        {
            AggregationFunction.Count => values.Count,
            AggregationFunction.Sum => values.Sum(),
            AggregationFunction.Average => values.Average(),
            AggregationFunction.Min => values.Min(),
            AggregationFunction.Max => values.Max(),
            AggregationFunction.Median => CalculateMedian(values),
            AggregationFunction.Mode => CalculateMode(values),
            AggregationFunction.Variance => CalculateVariance(values),
            AggregationFunction.StdDev => CalculateStdDev(values),
            AggregationFunction.Range => values.Max() - values.Min(),
            AggregationFunction.Percentile => CalculatePercentile(values, GetParameter<double>(parameters, "percentile", 50)),
            AggregationFunction.Quartile => CalculateQuartile(values, GetParameter<int>(parameters, "quartile", 1)),
            AggregationFunction.IQR => CalculateIQR(values),
            AggregationFunction.MAD => CalculateMAD(values),
            AggregationFunction.Skewness => CalculateSkewness(values),
            AggregationFunction.Kurtosis => CalculateKurtosis(values),
            AggregationFunction.GeometricMean => CalculateGeometricMean(values),
            AggregationFunction.HarmonicMean => CalculateHarmonicMean(values),
            AggregationFunction.RootMeanSquare => CalculateRootMeanSquare(values),
            AggregationFunction.CoefficientOfVariation => CalculateCoefficientOfVariation(values),
            AggregationFunction.ZScore => CalculateZScore(values, GetParameter<double>(parameters, "value", values.First())),
            AggregationFunction.PercentileRank => CalculatePercentileRank(values, GetParameter<double>(parameters, "value", values.First())),
            _ => null
        };
    }

    private List<double> ExtractNumericValues(List<Dictionary<string, object?>> data, string column)
    {
        return data
            .Where(row => row.ContainsKey(column) && row[column] != null)
            .Select(row =>
            {
                var value = row[column];
                return value switch
                {
                    double d => d,
                    int i => (double)i,
                    long l => (double)l,
                    float f => (double)f,
                    decimal dec => (double)dec,
                    _ => double.TryParse(value?.ToString(), out var parsed) ? parsed : 0.0
                };
            })
            .ToList();
    }

    private T GetParameter<T>(Dictionary<string, object>? parameters, string key, T defaultValue)
    {
        if (parameters == null || !parameters.ContainsKey(key))
            return defaultValue;

        return (T)Convert.ChangeType(parameters[key], typeof(T));
    }

    #region Statistical Functions

    private double CalculateMedian(List<double> values)
    {
        var sorted = values.OrderBy(x => x).ToList();
        int count = sorted.Count;
        
        if (count % 2 == 0)
            return (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0;
        else
            return sorted[count / 2];
    }

    private double CalculateMode(List<double> values)
    {
        return values
            .GroupBy(x => x)
            .OrderByDescending(g => g.Count())
            .First()
            .Key;
    }

    private double CalculateVariance(List<double> values)
    {
        var mean = values.Average();
        return values.Sum(x => Math.Pow(x - mean, 2)) / values.Count;
    }

    private double CalculateStdDev(List<double> values)
    {
        return Math.Sqrt(CalculateVariance(values));
    }

    private double CalculatePercentile(List<double> values, double percentile)
    {
        var sorted = values.OrderBy(x => x).ToList();
        double index = (percentile / 100.0) * (sorted.Count - 1);
        int lowerIndex = (int)Math.Floor(index);
        int upperIndex = (int)Math.Ceiling(index);
        
        if (lowerIndex == upperIndex)
            return sorted[lowerIndex];
        
        double fraction = index - lowerIndex;
        return sorted[lowerIndex] * (1 - fraction) + sorted[upperIndex] * fraction;
    }

    private double CalculateQuartile(List<double> values, int quartile)
    {
        return quartile switch
        {
            1 => CalculatePercentile(values, 25),
            2 => CalculatePercentile(values, 50),
            3 => CalculatePercentile(values, 75),
            _ => throw new ArgumentException("Quartile must be 1, 2, or 3")
        };
    }

    private double CalculateIQR(List<double> values)
    {
        return CalculateQuartile(values, 3) - CalculateQuartile(values, 1);
    }

    private double CalculateMAD(List<double> values)
    {
        var median = CalculateMedian(values);
        var deviations = values.Select(x => Math.Abs(x - median)).ToList();
        return CalculateMedian(deviations);
    }

    private double CalculateSkewness(List<double> values)
    {
        var mean = values.Average();
        var stdDev = CalculateStdDev(values);
        var n = values.Count;
        
        var sum = values.Sum(x => Math.Pow((x - mean) / stdDev, 3));
        return (n / ((n - 1.0) * (n - 2.0))) * sum;
    }

    private double CalculateKurtosis(List<double> values)
    {
        var mean = values.Average();
        var stdDev = CalculateStdDev(values);
        var n = values.Count;
        
        var sum = values.Sum(x => Math.Pow((x - mean) / stdDev, 4));
        return ((n * (n + 1)) / ((n - 1.0) * (n - 2.0) * (n - 3.0))) * sum - 
               (3 * Math.Pow(n - 1, 2)) / ((n - 2.0) * (n - 3.0));
    }

    private double CalculateGeometricMean(List<double> values)
    {
        if (values.Any(x => x <= 0))
            throw new ArgumentException("Geometric mean requires positive values");
        
        var product = values.Aggregate(1.0, (acc, x) => acc * x);
        return Math.Pow(product, 1.0 / values.Count);
    }

    private double CalculateHarmonicMean(List<double> values)
    {
        if (values.Any(x => x == 0))
            throw new ArgumentException("Harmonic mean requires non-zero values");
        
        var sumOfReciprocals = values.Sum(x => 1.0 / x);
        return values.Count / sumOfReciprocals;
    }

    private double CalculateRootMeanSquare(List<double> values)
    {
        var sumOfSquares = values.Sum(x => x * x);
        return Math.Sqrt(sumOfSquares / values.Count);
    }

    private double CalculateCoefficientOfVariation(List<double> values)
    {
        var mean = values.Average();
        var stdDev = CalculateStdDev(values);
        return (stdDev / mean) * 100;
    }

    private double CalculateZScore(List<double> values, double value)
    {
        var mean = values.Average();
        var stdDev = CalculateStdDev(values);
        return (value - mean) / stdDev;
    }

    private double CalculatePercentileRank(List<double> values, double value)
    {
        var countBelow = values.Count(x => x < value);
        var countEqual = values.Count(x => x == value);
        return ((countBelow + 0.5 * countEqual) / values.Count) * 100;
    }

    #endregion
}

public record AggregationSpec(
    string Column,
    AggregationFunction Function,
    string? OutputName = null,
    Dictionary<string, object>? Parameters = null
);

public enum AggregationFunction
{
    // Basic
    Count,
    Sum,
    Average,
    Min,
    Max,
    
    // Central Tendency
    Median,
    Mode,
    
    // Dispersion
    Variance,
    StdDev,
    Range,
    IQR,              // Interquartile Range
    MAD,              // Median Absolute Deviation
    CoefficientOfVariation,
    
    // Distribution
    Percentile,
    Quartile,
    Skewness,
    Kurtosis,
    ZScore,
    PercentileRank,
    
    // Specialized Means
    GeometricMean,
    HarmonicMean,
    RootMeanSquare
}
