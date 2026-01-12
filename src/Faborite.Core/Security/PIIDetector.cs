using Faborite.Core.Common;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Faborite.Core.Security;

/// <summary>
/// Detects personally identifiable information (PII) in data.
/// Issue #48
/// </summary>
public class PIIDetector
{
    private readonly ILogger<PIIDetector> _logger;
    private readonly Dictionary<PIIType, Regex> _patterns;

    public PIIDetector(ILogger<PIIDetector> logger)
    {
        _logger = logger;
        _patterns = InitializePatterns();
    }

    private Dictionary<PIIType, Regex> InitializePatterns()
    {
        return new Dictionary<PIIType, Regex>
        {
            [PIIType.Email] = new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled),
            [PIIType.Phone] = new Regex(@"\b(\+\d{1,3}[- ]?)?\(?\d{3}\)?[- ]?\d{3}[- ]?\d{4}\b", RegexOptions.Compiled),
            [PIIType.SSN] = new Regex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled),
            [PIIType.CreditCard] = new Regex(@"\b\d{4}[- ]?\d{4}[- ]?\d{4}[- ]?\d{4}\b", RegexOptions.Compiled),
            [PIIType.IPAddress] = new Regex(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", RegexOptions.Compiled),
            [PIIType.ZipCode] = new Regex(@"\b\d{5}(-\d{4})?\b", RegexOptions.Compiled)
        };
    }

    public async Task<PIIReport> ScanAsync(
        string tableName,
        TableData data,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Scanning {Table} for PII", tableName);

        var findings = new List<PIIFinding>();

        foreach (var column in data.Columns)
        {
            var columnValues = data.GetColumnValues(column);
            var piiTypes = DetectPIITypes(columnValues);
            
            if (piiTypes.Any())
            {
                var confidence = CalculateConfidence(columnValues, piiTypes);
                findings.Add(new PIIFinding(
                    ColumnName: column,
                    DetectedTypes: piiTypes,
                    Confidence: confidence,
                    SampleCount: columnValues.Count,
                    MatchCount: CountMatches(columnValues, piiTypes)
                ));

                _logger.LogWarning("PII detected in column {Column}: {Types}", 
                    column, string.Join(", ", piiTypes));
            }
        }

        return new PIIReport(
            TableName: tableName,
            Findings: findings,
            ScannedAt: DateTime.UtcNow,
            TotalColumns: data.Columns.Count,
            ColumnsWithPII: findings.Count
        );
    }

    private List<PIIType> DetectPIITypes(List<object?> values)
    {
        var detected = new List<PIIType>();
        var samples = values.Take(100).Where(v => v != null).Select(v => v!.ToString() ?? "").ToList();

        foreach (var (type, pattern) in _patterns)
        {
            if (samples.Any(s => pattern.IsMatch(s)))
            {
                detected.Add(type);
            }
        }

        return detected;
    }

    private double CalculateConfidence(List<object?> values, List<PIIType> types)
    {
        var samples = values.Take(100).Where(v => v != null).Select(v => v!.ToString() ?? "").ToList();
        var matches = samples.Count(s => types.Any(t => _patterns[t].IsMatch(s)));
        return (double)matches / samples.Count * 100;
    }

    private int CountMatches(List<object?> values, List<PIIType> types)
    {
        return values.Count(v => 
            v != null && types.Any(t => _patterns[t].IsMatch(v.ToString() ?? "")));
    }

    public TableData RedactPII(TableData data, PIIReport report, RedactionStrategy strategy)
    {
        var redactedData = new TableData
        {
            Columns = data.Columns,
            Rows = new List<Dictionary<string, object?>>()
        };

        var piiColumns = report.Findings.Select(f => f.ColumnName).ToHashSet();

        foreach (var row in data.Rows)
        {
            var redactedRow = new Dictionary<string, object?>();
            foreach (var (col, value) in row)
            {
                if (piiColumns.Contains(col) && value != null)
                {
                    redactedRow[col] = ApplyRedaction(value.ToString() ?? "", strategy);
                }
                else
                {
                    redactedRow[col] = value;
                }
            }
            redactedData.Rows.Add(redactedRow);
        }

        return redactedData;
    }

    private string ApplyRedaction(string value, RedactionStrategy strategy)
    {
        return strategy switch
        {
            RedactionStrategy.Replace => "[REDACTED]",
            RedactionStrategy.Mask => new string('*', value.Length),
            RedactionStrategy.Hash => ComputeHash(value),
            RedactionStrategy.Tokenize => $"TOKEN_{Guid.NewGuid():N}",
            _ => value
        };
    }

    private string ComputeHash(string value)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }
}

public enum PIIType
{
    Email,
    Phone,
    SSN,
    CreditCard,
    IPAddress,
    ZipCode,
    Name,
    Address
}

public enum RedactionStrategy
{
    Replace,
    Mask,
    Hash,
    Tokenize
}

public record PIIFinding(
    string ColumnName,
    List<PIIType> DetectedTypes,
    double Confidence,
    int SampleCount,
    int MatchCount);

public record PIIReport(
    string TableName,
    List<PIIFinding> Findings,
    DateTime ScannedAt,
    int TotalColumns,
    int ColumnsWithPII);
