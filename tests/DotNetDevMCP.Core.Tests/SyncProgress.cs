namespace DotNetDevMCP.Core.Tests;

/// <summary>Synchronous IProgress: Progress&lt;T&gt; posts callbacks asynchronously, which races with assertions.</summary>
internal sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
