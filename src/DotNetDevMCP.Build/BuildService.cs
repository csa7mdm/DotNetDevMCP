// Copyright (c) 2025 Ahmed Mustafa

using System.Diagnostics;
using System.Text.RegularExpressions;
using DotNetDevMCP.Core;

namespace DotNetDevMCP.Build;

/// <summary>
/// Result of a build operation
/// </summary>
public record BuildResult(
    bool Success,
    int ExitCode,
    TimeSpan Duration,
    int Warnings,
    int Errors,
    string Output,
    IEnumerable<BuildDiagnostic> Diagnostics);

/// <summary>
/// Represents a build diagnostic (warning or error)
/// </summary>
public record BuildDiagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    string? FilePath = null,
    int? Line = null,
    int? Column = null);

/// <summary>
/// Severity of a diagnostic
/// </summary>
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Options for build operations
/// </summary>
public record BuildOptions(
    string? Configuration = null,
    string? Framework = null,
    string? Runtime = null,
    bool NoBuild = false,
    bool NoRestore = false,
    int? Verbosity = null,
    Dictionary<string, string>? Properties = null);

/// <summary>
/// Service for building .NET projects and solutions
/// </summary>
public class BuildService
{
    private static readonly Regex DiagnosticRegex = new(
        @"^(?<file>.*?)\((?<line>\d+),(?<column>\d+)\):\s*(?<severity>warning|error)\s+(?<code>\w+):\s*(?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Build a project or solution
    /// </summary>
    public async Task<BuildResult> BuildAsync(
        string projectPath,
        BuildOptions? options = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new BuildOptions();
        var stopwatch = Stopwatch.StartNew();

        var validationError = ValidateOptions(options);
        if (validationError != null)
        {
            return ValidationFailure("BUILD002", validationError, stopwatch.Elapsed);
        }

        string fullProjectPath;
        try
        {
            fullProjectPath = NormalizeProjectPath(projectPath);
        }
        catch (ArgumentException ex)
        {
            return ValidationFailure("BUILD002", ex.Message, stopwatch.Elapsed);
        }

        try
        {
            var arguments = BuildArgumentList("build", fullProjectPath, options);

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(fullProjectPath) ?? Environment.CurrentDirectory
            };
            foreach (var arg in arguments) startInfo.ArgumentList.Add(arg);
            ChildProcess.Prepare(startInfo);

            using var process = new Process { StartInfo = startInfo };
            var output = new List<string>();
            var errors = new List<string>();

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    output.Add(e.Data);
                    progress?.Report(e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errors.Add(e.Data);
                }
            };

            process.Start();
            process.StandardInput.Close(); // don't inherit the MCP stdio pipe
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);
            stopwatch.Stop();

            var fullOutput = string.Join(Environment.NewLine, output);
            var diagnostics = ParseDiagnostics(fullOutput);
            var (warnings, errorCount) = CountDiagnostics(diagnostics);

            return new BuildResult(
                Success: process.ExitCode == 0,
                ExitCode: process.ExitCode,
                Duration: stopwatch.Elapsed,
                Warnings: warnings,
                Errors: errorCount,
                Output: fullOutput,
                Diagnostics: diagnostics);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new BuildResult(
                Success: false,
                ExitCode: -1,
                Duration: stopwatch.Elapsed,
                Warnings: 0,
                Errors: 1,
                Output: ex.Message,
                Diagnostics: new[] { new BuildDiagnostic(DiagnosticSeverity.Error, "BUILD001", ex.Message) });
        }
    }

    /// <summary>
    /// Clean build artifacts
    /// </summary>
    public async Task<BuildResult> CleanAsync(
        string projectPath,
        BuildOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new BuildOptions();
        var stopwatch = Stopwatch.StartNew();

        var validationError = ValidateOptions(options);
        if (validationError != null)
        {
            return ValidationFailure("CLEAN002", validationError, stopwatch.Elapsed);
        }

        string fullProjectPath;
        try
        {
            fullProjectPath = NormalizeProjectPath(projectPath);
        }
        catch (ArgumentException ex)
        {
            return ValidationFailure("CLEAN002", ex.Message, stopwatch.Elapsed);
        }

        try
        {
            var arguments = BuildArgumentList("clean", fullProjectPath, options);

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(fullProjectPath) ?? Environment.CurrentDirectory
            };
            foreach (var arg in arguments) startInfo.ArgumentList.Add(arg);
            ChildProcess.Prepare(startInfo);

            using var process = new Process { StartInfo = startInfo };
            var output = new List<string>();

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    output.Add(e.Data);
                }
            };

            process.Start();
            process.StandardInput.Close(); // don't inherit the MCP stdio pipe
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);
            stopwatch.Stop();

            return new BuildResult(
                Success: process.ExitCode == 0,
                ExitCode: process.ExitCode,
                Duration: stopwatch.Elapsed,
                Warnings: 0,
                Errors: process.ExitCode == 0 ? 0 : 1,
                Output: string.Join(Environment.NewLine, output),
                Diagnostics: Array.Empty<BuildDiagnostic>());
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new BuildResult(
                Success: false,
                ExitCode: -1,
                Duration: stopwatch.Elapsed,
                Warnings: 0,
                Errors: 1,
                Output: ex.Message,
                Diagnostics: new[] { new BuildDiagnostic(DiagnosticSeverity.Error, "CLEAN001", ex.Message) });
        }
    }

    /// <summary>
    /// Restore NuGet packages
    /// </summary>
    public async Task<BuildResult> RestoreAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        string fullProjectPath;
        try
        {
            fullProjectPath = NormalizeProjectPath(projectPath);
        }
        catch (ArgumentException ex)
        {
            return ValidationFailure("RESTORE002", ex.Message, stopwatch.Elapsed);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(fullProjectPath) ?? Environment.CurrentDirectory
            };
            startInfo.ArgumentList.Add("restore");
            startInfo.ArgumentList.Add(fullProjectPath);
            ChildProcess.Prepare(startInfo);

            using var process = new Process { StartInfo = startInfo };
            var output = new List<string>();

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    output.Add(e.Data);
                }
            };

            process.Start();
            process.StandardInput.Close(); // don't inherit the MCP stdio pipe
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);
            stopwatch.Stop();

            return new BuildResult(
                Success: process.ExitCode == 0,
                ExitCode: process.ExitCode,
                Duration: stopwatch.Elapsed,
                Warnings: 0,
                Errors: process.ExitCode == 0 ? 0 : 1,
                Output: string.Join(Environment.NewLine, output),
                Diagnostics: Array.Empty<BuildDiagnostic>());
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new BuildResult(
                Success: false,
                ExitCode: -1,
                Duration: stopwatch.Elapsed,
                Warnings: 0,
                Errors: 1,
                Output: ex.Message,
                Diagnostics: new[] { new BuildDiagnostic(DiagnosticSeverity.Error, "RESTORE001", ex.Message) });
        }
    }

    /// <summary>
    /// Checks every value in <paramref name="options"/> that names something (framework, runtime,
    /// configuration, MSBuild property names) before it reaches a process argument. Called once up front
    /// so build/clean fail fast with a clear message instead of handing dotnet a value that could be
    /// misread as another option or, for a property value, another MSBuild property.
    /// </summary>
    private static string? ValidateOptions(BuildOptions options) =>
        DotnetArgumentValidation.ValidateFramework(options.Framework)
        ?? DotnetArgumentValidation.ValidateRuntime(options.Runtime)
        ?? DotnetArgumentValidation.ValidateConfiguration(options.Configuration)
        ?? (options.Properties?.Keys.Select(DotnetArgumentValidation.ValidatePropertyName).FirstOrDefault(e => e != null));

    /// <summary>Resolves and validates a caller-supplied project/solution path. Doesn't restrict it to any
    /// directory (building another project on disk is a legitimate use), just normalizes it and rejects
    /// what isn't a usable path.</summary>
    private static string NormalizeProjectPath(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            throw new ArgumentException("projectPath is required.");
        try
        {
            return Path.GetFullPath(projectPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException($"Invalid projectPath '{projectPath}': {ex.Message}");
        }
    }

    private static BuildResult ValidationFailure(string code, string message, TimeSpan elapsed) => new(
        Success: false,
        ExitCode: -1,
        Duration: elapsed,
        Warnings: 0,
        Errors: 1,
        Output: message,
        Diagnostics: new[] { new BuildDiagnostic(DiagnosticSeverity.Error, code, message) });

    /// <summary>
    /// Builds the dotnet CLI arguments for build/clean as a list: each value becomes exactly one
    /// <see cref="ProcessStartInfo.ArgumentList"/> entry, so .NET does the quoting and a value like
    /// "1.0 -p:CustomBeforeMicrosoftCommonTargets=C:\evil.targets" can never be split into extra
    /// arguments the way it would be if concatenated into a single argument string. Assumes
    /// <see cref="ValidateOptions"/> already passed.
    /// </summary>
    public static List<string> BuildArgumentList(string command, string projectPath, BuildOptions options)
    {
        var args = new List<string> { command, projectPath };

        if (options.Configuration != null) { args.Add("--configuration"); args.Add(options.Configuration); }
        if (options.Framework != null) { args.Add("--framework"); args.Add(options.Framework); }
        if (options.Runtime != null) { args.Add("--runtime"); args.Add(options.Runtime); }
        if (options.NoBuild) args.Add("--no-build");
        if (options.NoRestore) args.Add("--no-restore");

        if (options.Verbosity != null)
        {
            var verbosity = options.Verbosity.Value switch
            {
                0 => "quiet",
                1 => "minimal",
                2 => "normal",
                3 => "detailed",
                _ => "diagnostic"
            };
            args.Add("--verbosity");
            args.Add(verbosity);
        }

        if (options.Properties != null)
        {
            foreach (var prop in options.Properties)
            {
                args.Add($"-p:{prop.Key}={DotnetArgumentValidation.EscapePropertyValue(prop.Value)}");
            }
        }

        return args;
    }

    private static List<BuildDiagnostic> ParseDiagnostics(string output)
    {
        var diagnostics = new List<BuildDiagnostic>();
        var matches = DiagnosticRegex.Matches(output);

        foreach (Match match in matches)
        {
            var severity = match.Groups["severity"].Value.ToLower() == "error"
                ? DiagnosticSeverity.Error
                : DiagnosticSeverity.Warning;

            diagnostics.Add(new BuildDiagnostic(
                Severity: severity,
                Code: match.Groups["code"].Value,
                Message: match.Groups["message"].Value.Trim(),
                FilePath: match.Groups["file"].Value,
                Line: int.Parse(match.Groups["line"].Value),
                Column: int.Parse(match.Groups["column"].Value)));
        }

        return diagnostics;
    }

    private static (int Warnings, int Errors) CountDiagnostics(List<BuildDiagnostic> diagnostics)
    {
        var warnings = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
        var errors = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        return (warnings, errors);
    }
}
