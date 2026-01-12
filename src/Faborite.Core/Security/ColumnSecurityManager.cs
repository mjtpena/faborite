using Faborite.Core.Common;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Security;

/// <summary>
/// Implements column-level security with fine-grained access control.
/// Issue #46
/// </summary>
public class ColumnSecurityManager
{
    private readonly ILogger<ColumnSecurityManager> _logger;
    private readonly Dictionary<string, ColumnPolicy> _policies = new();

    public ColumnSecurityManager(ILogger<ColumnSecurityManager> logger)
    {
        _logger = logger;
    }

    public void AddPolicy(string column, ColumnPolicy policy)
    {
        _policies[column] = policy;
        _logger.LogDebug("Added security policy for column {Column}", column);
    }

    public List<string> FilterColumns(List<string> columns, UserContext user)
    {
        var allowed = columns.Where(col => 
        {
            if (!_policies.TryGetValue(col, out var policy))
                return true; // No policy = allowed
            
            return policy.AllowedRoles.Any(role => user.Roles.Contains(role));
        }).ToList();

        _logger.LogDebug("User {User} has access to {Count}/{Total} columns", 
            user.Username, allowed.Count, columns.Count);
        
        return allowed;
    }

    public TableData ApplySecurity(TableData data, UserContext user)
    {
        var allowedColumns = FilterColumns(data.Columns, user);
        var securedData = new TableData
        {
            Columns = allowedColumns,
            Rows = data.Rows.Select(row =>
            {
                var securedRow = new Dictionary<string, object?>();
                foreach (var col in allowedColumns)
                {
                    if (row.TryGetValue(col, out var value))
                    {
                        // Apply masking if required
                        if (_policies.TryGetValue(col, out var policy) && policy.MaskingRequired)
                        {
                            securedRow[col] = MaskValue(value, policy.MaskingStrategy);
                        }
                        else
                        {
                            securedRow[col] = value;
                        }
                    }
                }
                return securedRow;
            }).ToList()
        };

        return securedData;
    }

    private object? MaskValue(object? value, MaskingStrategy strategy)
    {
        if (value == null) return null;
        
        return strategy switch
        {
            MaskingStrategy.Full => "***",
            MaskingStrategy.Partial => MaskPartial(value.ToString() ?? ""),
            MaskingStrategy.Hash => ComputeHash(value.ToString() ?? ""),
            _ => value
        };
    }

    private string MaskPartial(string value)
    {
        if (value.Length <= 4) return "***";
        return value[..2] + new string('*', value.Length - 4) + value[^2..];
    }

    private string ComputeHash(string value)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash)[..16];
    }
}

public record ColumnPolicy(
    List<string> AllowedRoles,
    bool MaskingRequired = false,
    MaskingStrategy MaskingStrategy = MaskingStrategy.None);

public record UserContext(string Username, List<string> Roles);

public enum MaskingStrategy { None, Full, Partial, Hash }
