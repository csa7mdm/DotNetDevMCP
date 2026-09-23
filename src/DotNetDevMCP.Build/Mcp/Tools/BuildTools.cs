using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
// Copyright (c) 2025 Ahmed Mustafa

using ModelContextProtocol;
using DotNetDevMCP.Build;

namespace DotNetDevMCP.Build.Mcp.Tools;

/// <summary>
/// Marker class for ILogger category specific to BuildTools
/// </summary>
public class BuildToolsLogCategory { }

/// <summary>
/// MCP Tools for building .NET projects and solutions
/// </summary>
[McpServerToolType]
public static partial class BuildTools
{
    [McpServerTool(Name = "dotnet_build", Idempotent = false, ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Builds a .NET project or solution with configurable options. Returns a compact summary by default (counts, all errors, up to the first 20 de-duplicated warnings); pass verbose=true for raw MSBuild output lines too.")]
    public static async Task<object> Build(
        BuildService buildService,
        ILogger<BuildToolsLogCategory> logger,
        [Description("Path to the project file (.csproj) or solution file (.sln)")] string projectPath,
        [Description("Build configuration (Debug/Release)")] string? configuration = null,
        [Description("Target framework (e.g., net8.0)")] string? framework = null,
        [Description("Target runtime (e.g., win-x64, linux-x64)")] string? runtime = null,
        [Description("Verbosity level (0=quiet, 1=minimal, 2=normal, 3=detailed, 4=diagnostic)")] int verbosity = 1,
        [Description("Skip restoring packages")] bool noRestore = false,
        [Description("Include raw MSBuild output lines in the response. Default false returns a compact summary only.")] bool verbose = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Building: {ProjectPath}", projectPath);

            var options = new BuildOptions(
                Configuration: configuration,
                Framework: framework,
                Runtime: runtime,
                Verbosity: verbosity,
                NoRestore: noRestore);

            var result = await buildService.BuildAsync(projectPath, options, cancellationToken: cancellationToken);

            logger.LogInformation("Build completed: Success={Success}, Errors={Errors}, Warnings={Warnings}",
                result.Success, result.Errors, result.Warnings);

            return BuildSummaryResponse(result, projectPath, verbose);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Build failed for {ProjectPath}", projectPath);
            return new
            {
                Success = false,
                Error = ex.Message,
                ExitCode = -1,
                Errors = 1,
                Warnings = 0
            };
        }
    }

    [McpServerTool(Name = "dotnet_clean", Idempotent = false, ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Cleans build artifacts from a .NET project or solution.")]
    public static async Task<object> Clean(
        BuildService buildService,
        ILogger<BuildToolsLogCategory> logger,
        [Description("Path to the project file (.csproj) or solution file (.sln)")] string projectPath,
        [Description("Build configuration to clean (Debug/Release)")] string? configuration = null,
        [Description("Include the full raw output in the response. Default false omits it on success and returns only a short tail on failure.")] bool verbose = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Cleaning: {ProjectPath}", projectPath);

            var options = new BuildOptions(Configuration: configuration);
            var result = await buildService.CleanAsync(projectPath, options, cancellationToken);

            logger.LogInformation("Clean completed: Success={Success}", result.Success);

            return RawOutputSummaryResponse(result, verbose);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Clean failed for {ProjectPath}", projectPath);
            return new
            {
                Success = false,
                Error = ex.Message,
                ExitCode = -1
            };
        }
    }

    [McpServerTool(Name = "dotnet_restore", Idempotent = false, ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Restores NuGet packages for a .NET project or solution.")]
    public static async Task<object> Restore(
        BuildService buildService,
        ILogger<BuildToolsLogCategory> logger,
        [Description("Path to the project file (.csproj) or solution file (.sln)")] string projectPath,
        [Description("Include the full raw output in the response. Default false omits it on success and returns only a short tail on failure.")] bool verbose = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Restoring packages for: {ProjectPath}", projectPath);

            var result = await buildService.RestoreAsync(projectPath, cancellationToken);

            logger.LogInformation("Restore completed: Success={Success}", result.Success);

            return RawOutputSummaryResponse(result, verbose);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Restore failed for {ProjectPath}", projectPath);
            return new
            {
                Success = false,
                Error = ex.Message,
                ExitCode = -1
            };
        }
    }

    [McpServerTool(Name = "dotnet_build_with_properties", Idempotent = false, ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Builds a .NET project with custom MSBuild properties. Useful for setting version numbers, configuration values, etc. Returns a compact summary by default; pass verbose=true for raw MSBuild output lines too.")]
    public static async Task<object> BuildWithProperties(
        BuildService buildService,
        ILogger<BuildToolsLogCategory> logger,
        [Description("Path to the project file (.csproj) or solution file (.sln)")] string projectPath,
        [Description("MSBuild properties as key-value pairs (e.g., Version=1.0.0, Configuration=Release)")] Dictionary<string, string> properties,
        [Description("Build configuration (Debug/Release)")] string? configuration = null,
        [Description("Target framework (e.g., net8.0)")] string? framework = null,
        [Description("Include raw MSBuild output lines in the response. Default false returns a compact summary only.")] bool verbose = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Building with properties: {ProjectPath}, Properties={PropsCount}",
                projectPath, properties.Count);

            var options = new BuildOptions(
                Configuration: configuration,
                Framework: framework,
                Properties: properties,
                Verbosity: 1);

            var result = await buildService.BuildAsync(projectPath, options, cancellationToken: cancellationToken);

            logger.LogInformation("Build completed: Success={Success}, Errors={Errors}", result.Success, result.Errors);

            return BuildSummaryResponse(result, projectPath, verbose, properties);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Build with properties failed for {ProjectPath}", projectPath);
            return new
            {
                Success = false,
                Error = ex.Message,
                ExitCode = -1,
                Errors = 1
            };
        }
    }

    /// <summary>
    /// Builds the tool response for a build-type result (dotnet_build / dotnet_build_with_properties):
    /// a compact summary of counts, all errors, and up to the first <see cref="BuildOutputCompactor.DefaultMaxWarnings"/>
    /// de-duplicated warnings, with raw output lines added only when <paramref name="verbose"/> is set.
    /// </summary>
    private static object BuildSummaryResponse(BuildResult result, string projectPath, bool verbose, Dictionary<string, string>? appliedProperties = null)
    {
        var baseDirectory = GetBaseDirectory(projectPath);
        var compacted = BuildOutputCompactor.Compact(result.Diagnostics, baseDirectory);

        if (verbose)
        {
            return new
            {
                Success = result.Success,
                ExitCode = result.ExitCode,
                DurationSeconds = Math.Round(result.Duration.TotalSeconds, 2),
                ErrorCount = compacted.Errors.Count,
                WarningCount = compacted.WarningCount,
                Errors = compacted.Errors,
                Warnings = compacted.Warnings,
                WarningsTruncated = compacted.WarningsTruncated,
                AppliedProperties = appliedProperties,
                OutputLines = result.Output.Split(Environment.NewLine)
            };
        }

        return new
        {
            Success = result.Success,
            ExitCode = result.ExitCode,
            DurationSeconds = Math.Round(result.Duration.TotalSeconds, 2),
            ErrorCount = compacted.Errors.Count,
            WarningCount = compacted.WarningCount,
            Errors = compacted.Errors,
            Warnings = compacted.Warnings,
            WarningsTruncated = compacted.WarningsTruncated,
            AppliedProperties = appliedProperties
        };
    }

    /// <summary>
    /// Builds the tool response for a clean/restore result: no diagnostics to compact, so the raw
    /// output is either omitted (success, non-verbose), tailed (failure, non-verbose), or returned
    /// in full (verbose).
    /// </summary>
    private static object RawOutputSummaryResponse(BuildResult result, bool verbose)
    {
        if (verbose)
        {
            return new
            {
                Success = result.Success,
                ExitCode = result.ExitCode,
                DurationSeconds = Math.Round(result.Duration.TotalSeconds, 2),
                Output = result.Output
            };
        }

        if (!result.Success)
        {
            return new
            {
                Success = result.Success,
                ExitCode = result.ExitCode,
                DurationSeconds = Math.Round(result.Duration.TotalSeconds, 2),
                OutputTail = BuildOutputCompactor.Tail(result.Output)
            };
        }

        return new
        {
            Success = result.Success,
            ExitCode = result.ExitCode,
            DurationSeconds = Math.Round(result.Duration.TotalSeconds, 2)
        };
    }

    private static string? GetBaseDirectory(string projectPath)
    {
        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(projectPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
