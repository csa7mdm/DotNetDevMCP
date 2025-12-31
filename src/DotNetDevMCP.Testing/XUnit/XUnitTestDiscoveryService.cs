// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Models;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace DotNetDevMCP.Testing.XUnit;

/// <summary>
/// Discovers xUnit tests from compiled assemblies
/// </summary>
public class XUnitTestDiscoveryService
{
    /// <summary>
    /// Discover all xUnit tests in the specified assembly
    /// </summary>
    public async Task<IEnumerable<TestCase>> DiscoverAsync(
        string assemblyPath,
        TestDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"Test assembly not found: {assemblyPath}");
        }

        var testCases = new List<TestCase>();

        await Task.Run(() =>
        {
            try
            {
                // Change working directory to the test assembly location
                // so xUnit can find its execution dependencies
                var originalDirectory = Directory.GetCurrentDirectory();
                var assemblyDirectory = Path.GetDirectoryName(assemblyPath)
                    ?? throw new InvalidOperationException("Could not determine assembly directory");

                try
                {
                    Directory.SetCurrentDirectory(assemblyDirectory);

                    // Use xUnit's discovery mechanism
                    using var framework = new XunitFrontController(
                        AppDomainSupport.Denied,
                        assemblyPath,
                        configFileName: null,
                        shadowCopy: true,
                        diagnosticMessageSink: new NullMessageSink());

                var discoveryOptions = TestFrameworkOptions.ForDiscovery();
                var discoverySink = new TestDiscoverySink();

                framework.Find(
                    includeSourceInformation: false,
                    discoverySink,
                    discoveryOptions);

                discoverySink.Finished.WaitOne();

                // Convert discovered tests to our TestCase model
                foreach (var testCase in discoverySink.TestCases)
                {
                    var tc = new TestCase(
                        FullyQualifiedName: testCase.TestMethod.Method.Name,
                        DisplayName: testCase.DisplayName,
                        Framework: TestFramework.XUnit,
                        AssemblyPath: assemblyPath,
                        Category: GetTraitValue(testCase, "Category"),
                        IsSkipped: testCase.SkipReason != null,
                        SkipReason: testCase.SkipReason,
                        Traits: GetTraits(testCase));

                    // Apply filters if provided
                    if (options != null && !PassesFilter(tc, options))
                    {
                        continue;
                    }

                    testCases.Add(tc);
                }
                }
                finally
                {
                    // Restore original directory
                    Directory.SetCurrentDirectory(originalDirectory);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to discover tests in assembly: {assemblyPath}", ex);
            }
        }, cancellationToken);

        return testCases;
    }

    /// <summary>
    /// Detect if the assembly contains xUnit tests
    /// </summary>
    public bool IsXUnitAssembly(string assemblyPath)
    {
        try
        {
            var assembly = Assembly.LoadFrom(assemblyPath);
            var referencedAssemblies = assembly.GetReferencedAssemblies();

            return referencedAssemblies.Any(a =>
                a.Name != null &&
                (a.Name.Equals("xunit.core", StringComparison.OrdinalIgnoreCase) ||
                 a.Name.Equals("xunit.assert", StringComparison.OrdinalIgnoreCase)));
        }
        catch
        {
            return false;
        }
    }

    private static string? GetTraitValue(ITestCase testCase, string traitName)
    {
        if (testCase.Traits.TryGetValue(traitName, out var values))
        {
            return values.FirstOrDefault();
        }
        return null;
    }

    private static Dictionary<string, string> GetTraits(ITestCase testCase)
    {
        var traits = new Dictionary<string, string>();
        foreach (var trait in testCase.Traits)
        {
            traits[trait.Key] = string.Join(", ", trait.Value);
        }
        return traits;
    }

    private static bool PassesFilter(TestCase testCase, TestDiscoveryOptions options)
    {
        // Name filter
        if (options.NameFilter != null &&
            !testCase.DisplayName.Contains(options.NameFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Category filter
        if (options.CategoryFilter != null &&
            testCase.Category != options.CategoryFilter)
        {
            return false;
        }

        // Trait filters
        if (options.TraitFilters != null && options.TraitFilters.Any())
        {
            if (testCase.Traits == null)
            {
                return false;
            }

            foreach (var filter in options.TraitFilters)
            {
                if (!testCase.Traits.TryGetValue(filter.Key, out var value) ||
                    value != filter.Value)
                {
                    return false;
                }
            }
        }

        // Include skipped tests filter
        if (!options.IncludeSkippedTests && testCase.IsSkipped)
        {
            return false;
        }

        return true;
    }
}

/// <summary>
/// Message sink for test discovery
/// </summary>
internal class TestDiscoverySink : IMessageSink
{
    public List<ITestCase> TestCases { get; } = new();
    public ManualResetEvent Finished { get; } = new(false);

    public bool OnMessage(IMessageSinkMessage message)
    {
        if (message is ITestCaseDiscoveryMessage discoveryMessage)
        {
            TestCases.Add(discoveryMessage.TestCase);
            return true;
        }

        if (message is IDiscoveryCompleteMessage)
        {
            Finished.Set();
        }

        return true;
    }

    public void Dispose()
    {
        Finished.Dispose();
    }
}

/// <summary>
/// Null message sink for diagnostics
/// </summary>
internal class NullMessageSink : IMessageSink
{
    public bool OnMessage(IMessageSinkMessage message) => true;

    public void Dispose() { }
}
