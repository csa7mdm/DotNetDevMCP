// Copyright (c) 2025 Ahmed Mustafa

using BenchmarkDotNet.Running;

namespace DotNetDevMCP.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("DotNetDevMCP Performance Benchmarks");
        Console.WriteLine("===================================");
        Console.WriteLine();
        Console.WriteLine("Running comprehensive benchmarks to measure orchestration performance.");
        Console.WriteLine("This will take several minutes...");
        Console.WriteLine();

        // Run all benchmarks
        var summary1 = BenchmarkRunner.Run<OrchestrationBenchmarks>();
        var summary2 = BenchmarkRunner.Run<BatchOperationBenchmarks>();
        var summary3 = BenchmarkRunner.Run<WorkflowBenchmarks>();

        Console.WriteLine();
        Console.WriteLine("Benchmarks completed! Check BenchmarkDotNet.Artifacts for detailed results.");
    }
}
