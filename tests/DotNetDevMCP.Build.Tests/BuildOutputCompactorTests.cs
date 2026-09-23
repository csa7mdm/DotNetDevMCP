// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Build;

namespace DotNetDevMCP.Build.Tests;

public class BuildOutputCompactorTests
{
    private static BuildDiagnostic Warning(string code, string file, int line, string message) =>
        new(DiagnosticSeverity.Warning, code, message, file, line, 1);

    private static BuildDiagnostic Error(string code, string file, int line, string message) =>
        new(DiagnosticSeverity.Error, code, message, file, line, 1);

    [Fact]
    public void Compact_returns_all_errors_unfiltered()
    {
        var diagnostics = new[]
        {
            Error("CS0103", @"C:\src\Foo.cs", 10, "The name 'x' does not exist"),
            Error("CS0246", @"C:\src\Bar.cs", 20, "Type or namespace not found"),
        };

        var result = BuildOutputCompactor.Compact(diagnostics);

        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(0, result.WarningCount);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Compact_deduplicates_warnings_with_same_code_file_line_and_message_across_target_frameworks()
    {
        // Same warning reported once per TFM in a multi-targeted build.
        var diagnostics = new[]
        {
            Warning("CS8602", @"C:\src\Foo.cs", 42, "Dereference of a possibly null reference"),
            Warning("CS8602", @"C:\src\Foo.cs", 42, "Dereference of a possibly null reference"),
            Warning("CS8602", @"C:\src\Foo.cs", 42, "Dereference of a possibly null reference"),
        };

        var result = BuildOutputCompactor.Compact(diagnostics);

        Assert.Equal(3, result.WarningCount); // raw count preserved
        Assert.Equal(1, result.UniqueWarningCount);
        Assert.Single(result.Warnings);
        Assert.Equal(0, result.WarningsTruncated);
    }

    [Fact]
    public void Compact_treats_different_line_or_message_as_distinct_warnings()
    {
        var diagnostics = new[]
        {
            Warning("CS8602", @"C:\src\Foo.cs", 42, "Dereference of a possibly null reference"),
            Warning("CS8602", @"C:\src\Foo.cs", 43, "Dereference of a possibly null reference"), // different line
            Warning("CS8602", @"C:\src\Foo.cs", 42, "A different message"), // different message
        };

        var result = BuildOutputCompactor.Compact(diagnostics);

        Assert.Equal(3, result.UniqueWarningCount);
        Assert.Equal(3, result.Warnings.Count);
    }

    [Fact]
    public void Compact_caps_unique_warnings_and_reports_truncated_count()
    {
        var diagnostics = Enumerable.Range(0, 30)
            .Select(i => Warning("CS0168", @"C:\src\Foo.cs", i, $"Unused variable v{i}"))
            .ToArray();

        var result = BuildOutputCompactor.Compact(diagnostics, maxWarnings: 20);

        Assert.Equal(30, result.WarningCount);
        Assert.Equal(30, result.UniqueWarningCount);
        Assert.Equal(20, result.Warnings.Count);
        Assert.Equal(10, result.WarningsTruncated);
    }

    [Fact]
    public void Compact_relativizes_paths_to_base_directory_when_safe()
    {
        var diagnostics = new[]
        {
            Error("CS0103", Path.Combine(ProjectDir, "Foo.cs"), 10, "boom"),
        };

        var result = BuildOutputCompactor.Compact(diagnostics, baseDirectory: ProjectDir);

        Assert.Equal("Foo.cs", result.Errors[0].FilePath);
    }

    [Fact]
    public void RelativizePath_makes_nested_paths_relative()
    {
        var relative = BuildOutputCompactor.RelativizePath(Path.Combine(ProjectDir, "Sub", "Foo.cs"), ProjectDir);
        Assert.Equal(Path.Combine("Sub", "Foo.cs"), relative);
    }

    // Rooted on every OS: C:\repo\src\Project on Windows, /repo/src/Project on Linux.
    private static readonly string ProjectDir = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "repo", "src", "Project");

    [Fact]
    public void RelativizePath_leaves_path_unchanged_when_base_directory_is_null()
    {
        var path = @"C:\repo\src\Foo.cs";
        Assert.Equal(path, BuildOutputCompactor.RelativizePath(path, null));
    }

    [Fact]
    public void RelativizePath_returns_null_or_empty_input_unchanged()
    {
        Assert.Null(BuildOutputCompactor.RelativizePath(null, @"C:\repo"));
        Assert.Equal(string.Empty, BuildOutputCompactor.RelativizePath(string.Empty, @"C:\repo"));
    }

    [Fact]
    public void Tail_returns_only_the_last_N_non_empty_lines()
    {
        var output = string.Join("\n", Enumerable.Range(1, 50).Select(i => $"line {i}"));

        var tail = BuildOutputCompactor.Tail(output, maxLines: 5);
        var lines = tail.Split(Environment.NewLine);

        Assert.Equal(5, lines.Length);
        Assert.Equal("line 46", lines[0]);
        Assert.Equal("line 50", lines[4]);
    }

    [Fact]
    public void Tail_handles_empty_output()
    {
        Assert.Equal(string.Empty, BuildOutputCompactor.Tail(string.Empty));
    }
}
