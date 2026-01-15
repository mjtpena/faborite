using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace Faborite.Core.AI;

/// <summary>
/// Production-ready schema inference engine with intelligent type detection.
/// Issue #173 - Smart schema mapping and data type inference
/// </summary>
public class SchemaInferenceEngine : IDisposable
{
    private readonly ILogger<SchemaInferenceEngine> _logger;

    public SchemaInferenceEngine(ILogger<SchemaInferenceEngine> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("Schema inference engine initialized");
    }

    public async Task<SchemaInferenceResult> InferSchemaAsync(
        List<Dictionary<string, object?>> data,
        int sampleSize = 1000,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Inferring schema from {Rows} rows", data.Count);

            var sample = data.Take(sampleSize).ToList();
            
            if (sample.Count == 0)
                throw new ArgumentException("Data cannot be empty");

            var columns = new List<InferredColumn>();
            var firstRow = sample.First();

            foreach (var (columnName, _) in firstRow)
            {
                var columnValues = sample
                    .Select(row => row.TryGetValue(columnName, out var val) ? val : null)
                    .ToList();

                var inferredColumn = InferColumnType(columnName, columnValues);
                columns.Add(inferredColumn);
            }

            var result = new SchemaInferenceResult
            {
                Columns = columns,
                RowCount = data.Count,
                SampleSize = sample.Count
            };

            _logger.LogInformation("Inferred {Count} columns", columns.Count);

            return await Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to infer schema");
            throw;
        }
    }

    private InferredColumn InferColumnType(string columnName, List<object?> values)
    {
        var nonNullValues = values.Where(v => v != null).ToList();
        var nullCount = values.Count - nonNullValues.Count;

        if (nonNullValues.Count == 0)
        {
            return new InferredColumn
            {
                Name = columnName,
                InferredType = DataType.String,
                IsNullable = true,
                NullCount = nullCount,
                Confidence = 0.0
            };
        }

        // Try different type inferences
        var typeScores = new Dictionary<DataType, double>
        {
            { DataType.Boolean, ScoreBoolean(nonNullValues) },
            { DataType.Integer, ScoreInteger(nonNullValues) },
            { DataType.Decimal, ScoreDecimal(nonNullValues) },
            { DataType.DateTime, ScoreDateTime(nonNullValues) },
            { DataType.Guid, ScoreGuid(nonNullValues) },
            { DataType.Email, ScoreEmail(nonNullValues) },
            { DataType.Url, ScoreUrl(nonNullValues) },
            { DataType.Json, ScoreJson(nonNullValues) }
        };

        var bestType = typeScores.OrderByDescending(kvp => kvp.Value).First();

        // Default to string if confidence is too low
        if (bestType.Value < 0.5)
        {
            bestType = new KeyValuePair<DataType, double>(DataType.String, 1.0);
        }

        var statistics = CalculateStatistics(nonNullValues, bestType.Key);

        return new InferredColumn
        {
            Name = columnName,
            InferredType = bestType.Key,
            IsNullable = nullCount > 0,
            NullCount = nullCount,
            Confidence = bestType.Value,
            UniqueCount = nonNullValues.Distinct().Count(),
            Statistics = statistics
        };
    }

    private double ScoreBoolean(List<object?> values)
    {
        var stringValues = values.Select(v => v?.ToString()?.ToLower()).Where(s => s != null).ToList();
        var boolStrings = new HashSet<string> { "true", "false", "yes", "no", "1", "0", "t", "f", "y", "n" };

        var matches = stringValues.Count(s => boolStrings.Contains(s!));
        return (double)matches / values.Count;
    }

    private double ScoreInteger(List<object?> values)
    {
        var matches = values.Count(v =>
        {
            if (v is int || v is long || v is short)
                return true;

            var str = v?.ToString();
            return str != null && long.TryParse(str, out _);
        });

        return (double)matches / values.Count;
    }

    private double ScoreDecimal(List<object?> values)
    {
        var matches = values.Count(v =>
        {
            if (v is float || v is double || v is decimal)
                return true;

            var str = v?.ToString();
            return str != null && decimal.TryParse(str, out _);
        });

        return (double)matches / values.Count;
    }

    private double ScoreDateTime(List<object?> values)
    {
        var matches = values.Count(v =>
        {
            if (v is DateTime || v is DateTimeOffset)
                return true;

            var str = v?.ToString();
            return str != null && DateTime.TryParse(str, out _);
        });

        return (double)matches / values.Count;
    }

    private double ScoreGuid(List<object?> values)
    {
        var matches = values.Count(v =>
        {
            if (v is Guid)
                return true;

            var str = v?.ToString();
            return str != null && Guid.TryParse(str, out _);
        });

        return (double)matches / values.Count;
    }

    private double ScoreEmail(List<object?> values)
    {
        var emailRegex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
        var matches = values.Count(v =>
        {
            var str = v?.ToString();
            return str != null && emailRegex.IsMatch(str);
        });

        return (double)matches / values.Count;
    }

    private double ScoreUrl(List<object?> values)
    {
        var matches = values.Count(v =>
        {
            var str = v?.ToString();
            return str != null && Uri.TryCreate(str, UriKind.Absolute, out _);
        });

        return (double)matches / values.Count;
    }

    private double ScoreJson(List<object?> values)
    {
        var matches = values.Count(v =>
        {
            var str = v?.ToString();
            if (str == null)
                return false;

            return (str.StartsWith("{") && str.EndsWith("}")) ||
                   (str.StartsWith("[") && str.EndsWith("]"));
        });

        return (double)matches / values.Count;
    }

    private ColumnStatistics CalculateStatistics(List<object?> values, DataType type)
    {
        var stats = new ColumnStatistics();

        if (type == DataType.Integer || type == DataType.Decimal)
        {
            var numericValues = values
                .Select(v => v?.ToString())
                .Where(s => s != null && double.TryParse(s, out _))
                .Select(s => double.Parse(s!))
                .ToList();

            if (numericValues.Count > 0)
            {
                stats.Min = numericValues.Min();
                stats.Max = numericValues.Max();
                stats.Mean = numericValues.Average();
                stats.Median = CalculateMedian(numericValues);
            }
        }

        if (type == DataType.String || type == DataType.Email || type == DataType.Url)
        {
            var stringValues = values.Select(v => v?.ToString()).Where(s => s != null).ToList();

            if (stringValues.Count > 0)
            {
                stats.MinLength = stringValues.Min(s => s!.Length);
                stats.MaxLength = stringValues.Max(s => s!.Length);
                stats.AvgLength = stringValues.Average(s => s!.Length);
            }
        }

        return stats;
    }

    private double CalculateMedian(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;

        if (sorted.Count % 2 == 0)
            return (sorted[mid - 1] + sorted[mid]) / 2.0;
        else
            return sorted[mid];
    }

    public async Task<SchemaMappingResult> MapSchemasAsync(
        SchemaInferenceResult sourceSchema,
        SchemaInferenceResult targetSchema,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Mapping schema from {Source} to {Target} columns",
                sourceSchema.Columns.Count, targetSchema.Columns.Count);

            var mappings = new List<ColumnMapping>();

            foreach (var sourceColumn in sourceSchema.Columns)
            {
                var bestMatch = FindBestMatch(sourceColumn, targetSchema.Columns);

                if (bestMatch != null)
                {
                    mappings.Add(bestMatch);
                }
            }

            var result = new SchemaMappingResult
            {
                Mappings = mappings,
                UnmappedSourceColumns = sourceSchema.Columns
                    .Where(c => !mappings.Any(m => m.SourceColumn == c.Name))
                    .Select(c => c.Name)
                    .ToList(),
                UnmappedTargetColumns = targetSchema.Columns
                    .Where(c => !mappings.Any(m => m.TargetColumn == c.Name))
                    .Select(c => c.Name)
                    .ToList()
            };

            _logger.LogInformation("Mapped {Count} columns with {Unmapped} unmapped",
                mappings.Count, result.UnmappedSourceColumns.Count);

            return await Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to map schemas");
            throw;
        }
    }

    private ColumnMapping? FindBestMatch(InferredColumn source, List<InferredColumn> targets)
    {
        var scores = new List<(InferredColumn target, double score)>();

        foreach (var target in targets)
        {
            var nameScore = CalculateNameSimilarity(source.Name, target.Name);
            var typeScore = source.InferredType == target.InferredType ? 1.0 : 0.5;
            var totalScore = (nameScore * 0.7) + (typeScore * 0.3);

            scores.Add((target, totalScore));
        }

        var best = scores.OrderByDescending(s => s.score).FirstOrDefault();

        if (best.score >= 0.5)
        {
            return new ColumnMapping
            {
                SourceColumn = source.Name,
                TargetColumn = best.target.Name,
                SourceType = source.InferredType,
                TargetType = best.target.InferredType,
                Confidence = best.score,
                RequiresTransformation = source.InferredType != best.target.InferredType
            };
        }

        return null;
    }

    private double CalculateNameSimilarity(string name1, string name2)
    {
        name1 = name1.ToLower();
        name2 = name2.ToLower();

        if (name1 == name2)
            return 1.0;

        // Levenshtein distance
        var distance = LevenshteinDistance(name1, name2);
        var maxLength = Math.Max(name1.Length, name2.Length);

        return 1.0 - ((double)distance / maxLength);
    }

    private int LevenshteinDistance(string s1, string s2)
    {
        var matrix = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++)
            matrix[i, 0] = i;

        for (int j = 0; j <= s2.Length; j++)
            matrix[0, j] = j;

        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;

                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[s1.Length, s2.Length];
    }

    public void Dispose()
    {
        _logger.LogDebug("Schema inference engine disposed");
    }
}

public enum DataType
{
    String,
    Integer,
    Decimal,
    Boolean,
    DateTime,
    Guid,
    Email,
    Url,
    Json
}

public class InferredColumn
{
    public string Name { get; set; } = "";
    public DataType InferredType { get; set; }
    public bool IsNullable { get; set; }
    public int NullCount { get; set; }
    public double Confidence { get; set; }
    public int UniqueCount { get; set; }
    public ColumnStatistics Statistics { get; set; } = new();
}

public class ColumnStatistics
{
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Mean { get; set; }
    public double? Median { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public double? AvgLength { get; set; }
}

public class SchemaInferenceResult
{
    public List<InferredColumn> Columns { get; set; } = new();
    public int RowCount { get; set; }
    public int SampleSize { get; set; }
}

public class ColumnMapping
{
    public string SourceColumn { get; set; } = "";
    public string TargetColumn { get; set; } = "";
    public DataType SourceType { get; set; }
    public DataType TargetType { get; set; }
    public double Confidence { get; set; }
    public bool RequiresTransformation { get; set; }
}

public class SchemaMappingResult
{
    public List<ColumnMapping> Mappings { get; set; } = new();
    public List<string> UnmappedSourceColumns { get; set; } = new();
    public List<string> UnmappedTargetColumns { get; set; } = new();
}
