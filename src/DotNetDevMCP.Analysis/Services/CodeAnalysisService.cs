using System.Collections.Concurrent;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using NuGet.ProjectModel;
using DotNetDevMCP.Analysis.Models;

namespace DotNetDevMCP.Analysis.Services;

/// <summary>
/// Service for analyzing .NET code projects and solutions
/// </summary>
public sealed class CodeAnalysisService(ILogger<CodeAnalysisService> logger)
{
    private readonly ILogger<CodeAnalysisService> _logger = logger;
    private readonly ProjectCollection _projectCollection = new();

    /// <summary>
    /// Analyzes a project or solution and returns comprehensive metrics
    /// </summary>
    public async Task<AnalysisResult> AnalyzeAsync(string path, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting analysis of {Path}", path);
        
        var files = Directory.Exists(path) 
            ? Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)
            : [path];

        var totalLines = 0;
        var codeLines = 0;
        var commentLines = 0;
        var blankLines = 0;
        var classes = 0;
        var methods = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            var lines = content.Split('\n');
            
            totalLines += lines.Length;
            blankLines += lines.Count(l => string.IsNullOrWhiteSpace(l));
            commentLines += lines.Count(l => l.Trim().StartsWith("//") || l.Trim().StartsWith("/*") || l.Trim().StartsWith("*"));
            codeLines += lines.Length - blankLines - commentLines;
            
            classes += CountOccurrences(content, "class ") + CountOccurrences(content, "record ");
            methods += CountOccurrences(content, "void ") + CountOccurrences(content, "async ") + CountOccurrences(content, "public ") + CountOccurrences(content, "private ");
        }

        var result = new AnalysisResult(
            ProjectPath: path,
            TotalFiles: files.Length,
            TotalLines: totalLines,
            CodeLines: codeLines,
            CommentLines: commentLines,
            BlankLines: blankLines,
            Classes: classes,
            Methods: methods,
            MaintainabilityIndex: CalculateMaintainabilityIndex(codeLines, classes, methods),
            CyclomaticComplexity: EstimateCyclomaticComplexity(files),
            AnalyzedAt: DateTime.UtcNow
        );

        _logger.LogInformation("Analysis complete: {Files} files, {Lines} lines", files.Length, totalLines);
        return result;
    }

    /// <summary>
    /// Gets dependency information for a project
    /// </summary>
    public async Task<DependencyInfo> GetDependenciesAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing dependencies for {Path}", projectPath);

        var packageDependencies = new List<PackageDependency>();
        var projectReferences = new List<ProjectReference>();
        var frameworkReferences = new List<string>();

        if (File.Exists(projectPath))
        {
            var content = await File.ReadAllTextAsync(projectPath, cancellationToken);
            
            // Parse package references
            var packageRefs = System.Text.RegularExpressions.Regex.Matches(
                content, 
                @"<PackageReference\s+Include=""([^""]+)""\s+Version=""([^""]+)""");
            
            foreach (System.Text.RegularExpressions.Match match in packageRefs)
            {
                packageDependencies.Add(new PackageDependency(
                    Name: match.Groups[1].Value,
                    Version: match.Groups[2].Value,
                    IsDirect: true,
                    IsDevelopmentDependency: false
                ));
            }

            // Parse project references
            var projectRefs = System.Text.RegularExpressions.Regex.Matches(
                content,
                @"<ProjectReference\s+Include=""([^""]+)""");
            
            foreach (System.Text.RegularExpressions.Match match in projectRefs)
            {
                var refPath = match.Groups[1].Value;
                projectReferences.Add(new ProjectReference(
                    Name: Path.GetFileNameWithoutExtension(refPath),
                    Path: refPath,
                    IsProjectReference: true
                ));
            }

            // Parse framework references
            var frameworkRefs = System.Text.RegularExpressions.Regex.Matches(
                content,
                @"<FrameworkReference\s+Include=""([^""]+)""");
            
            foreach (System.Text.RegularExpressions.Match match in frameworkRefs)
            {
                frameworkReferences.Add(match.Groups[1].Value);
            }
        }

        return new DependencyInfo(
            ProjectPath: projectPath,
            PackageDependencies: packageDependencies.AsReadOnly(),
            ProjectReferences: projectReferences.AsReadOnly(),
            FrameworkReferences: frameworkReferences.AsReadOnly(),
            HasCircularDependencies: false, // TODO: Implement cycle detection
            AnalyzedAt: DateTime.UtcNow
        );
    }

    /// <summary>
    /// Analyzes code quality and returns metrics
    /// </summary>
    public async Task<CodeQualityMetrics> AnalyzeQualityAsync(string path, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing code quality for {Path}", path);
        
        var issues = new List<CodeIssue>();
        
        // Basic code smell detection
        var files = Directory.Exists(path)
            ? Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)
            : [path];

        int codeSmells = 0;
        int bugs = 0;
        int vulnerabilities = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            
            // Detect long methods
            var methods = System.Text.RegularExpressions.Regex.Matches(content, @"\{[^{}]*\}");
            foreach (System.Text.RegularExpressions.Match method in methods)
            {
                var lineCount = method.Value.Count(c => c == '\n');
                if (lineCount > 50)
                {
                    codeSmells++;
                    issues.Add(new CodeIssue(
                        RuleId: "CS0001",
                        Message: "Method is too long (>50 lines)",
                        Severity: "Warning",
                        FilePath: file,
                        LineNumber: 0,
                        ColumnNumber: 0,
                        Suggestion: "Consider breaking this method into smaller methods"
                    ));
                }
            }

            // Detect potential null reference issues
            if (content.Contains(".ToString()") && !content.Contains("?."))
            {
                bugs++;
                issues.Add(new CodeIssue(
                    RuleId: "CS0002",
                    Message: "Potential NullReferenceException",
                    Severity: "Warning",
                    FilePath: file,
                    LineNumber: 0,
                    ColumnNumber: 0,
                    Suggestion: "Use null-conditional operator (?.)"
                ));
            }
        }

        return new CodeQualityMetrics(
            ProjectPath: path,
            TechnicalDebtHours: codeSmells * 0.1,
            CodeSmells: codeSmells,
            Bugs: bugs,
            Vulnerabilities: vulnerabilities,
            Coverage: 0.0, // Would need test coverage tool integration
            Duplication: 0.0, // Would need duplication detection
            Issues: issues.AsReadOnly(),
            AnalyzedAt: DateTime.UtcNow
        );
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    private static double CalculateMaintainabilityIndex(int codeLines, int classes, int methods)
    {
        // Simplified maintainability index calculation
        if (codeLines == 0) return 100.0;
        
        var avgMethodLength = codeLines / Math.Max(1, methods);
        var avgMethodsPerClass = methods / Math.Max(1, classes);
        
        var index = 171.0 - 5.2 * Math.Log(avgMethodLength) - 0.23 * avgMethodsPerClass - 16.2 * Math.Log(codeLines);
        return Math.Max(0, Math.Min(100, index * 100 / 171));
    }

    private static double EstimateCyclomaticComplexity(string[] files)
    {
        var totalComplexity = 0;
        foreach (var file in files)
        {
            if (!File.Exists(file)) continue;
            
            var content = File.ReadAllText(file);
            totalComplexity += CountOccurrences(content, "if ");
            totalComplexity += CountOccurrences(content, "else ");
            totalComplexity += CountOccurrences(content, "for ");
            totalComplexity += CountOccurrences(content, "while ");
            totalComplexity += CountOccurrences(content, "case ");
            totalComplexity += CountOccurrences(content, "catch ");
            totalComplexity += CountOccurrences(content, "&& ");
            totalComplexity += CountOccurrences(content, "|| ");
        }
        return files.Length > 0 ? (double)totalComplexity / files.Length : 0;
    }

    public void Dispose() => _projectCollection.Dispose();
}
