using System.ComponentModel;
using Faborite.Core;
using Faborite.Core.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Faborite.Cli.Commands;

public class ListTablesSettings : CommandSettings
{
    [CommandOption("-w|--workspace")]
    [Description("Workspace ID (GUID)")]
    public string? WorkspaceId { get; set; }

    [CommandOption("-l|--lakehouse")]
    [Description("Lakehouse ID (GUID)")]
    public string? LakehouseId { get; set; }

    [CommandOption("-c|--config")]
    [Description("Path to config file")]
    public string? ConfigPath { get; set; }
}

public class ListTablesCommand : AsyncCommand<ListTablesSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ListTablesSettings settings)
    {
        try
        {
            var config = BuildConfig(settings);

            using var service = new FaboriteService(config);

            AnsiConsole.MarkupLine("[dim]Fetching tables...[/]");
            var tables = await service.ListTablesAsync();

            if (tables.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No tables found[/]");
                return 0;
            }

            var table = new Table()
                .Title($"Tables in Lakehouse")
                .AddColumn("Name")
                .AddColumn("Last Modified");

            foreach (var t in tables)
            {
                table.AddRow(
                    $"[cyan]{t.Name}[/]",
                    t.LastModified?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-");
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"\n[dim]Total: {tables.Count} tables[/]");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }

    private static FaboriteConfig BuildConfig(ListTablesSettings settings)
    {
        FaboriteConfig config;

        if (!string.IsNullOrEmpty(settings.ConfigPath) && File.Exists(settings.ConfigPath))
        {
            config = ConfigLoader.Load(settings.ConfigPath);
        }
        else
        {
            config = ConfigLoader.Load();
        }

        if (!string.IsNullOrEmpty(settings.WorkspaceId))
            config.WorkspaceId = settings.WorkspaceId;
        
        if (!string.IsNullOrEmpty(settings.LakehouseId))
            config.LakehouseId = settings.LakehouseId;

        if (string.IsNullOrEmpty(config.WorkspaceId) || string.IsNullOrEmpty(config.LakehouseId))
        {
            throw new ArgumentException("Either --config or both --workspace and --lakehouse are required");
        }

        return config;
    }
}
