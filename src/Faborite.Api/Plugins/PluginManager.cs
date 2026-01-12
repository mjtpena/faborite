using Microsoft.Extensions.Logging;

namespace Faborite.Api.Plugins;

/// <summary>
/// Plugin system for extending Faborite with custom functionality.
/// Issue #68
/// </summary>
public interface IFaboritePlugin
{
    string Name { get; }
    string Version { get; }
    Task InitializeAsync(PluginContext context, CancellationToken cancellationToken);
    Task ExecuteAsync(PluginExecutionContext context, CancellationToken cancellationToken);
}

public class PluginManager
{
    private readonly ILogger<PluginManager> _logger;
    private readonly Dictionary<string, IFaboritePlugin> _plugins = new();

    public PluginManager(ILogger<PluginManager> logger)
    {
        _logger = logger;
    }

    public void RegisterPlugin(IFaboritePlugin plugin)
    {
        _plugins[plugin.Name] = plugin;
        _logger.LogInformation("Registered plugin: {Name} v{Version}", plugin.Name, plugin.Version);
    }

    public async Task<PluginResult> ExecutePluginAsync(
        string pluginName,
        object input,
        CancellationToken cancellationToken = default)
    {
        if (!_plugins.TryGetValue(pluginName, out var plugin))
        {
            throw new ArgumentException($"Plugin not found: {pluginName}");
        }

        _logger.LogInformation("Executing plugin: {Plugin}", pluginName);

        var context = new PluginExecutionContext(input);
        await plugin.ExecuteAsync(context, cancellationToken);

        return new PluginResult(
            Success: true,
            Output: context.Output,
            Duration: context.Duration
        );
    }

    public List<PluginInfo> GetInstalledPlugins()
    {
        return _plugins.Values.Select(p => new PluginInfo(p.Name, p.Version, p.GetType().FullName ?? "")).ToList();
    }
}

public record PluginContext(IServiceProvider Services, IConfiguration Configuration);

public class PluginExecutionContext
{
    public object Input { get; }
    public object? Output { get; set; }
    public DateTime StartTime { get; } = DateTime.UtcNow;
    public TimeSpan Duration => DateTime.UtcNow - StartTime;

    public PluginExecutionContext(object input)
    {
        Input = input;
    }
}

public record PluginResult(bool Success, object? Output, TimeSpan Duration);
public record PluginInfo(string Name, string Version, string TypeName);

/// <summary>
/// Example plugin implementation for data transformation.
/// </summary>
public class TransformationPlugin : IFaboritePlugin
{
    public string Name => "DataTransformer";
    public string Version => "1.0.0";

    public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken)
    {
        // Load config, connect to resources, etc.
        return Task.CompletedTask;
    }

    public Task ExecuteAsync(PluginExecutionContext context, CancellationToken cancellationToken)
    {
        // Transform data
        context.Output = context.Input; // Placeholder
        return Task.CompletedTask;
    }
}
