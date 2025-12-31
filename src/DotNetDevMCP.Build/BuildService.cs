// Copyright (c) 2025 Ahmed Mustafa

using System.Diagnostics;
using System.Text.RegularExpressions;

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

        try
        {
            var arguments = BuildArguments("build", projectPath, options);

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory
            };

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

        try
        {
            var arguments = BuildArguments("clean", projectPath, options);

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

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

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"restore \"{projectPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

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

    private static string BuildArguments(string command, string projectPath, BuildOptions options)
    {
        var args = new List<string> { command, $"\"{projectPath}\"" };

        if (options.Configuration != null)
            args.Add($"--configuration {options.Configuration}");

        if (options.Framework != null)
            args.Add($"--framework {options.Framework}");

        if (options.Runtime != null)
            args.Add($"--runtime {options.Runtime}");

        if (options.NoBuild)
            args.Add("--no-build");

        if (options.NoRestore)
            args.Add("--no-restore");

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
            args.Add($"--verbosity {verbosity}");
        }

        if (options.Properties != null)
        {
            foreach (var prop in options.Properties)
            {
                args.Add($"-p:{prop.Key}={prop.Value}");
            }
        }

        return string.Join(" ", args);
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
