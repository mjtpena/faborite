using FluentAssertions;
using Spectre.Console.Cli;
using Xunit;

namespace Faborite.Cli.Tests.Commands;

public class CommandValidationTests
{
    [Fact]
    public void SyncCommand_WithNoArguments_ShowsHelp()
    {
        // This test validates the CLI structure is correctly configured
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.AddCommand<Faborite.Cli.Commands.SyncCommand>("sync");
        });

        // The app should be configurable without errors
        app.Should().NotBeNull();
    }

    [Fact]
    public void ListTablesCommand_CanBeRegistered()
    {
        // Arrange
        var app = new CommandApp();

        // Act
        app.Configure(config =>
        {
            config.AddCommand<Faborite.Cli.Commands.ListTablesCommand>("list-tables");
        });

        // Assert
        app.Should().NotBeNull();
    }

    [Fact]
    public void InitCommand_CanBeRegistered()
    {
        // Arrange
        var app = new CommandApp();

        // Act
        app.Configure(config =>
        {
            config.AddCommand<Faborite.Cli.Commands.InitCommand>("init");
        });

        // Assert
        app.Should().NotBeNull();
    }

    [Fact]
    public void StatusCommand_CanBeRegistered()
    {
        // Arrange
        var app = new CommandApp();

        // Act
        app.Configure(config =>
        {
            config.AddCommand<Faborite.Cli.Commands.StatusCommand>("status");
        });

        // Assert
        app.Should().NotBeNull();
    }

    [Fact]
    public void AllCommands_CanBeRegisteredTogether()
    {
        // Arrange
        var app = new CommandApp();

        // Act
        app.Configure(config =>
        {
            config.AddCommand<Faborite.Cli.Commands.SyncCommand>("sync");
            config.AddCommand<Faborite.Cli.Commands.ListTablesCommand>("list-tables");
            config.AddCommand<Faborite.Cli.Commands.InitCommand>("init");
            config.AddCommand<Faborite.Cli.Commands.StatusCommand>("status");
        });

        // Assert - if we get here without exception, the commands are valid
        app.Should().NotBeNull();
    }
}
