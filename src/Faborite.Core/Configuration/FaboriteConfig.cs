namespace Faborite.Core.Configuration;

/// <summary>
/// Main configuration for Faborite.
/// </summary>
public class FaboriteConfig
{
    /// <summary>
    /// Fabric workspace ID (GUID).
    /// </summary>
    public string? WorkspaceId { get; set; }
    
    /// <summary>
    /// Fabric workspace name (alternative to ID).
    /// </summary>
    public string? WorkspaceName { get; set; }
    
    /// <summary>
    /// Lakehouse ID (GUID).
    /// </summary>
    public string? LakehouseId { get; set; }
    
    /// <summary>
    /// Lakehouse name (alternative to ID).
    /// </summary>
    public string? LakehouseName { get; set; }
    
    /// <summary>
    /// Sampling configuration.
    /// </summary>
    public SampleConfig Sample { get; set; } = new();
    
    /// <summary>
    /// Output format configuration.
    /// </summary>
    public FormatConfig Format { get; set; } = new();
    
    /// <summary>
    /// Sync behavior configuration.
    /// </summary>
    public SyncConfig Sync { get; set; } = new();
    
    /// <summary>
    /// Authentication configuration.
    /// </summary>
    public AuthConfig Auth { get; set; } = new();
    
    /// <summary>
    /// Per-table configuration overrides.
    /// </summary>
    public Dictionary<string, TableOverride> Tables { get; set; } = new();
}

/// <summary>
/// Sampling strategy.
/// </summary>
public enum SampleStrategy
{
    /// <summary>Random N rows.</summary>
    Random,
    
    /// <summary>Most recent N rows by date column.</summary>
    Recent,
    
    /// <summary>First N rows.</summary>
    Head,
    
    /// <summary>Last N rows.</summary>
    Tail,
    
    /// <summary>Preserve distribution of a column.</summary>
    Stratified,
    
    /// <summary>Custom SQL WHERE clause.</summary>
    Query,
    
    /// <summary>All rows (no sampling).</summary>
    Full
}

/// <summary>
/// Output format.
/// </summary>
public enum OutputFormat
{
    /// <summary>Apache Parquet.</summary>
    Parquet,
    
    /// <summary>Delta Lake.</summary>
    Delta,
    
    /// <summary>CSV.</summary>
    Csv,
    
    /// <summary>JSON Lines.</summary>
    Json,
    
    /// <summary>DuckDB database file.</summary>
    DuckDb
}

/// <summary>
/// Sampling configuration.
/// </summary>
public class SampleConfig
{
    /// <summary>
    /// Sampling strategy to use.
    /// </summary>
    public SampleStrategy Strategy { get; set; } = SampleStrategy.Random;
    
    /// <summary>
    /// Number of rows to sample.
    /// </summary>
    public int Rows { get; set; } = 10000;
    
    /// <summary>
    /// Date column for 'Recent' strategy.
    /// </summary>
    public string? DateColumn { get; set; }
    
    /// <summary>
    /// Column to stratify by for 'Stratified' strategy.
    /// </summary>
    public string? StratifyColumn { get; set; }
    
    /// <summary>
    /// Custom WHERE clause for 'Query' strategy.
    /// </summary>
    public string? WhereClause { get; set; }
    
    /// <summary>
    /// Random seed for reproducible sampling.
    /// </summary>
    public int Seed { get; set; } = 42;
    
    /// <summary>
    /// Auto-detect date column for 'Recent' strategy.
    /// </summary>
    public bool AutoDetectDate { get; set; } = true;
    
    /// <summary>
    /// Tables smaller than this are pulled in full.
    /// </summary>
    public int MaxFullTableRows { get; set; } = 50000;
}

/// <summary>
/// Output format configuration.
/// </summary>
public class FormatConfig
{
    /// <summary>
    /// Output format for local files.
    /// </summary>
    public OutputFormat Format { get; set; } = OutputFormat.Parquet;
    
    /// <summary>
    /// Compression codec.
    /// </summary>
    public string Compression { get; set; } = "snappy";
    
    /// <summary>
    /// Columns to partition by.
    /// </summary>
    public List<string>? PartitionBy { get; set; }
    
    /// <summary>
    /// Write as single file vs partitioned directory.
    /// </summary>
    public bool SingleFile { get; set; } = true;
}

/// <summary>
/// Sync behavior configuration.
/// </summary>
public class SyncConfig
{
    /// <summary>
    /// Local directory to store synced data.
    /// </summary>
    public string LocalPath { get; set; } = "./local_lakehouse";
    
    /// <summary>
    /// Overwrite existing local files.
    /// </summary>
    public bool Overwrite { get; set; } = true;
    
    /// <summary>
    /// Save table schema alongside data.
    /// </summary>
    public bool IncludeSchema { get; set; } = true;
    
    /// <summary>
    /// Number of tables to sync in parallel.
    /// </summary>
    public int ParallelTables { get; set; } = 4;
    
    /// <summary>
    /// Table names to skip.
    /// </summary>
    public List<string> SkipTables { get; set; } = new();
    
    /// <summary>
    /// Only sync these tables (null = all).
    /// </summary>
    public List<string>? IncludeTables { get; set; }
}

/// <summary>
/// Authentication method.
/// </summary>
public enum AuthMethod
{
    /// <summary>Use DefaultAzureCredential (tries multiple methods).</summary>
    Default,
    
    /// <summary>Use Azure CLI credentials.</summary>
    AzureCli,
    
    /// <summary>Use service principal credentials.</summary>
    ServicePrincipal,
    
    /// <summary>Use managed identity.</summary>
    ManagedIdentity
}

/// <summary>
/// Authentication configuration.
/// </summary>
public class AuthConfig
{
    /// <summary>
    /// Auth method: Default, AzureCli, ServicePrincipal, ManagedIdentity.
    /// </summary>
    public AuthMethod Method { get; set; } = AuthMethod.Default;
    
    /// <summary>
    /// Azure tenant ID (for service principal).
    /// </summary>
    public string? TenantId { get; set; }
    
    /// <summary>
    /// Azure client ID (for service principal).
    /// </summary>
    public string? ClientId { get; set; }
    
    /// <summary>
    /// Azure client secret (for service principal).
    /// </summary>
    public string? ClientSecret { get; set; }
}

/// <summary>
/// Per-table configuration overrides.
/// </summary>
public class TableOverride
{
    /// <summary>
    /// Override sampling config for this table.
    /// </summary>
    public SampleConfig? Sample { get; set; }
    
    /// <summary>
    /// Override format config for this table.
    /// </summary>
    public FormatConfig? Format { get; set; }
}
