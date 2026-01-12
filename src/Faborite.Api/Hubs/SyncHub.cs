using Microsoft.AspNetCore.SignalR;
using Faborite.Api.Services;

namespace Faborite.Api.Hubs;

/// <summary>
/// SignalR hub for real-time sync progress updates.
/// </summary>
public class SyncHub : Hub
{
    private readonly SyncProgressService _progressService;

    public SyncHub(SyncProgressService progressService)
    {
        _progressService = progressService;
    }

    /// <summary>
    /// Subscribe to updates for a specific sync session.
    /// </summary>
    public async Task SubscribeToSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        
        // Send current state
        var session = _progressService.GetSession(sessionId);
        if (session != null)
        {
            await Clients.Caller.SendAsync("SessionState", session);
        }
    }

    /// <summary>
    /// Unsubscribe from session updates.
    /// </summary>
    public async Task UnsubscribeFromSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
    }

    /// <summary>
    /// Get current session state.
    /// </summary>
    public SyncSession? GetSessionState(string sessionId)
    {
        return _progressService.GetSession(sessionId);
    }
}

/// <summary>
/// Extension methods for broadcasting sync progress.
/// </summary>
public static class SyncHubExtensions
{
    public static async Task BroadcastProgress(
        this IHubContext<SyncHub> hubContext,
        string sessionId,
        string tableName,
        int current,
        int total,
        long rowsSynced)
    {
        await hubContext.Clients.Group(sessionId).SendAsync("Progress", new
        {
            SessionId = sessionId,
            TableName = tableName,
            Current = current,
            Total = total,
            RowsSynced = rowsSynced,
            Timestamp = DateTime.UtcNow
        });
    }

    public static async Task BroadcastTableStarted(
        this IHubContext<SyncHub> hubContext,
        string sessionId,
        string tableName)
    {
        await hubContext.Clients.Group(sessionId).SendAsync("TableStarted", new
        {
            SessionId = sessionId,
            TableName = tableName,
            Timestamp = DateTime.UtcNow
        });
    }

    public static async Task BroadcastTableCompleted(
        this IHubContext<SyncHub> hubContext,
        string sessionId,
        string tableName,
        long rowsSynced,
        TimeSpan duration)
    {
        await hubContext.Clients.Group(sessionId).SendAsync("TableCompleted", new
        {
            SessionId = sessionId,
            TableName = tableName,
            RowsSynced = rowsSynced,
            Duration = duration.TotalSeconds,
            Timestamp = DateTime.UtcNow
        });
    }

    public static async Task BroadcastTableError(
        this IHubContext<SyncHub> hubContext,
        string sessionId,
        string tableName,
        string error)
    {
        await hubContext.Clients.Group(sessionId).SendAsync("TableError", new
        {
            SessionId = sessionId,
            TableName = tableName,
            Error = error,
            Timestamp = DateTime.UtcNow
        });
    }

    public static async Task BroadcastSyncCompleted(
        this IHubContext<SyncHub> hubContext,
        string sessionId,
        bool success,
        int tablesCompleted,
        long totalRows,
        TimeSpan duration)
    {
        await hubContext.Clients.Group(sessionId).SendAsync("SyncCompleted", new
        {
            SessionId = sessionId,
            Success = success,
            TablesCompleted = tablesCompleted,
            TotalRows = totalRows,
            Duration = duration.TotalSeconds,
            Timestamp = DateTime.UtcNow
        });
    }
}
