using Blazored.LocalStorage;
using Faborite.Web.Models;

namespace Faborite.Web.Services;

public class AppStateService
{
    private readonly ILocalStorageService _localStorage;

    public event Action? OnChange;

    public bool IsDarkMode { get; private set; } = true;
    public ConnectionStatus ConnectionStatus { get; private set; } = new();
    public string? LastWorkspaceId { get; private set; }
    public string? LastLakehouseId { get; private set; }
    public string? ActiveSyncSessionId { get; private set; }

    public AppStateService(ILocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }

    public async Task InitializeAsync()
    {
        IsDarkMode = await _localStorage.GetItemAsync<bool?>("darkMode") ?? true;
        LastWorkspaceId = await _localStorage.GetItemAsync<string>("lastWorkspaceId");
        LastLakehouseId = await _localStorage.GetItemAsync<string>("lastLakehouseId");
    }

    public void SetConnectionStatus(ConnectionStatus status)
    {
        ConnectionStatus = status;
        NotifyStateChanged();
    }

    public void SetActiveSyncSession(string? sessionId)
    {
        ActiveSyncSessionId = sessionId;
        NotifyStateChanged();
    }

    public async Task SetLastConnectionAsync(string workspaceId, string lakehouseId)
    {
        LastWorkspaceId = workspaceId;
        LastLakehouseId = lakehouseId;
        await _localStorage.SetItemAsync("lastWorkspaceId", workspaceId);
        await _localStorage.SetItemAsync("lastLakehouseId", lakehouseId);
    }

    public async Task ToggleDarkMode()
    {
        IsDarkMode = !IsDarkMode;
        await _localStorage.SetItemAsync("darkMode", IsDarkMode);
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
