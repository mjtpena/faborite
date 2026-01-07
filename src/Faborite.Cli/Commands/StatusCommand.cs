using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Faborite.Cli.Commands;

public class StatusSettings : CommandSettings
{
    [CommandOption("-p|--path")]
    [Description("Local output directory to check")]
    [DefaultValue("./local_lakehouse")]
    public string OutputPath { get; set; } = "./local_lakehouse";
}

public class StatusCommand : Command<StatusSettings>
{
    public override int Execute(CommandContext context, StatusSettings settings)
    {
        try
        {
            if (!Directory.Exists(settings.OutputPath))
            {
                AnsiConsole.MarkupLine($"[yellow]No local data found at:[/] {settings.OutputPath}");
                AnsiConsole.MarkupLine("Run [bold]faborite sync[/] to download data");
                return 0;
            }

            var table = new Table();
            table.AddColumn("Table");
            table.AddColumn("Format");
            table.AddColumn("Files");
            table.AddColumn("Size");
            table.AddColumn("Modified");

            var directories = Directory.GetDirectories(settings.OutputPath);
            long totalSize = 0;
            int totalFiles = 0;

            foreach (var dir in directories.OrderBy(d => d))
            {
                var tableName = Path.GetFileName(dir);
                var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                var fileCount = files.Length;
                var dirSize = files.Sum(f => new FileInfo(f).Length);
                var lastModified = files.Any() 
                    ? files.Max(f => File.GetLastWriteTime(f)) 
                    : DateTime.MinValue;

                // Detect format
                var format = DetectFormat(files);

                totalSize += dirSize;
                totalFiles += fileCount;

                table.AddRow(
                    $"[blue]{tableName}[/]",
                    format,
                    fileCount.ToString(),
                    FormatSize(dirSize),
                    lastModified != DateTime.MinValue 
                        ? lastModified.ToString("yyyy-MM-dd HH:mm") 
                        : "[dim]N/A[/]"
                );
            }

            if (directories.Length == 0)
            {
                AnsiConsole.MarkupLine($"[yellow]No tables found in:[/] {settings.OutputPath}");
                return 0;
            }

            table.Title = new TableTitle($"[bold]Local Data Status[/] - {settings.OutputPath}");
            table.Border(TableBorder.Rounded);

            AnsiConsole.Write(table);

            // Summary
            AnsiConsole.MarkupLine($"\n[dim]Total:[/] {directories.Length} tables, {totalFiles} files, {FormatSize(totalSize)}");

            // Check for schema files
            var schemaFiles = Directory.GetFiles(settings.OutputPath, "*.schema.json", SearchOption.AllDirectories);
            if (schemaFiles.Any())
            {
                AnsiConsole.MarkupLine($"[dim]Schemas:[/] {schemaFiles.Length} schema files available");
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }

    private static string DetectFormat(string[] files)
    {
        var extensions = files
            .Select(f => Path.GetExtension(f).ToLowerInvariant())
            .Where(e => !string.IsNullOrEmpty(e) && e != ".json") // Exclude schema files
            .Distinct()
            .ToList();

        if (extensions.Contains(".parquet"))
            return "[green]Parquet[/]";
        if (extensions.Contains(".csv"))
            return "[cyan]CSV[/]";
        if (extensions.Contains(".duckdb"))
            return "[yellow]DuckDB[/]";
        if (extensions.Any(e => e == ".json"))
            return "[magenta]JSON[/]";

        return "[dim]Unknown[/]";
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
