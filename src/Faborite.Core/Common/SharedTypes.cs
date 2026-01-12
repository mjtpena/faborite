namespace Faborite.Core.Common;

/// <summary>
/// Shared enums and common types.
/// </summary>

public enum Severity { Low, Medium, High, Critical }

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
