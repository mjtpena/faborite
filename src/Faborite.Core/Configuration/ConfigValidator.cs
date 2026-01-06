namespace Faborite.Core.Configuration;

/// <summary>
/// Result of configuration validation.
/// </summary>
public class ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();

    public void AddError(string error) => Errors.Add(error);
    public void AddWarning(string warning) => Warnings.Add(warning);
}

/// <summary>
/// Validates Faborite configuration.
/// </summary>
public static class ConfigValidator
{
    /// <summary>
    /// Validate the configuration and return any errors/warnings.
    /// </summary>
    public static ValidationResult Validate(FaboriteConfig config)
    {
        var result = new ValidationResult();

        ValidateIdentifiers(config, result);
        ValidateSampleConfig(config.Sample, result);
        ValidateSyncConfig(config.Sync, result);
        ValidateAuthConfig(config.Auth, result);
        ValidateTableOverrides(config.Tables, result);

        return result;
    }

    private static void ValidateIdentifiers(FaboriteConfig config, ValidationResult result)
    {
        // Workspace: need either ID or Name
        if (string.IsNullOrWhiteSpace(config.WorkspaceId) && string.IsNullOrWhiteSpace(config.WorkspaceName))
        {
            result.AddError("Either WorkspaceId or WorkspaceName must be specified.");
        }
        else if (!string.IsNullOrWhiteSpace(config.WorkspaceId) && !IsValidGuid(config.WorkspaceId))
        {
            result.AddError($"WorkspaceId '{config.WorkspaceId}' is not a valid GUID.");
        }

        // Lakehouse: need either ID or Name
        if (string.IsNullOrWhiteSpace(config.LakehouseId) && string.IsNullOrWhiteSpace(config.LakehouseName))
        {
            result.AddError("Either LakehouseId or LakehouseName must be specified.");
        }
        else if (!string.IsNullOrWhiteSpace(config.LakehouseId) && !IsValidGuid(config.LakehouseId))
        {
            result.AddError($"LakehouseId '{config.LakehouseId}' is not a valid GUID.");
        }
    }

    private static void ValidateSampleConfig(SampleConfig sample, ValidationResult result)
    {
        if (sample.Rows <= 0)
        {
            result.AddError("Sample.Rows must be a positive integer.");
        }

        if (sample.MaxFullTableRows < 0)
        {
            result.AddError("Sample.MaxFullTableRows cannot be negative.");
        }

        switch (sample.Strategy)
        {
            case SampleStrategy.Recent:
                if (string.IsNullOrWhiteSpace(sample.DateColumn) && !sample.AutoDetectDate)
                {
                    result.AddWarning("Sample.DateColumn is not set and AutoDetectDate is disabled. Recent strategy may not work correctly.");
                }
                break;

            case SampleStrategy.Stratified:
                if (string.IsNullOrWhiteSpace(sample.StratifyColumn))
                {
                    result.AddError("Sample.StratifyColumn is required when using Stratified strategy.");
                }
                break;

            case SampleStrategy.Query:
                if (string.IsNullOrWhiteSpace(sample.WhereClause))
                {
                    result.AddError("Sample.WhereClause is required when using Query strategy.");
                }
                break;
        }
    }

    private static void ValidateSyncConfig(SyncConfig sync, ValidationResult result)
    {
        if (sync.ParallelTables <= 0)
        {
            result.AddError("Sync.ParallelTables must be a positive integer.");
        }

        if (sync.ParallelTables > 16)
        {
            result.AddWarning("Sync.ParallelTables is set to a high value. This may cause rate limiting or memory issues.");
        }

        if (string.IsNullOrWhiteSpace(sync.LocalPath))
        {
            result.AddError("Sync.LocalPath cannot be empty.");
        }
    }

    private static void ValidateAuthConfig(AuthConfig auth, ValidationResult result)
    {
        if (auth.Method == AuthMethod.ServicePrincipal)
        {
            if (string.IsNullOrWhiteSpace(auth.TenantId))
            {
                result.AddError("Auth.TenantId is required when using ServicePrincipal authentication.");
            }
            else if (!IsValidGuid(auth.TenantId))
            {
                result.AddError($"Auth.TenantId '{auth.TenantId}' is not a valid GUID.");
            }

            if (string.IsNullOrWhiteSpace(auth.ClientId))
            {
                result.AddError("Auth.ClientId is required when using ServicePrincipal authentication.");
            }
            else if (!IsValidGuid(auth.ClientId))
            {
                result.AddError($"Auth.ClientId '{auth.ClientId}' is not a valid GUID.");
            }

            if (string.IsNullOrWhiteSpace(auth.ClientSecret))
            {
                result.AddError("Auth.ClientSecret is required when using ServicePrincipal authentication.");
            }
        }
    }

    private static void ValidateTableOverrides(Dictionary<string, TableOverride> tables, ValidationResult result)
    {
        foreach (var (tableName, tableOverride) in tables)
        {
            if (string.IsNullOrWhiteSpace(tableName))
            {
                result.AddError("Table override key cannot be empty.");
                continue;
            }

            if (tableOverride.Sample != null)
            {
                if (tableOverride.Sample.Rows <= 0)
                {
                    result.AddError($"Table '{tableName}': Sample.Rows must be positive.");
                }

                if (tableOverride.Sample.Strategy == SampleStrategy.Stratified && 
                    string.IsNullOrWhiteSpace(tableOverride.Sample.StratifyColumn))
                {
                    result.AddError($"Table '{tableName}': StratifyColumn is required for Stratified strategy.");
                }

                if (tableOverride.Sample.Strategy == SampleStrategy.Query && 
                    string.IsNullOrWhiteSpace(tableOverride.Sample.WhereClause))
                {
                    result.AddError($"Table '{tableName}': WhereClause is required for Query strategy.");
                }
            }
        }
    }

    private static bool IsValidGuid(string value)
    {
        return Guid.TryParse(value, out _);
    }
}
