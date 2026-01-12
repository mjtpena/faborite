using Faborite.Core;
using Faborite.Core.Configuration;
using Faborite.Core.OneLake;

namespace Faborite.Api.Services;

/// <summary>
/// Service layer for API operations, wrapping FaboriteService functionality.
/// </summary>
public class FaboriteApiService : IDisposable
{
    private FaboriteService? _service;
    private FaboriteConfig? _currentConfig;

    public bool IsConnected => _service != null;
    public FaboriteConfig? CurrentConfig => _currentConfig;

    public async Task<bool> ConnectAsync(FaboriteConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate configuration first
            var validationResult = ConfigValidator.Validate(config);
            if (!validationResult.IsValid)
            {
                throw new InvalidOperationException(
                    $"Configuration validation failed: {string.Join(", ", validationResult.Errors)}");
            }

            _currentConfig = config;
            _service = new FaboriteService(config);
            return await _service.TestConnectionAsync(cancellationToken);
        }
        catch
        {
            _service?.Dispose();
            _service = null;
            _currentConfig = null;
            return false;
        }
    }

    public void Disconnect()
    {
        _service?.Dispose();
        _service = null;
        _currentConfig = null;
    }

    public async Task<List<LakehouseTable>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        return await _service!.ListTablesAsync(cancellationToken);
    }

    public async Task<SyncSummary> SyncAsync(
        IEnumerable<string>? tables = null,
        SampleConfig? sampleOverride = null,
        FormatConfig? formatOverride = null,
        IProgress<(string tableName, int current, int total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        return await _service!.SyncAsync(tables, sampleOverride, formatOverride, progress, cancellationToken);
    }

    public async Task<TableSyncResult> SyncTableAsync(
        string tableName,
        string outputPath,
        SampleConfig? sampleConfig = null,
        FormatConfig? formatConfig = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        return await _service!.SyncTableAsync(tableName, outputPath, sampleConfig, formatConfig, cancellationToken);
    }

    private void EnsureConnected()
    {
        if (_service == null)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
    }

    public void Dispose()
    {
        _service?.Dispose();
    }
}
