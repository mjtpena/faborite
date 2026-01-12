using Microsoft.AspNetCore.SignalR.Client;
using Faborite.Web.Models;

namespace Faborite.Web.Services;

public class SyncHubService : IAsyncDisposable
{
    private HubConnection? _connection;
    private readonly string _hubUrl;

    public event Action<SyncProgressEvent>? OnProgress;
    public event Action<TableStartedEvent>? OnTableStarted;
    public event Action<TableCompletedEvent>? OnTableCompleted;
    public event Action<TableErrorEvent>? OnTableError;
    public event Action<SyncCompletedEvent>? OnSyncCompleted;
    public event Action<SyncSession>? OnSessionState;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public SyncHubService(IConfiguration configuration)
    {
        _hubUrl = (configuration["ApiBaseUrl"] ?? "https://localhost:5001") + "/hubs/sync";
    }

    public async Task ConnectAsync()
    {
        if (_connection != null)
            return;

        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _connection.On<SyncProgressEvent>("Progress", e => OnProgress?.Invoke(e));
        _connection.On<TableStartedEvent>("TableStarted", e => OnTableStarted?.Invoke(e));
        _connection.On<TableCompletedEvent>("TableCompleted", e => OnTableCompleted?.Invoke(e));
        _connection.On<TableErrorEvent>("TableError", e => OnTableError?.Invoke(e));
        _connection.On<SyncCompletedEvent>("SyncCompleted", e => OnSyncCompleted?.Invoke(e));
        _connection.On<SyncSession>("SessionState", s => OnSessionState?.Invoke(s));

        await _connection.StartAsync();
    }

    public async Task SubscribeToSessionAsync(string sessionId)
    {
        if (_connection == null)
            await ConnectAsync();

        await _connection!.InvokeAsync("SubscribeToSession", sessionId);
    }

    public async Task UnsubscribeFromSessionAsync(string sessionId)
    {
        if (_connection != null)
        {
            await _connection.InvokeAsync("UnsubscribeFromSession", sessionId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync();
        }
    }
}

public class SyncProgressEvent
{
    public string SessionId { get; set; } = "";
    public string TableName { get; set; } = "";
    public int Current { get; set; }
    public int Total { get; set; }
    public long RowsSynced { get; set; }
    public DateTime Timestamp { get; set; }
}

public class TableStartedEvent
{
    public string SessionId { get; set; } = "";
    public string TableName { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

public class TableCompletedEvent
{
    public string SessionId { get; set; } = "";
    public string TableName { get; set; } = "";
    public long RowsSynced { get; set; }
    public double Duration { get; set; }
    public DateTime Timestamp { get; set; }
}

public class TableErrorEvent
{
    public string SessionId { get; set; } = "";
    public string TableName { get; set; } = "";
    public string Error { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

public class SyncCompletedEvent
{
    public string SessionId { get; set; } = "";
    public bool Success { get; set; }
    public int TablesCompleted { get; set; }
    public long TotalRows { get; set; }
    public double Duration { get; set; }
    public DateTime Timestamp { get; set; }
}
