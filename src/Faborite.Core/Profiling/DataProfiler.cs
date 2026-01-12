using Faborite.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Profiling;

/// <summary>
/// Profiles data to generate quality metrics and statistics.
/// Issue #41
/// </summary>
public class DataProfiler
{
    private readonly ILogger<DataProfiler> _logger;

    public DataProfiler(ILogger<DataProfiler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generates comprehensive data profile for a table.
    /// </summary>
    public async Task<DataProfile> ProfileTableAsync(
        string tableName,
        TableData data,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Profiling table {Table} with {Rows} rows", tableName, data.RowCount);

        var startTime = DateTime.UtcNow;
        var columnProfiles = new List<ColumnProfile>();

        foreach (var columnName in data.Columns)
        {
            var profile = ProfileColumn(columnName, data);
            columnProfiles.Add(profile);
        }

        var duration = DateTime.UtcNow - startTime;

        return new DataProfile(
            TableName: tableName,
            RowCount: data.RowCount,
            ColumnCount: data.Columns.Count,
            ColumnProfiles: columnProfiles,
            ProfiledAt: DateTime.UtcNow,
            Duration: duration
        );
    }

    private ColumnProfile ProfileColumn(string columnName, TableData data)
    {
        var values = data.GetColumnValues(columnName);
        var nonNullValues = values.Where(v => v != null).ToList();

        var nullCount = values.Count - nonNullValues.Count;
        var distinctCount = nonNullValues.Distinct().Count();
        var nullPercentage = (double)nullCount / values.Count * 100;
        var uniquePercentage = (double)distinctCount / values.Count * 100;

        // Detect data type
        var detectedType = DetectDataType(nonNullValues);

        // Calculate statistics based on type
        NumericStatistics? numericStats = null;
        StringStatistics? stringStats = null;
        DateStatistics? dateStats = null;

        if (detectedType == DataType.Numeric && nonNullValues.Any())
        {
            numericStats = CalculateNumericStatistics(nonNullValues);
        }
        else if (detectedType == DataType.String && nonNullValues.Any())
        {
            stringStats = CalculateStringStatistics(nonNullValues);
        }
        else if (detectedType == DataType.Date && nonNullValues.Any())
        {
            dateStats = CalculateDateStatistics(nonNullValues);
        }

        return new ColumnProfile(
            ColumnName: columnName,
            DataType: detectedType.ToString(),
            TotalCount: values.Count,
            NullCount: nullCount,
            NullPercentage: nullPercentage,
            DistinctCount: distinctCount,
            UniquePercentage: uniquePercentage,
            NumericStats: numericStats,
            StringStats: stringStats,
            DateStats: dateStats
        );
    }

    private DataType DetectDataType(List<object> values)
    {
        if (!values.Any()) return DataType.Unknown;

        var sample = values.Take(100).ToList();

        if (sample.All(v => IsNumeric(v)))
            return DataType.Numeric;

        if (sample.All(v => IsDate(v)))
            return DataType.Date;

        return DataType.String;
    }

    private bool IsNumeric(object value) =>
        value is int or long or float or double or decimal;

    private bool IsDate(object value) =>
        value is DateTime || DateTime.TryParse(value.ToString(), out _);

    private NumericStatistics CalculateNumericStatistics(List<object> values)
    {
        var numbers = values.Select(v => Convert.ToDouble(v)).OrderBy(n => n).ToList();

        var min = numbers.Min();
        var max = numbers.Max();
        var mean = numbers.Average();
        var median = numbers[numbers.Count / 2];
        
        var variance = numbers.Sum(n => Math.Pow(n - mean, 2)) / numbers.Count;
        var stdDev = Math.Sqrt(variance);

        return new NumericStatistics(min, max, mean, median, stdDev, variance);
    }

    private StringStatistics CalculateStringStatistics(List<object> values)
    {
        var strings = values.Select(v => v.ToString() ?? "").ToList();

        var minLength = strings.Min(s => s.Length);
        var maxLength = strings.Max(s => s.Length);
        var avgLength = strings.Average(s => s.Length);

        var topValues = strings
            .GroupBy(s => s)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => (g.Key, g.Count()))
            .ToList();

        return new StringStatistics(minLength, maxLength, avgLength, topValues);
    }

    private DateStatistics CalculateDateStatistics(List<object> values)
    {
        var dates = values
            .Select(v => v is DateTime dt ? dt : DateTime.Parse(v.ToString()!))
            .OrderBy(d => d)
            .ToList();

        var minDate = dates.Min();
        var maxDate = dates.Max();
        var range = maxDate - minDate;

        return new DateStatistics(minDate, maxDate, range);
    }
}

public enum DataType
{
    Unknown,
    Numeric,
    String,
    Date,
    Boolean
}

public record DataProfile(
    string TableName,
    long RowCount,
    int ColumnCount,
    List<ColumnProfile> ColumnProfiles,
    DateTime ProfiledAt,
    TimeSpan Duration);

public record ColumnProfile(
    string ColumnName,
    string DataType,
    long TotalCount,
    long NullCount,
    double NullPercentage,
    long DistinctCount,
    double UniquePercentage,
    NumericStatistics? NumericStats,
    StringStatistics? StringStats,
    DateStatistics? DateStats);

public record NumericStatistics(
    double Min,
    double Max,
    double Mean,
    double Median,
    double StdDev,
    double Variance);

public record StringStatistics(
    int MinLength,
    int MaxLength,
    double AvgLength,
    List<(string Value, int Count)> TopValues);

public record DateStatistics(
    DateTime MinDate,
    DateTime MaxDate,
    TimeSpan Range);

/// <summary>
/// Simple in-memory table data representation for profiling.
/// </summary>
public class TableData
{
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public long RowCount => Rows.Count;

    public List<object?> GetColumnValues(string columnName)
    {
        return Rows.Select(row => row.TryGetValue(columnName, out var value) ? value : null).ToList();
    }
}
