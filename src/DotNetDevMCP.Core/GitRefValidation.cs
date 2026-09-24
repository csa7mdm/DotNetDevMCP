// Copyright (c) 2025 Ahmed Mustafa

namespace DotNetDevMCP.Core;

/// <summary>
/// Validates a git ref/branch/remote name supplied by a caller before it becomes a process argument.
/// Git treats any token starting with '-' as an option rather than a ref, so an unvalidated value like
/// "--upload-pack=x" (as a remote) or "--output=C:/x.txt" (as a diff base) gets parsed as a flag instead
/// of the ref/path it looks like. Rejecting a leading '-' and control characters keeps every well-formed
/// ref, branch and remote name working while closing that off. Shared by GitService (SourceControl) and
/// the git-diff path in TestingTools (Testing), both of which take a caller-supplied ref.
/// </summary>
public static class GitRefValidation
{
    public static string? Validate(string? value, string paramName)
    {
        if (value is null) return null;
        if (value.Length == 0) return $"{paramName} must not be empty.";
        if (value[0] == '-') return $"Invalid {paramName} '{value}': must not start with '-' (would be parsed as a git option).";
        if (value.Any(char.IsControl)) return $"Invalid {paramName} '{value}': control characters are not allowed.";
        return null;
    }
}
