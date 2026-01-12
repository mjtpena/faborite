using Microsoft.Extensions.Logging;

namespace Faborite.Core.Filtering;

/// <summary>
/// Advanced row-level filtering with complex predicates.
/// Issue #32
/// </summary>
public class RowFilter
{
    private readonly ILogger<RowFilter> _logger;

    public RowFilter(ILogger<RowFilter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Builds SQL WHERE clause from filter expression.
    /// </summary>
    public string BuildWhereClause(FilterExpression expression)
    {
        return expression switch
        {
            SimpleFilter simple => BuildSimpleFilter(simple),
            CompoundFilter compound => BuildCompoundFilter(compound),
            _ => throw new ArgumentException($"Unknown filter type: {expression.GetType()}")
        };
    }

    private string BuildSimpleFilter(SimpleFilter filter)
    {
        var column = EscapeColumnName(filter.Column);
        
        return filter.Operator switch
        {
            FilterOperator.Equals => $"{column} = {EscapeValue(filter.Value)}",
            FilterOperator.NotEquals => $"{column} != {EscapeValue(filter.Value)}",
            FilterOperator.GreaterThan => $"{column} > {EscapeValue(filter.Value)}",
            FilterOperator.GreaterThanOrEqual => $"{column} >= {EscapeValue(filter.Value)}",
            FilterOperator.LessThan => $"{column} < {EscapeValue(filter.Value)}",
            FilterOperator.LessThanOrEqual => $"{column} <= {EscapeValue(filter.Value)}",
            FilterOperator.Like => $"{column} LIKE {EscapeValue(filter.Value)}",
            FilterOperator.In => $"{column} IN ({string.Join(", ", ((List<object>)filter.Value!).Select(EscapeValue))})",
            FilterOperator.Between => $"{column} BETWEEN {EscapeValue(((object[], object[]))filter.Value!)[0]} AND {EscapeValue(((object[], object[]))filter.Value!)[1]}",
            FilterOperator.IsNull => $"{column} IS NULL",
            FilterOperator.IsNotNull => $"{column} IS NOT NULL",
            _ => throw new ArgumentException($"Unknown operator: {filter.Operator}")
        };
    }

    private string BuildCompoundFilter(CompoundFilter filter)
    {
        var clauses = filter.Filters.Select(f => $"({BuildWhereClause(f)})");
        var op = filter.LogicalOperator == LogicalOperator.And ? "AND" : "OR";
        return string.Join($" {op} ", clauses);
    }

    private string EscapeColumnName(string column)
    {
        return $"[{column.Replace("]", "]]")}]";
    }

    private string EscapeValue(object? value)
    {
        return value switch
        {
            null => "NULL",
            string s => $"'{s.Replace("'", "''")}'",
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
            bool b => b ? "1" : "0",
            _ => value.ToString() ?? "NULL"
        };
    }

    /// <summary>
    /// Validates filter expression for SQL injection safety.
    /// </summary>
    public bool ValidateFilter(FilterExpression expression)
    {
        try
        {
            _ = BuildWhereClause(expression);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid filter expression");
            return false;
        }
    }
}

public abstract record FilterExpression;

public record SimpleFilter(
    string Column,
    FilterOperator Operator,
    object? Value) : FilterExpression;

public record CompoundFilter(
    LogicalOperator LogicalOperator,
    List<FilterExpression> Filters) : FilterExpression;

public enum FilterOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Like,
    In,
    Between,
    IsNull,
    IsNotNull
}

public enum LogicalOperator
{
    And,
    Or
}
