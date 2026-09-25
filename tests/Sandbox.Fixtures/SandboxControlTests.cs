namespace Sandbox.Fixtures;

/// <summary>
/// Asserts the container's sandbox controls directly, by reading the kernel's own
/// bookkeeping (/proc, /sys/fs/cgroup) rather than by attempting an attack and
/// timing out. That makes these deterministic and fast: no forked child
/// processes, no waiting out a memory-limit kill, no depending on how quickly a
/// blocked network connection fails.
///
/// Run with the documented sandbox flags (--network none --memory 8g
/// --memory-swap 8g --pids-limit 512 --cap-drop ALL --security-opt
/// no-new-privileges; see README/SECURITY.md "Run it in a container" and the CI
/// "sandbox" job), every assertion here should hold and the suite passes.
///
/// Run the same image WITHOUT those flags (plain `docker run`), several of these
/// assertions are expected to FAIL - that's the negative control that proves the
/// assertions are reading something the flags actually change, not a constant.
///
/// These read Linux-only files (/proc/self/status, /sys/fs/cgroup, /proc/self/mountinfo,
/// /sys/class/net) that don't exist on Windows, so every test is a
/// <see cref="LinuxFact"/> and is skipped (not faked as passing) on a Windows host.
/// </summary>
public class SandboxControlTests
{
    /// <summary>
    /// Where the NuGet global package cache is mounted from the host. Defaults to
    /// the image's built-in mcp user's home (the primary, Docker Desktop /
    /// Windows / macOS documented form: `-v ...:/home/mcp/.nuget/packages`).
    /// The native-Linux documented form runs the container with
    /// `--user "$(id -u):$(id -g)"` and passes `-e NUGET_PACKAGES=/nuget
    /// -v ...:/nuget` instead, since uid 10001 isn't a real account for that
    /// process - reading the same env var dotnet/NuGet itself honors means this
    /// one test file works unchanged against either documented command.
    /// </summary>
    private static string PackageCacheDir =>
        Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? "/home/mcp/.nuget/packages";

    private static string Status(string key) =>
        File.ReadLines("/proc/self/status").First(l => l.StartsWith(key + ":", StringComparison.Ordinal))
            .Split(':', 2)[1].Trim();

    private static string Cg(string file) =>
        File.ReadAllText(Path.Combine("/sys/fs/cgroup", file)).Trim();

    [LinuxFact]
    public void CapabilityBoundingSetIsEmpty()
    {
        // --cap-drop ALL should leave nothing in the bounding set: a process
        // can never gain back a capability that isn't in its bounding set, even
        // via a setuid binary, so this is a stronger claim than "capabilities
        // aren't currently in effect right now".
        Assert.Equal(0UL, Convert.ToUInt64(Status("CapBnd"), 16));
    }

    [LinuxFact]
    public void NoNewPrivsIsSet()
    {
        // --security-opt no-new-privileges sets this bit for the whole process
        // tree; once set it can never be unset, and it blocks setuid/setgid
        // execve() and file capabilities from granting anything beyond what
        // the process already has.
        Assert.Equal("1", Status("NoNewPrivs"));
    }

    [LinuxFact]
    public void MemoryIsCappedAt8GiBWithNoSwap()
    {
        // --memory 8g alone still allows Docker's default extra swap (2x
        // memory) on top; --memory-swap 8g pins memory+swap together so
        // memory.swap.max collapses to 0. Both flags together are what
        // README/SECURITY.md document, and both are needed for this to hold.
        Assert.Equal("8589934592", Cg("memory.max")); // 8 GiB
        Assert.Equal("0", Cg("memory.swap.max"));
    }

    [LinuxFact]
    public void PidsAreCappedAt512()
    {
        // --pids-limit 512. Note this counts threads, not just processes: a
        // build with heavy thread-per-core parallelism can approach this on a
        // large-core-count host well before spawning anywhere near 512
        // *processes* - see SECURITY.md for the measured number on this repo.
        Assert.Equal("512", Cg("pids.max"));
    }

    [LinuxFact]
    public void OnlyLoopbackInterfaceExists()
    {
        // --network none: no veth/eth0, just the loopback interface every
        // network namespace gets regardless.
        Assert.Equal(["lo"], Directory.GetDirectories("/sys/class/net").Select(Path.GetFileName));
    }

    [LinuxFact]
    public void OnlyTheWorkspaceAndPackageCacheAreMountedFromTheHost()
    {
        string[] virtualFs = ["proc", "sysfs", "tmpfs", "cgroup2", "devpts", "mqueue", "overlay"];
        var hostMounts = File.ReadLines("/proc/self/mountinfo")
            .Select(l => l.Split(' '))
            .Where(f => !virtualFs.Contains(f[Array.IndexOf(f, "-") + 1]))
            .Select(f => f[4])
            // Docker always injects these three from the host regardless of
            // -v flags; they're not something the caller chose to mount.
            .Where(p => p is not ("/etc/hosts" or "/etc/hostname" or "/etc/resolv.conf"))
            .Order()
            .ToArray();

        var expected = new[] { PackageCacheDir, "/src" }.Order().ToArray();
        Assert.Equal(expected, hostMounts);

        // Mounting the host's docker.sock would hand the container the same
        // privileges as the host's Docker daemon (root-equivalent); it should
        // never be present regardless of which form the caller used.
        Assert.False(File.Exists("/var/run/docker.sock"));
    }

    [LinuxFact]
    public void WorkspaceWritableButServerAndPackageCacheAreNot()
    {
        // Running as non-root at all (either the image's built-in uid 10001,
        // or the caller's own uid via `--user`) - never root.
        Assert.NotEqual("0", Status("Uid").Split('\t')[0]);

        // /src is writable by design: the whole point of the server is to
        // build, test, and apply edits to the mounted repo.
        var probe = Path.Combine("/src", ".sandbox-probe");
        File.WriteAllText(probe, "x");
        File.Delete(probe);

        // The server's own files should never be writable from inside the
        // container it serves.
        Assert.ThrowsAny<Exception>(() => File.WriteAllText("/app/probe", "x"));

        // Once restore has populated it, the package cache is mounted `:ro`
        // for the actual server run (see README/SECURITY.md "Run it in a
        // container"), specifically so one repo's build can't plant a
        // malicious .targets/.props file in a shared package that would then
        // run during some other repo's build against the same cache volume.
        Assert.ThrowsAny<Exception>(() => File.WriteAllText(Path.Combine(PackageCacheDir, "probe"), "x"));
    }
}

/// <summary>
/// An xUnit v2 [Fact] that skips (with an explicit reason, not a silent pass)
/// when the current OS isn't Linux. All of SandboxControlTests reads
/// Linux-only kernel interfaces (cgroups, /proc, /sys/class/net) that have no
/// Windows equivalent, so there is nothing meaningful to assert there.
/// </summary>
internal sealed class LinuxFact : FactAttribute
{
    public LinuxFact()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip = "Linux-only: exercises /proc and /sys/fs/cgroup, which don't exist on this OS.";
        }
    }
}
