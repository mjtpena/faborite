using System.ComponentModel;
using Faborite.Core;
using Faborite.Core.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Faborite.Cli.Commands;

public class SyncSettings : CommandSettings
{
    [CommandOption("-w|--workspace")]
    [Description("Workspace ID (GUID)")]
    public string? WorkspaceId { get; set; }

    [CommandOption("-l|--lakehouse")]
    [Description("Lakehouse ID (GUID)")]
    public string? LakehouseId { get; set; }

    [CommandOption("-c|--config")]
    [Description("Path to config file (faborite.json)")]
    public string? ConfigPath { get; set; }

    [CommandOption("-n|--rows")]
    [Description("Number of rows to sample per table")]
    [DefaultValue(10000)]
    public int Rows { get; set; } = 10000;

    [CommandOption("-s|--strategy")]
    [Description("Sampling strategy (random, recent, head, tail, stratified, query, full)")]
    [DefaultValue("random")]
    public string Strategy { get; set; } = "random";

    [CommandOption("--date-column")]
    [Description("Date column for 'recent' strategy")]
    public string? DateColumn { get; set; }

    [CommandOption("-f|--format")]
    [Description("Output format (parquet, delta, csv, json, duckdb)")]
    [DefaultValue("parquet")]
    public string Format { get; set; } = "parquet";

    [CommandOption("-o|--output")]
    [Description("Output directory")]
    [DefaultValue("./local_lakehouse")]
    public string OutputPath { get; set; } = "./local_lakehouse";

    [CommandOption("-t|--table")]
    [Description("Specific tables to sync (can be repeated)")]
    public string[]? Tables { get; set; }

    [CommandOption("--skip")]
    [Description("Tables to skip (can be repeated)")]
    public string[]? Skip { get; set; }

    [CommandOption("-p|--parallel")]
    [Description("Number of tables to sync in parallel")]
    [DefaultValue(4)]
    public int Parallel { get; set; } = 4;

    [CommandOption("--no-schema")]
    [Description("Don't export table schemas")]
    public bool NoSchema { get; set; }
}

public class SyncCommand : AsyncCommand<SyncSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SyncSettings settings)
    {
        try
        {
            var config = BuildConfig(settings);

            using var service = new FaboriteService(config);

            // Test connection
            AnsiConsole.MarkupLine("[dim]Testing connection to OneLake...[/]");
            if (!await service.TestConnectionAsync())
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Could not connect to OneLake. Check your credentials.");
                return 1;
            }
            AnsiConsole.MarkupLine("[green]✓[/] Connected to OneLake");

            // Sync with progress
            SyncSummary? summary = null;
            
            await AnsiConsole.Progress()
                .AutoClear(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[cyan]Syncing tables...[/]");
                    
                    var progress = new Progress<(string tableName, int current, int total)>(p =>
                    {
                        task.Description = $"[cyan]Syncing {p.tableName}...[/]";
                        task.MaxValue = p.total;
                        task.Value = p.current;
                    });

                    summary = await service.SyncAsync(
                        tables: settings.Tables,
                        progress: progress);

                    task.Value = task.MaxValue;
                    task.Description = "[green]Done![/]";
                });

            if (summary == null)
            {
                AnsiConsole.MarkupLine("[yellow]No tables synced[/]");
                return 0;
            }

            // Show results table
            AnsiConsole.WriteLine();
            var resultsTable = new Table()
                .Title("Sync Results")
                .AddColumn("Table")
                .AddColumn("Status")
                .AddColumn(new TableColumn("Rows").RightAligned())
                .AddColumn(new TableColumn("Source").RightAligned())
                .AddColumn(new TableColumn("Duration").RightAligned());

            foreach (var result in summary.Tables)
            {
                var status = result.Success ? "[green]✓[/]" : "[red]✗[/]";
                var source = result.SourceRows?.ToString("N0") ?? "-";
                
                resultsTable.AddRow(
                    result.TableName,
                    status,
                    result.RowsSynced.ToString("N0"),
                    source,
                    $"{result.Duration.TotalSeconds:F1}s");
            }

            AnsiConsole.Write(resultsTable);

            // Summary panel
            var summaryText = $"""
                [bold]Tables synced:[/] {summary.SuccessfulTables}/{summary.Tables.Count}
                [bold]Total rows:[/] {summary.TotalRows:N0}
                [bold]Duration:[/] {summary.Duration.TotalSeconds:F1}s
                [bold]Output:[/] {config.Sync.LocalPath}
                """;

            if (summary.FailedTables > 0)
            {
                summaryText += $"\n[red]Failed: {summary.FailedTables} tables[/]";
            }

            var panel = new Panel(summaryText)
                .Header("Summary")
                .BorderColor(summary.FailedTables == 0 ? Color.Green : Color.Yellow);

            AnsiConsole.Write(panel);

            return summary.FailedTables > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }

    private static FaboriteConfig BuildConfig(SyncSettings settings)
    {
        FaboriteConfig config;

        // Load from config file if specified
        if (!string.IsNullOrEmpty(settings.ConfigPath) && File.Exists(settings.ConfigPath))
        {
            config = ConfigLoader.Load(settings.ConfigPath);
        }
        else
        {
            // Try to auto-detect config file
            config = ConfigLoader.Load();
        }

        // Override with CLI args
        if (!string.IsNullOrEmpty(settings.WorkspaceId))
            config.WorkspaceId = settings.WorkspaceId;
        
        if (!string.IsNullOrEmpty(settings.LakehouseId))
            config.LakehouseId = settings.LakehouseId;

        // Validate required settings
        if (string.IsNullOrEmpty(config.WorkspaceId) || string.IsNullOrEmpty(config.LakehouseId))
        {
            throw new ArgumentException("Either --config or both --workspace and --lakehouse are required");
        }

        // Apply sampling settings
        config.Sample.Strategy = Enum.Parse<SampleStrategy>(settings.Strategy, ignoreCase: true);
        config.Sample.Rows = settings.Rows;
        if (!string.IsNullOrEmpty(settings.DateColumn))
            config.Sample.DateColumn = settings.DateColumn;

        // Apply format settings
        config.Format.Format = Enum.Parse<OutputFormat>(settings.Format, ignoreCase: true);

        // Apply sync settings
        config.Sync.LocalPath = settings.OutputPath;
        config.Sync.ParallelTables = settings.Parallel;
        config.Sync.IncludeSchema = !settings.NoSchema;

        if (settings.Skip != null && settings.Skip.Length > 0)
            config.Sync.SkipTables = settings.Skip.ToList();

        return config;
    }
}
