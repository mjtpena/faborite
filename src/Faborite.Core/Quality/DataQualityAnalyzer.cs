using Faborite.Core.Common;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Quality;

/// <summary>
/// Generates comprehensive data quality metrics.
/// Issue #42
/// </summary>
public class DataQualityAnalyzer
{
    private readonly ILogger<DataQualityAnalyzer> _logger;

    public DataQualityAnalyzer(ILogger<DataQualityAnalyzer> logger)
    {
        _logger = logger;
    }

    public async Task<QualityReport> AnalyzeAsync(
        string tableName,
        TableData data,
        QualityRules rules,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing data quality for {Table}", tableName);

        var issues = new List<QualityIssue>();
        var metrics = new Dictionary<string, double>();

        // Completeness check
        var completeness = CalculateCompleteness(data);
        metrics["completeness"] = completeness;
        if (completeness < rules.MinCompleteness)
        {
            issues.Add(new QualityIssue("Completeness", $"Below threshold: {completeness:F2}% < {rules.MinCompleteness}%", Severity.High));
        }

        // Uniqueness check
        foreach (var uniqueColumn in rules.UniqueColumns)
        {
            var uniqueness = CalculateUniqueness(data, uniqueColumn);
            metrics[$"uniqueness_{uniqueColumn}"] = uniqueness;
            if (uniqueness < 100)
            {
                issues.Add(new QualityIssue("Uniqueness", $"Column {uniqueColumn} has duplicates: {uniqueness:F2}%", Severity.Medium));
            }
        }

        // Validity check
        foreach (var validation in rules.ValidationRules)
        {
            var validity = ValidateRule(data, validation);
            metrics[$"validity_{validation.ColumnName}"] = validity;
            if (validity < rules.MinValidity)
            {
                issues.Add(new QualityIssue("Validity", $"Column {validation.ColumnName} failed validation: {validity:F2}%", Severity.Medium));
            }
        }

        // Consistency check
        var consistency = CheckConsistency(data, rules.ConsistencyRules);
        metrics["consistency"] = consistency;

        // Calculate overall score
        var overallScore = metrics.Values.Average();

        return new QualityReport(
            TableName: tableName,
            OverallScore: overallScore,
            Metrics: metrics,
            Issues: issues,
            AnalyzedAt: DateTime.UtcNow,
            RowsAnalyzed: data.RowCount
        );
    }

    private double CalculateCompleteness(TableData data)
    {
        var totalCells = (long)data.Columns.Count * data.RowCount;
        var nonNullCells = data.Columns.Sum(col => 
            data.GetColumnValues(col).Count(v => v != null));
        return (double)nonNullCells / totalCells * 100;
    }

    private double CalculateUniqueness(TableData data, string column)
    {
        var values = data.GetColumnValues(column);
        var distinct = values.Distinct().Count();
        return (double)distinct / values.Count * 100;
    }

    private double ValidateRule(TableData data, QualityValidationRule rule)
    {
        var values = data.GetColumnValues(rule.ColumnName);
        var valid = values.Count(v => rule.Validator(v));
        return (double)valid / values.Count * 100;
    }

    private double CheckConsistency(TableData data, List<ConsistencyRule> rules)
    {
        if (!rules.Any()) return 100;
        var passed = rules.Count(rule => CheckConsistencyRule(data, rule));
        return (double)passed / rules.Count * 100;
    }

    private bool CheckConsistencyRule(TableData data, ConsistencyRule rule)
    {
        // Check if referenced values exist
        var sourceValues = data.GetColumnValues(rule.SourceColumn).Where(v => v != null).ToHashSet();
        var targetValues = data.GetColumnValues(rule.TargetColumn).Where(v => v != null).ToHashSet();
        return sourceValues.All(v => targetValues.Contains(v));
    }
}

public record QualityRules(
    double MinCompleteness = 95.0,
    double MinValidity = 95.0,
    List<string>? UniqueColumns = null,
    List<QualityValidationRule>? ValidationRules = null,
    List<ConsistencyRule>? ConsistencyRules = null)
{
    public List<string> UniqueColumns { get; init; } = UniqueColumns ?? new();
    public List<QualityValidationRule> ValidationRules { get; init; } = ValidationRules ?? new();
    public List<ConsistencyRule> ConsistencyRules { get; init; } = ConsistencyRules ?? new();
}

public record QualityValidationRule(string ColumnName, Func<object?, bool> Validator, string Description);
public record ConsistencyRule(string SourceColumn, string TargetColumn, string Description);

public record QualityReport(
    string TableName,
    double OverallScore,
    Dictionary<string, double> Metrics,
    List<QualityIssue> Issues,
    DateTime AnalyzedAt,
    long RowsAnalyzed);

public record QualityIssue(string Category, string Description, Severity Severity);
