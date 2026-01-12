namespace Faborite.Web.Models;

public class ConnectionStatus
{
    public bool IsConnected { get; set; }
    public string? WorkspaceId { get; set; }
    public string? LakehouseId { get; set; }
}

public class TableInfo
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Format { get; set; } = "";
    public long? RowCount { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime? LastModified { get; set; }
    public List<ColumnInfo>? Columns { get; set; }
}

public class ColumnInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Nullable { get; set; }
}

public class SyncSession
{
    public string SessionId { get; set; } = "";
    public string WorkspaceId { get; set; } = "";
    public string LakehouseId { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? CurrentTable { get; set; }
    public int TablesCompleted { get; set; }
    public int TotalTables { get; set; }
    public long TotalRowsSynced { get; set; }
    public Dictionary<string, TableProgress> TableProgress { get; set; } = new();
}

public class TableProgress
{
    public string TableName { get; set; } = "";
    public string Status { get; set; } = "";
    public long RowsSynced { get; set; }
    public string? Error { get; set; }
}

public class LocalTableInfo
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public List<LocalFileInfo> Files { get; set; } = new();
    public bool HasSchema { get; set; }
    public long TotalSizeBytes { get; set; }
}

public class LocalFileInfo
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Extension { get; set; } = "";
    public DateTime? LastModified { get; set; }
}

public class QueryResult
{
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public int RowCount { get; set; }
    public bool Truncated { get; set; }
}

public class FaboriteConfig
{
    public string? WorkspaceId { get; set; }
    public string? WorkspaceName { get; set; }
    public string? LakehouseId { get; set; }
    public string? LakehouseName { get; set; }
    public SampleConfig Sample { get; set; } = new();
    public FormatConfig Format { get; set; } = new();
    public SyncConfig Sync { get; set; } = new();
    public AuthConfig Auth { get; set; } = new();
    public Dictionary<string, TableOverride> Tables { get; set; } = new();
}

public class SampleConfig
{
    public string Strategy { get; set; } = "random";
    public int Rows { get; set; } = 10000;
    public string? DateColumn { get; set; }
    public string? StratifyColumn { get; set; }
    public string? WhereClause { get; set; }
    public int Seed { get; set; } = 42;
    public bool AutoDetectDate { get; set; } = true;
    public int MaxFullTableRows { get; set; } = 50000;
}

public class FormatConfig
{
    public string Format { get; set; } = "parquet";
    public string Compression { get; set; } = "snappy";
    public string? PartitionBy { get; set; }
    public bool SingleFile { get; set; } = true;
}

public class SyncConfig
{
    public string LocalPath { get; set; } = "./local_lakehouse";
    public bool Overwrite { get; set; } = true;
    public bool IncludeSchema { get; set; } = true;
    public int ParallelTables { get; set; } = 4;
    public List<string>? SkipTables { get; set; }
    public List<string>? IncludeTables { get; set; }
}

public class AuthConfig
{
    public string Method { get; set; } = "Default";
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}

public class TableOverride
{
    public SampleConfig? Sample { get; set; }
    public FormatConfig? Format { get; set; }
}
