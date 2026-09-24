// Copyright (c) 2025 Ahmed Mustafa
// Single entry point for DotNetDevMCP. Speaks stdio by default (what MCP clients launch),
// or Streamable HTTP with --http for remote/shared use.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reflection;
using DotNetDevMCP.Analysis.Extensions;
using DotNetDevMCP.Build.Extensions;
using DotNetDevMCP.CodeIntelligence.Extensions;
using DotNetDevMCP.CodeIntelligence.Interfaces;
using DotNetDevMCP.Core;
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
        var cleanEnvOption = new Option<bool>("--clean-env") { Description = "Give every dotnet/git child process a minimal, allow-listed environment instead of inheriting this server's full one. Scrubs environment variables only; it is not a sandbox: child processes still run with your user's file-system and network access. Off by default." };
        var enableOption = new Option<string[]>("--enable")
        {
            Description = "Enable optional tool groups, off by default: 'git' (repo status/branch/stage/commit/push/pull/log/diff) and 'monitoring' (process performance/GC/health/profiling). Comma-separated and/or repeated, e.g. \"--enable git,monitoring\" or \"--enable git --enable monitoring\".",
            AllowMultipleArgumentsPerToken = true,
            DefaultValueFactory = _ => [],
            CustomParser = ParseEnableGroups
        };

        var root = new RootCommand("DotNetDevMCP - MCP server for .NET development: Roslyn code intelligence, build, affected-test selection, git, orchestration.")
        {
            httpOption, portOption, logDirOption, logLevelOption, loadSolutionOption, buildConfigurationOption, gitCommitEditsOption, disableGitOption, enableOption, cleanEnvOption
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
        string[] enabledGroups = parsed.GetValue(enableOption) ?? [];
        bool enableGit = enabledGroups.Contains("git");
        bool enableMonitoring = enabledGroups.Contains("monitoring");
        bool cleanEnv = parsed.GetValue(cleanEnvOption);

        Log.Logger = BuildLogger(logLevel, logDir);

        if (disableGitLegacyFlag)
        {
            Log.Warning("--disable-git is deprecated and has no effect: git integration is already off by default. Use --git-commit-edits to opt into it.");
        }

        if (cleanEnv)
        {
            ChildProcess.CleanEnvironment = true;
            var (kept, dropped, droppedNames) = ChildProcess.DescribeEnvironment();
            Log.Information(
                "--clean-env enabled: dotnet/git child processes get a minimal environment ({Kept} variables kept, {Dropped} dropped). " +
                "Scrubs environment variables only; it is not a sandbox: child processes still run with your user's file-system and network access.",
                kept, dropped);
            Log.Debug("Dropped environment variables: {Names}", string.Join(", ", droppedNames));
        }

        try
        {
            Log.Information("Starting {App} v{Version} ({Transport})", ApplicationName, ApplicationVersion, http ? $"http://localhost:{port}" : "stdio");

            IHost host = http
                ? BuildHttpHost(args, port, gitCommitEdits, buildConfiguration, enableGit, enableMonitoring)
                : BuildStdioHost(args, gitCommitEdits, buildConfiguration, enableGit, enableMonitoring);

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

    private static IHost BuildStdioHost(string[] args, bool gitCommitEdits, string? buildConfiguration, bool enableGit, bool enableMonitoring)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog();
        AddServices(builder.Services, gitCommitEdits, buildConfiguration, enableGit, enableMonitoring).WithStdioServerTransport();
        return builder.Build();
    }

    private static IHost BuildHttpHost(string[] args, int port, bool gitCommitEdits, string? buildConfiguration, bool enableGit, bool enableMonitoring)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args });
        builder.Host.UseSerilog();
        builder.WebHost.UseUrls($"http://localhost:{port}");
        AddServices(builder.Services, gitCommitEdits, buildConfiguration, enableGit, enableMonitoring).WithHttpTransport();
        var app = builder.Build();
        app.MapMcp();
        return app;
    }

    /// <summary>Registers every service and every MCP tool of the server. One place, so nothing gets left out again.</summary>
    /// <remarks>
    /// Git (<see cref="DotNetDevMCP.SourceControl"/>) and Monitoring (<see cref="MonitoringTools"/>) are opt-in via
    /// <c>--enable git,monitoring</c>: a product review found they add nothing over the shell an agent already has,
    /// and every registered tool costs context tokens in every session. Their services are only registered when the
    /// corresponding group is enabled, since nothing else in the server depends on them.
    /// </remarks>
    private static IMcpServerBuilder AddServices(IServiceCollection services, bool gitCommitEdits, string? buildConfiguration, bool enableGit, bool enableMonitoring)
    {
        services.WithCodeIntelligenceServices(gitCommitEdits, buildConfiguration);
        services.AddAnalysisServices();
        services.WithOrchestrationServices();
        services.WithTestingServices();
        services.WithBuildServices();
        if (enableGit) services.WithSourceControlServices();
        if (enableMonitoring) services.AddMonitoringServices();

        var mcp = services
            .AddMcpServer(o => o.ServerInfo = new Implementation { Name = ApplicationName, Version = ApplicationVersion })
            .WithCodeIntelligence()
            .WithOrchestration()
            .WithTesting()
            .WithBuild()
            .WithTools<Analysis.Mcp.Tools.AnalysisTools>();

        if (enableGit) mcp = mcp.WithSourceControl();
        if (enableMonitoring) mcp = mcp.WithTools<MonitoringTools>();

        return mcp;
    }

    private static readonly string[] ValidEnableGroups = ["git", "monitoring"];

    /// <summary>Custom parser for <c>--enable</c>: splits comma-separated and/or repeated tokens, lower-cases them,
    /// and reports a clear error (listing the valid groups) for anything unrecognized.</summary>
    private static string[] ParseEnableGroups(ArgumentResult result)
    {
        var groups = new List<string>();
        foreach (var token in result.Tokens)
        {
            foreach (var part in token.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var group = part.ToLowerInvariant();
                if (!ValidEnableGroups.Contains(group))
                {
                    result.AddError($"Unknown --enable value '{part}'. Valid values: {string.Join(", ", ValidEnableGroups)}.");
                    continue;
                }
                groups.Add(group);
            }
        }
        return [.. groups];
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
