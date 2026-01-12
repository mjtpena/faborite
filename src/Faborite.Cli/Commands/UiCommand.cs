using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Faborite.Cli.Commands;

public class UiSettings : CommandSettings
{
    [CommandOption("-p|--port")]
    [Description("Port for the API server (default: 5001)")]
    [DefaultValue(5001)]
    public int Port { get; set; } = 5001;

    [CommandOption("--no-browser")]
    [Description("Don't open browser automatically")]
    public bool NoBrowser { get; set; }

    [CommandOption("-c|--config")]
    [Description("Path to config file")]
    public string? ConfigPath { get; set; }
}

public class UiCommand : AsyncCommand<UiSettings>
{
    private Process? _apiProcess;
    private Process? _webProcess;
    private bool _shuttingDown = false;

    public override async Task<int> ExecuteAsync(CommandContext context, UiSettings settings)
    {
        AnsiConsole.Write(new FigletText("Faborite UI").Color(Color.Purple));
        AnsiConsole.MarkupLine("[dim]Starting web interface...[/]");

        var port = settings.Port;
        var apiUrl = $"http://localhost:{port}";
        var webUrl = $"http://localhost:{port + 1}";

        AnsiConsole.MarkupLine($"[cyan]API Server:[/] {apiUrl}");
        AnsiConsole.MarkupLine($"[cyan]Web UI:[/] {webUrl}");
        AnsiConsole.WriteLine();

        // Check if running in development mode (projects exist)
        var solutionDir = FindSolutionDirectory();
        
        if (solutionDir != null)
        {
            AnsiConsole.MarkupLine("[yellow]Running in development mode...[/]");
            return await RunDevelopmentModeAsync(solutionDir, port, settings);
        }
        else
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Could not find Faborite solution directory.");
            AnsiConsole.MarkupLine("Please run this command from within the Faborite source directory.");
            return 1;
        }
    }

    private async Task<int> RunDevelopmentModeAsync(string solutionDir, int port, UiSettings settings)
    {
        var apiProject = Path.Combine(solutionDir, "src", "Faborite.Api", "Faborite.Api.csproj");
        var webProject = Path.Combine(solutionDir, "src", "Faborite.Web", "Faborite.Web.csproj");

        if (!File.Exists(apiProject))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] API project not found at {apiProject}");
            return 1;
        }

        if (!File.Exists(webProject))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Web project not found at {webProject}");
            return 1;
        }

        // Setup Ctrl+C handler
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            if (!_shuttingDown)
            {
                _shuttingDown = true;
                AnsiConsole.MarkupLine("\n[yellow]Shutting down...[/]");
                StopProcesses();
            }
        };

        try
        {
            // Start API server
            AnsiConsole.MarkupLine("[dim]Starting API server...[/]");
            _apiProcess = StartDotnetProject(apiProject, port, "API");
            
            if (_apiProcess == null)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Failed to start API server");
                return 1;
            }

            // Wait for API to be ready
            await WaitForServerAsync($"http://localhost:{port}/api/auth/status", 30);
            AnsiConsole.MarkupLine("[green]✓[/] API server started");

            // Start Web server
            AnsiConsole.MarkupLine("[dim]Starting Web UI...[/]");
            _webProcess = StartDotnetProject(webProject, port + 1, "Web");
            
            if (_webProcess == null)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Failed to start Web UI");
                StopProcesses();
                return 1;
            }

            // Wait for Web to be ready
            await Task.Delay(5000);
            AnsiConsole.MarkupLine("[green]✓[/] Web UI started");

            // Open browser
            if (!settings.NoBrowser)
            {
                AnsiConsole.MarkupLine("[dim]Opening browser...[/]");
                OpenBrowser($"http://localhost:{port + 1}");
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]✓ Faborite UI is running![/]");
            AnsiConsole.MarkupLine($"  [link]http://localhost:{port + 1}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press Ctrl+C to stop[/]");
            AnsiConsole.WriteLine();

            // Wait for processes to exit or user cancellation
            while (!_shuttingDown)
            {
                if (_apiProcess.HasExited || _webProcess.HasExited)
                {
                    if (!_shuttingDown)
                    {
                        AnsiConsole.MarkupLine("[yellow]A server process has stopped unexpectedly[/]");
                        _shuttingDown = true;
                    }
                    break;
                }
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
        finally
        {
            StopProcesses();
        }

        AnsiConsole.MarkupLine("[green]✓[/] Shutdown complete");
        return 0;
    }

    private Process? StartDotnetProject(string projectPath, int port, string name)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{projectPath}\" --urls http://localhost:{port}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(projectPath)
            };

            var process = new Process { StartInfo = startInfo };
            
            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    // Only show errors or important messages
                    if (e.Data.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                        e.Data.Contains("fail", StringComparison.OrdinalIgnoreCase))
                    {
                        AnsiConsole.MarkupLine($"[red][{name}][/] {EscapeMarkup(e.Data)}");
                    }
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    AnsiConsole.MarkupLine($"[red][{name}][/] {EscapeMarkup(e.Data)}");
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return process;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to start {name}:[/] {EscapeMarkup(ex.Message)}");
            return null;
        }
    }

    private static string EscapeMarkup(string text)
    {
        return text.Replace("[", "[[").Replace("]", "]]");
    }

    private async Task WaitForServerAsync(string url, int timeoutSeconds)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline && !_shuttingDown)
        {
            try
            {
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch
            {
                // Server not ready yet
            }
            await Task.Delay(500);
        }
    }

    private void StopProcesses()
    {
        try
        {
            if (_apiProcess != null && !_apiProcess.HasExited)
            {
                _apiProcess.Kill(entireProcessTree: true);
                _apiProcess.Dispose();
            }
        }
        catch { }

        try
        {
            if (_webProcess != null && !_webProcess.HasExited)
            {
                _webProcess.Kill(entireProcessTree: true);
                _webProcess.Dispose();
            }
        }
        catch { }
    }

    private static string? FindSolutionDirectory()
    {
        var dir = Directory.GetCurrentDirectory();
        
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Faborite.sln")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", url);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", url);
            }
        }
        catch
        {
            // Ignore browser open errors
        }
    }
}
