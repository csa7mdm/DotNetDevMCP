// Copyright (c) 2025 Ahmed Mustafa
// Single entry point for DotNetDevMCP. Speaks stdio by default (what MCP clients launch),
// or Streamable HTTP with --http for remote/shared use.

using System.CommandLine;
using System.Reflection;
using DotNetDevMCP.Analysis.Extensions;
using DotNetDevMCP.Build.Extensions;
using DotNetDevMCP.CodeIntelligence.Extensions;
using DotNetDevMCP.CodeIntelligence.Interfaces;
using DotNetDevMCP.Monitoring.Extensions;
using DotNetDevMCP.Monitoring.Mcp.Tools;
using DotNetDevMCP.Orchestration.Extensions;
using DotNetDevMCP.SourceControl.Extensions;
using DotNetDevMCP.Testing.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Serilog;
using Serilog.Events;

namespace DotNetDevMCP.Server;

public static class Program
{
    public const string ApplicationName = "DotNetDevMCP";
    public static readonly string ApplicationVersion =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? "0.0.0";

    private const string LogOutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

    public static async Task<int> Main(string[] args)
    {
        var httpOption = new Option<bool>("--http") { Description = "Serve MCP over Streamable HTTP instead of stdio." };
        var portOption = new Option<int>("--port") { Description = "Port for --http mode.", DefaultValueFactory = _ => 3001 };
        var logDirOption = new Option<string?>("--log-directory") { Description = "Directory for rolling log files. Logs always go to stderr." };
        var logLevelOption = new Option<LogEventLevel>("--log-level") { Description = "Minimum log level.", DefaultValueFactory = _ => LogEventLevel.Information };
        var loadSolutionOption = new Option<string?>("--load-solution") { Description = "Solution (.sln) to load on startup." };
        var buildConfigurationOption = new Option<string?>("--build-configuration") { Description = "Build configuration used when loading the solution (Debug, Release)." };
        var gitCommitEditsOption = new Option<bool>("--git-commit-edits") { Description = "Let edit tools (RenameSymbol, OverwriteMember, AddMember, MoveMember, FindAndReplace, CreateRoslynDocument, OverwriteRoslynDocument, ManageUsings, ManageAttributes) create a git branch and commit after each change, and enable SharpTool_Undo. Off by default: edits are still applied to disk and compile-checked, they just don't touch git or your current branch." };
        var disableGitOption = new Option<bool>("--disable-git") { Description = "Deprecated, no-op. Git integration in code-intelligence tools is off by default; use --git-commit-edits to opt in." };

        var root = new RootCommand("DotNetDevMCP - MCP server for .NET development: Roslyn code intelligence, build, affected-test selection, git, orchestration.")
        {
            httpOption, portOption, logDirOption, logLevelOption, loadSolutionOption, buildConfigurationOption, gitCommitEditsOption, disableGitOption
        };

        var parsed = root.Parse(args);
        if (parsed.Errors.Count > 0)
        {
            foreach (var e in parsed.Errors) Console.Error.WriteLine(e.Message);
            return 1;
        }
        if (parsed.Action is not null) return await parsed.InvokeAsync(); // --help / --version

        bool http = parsed.GetValue(httpOption);
        int port = parsed.GetValue(portOption);
        string? logDir = parsed.GetValue(logDirOption);
        LogEventLevel logLevel = parsed.GetValue(logLevelOption);
        string? solutionPath = parsed.GetValue(loadSolutionOption);
        string? buildConfiguration = parsed.GetValue(buildConfigurationOption);
        bool gitCommitEdits = parsed.GetValue(gitCommitEditsOption);
        bool disableGitLegacyFlag = parsed.GetValue(disableGitOption);

        Log.Logger = BuildLogger(logLevel, logDir);

        if (disableGitLegacyFlag)
        {
            Log.Warning("--disable-git is deprecated and has no effect: git integration is already off by default. Use --git-commit-edits to opt into it.");
        }

        try
        {
            Log.Information("Starting {App} v{Version} ({Transport})", ApplicationName, ApplicationVersion, http ? $"http://localhost:{port}" : "stdio");

            IHost host = http ? BuildHttpHost(args, port, gitCommitEdits, buildConfiguration) : BuildStdioHost(args, gitCommitEdits, buildConfiguration);

            if (!string.IsNullOrEmpty(solutionPath))
            {
                await LoadSolutionAsync(host.Services, solutionPath);
            }

            await host.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "{App} terminated unexpectedly.", ApplicationName);
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static IHost BuildStdioHost(string[] args, bool gitCommitEdits, string? buildConfiguration)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog();
        AddServices(builder.Services, gitCommitEdits, buildConfiguration).WithStdioServerTransport();
        return builder.Build();
    }

    private static IHost BuildHttpHost(string[] args, int port, bool gitCommitEdits, string? buildConfiguration)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args });
        builder.Host.UseSerilog();
        builder.WebHost.UseUrls($"http://localhost:{port}");
        AddServices(builder.Services, gitCommitEdits, buildConfiguration).WithHttpTransport();
        var app = builder.Build();
        app.MapMcp();
        return app;
    }

    /// <summary>Registers every service and every MCP tool of the server. One place, so nothing gets left out again.</summary>
    private static IMcpServerBuilder AddServices(IServiceCollection services, bool gitCommitEdits, string? buildConfiguration)
    {
        services.WithCodeIntelligenceServices(gitCommitEdits, buildConfiguration);
        services.AddAnalysisServices();
        services.AddMonitoringServices();
        services.WithOrchestrationServices();
        services.WithTestingServices();
        services.WithBuildServices();
        services.WithSourceControlServices();

        return services
            .AddMcpServer(o => o.ServerInfo = new Implementation { Name = ApplicationName, Version = ApplicationVersion })
            .WithCodeIntelligence()
            .WithOrchestration()
            .WithTesting()
            .WithBuild()
            .WithSourceControl()
            .WithTools<Analysis.Mcp.Tools.AnalysisTools>()
            .WithTools<MonitoringTools>();
    }

    private static async Task LoadSolutionAsync(IServiceProvider services, string solutionPath)
    {
        try
        {
            Log.Information("Loading solution: {Path}", solutionPath);
            await services.GetRequiredService<ISolutionManager>().LoadSolutionAsync(solutionPath, CancellationToken.None);
            var dir = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
            await services.GetRequiredService<IEditorConfigProvider>().InitializeAsync(dir, CancellationToken.None);
            Log.Information("Solution loaded: {Path}", solutionPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load solution {Path}", solutionPath);
        }
    }

    private static Serilog.Core.Logger BuildLogger(LogEventLevel level, string? logDir)
    {
        // stdout is the MCP channel in stdio mode, so every log line goes to stderr.
        var cfg = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("ModelContextProtocol", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Async(a => a.Console(outputTemplate: LogOutputTemplate, standardErrorFromLevel: LogEventLevel.Verbose));

        if (!string.IsNullOrWhiteSpace(logDir))
        {
            Directory.CreateDirectory(logDir);
            cfg.WriteTo.Async(a => a.File(Path.Combine(logDir, "dotnetdevmcp-.log"),
                rollingInterval: RollingInterval.Day, outputTemplate: LogOutputTemplate,
                fileSizeLimitBytes: 10 * 1024 * 1024, rollOnFileSizeLimit: true, retainedFileCountLimit: 7));
        }
        return cfg.CreateLogger();
    }
}
