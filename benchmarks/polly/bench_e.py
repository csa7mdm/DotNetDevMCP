# Benchmark E: the five improvements, measured on Polly.
#  1. first selection after load: immediately (cold) vs after the background warm-up had time to run
#  2. big selections run the whole solution instead of a slower filtered run
#  3. tool count by default
import sys, os, json, subprocess, time
here = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, here); from mcpc import Mcp, server_cmd
repo = os.path.join(here, "bench", "Polly"); sln = os.path.join(repo, "Polly.slnx")
cmd = server_cmd(here)
git = lambda *a: subprocess.run(["git", *a], cwd=repo, capture_output=True, text=True).stdout
COLD_FILES = ["src/Polly/Wrap/IAsyncPolicyPolicyWrapExtensions.cs", "src/Polly.Core/ResiliencePipeline.SyncT.cs"]
rows = []
def log(row): rows.append(row); print(row, flush=True)

m = Mcp(cmd, cwd=here)
log(dict(check="tools by default", count=len(m.req("tools/list")["result"]["tools"])))
m.close()

for wait in (0, 90):
    for f in COLD_FILES:
        m = Mcp(cmd, cwd=here)
        dt_load, txt = m.call("SharpTool_LoadSolution", solutionPath=sln)
        time.sleep(wait)
        dt, txt = m.call("dotnet_test_affected", changedFiles=[os.path.join(repo, f)], dryRun=True)
        d = json.loads(txt)
        log(dict(check=f"first selection, {wait}s after load", file=f.split("/")[-1], load_s=round(dt_load, 1), seconds=round(dt, 1),
                 complete=d.get("selectionComplete"), selected=len(d.get("affectedTests", []))))
        m.close()

m = Mcp(cmd, cwd=here)
m.call("SharpTool_LoadSolution", solutionPath=sln)
for sha, label in {"482bdf82": "small", "e984839b": "medium", "7a1d10f4": "large"}.items():
    files = [os.path.join(repo, f) for f in git("diff-tree", "--no-commit-id", "--name-only", "-r", sha).splitlines()
             if f.startswith("src/") and f.endswith(".cs") and os.path.exists(os.path.join(repo, f))]
    m.call("dotnet_test_affected", changedFiles=files, dryRun=True)  # warm this selection
    t = time.perf_counter(); _, txt = m.call("dotnet_test_affected", changedFiles=files, noBuild=True, framework="net10.0"); dt = time.perf_counter() - t
    d = json.loads(txt); run = d.get("run") or {}
    log(dict(check="run net10.0", label=label, seconds=round(dt, 1), selected=len(d.get("affectedTests", [])), total_methods=d.get("totalTestMethods"),
             ran_whole_solution=d.get("ranWholeSolution"), executed=run.get("totalTests"), failed=run.get("failedTests")))
m.close()
json.dump(rows, open(os.path.join(here, "bench", "e-improvements.json"), "w"), indent=1)
