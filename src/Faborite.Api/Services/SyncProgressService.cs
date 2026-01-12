using System.Collections.Concurrent;

namespace Faborite.Api.Services;

/// <summary>
/// Manages sync progress state for SignalR broadcasting.
/// </summary>
public class SyncProgressService
{
    private readonly ConcurrentDictionary<string, SyncSession> _sessions = new();

    public string CreateSession(string workspaceId, string lakehouseId)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var session = new SyncSession
        {
            SessionId = sessionId,
            WorkspaceId = workspaceId,
            LakehouseId = lakehouseId,
            Status = SyncStatus.Pending,
            StartTime = DateTime.UtcNow
        };
        _sessions[sessionId] = session;
        return sessionId;
    }

    public SyncSession? GetSession(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    public void UpdateProgress(string sessionId, string tableName, int current, int total, long rowsSynced)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.CurrentTable = tableName;
            session.TablesCompleted = current;
            session.TotalTables = total;
            session.TotalRowsSynced += rowsSynced;
            session.Status = SyncStatus.InProgress;
            
            session.TableProgress[tableName] = new TableProgress
            {
                TableName = tableName,
                Status = TableSyncStatus.Completed,
                RowsSynced = rowsSynced
            };
        }
    }

    public void SetTableInProgress(string sessionId, string tableName)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.CurrentTable = tableName;
            session.TableProgress[tableName] = new TableProgress
            {
                TableName = tableName,
                Status = TableSyncStatus.InProgress
            };
        }
    }

    public void SetTableError(string sessionId, string tableName, string error)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.TableProgress[tableName] = new TableProgress
            {
                TableName = tableName,
                Status = TableSyncStatus.Failed,
                Error = error
            };
        }
    }

    public void CompleteSession(string sessionId, bool success)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.Status = success ? SyncStatus.Completed : SyncStatus.Failed;
            session.EndTime = DateTime.UtcNow;
        }
    }

    public IEnumerable<SyncSession> GetRecentSessions(int count = 10)
    {
        return _sessions.Values
            .OrderByDescending(s => s.StartTime)
            .Take(count);
    }
}

public class SyncSession
{
    public string SessionId { get; set; } = "";
    public string WorkspaceId { get; set; } = "";
    public string LakehouseId { get; set; } = "";
    public SyncStatus Status { get; set; }
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
    public TableSyncStatus Status { get; set; }
    public long RowsSynced { get; set; }
    public string? Error { get; set; }
}

public enum SyncStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled
}

public enum TableSyncStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Skipped
}
