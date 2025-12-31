// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Models;

namespace DotNetDevMCP.Core.Interfaces;

/// <summary>
/// Service for solution-level test orchestration and parallel execution
/// </summary>
public interface ISolutionTestingService
{
    /// <summary>
    /// Discovers all tests in a solution
    /// </summary>
    Task<TestDiscoveryResult> DiscoverTestsAsync(string solutionPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes tests in parallel across projects
    /// </summary>
    Task<TestExecutionResult> ExecuteTestsAsync(TestDiscoveryResult tests, SolutionTestExecutionOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects tests affected by code changes
    /// </summary>
    Task<IEnumerable<string>> SelectAffectedTestsAsync(string solutionPath, IEnumerable<string> changedFiles, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes test coverage
    /// </summary>
    Task<CoverageAnalysis> AnalyzeCoverageAsync(string solutionPath, CancellationToken cancellationToken = default);
}
