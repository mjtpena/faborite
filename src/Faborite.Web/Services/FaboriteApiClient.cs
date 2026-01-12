using System.Net.Http.Json;
using Faborite.Web.Models;

namespace Faborite.Web.Services;

public class FaboriteApiClient
{
    private readonly HttpClient _http;

    public FaboriteApiClient(HttpClient http)
    {
        _http = http;
    }

    // Auth
    public async Task<(bool Success, string Message)> ConnectAsync(string workspaceId, string lakehouseId, string authMethod = "Default")
    {
        var response = await _http.PostAsJsonAsync("/api/auth/connect", new
        {
            WorkspaceId = workspaceId,
            LakehouseId = lakehouseId,
            AuthMethod = authMethod
        });

        var result = await response.Content.ReadFromJsonAsync<ConnectResponse>();
        return (result?.Connected ?? false, result?.Message ?? "Unknown error");
    }

    public async Task DisconnectAsync()
    {
        await _http.PostAsync("/api/auth/disconnect", null);
    }

    public async Task<ConnectionStatus> GetConnectionStatusAsync()
    {
        return await _http.GetFromJsonAsync<ConnectionStatus>("/api/auth/status") ?? new();
    }

    // Tables
    public async Task<List<TableInfo>> GetTablesAsync()
    {
        return await _http.GetFromJsonAsync<List<TableInfo>>("/api/tables") ?? new();
    }

    public async Task<TableInfo?> GetTableSchemaAsync(string tableName)
    {
        return await _http.GetFromJsonAsync<TableInfo>($"/api/tables/{tableName}/schema");
    }

    // Sync
    public async Task<SyncStartResponse> StartSyncAsync(SyncRequest request)
    {
        var response = await _http.PostAsJsonAsync("/api/sync/start", request);
        return await response.Content.ReadFromJsonAsync<SyncStartResponse>() ?? new();
    }

    public async Task<SyncSession?> GetSyncStatusAsync(string sessionId)
    {
        return await _http.GetFromJsonAsync<SyncSession>($"/api/sync/status/{sessionId}");
    }

    public async Task CancelSyncAsync(string sessionId)
    {
        await _http.PostAsync($"/api/sync/cancel/{sessionId}", null);
    }

    public async Task<List<SyncSession>> GetSyncHistoryAsync(int count = 10)
    {
        return await _http.GetFromJsonAsync<List<SyncSession>>($"/api/sync/history?count={count}") ?? new();
    }

    // Config
    public async Task<ConfigResponse> GetConfigAsync(string? path = null)
    {
        var url = string.IsNullOrEmpty(path) ? "/api/config" : $"/api/config?path={path}";
        return await _http.GetFromJsonAsync<ConfigResponse>(url) ?? new();
    }

    public async Task<bool> SaveConfigAsync(FaboriteConfig config, string? path = null)
    {
        var response = await _http.PostAsJsonAsync("/api/config", new { Config = config, Path = path });
        return response.IsSuccessStatusCode;
    }

    public async Task<ValidationResult> ValidateConfigAsync(FaboriteConfig config)
    {
        var response = await _http.PostAsJsonAsync("/api/config/validate", config);
        return await response.Content.ReadFromJsonAsync<ValidationResult>() ?? new();
    }

    // Local Data
    public async Task<LocalDataResponse> GetLocalDataAsync(string? path = null)
    {
        var url = string.IsNullOrEmpty(path) ? "/api/local" : $"/api/local?path={path}";
        return await _http.GetFromJsonAsync<LocalDataResponse>(url) ?? new();
    }

    public async Task<bool> DeleteLocalTableAsync(string tableName)
    {
        var response = await _http.DeleteAsync($"/api/local/{tableName}");
        return response.IsSuccessStatusCode;
    }

    // Query
    public async Task<QueryResult> ExecuteQueryAsync(string sql, int maxRows = 1000)
    {
        var response = await _http.PostAsJsonAsync("/api/query", new { Sql = sql, MaxRows = maxRows });
        return await response.Content.ReadFromJsonAsync<QueryResult>() ?? new();
    }

    public async Task<QueryResult> PreviewTableAsync(string tableName, int rows = 100)
    {
        var response = await _http.PostAsync($"/api/query/preview/{tableName}?rows={rows}", null);
        return await response.Content.ReadFromJsonAsync<QueryResult>() ?? new();
    }

    public async Task<List<string>> GetQueryableTablesAsync()
    {
        var result = await _http.GetFromJsonAsync<TablesResponse>("/api/query/tables");
        return result?.Tables ?? new();
    }
}

public class ConnectResponse
{
    public bool Connected { get; set; }
    public string? Message { get; set; }
}

public class SyncStartResponse
{
    public string? SessionId { get; set; }
    public string? Message { get; set; }
    public string? StatusUrl { get; set; }
}

public class SyncRequest
{
    public string[]? Tables { get; set; }
    public SampleConfigDto? SampleConfig { get; set; }
    public FormatConfigDto? FormatConfig { get; set; }
}

public class SampleConfigDto
{
    public string? Strategy { get; set; }
    public int? Rows { get; set; }
    public string? DateColumn { get; set; }
}

public class FormatConfigDto
{
    public string? Format { get; set; }
    public string? Compression { get; set; }
}

public class ConfigResponse
{
    public bool Exists { get; set; }
    public string? Path { get; set; }
    public FaboriteConfig? Config { get; set; }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class LocalDataResponse
{
    public bool Exists { get; set; }
    public string? Path { get; set; }
    public List<LocalTableInfo> Tables { get; set; } = new();
    public int TotalTables { get; set; }
    public long TotalSizeBytes { get; set; }
}

public class TablesResponse
{
    public List<string> Tables { get; set; } = new();
}
