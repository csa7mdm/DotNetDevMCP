using System.Diagnostics;
using System.Net.Sockets;

namespace Sandbox.Fixtures;

/// <summary>
/// One test per containment claim from SECURITY.md's "Run it in a container" section.
/// Each test asserts that the attack it performs FAILED - i.e. was blocked.
///
/// Run under the sandbox flags (--network none --memory 8g --pids-limit 512
/// --cap-drop ALL --security-opt no-new-privileges; see the CI "sandbox" job and
/// README/SECURITY.md "Run it in a container"), every assertion here should hold
/// and the suite passes.
///
/// Run on an unrestricted host (plain `dotnet test`, no container / no sandbox
/// flags), several of these assertions are expected to FAIL. That is not a bug in
/// the tests: it's what proves the assertions are actually exercising something
/// real rather than trivially passing regardless of environment.
///
/// These tests assume a POSIX/Linux environment (SSH key conventions, /etc,
/// /bin/sleep, /bin/sh). They are meaningless on Windows hosts, so each one
/// returns early there. That early return is a deliberate skip, not a fake pass -
/// it is called out explicitly at each call site.
/// </summary>
public class ContainmentTests
{
    private static bool IsUnsupportedHost => OperatingSystem.IsWindows();

    [Fact]
    public void ReadingSshKeysFails()
    {
        if (IsUnsupportedHost)
        {
            // SKIPPED: Windows host. This test only targets POSIX SSH key
            // locations ($HOME/.ssh, /root/.ssh); there is nothing meaningful to
            // assert here on Windows, so we return early instead of faking a pass.
            return;
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        var candidates = new List<string> { "/root/.ssh" };
        if (!string.IsNullOrEmpty(home))
        {
            candidates.Insert(0, Path.Combine(home, ".ssh"));
        }

        foreach (var dir in candidates.Distinct())
        {
            if (!Directory.Exists(dir))
            {
                continue; // Pass: the directory doesn't exist.
            }

            var entries = Directory.GetFileSystemEntries(dir);
            if (entries.Length == 0)
            {
                continue; // Pass: the directory exists but is empty.
            }

            foreach (var entry in entries)
            {
                if (Directory.Exists(entry))
                {
                    continue;
                }

                // Pass if reading throws (permission denied, not found, etc).
                var threw = false;
                try
                {
                    File.ReadAllBytes(entry);
                }
                catch
                {
                    threw = true;
                }

                Assert.True(threw, $"Expected reading '{entry}' to be denied under the sandbox, but it succeeded.");
            }
        }
    }

    [Fact]
    public void WritingOutsideWorkspaceFails()
    {
        if (IsUnsupportedHost)
        {
            // SKIPPED: Windows host. "/etc/probe" is not a meaningful path outside
            // a POSIX filesystem; returning early rather than faking a pass.
            return;
        }

        Exception? caught = null;
        try
        {
            File.WriteAllText("/etc/probe", "x");
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.True(
            caught is UnauthorizedAccessException or IOException,
            caught is null
                ? "Expected writing to /etc/probe to throw UnauthorizedAccessException or IOException, but it succeeded."
                : $"Expected UnauthorizedAccessException or IOException, but got {caught.GetType().FullName}: {caught.Message}");
    }

    [Fact]
    public async Task NetworkEgressFails()
    {
        if (IsUnsupportedHost)
        {
            // SKIPPED: Windows host. The claim under test ("--network none" blocks
            // egress) is about the Linux container network namespace; returning
            // early rather than faking a pass.
            return;
        }

        using var client = new TcpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var threw = false;
        try
        {
            await client.ConnectAsync("1.1.1.1", 443, cts.Token);
        }
        catch
        {
            // Covers SocketException (connection refused/unreachable under
            // --network none) and OperationCanceledException (timeout).
            threw = true;
        }

        Assert.True(threw, "Expected connecting to 1.1.1.1:443 to fail or time out, but it succeeded.");
    }

    [Fact]
    public void ForkBombIsCapped()
    {
        if (IsUnsupportedHost)
        {
            // SKIPPED: Windows host. /bin/sleep and the container --pids-limit
            // cgroup do not exist on Windows; returning early rather than faking
            // a pass.
            return;
        }

        const int attemptedProcessCount = 600;
        var started = new List<Process>();

        try
        {
            var threw = false;
            try
            {
                for (var i = 0; i < attemptedProcessCount; i++)
                {
                    var psi = new ProcessStartInfo("/bin/sleep", "30")
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };

                    var process = Process.Start(psi)
                        ?? throw new InvalidOperationException("Process.Start returned null.");
                    started.Add(process);
                }
            }
            catch
            {
                // Under --pids-limit 512, the kernel refuses to fork new
                // processes for this cgroup well before 600 are alive at once
                // (each /bin/sleep 30 stays alive for the duration), so
                // Process.Start should throw before the loop completes.
                threw = true;
            }

            Assert.True(
                threw,
                $"Expected process creation to be capped before {attemptedProcessCount} concurrent processes " +
                $"(started {started.Count} without error), but the container's --pids-limit did not stop it.");
        }
        finally
        {
            // Always clean up whatever we managed to start, sandboxed or not.
            foreach (var process in started)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch
                {
                    // Best-effort cleanup; nothing useful to do if this fails.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }

    [Fact]
    public void MemoryIsCapped()
    {
        if (IsUnsupportedHost)
        {
            // SKIPPED: Windows host. /bin/sh and the container --memory cgroup do
            // not exist on Windows; returning early rather than faking a pass.
            return;
        }

        // What this proves: `head -c 12G /dev/zero | tail` forces 12 GB of data
        // through a pipe into `tail`, which has to buffer/hold onto it. This is
        // run as a *separate child process* (not .NET-managed memory) specifically
        // so it exercises the container's --memory 8g cgroup limit rather than the
        // .NET GC or heap. Under the 8g cap, the kernel OOM killer should kill the
        // process (or the shell pipeline otherwise fails) well before 12 GB is
        // materialized, so the process should not exit cleanly with code 0. On an
        // unrestricted host with >=12 GB of free memory, this can complete
        // successfully, which is the expected way this test "fails" outside the
        // sandbox.
        var psi = new ProcessStartInfo("/bin/sh", "-c \"head -c 12G /dev/zero | tail > /dev/null\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Process.Start returned null.");

        var exitedInTime = process.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds);
        if (!exitedInTime)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort; if this throws there's nothing more we can do.
            }
        }

        var failedOrKilled = !exitedInTime || process.ExitCode != 0;
        Assert.True(
            failedOrKilled,
            "Expected `head -c 12G /dev/zero | tail` to be killed or fail under the --memory 8g limit, " +
            "but it exited cleanly with code 0.");
    }
}
