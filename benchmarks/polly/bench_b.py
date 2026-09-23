# Benchmark B: wall-clock of dotnet_test_affected (selection + run, no build) vs the full `dotnet test --no-build` (64.5 s, measured).
import sys, os, json, subprocess, time
here = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, here); from mcpc import Mcp, server_cmd
repo = os.path.join(here, "bench", "Polly"); sln = os.path.join(repo, "Polly.slnx")
git = lambda *a: subprocess.run(["git", *a], cwd=repo, capture_output=True, text=True).stdout
PICKS = {"482bdf82": "small", "e984839b": "medium", "7a1d10f4": "large", "a14508b9": "fallback (40 files)"}

m = Mcp(server_cmd(here), cwd=here)
m.call("SharpTool_LoadSolution", solutionPath=sln)
rows = []
for fw in [None, "net10.0"]:
    args = ["dotnet", "test", "--solution", "Polly.slnx", "--no-build"] + (["--framework", fw] if fw else [])
    t = time.perf_counter(); r = subprocess.run(args, cwd=repo, capture_output=True, text=True); dt = time.perf_counter() - t
    total = next((l.split(":")[1].strip() for l in r.stdout.splitlines() if l.strip().startswith("total:")), "?")
    row = dict(sha="-", label=f"FULL suite ({fw or 'all TFMs'})", rep=1, seconds=round(dt, 1), executed=total); rows.append(row); print(row, flush=True)
for (sha, label), fw in [(p, fw) for fw in [None, "net10.0"] for p in PICKS.items()]:
    files = [os.path.join(repo, f) for f in git("diff-tree", "--no-commit-id", "--name-only", "-r", sha).splitlines()
             if f.startswith("src/") and f.endswith(".cs") and os.path.exists(os.path.join(repo, f))]
    for rep in range(2):  # second repetition shows the warm-cache number an agent sees mid-session
        kw = {"framework": fw} if fw else {}
        t = time.perf_counter(); _, txt = m.call("dotnet_test_affected", changedFiles=files, noBuild=True, **kw); dt = time.perf_counter() - t
        d = json.loads(txt); run = d.get("run") or {}
        row = dict(sha=sha, label=f"{label} ({fw or 'all TFMs'})", rep=rep + 1, seconds=round(dt, 1), selected_methods=len(d.get("affectedTests", [])),
                   complete=d.get("selectionComplete"), executed=run.get("totalTests"), failed=run.get("failedTests"), error=(run.get("error") or "")[:200])
        rows.append(row); print(row, flush=True)
m.close()
json.dump(rows, open(os.path.join(here, "bench", "b-timing.json"), "w"), indent=1)
