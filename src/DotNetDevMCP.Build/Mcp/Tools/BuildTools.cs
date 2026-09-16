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
    [Description("Builds a .NET project or solution with configurable options. Returns build results including warnings, errors, and diagnostics.")]
    public static async Task<object> Build(
        BuildService buildService,
        ILogger<BuildToolsLogCategory> logger,
        [Description("Path to the project file (.csproj) or solution file (.sln)")] string projectPath,
        [Description("Build configuration (Debug/Release)")] string? configuration = null,
        [Description("Target framework (e.g., net8.0)")] string? framework = null,
        [Description("Target runtime (e.g., win-x64, linux-x64)")] string? runtime = null,
        [Description("Verbosity level (0=quiet, 1=minimal, 2=normal, 3=detailed, 4=diagnostic)")] int verbosity = 1,
        [Description("Skip restoring packages")] bool noRestore = false,
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
            
            return new
            {
                Success = result.Success,
                ExitCode = result.ExitCode,
                DurationSeconds = Math.Round(result.Duration.TotalSeconds, 2),
                Errors = result.Errors,
                Warnings = result.Warnings,
                OutputLines = result.Output.Split(Environment.NewLine).Take(100),
                Diagnostics = result.Diagnostics.Select(d => new
                {
                    d.Severity,
                    d.Code,
                    d.Message,
                    d.FilePath,
                    d.Line,
                    d.Column
                }).Take(50)
            };
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
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Cleaning: {ProjectPath}", projectPath);
            
            var options = new BuildOptions(Configuration: configuration);
            var result = await buildService.CleanAsync(projectPath, options, cancellationToken);
            
            logger.LogInformation("Clean completed: Success={Success}", result.Success);
            
            return new
            {
                Success = result.Success,
                ExitCode = result.ExitCode,
                DurationSeconds = Math.Round(result.Duration.TotalSeconds, 2),
                Output = result.Output
            };
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
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Restoring packages for: {ProjectPath}", projectPath);
            
            var result = await buildService.RestoreAsync(projectPath, cancellationToken);
            
            logger.LogInformation("Restore completed: Success={Success}", result.Success);
            
            return new
            {
                Success = result.Success,
                ExitCode = result.ExitCode,
                DurationSeconds = Math.Round(result.Duration.TotalSeconds, 2),
                Output = result.Output
            };
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
    [Description("Builds a .NET project with custom MSBuild properties. Useful for setting version numbers, configuration values, etc.")]
    public static async Task<object> BuildWithProperties(
        BuildService buildService,
        ILogger<BuildToolsLogCategory> logger,
        [Description("Path to the project file (.csproj) or solution file (.sln)")] string projectPath,
        [Description("MSBuild properties as key-value pairs (e.g., Version=1.0.0, Configuration=Release)")] Dictionary<string, string> properties,
        [Description("Build configuration (Debug/Release)")] string? configuration = null,
        [Description("Target framework (e.g., net8.0)")] string? framework = null,
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
            
            return new
            {
                Success = result.Success,
                ExitCode = result.ExitCode,
                DurationSeconds = Math.Round(result.Duration.TotalSeconds, 2),
                Errors = result.Errors,
                Warnings = result.Warnings,
                AppliedProperties = properties,
                OutputLines = result.Output.Split(Environment.NewLine).Take(50)
            };
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
}
