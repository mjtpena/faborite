using Faborite.Core.Common;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Quality;

/// <summary>
/// Provides data validation rules and engines.
/// Issue #49
/// </summary>
public class DataValidator
{
    private readonly ILogger<DataValidator> _logger;

    public DataValidator(ILogger<DataValidator> logger)
    {
        _logger = logger;
    }

    public async Task<ValidationResult> ValidateAsync(
        TableData data,
        ValidationSchema schema,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Validating data with {Count} rules", schema.Rules.Count);

        var violations = new List<ValidationViolation>();
        var rowIndex = 0;

        foreach (var row in data.Rows)
        {
            foreach (var rule in schema.Rules)
            {
                if (!rule.Validate(row))
                {
                    violations.Add(new ValidationViolation(
                        RowIndex: rowIndex,
                        RuleName: rule.Name,
                        Column: rule.ColumnName,
                        Message: rule.ErrorMessage,
                        Severity: rule.Severity,
                        Value: row.GetValueOrDefault(rule.ColumnName)
                    ));
                }
            }
            rowIndex++;
        }

        var passRate = (double)(data.RowCount - violations.Count) / data.RowCount * 100;

        return new ValidationResult(
            TotalRows: data.RowCount,
            ValidRows: data.RowCount - violations.Count,
            InvalidRows: violations.Count,
            PassRate: passRate,
            Violations: violations,
            ValidatedAt: DateTime.UtcNow
        );
    }
}

public class ValidationSchema
{
    public List<ValidationRule> Rules { get; set; } = new();

    public void AddRule(ValidationRule rule) => Rules.Add(rule);

    public static ValidationSchema Create()
    {
        return new ValidationSchema();
    }

    public ValidationSchema Required(string column, string? message = null)
    {
        Rules.Add(new ValidationRule(
            Name: $"{column}_required",
            ColumnName: column,
            Validator: row => row.ContainsKey(column) && row[column] != null,
            ErrorMessage: message ?? $"{column} is required",
            Severity: Severity.High
        ));
        return this;
    }

    public ValidationSchema Range(string column, double min, double max, string? message = null)
    {
        Rules.Add(new ValidationRule(
            Name: $"{column}_range",
            ColumnName: column,
            Validator: row =>
            {
                if (!row.TryGetValue(column, out var value) || value == null) return true;
                var num = Convert.ToDouble(value);
                return num >= min && num <= max;
            },
            ErrorMessage: message ?? $"{column} must be between {min} and {max}",
            Severity: Severity.Medium
        ));
        return this;
    }

    public ValidationSchema Pattern(string column, string pattern, string? message = null)
    {
        var regex = new System.Text.RegularExpressions.Regex(pattern);
        Rules.Add(new ValidationRule(
            Name: $"{column}_pattern",
            ColumnName: column,
            Validator: row =>
            {
                if (!row.TryGetValue(column, out var value) || value == null) return true;
                return regex.IsMatch(value.ToString() ?? "");
            },
            ErrorMessage: message ?? $"{column} must match pattern {pattern}",
            Severity: Severity.Medium
        ));
        return this;
    }

    public ValidationSchema Length(string column, int min, int max, string? message = null)
    {
        Rules.Add(new ValidationRule(
            Name: $"{column}_length",
            ColumnName: column,
            Validator: row =>
            {
                if (!row.TryGetValue(column, out var value) || value == null) return true;
                var len = value.ToString()?.Length ?? 0;
                return len >= min && len <= max;
            },
            ErrorMessage: message ?? $"{column} length must be between {min} and {max}",
            Severity: Severity.Low
        ));
        return this;
    }

    public ValidationSchema Custom(string name, string column, Func<Dictionary<string, object?>, bool> validator, string message, Severity severity = Severity.Medium)
    {
        Rules.Add(new ValidationRule(name, column, validator, message, severity));
        return this;
    }
}

public record ValidationRule(
    string Name,
    string ColumnName,
    Func<Dictionary<string, object?>, bool> Validator,
    string ErrorMessage,
    Severity Severity)
{
    public bool Validate(Dictionary<string, object?> row) => Validator(row);
}

public record ValidationResult(
    long TotalRows,
    long ValidRows,
    long InvalidRows,
    double PassRate,
    List<ValidationViolation> Violations,
    DateTime ValidatedAt);

public record ValidationViolation(
    int RowIndex,
    string RuleName,
    string Column,
    string Message,
    Severity Severity,
    object? Value);
