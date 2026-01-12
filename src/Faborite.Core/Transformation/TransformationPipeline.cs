using Microsoft.Extensions.Logging;

namespace Faborite.Core.Transformation;

/// <summary>
/// Pipeline for applying custom column transformations during sync.
/// Issue #30
/// </summary>
public class TransformationPipeline
{
    private readonly ILogger<TransformationPipeline> _logger;
    private readonly List<IColumnTransformation> _transformations = new();

    public TransformationPipeline(ILogger<TransformationPipeline> logger)
    {
        _logger = logger;
    }

    public void AddTransformation(IColumnTransformation transformation)
    {
        _transformations.Add(transformation);
        _logger.LogDebug("Added transformation: {Type}", transformation.GetType().Name);
    }

    /// <summary>
    /// Applies all transformations to the data.
    /// </summary>
    public async Task<TransformationResult> ApplyAsync(
        DataTable data,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var transformedCount = 0;

        foreach (var transformation in _transformations)
        {
            if (!transformation.IsApplicable(data))
            {
                _logger.LogDebug("Skipping transformation {Name} - not applicable", 
                    transformation.GetType().Name);
                continue;
            }

            try
            {
                await transformation.ApplyAsync(data, cancellationToken);
                transformedCount++;
                
                _logger.LogDebug("Applied transformation {Name} to column {Column}", 
                    transformation.GetType().Name, transformation.TargetColumn);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transformation {Name} failed", transformation.GetType().Name);
                throw;
            }
        }

        var duration = DateTime.UtcNow - startTime;
        return new TransformationResult(transformedCount, duration);
    }

    /// <summary>
    /// Generates SQL expression for transformation if supported.
    /// </summary>
    public string? GenerateSqlExpression(string column)
    {
        var transformation = _transformations
            .FirstOrDefault(t => t.TargetColumn == column && t is ISqlTransformation);

        return (transformation as ISqlTransformation)?.ToSqlExpression();
    }
}

public interface IColumnTransformation
{
    string TargetColumn { get; }
    bool IsApplicable(DataTable data);
    Task ApplyAsync(DataTable data, CancellationToken cancellationToken);
}

public interface ISqlTransformation
{
    string ToSqlExpression();
}

/// <summary>
/// Masks sensitive data by replacing with asterisks.
/// </summary>
public class MaskingTransformation : IColumnTransformation, ISqlTransformation
{
    public string TargetColumn { get; init; }
    public int VisibleCharacters { get; init; } = 0;
    public char MaskCharacter { get; init; } = '*';

    public MaskingTransformation(string column, int visibleChars = 0)
    {
        TargetColumn = column;
        VisibleCharacters = visibleChars;
    }

    public bool IsApplicable(DataTable data)
    {
        return data.Columns.Contains(TargetColumn);
    }

    public Task ApplyAsync(DataTable data, CancellationToken cancellationToken)
    {
        foreach (DataRow row in data.Rows)
        {
            if (row[TargetColumn] is string value && !string.IsNullOrEmpty(value))
            {
                var visible = Math.Min(VisibleCharacters, value.Length);
                var masked = value.Length > visible 
                    ? value[..visible] + new string(MaskCharacter, value.Length - visible)
                    : value;
                row[TargetColumn] = masked;
            }
        }

        return Task.CompletedTask;
    }

    public string ToSqlExpression()
    {
        if (VisibleCharacters == 0)
            return $"REPLICATE('{MaskCharacter}', LEN([{TargetColumn}]))";
        
        return $"LEFT([{TargetColumn}], {VisibleCharacters}) + REPLICATE('{MaskCharacter}', LEN([{TargetColumn}]) - {VisibleCharacters})";
    }
}

/// <summary>
/// Hashes column values using SHA256.
/// </summary>
public class HashingTransformation : IColumnTransformation, ISqlTransformation
{
    public string TargetColumn { get; init; }

    public HashingTransformation(string column)
    {
        TargetColumn = column;
    }

    public bool IsApplicable(DataTable data)
    {
        return data.Columns.Contains(TargetColumn);
    }

    public Task ApplyAsync(DataTable data, CancellationToken cancellationToken)
    {
        foreach (DataRow row in data.Rows)
        {
            if (row[TargetColumn] is string value && !string.IsNullOrEmpty(value))
            {
                row[TargetColumn] = ComputeHash(value);
            }
        }

        return Task.CompletedTask;
    }

    private static string ComputeHash(string value)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    public string ToSqlExpression()
    {
        return $"CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', CAST([{TargetColumn}] AS NVARCHAR(MAX))), 2)";
    }
}

/// <summary>
/// Applies regex replacement to column values.
/// </summary>
public class RegexTransformation : IColumnTransformation
{
    public string TargetColumn { get; init; }
    public string Pattern { get; init; }
    public string Replacement { get; init; }

    public RegexTransformation(string column, string pattern, string replacement)
    {
        TargetColumn = column;
        Pattern = pattern;
        Replacement = replacement;
    }

    public bool IsApplicable(DataTable data)
    {
        return data.Columns.Contains(TargetColumn);
    }

    public Task ApplyAsync(DataTable data, CancellationToken cancellationToken)
    {
        var regex = new System.Text.RegularExpressions.Regex(Pattern);
        
        foreach (DataRow row in data.Rows)
        {
            if (row[TargetColumn] is string value)
            {
                row[TargetColumn] = regex.Replace(value, Replacement);
            }
        }

        return Task.CompletedTask;
    }
}

public record TransformationResult(int TransformationsApplied, TimeSpan Duration);

public class DataTable
{
    public List<string> Columns { get; set; } = new();
    public List<DataRow> Rows { get; set; } = new();
}

public class DataRow
{
    private readonly Dictionary<string, object?> _values = new();

    public object? this[string column]
    {
        get => _values.TryGetValue(column, out var value) ? value : null;
        set => _values[column] = value;
    }
}
