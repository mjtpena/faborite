using Faborite.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("faborite");
    config.SetApplicationVersion("0.1.0");
    
    config.AddCommand<SyncCommand>("sync")
        .WithDescription("Sync lakehouse tables to local storage")
        .WithExample(new[] { "sync", "-w", "<workspace-id>", "-l", "<lakehouse-id>" })
        .WithExample(new[] { "sync", "--config", "faborite.json" })
        .WithExample(new[] { "sync", "-w", "<id>", "-l", "<id>", "--format", "duckdb" });

    config.AddCommand<ListTablesCommand>("list-tables")
        .WithDescription("List all tables in a lakehouse")
        .WithAlias("ls")
        .WithExample(new[] { "list-tables", "-w", "<workspace-id>", "-l", "<lakehouse-id>" });

    config.AddCommand<InitCommand>("init")
        .WithDescription("Generate a sample configuration file")
        .WithExample(new[] { "init" })
        .WithExample(new[] { "init", "-o", "my-config.json" });

    config.AddCommand<StatusCommand>("status")
        .WithDescription("Show status of locally synced data")
        .WithExample(new[] { "status" })
        .WithExample(new[] { "status", "-p", "./my_lakehouse" });
});

return await app.RunAsync(args);
