using System.ComponentModel;
using Faborite.Core.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Faborite.Cli.Commands;

public class InitSettings : CommandSettings
{
    [CommandOption("-o|--output")]
    [Description("Output path for config file")]
    [DefaultValue("faborite.json")]
    public string OutputPath { get; set; } = "faborite.json";

    [CommandOption("-f|--force")]
    [Description("Overwrite existing config file")]
    public bool Force { get; set; }
}

public class InitCommand : Command<InitSettings>
{
    public override int Execute(CommandContext context, InitSettings settings)
    {
        try
        {
            if (File.Exists(settings.OutputPath) && !settings.Force)
            {
                AnsiConsole.MarkupLine($"[yellow]Config file already exists:[/] {settings.OutputPath}");
                AnsiConsole.MarkupLine("Use [bold]--force[/] to overwrite");
                return 1;
            }

            var exampleConfig = ConfigLoader.GenerateExample();
            File.WriteAllText(settings.OutputPath, exampleConfig);

            AnsiConsole.MarkupLine($"[green]✓[/] Created config file: {settings.OutputPath}");
            AnsiConsole.MarkupLine("[dim]Edit the file and fill in your workspace/lakehouse IDs[/]");

            // Show a preview of the config (escape markup characters)
            var escapedConfig = Markup.Escape(exampleConfig);
            var panel = new Panel(escapedConfig)
                .Header("Config Preview")
                .BorderColor(Color.Grey);

            AnsiConsole.Write(panel);

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
