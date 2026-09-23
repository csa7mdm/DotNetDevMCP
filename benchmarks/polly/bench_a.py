# Benchmark A: replay Polly's last 40 source commits through dotnet_test_affected (dryRun) and record selection size.
import sys, os, json, subprocess, time
here = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, here); from mcpc import Mcp, server_cmd
repo = os.path.join(here, "bench", "Polly"); sln = os.path.join(repo, "Polly.slnx")
git = lambda *a: subprocess.run(["git", *a], cwd=repo, capture_output=True, text=True).stdout

commits = git("log", "--no-merges", "--format=%h|%cs|%s", "-40", "--", "src/*.cs").splitlines()
m = Mcp(server_cmd(here), cwd=here)
dt, _ = m.call("SharpTool_LoadSolution", solutionPath=sln); print(f"load {dt:.1f}s")

rows = []
for line in commits:
    sha, date, subject = line.split("|", 2)
    files = [f for f in git("diff-tree", "--no-commit-id", "--name-only", "-r", sha).splitlines()
             if f.startswith("src/") and f.endswith(".cs") and os.path.exists(os.path.join(repo, f))]
    if not files: continue
    dt, txt = m.call("dotnet_test_affected", changedFiles=[os.path.join(repo, f) for f in files], dryRun=True)
    d = json.loads(txt); tests = d.get("affectedTests", [])
    rows.append(dict(sha=sha, date=date, subject=subject[:70], files=len(files), tests=len(tests), complete=d.get("selectionComplete"),
                     symbols=d.get("symbolsSearched"), projects=sorted({t["project"] for t in tests}), seconds=round(dt, 2)))
    line = f"{sha} files={len(files):3} tests={len(tests):5} symbols={d.get('symbolsSearched')} complete={d.get('selectionComplete')} {dt:5.1f}s  {subject[:50]}"
    print(line, flush=True); open(os.path.join(here, "bench", "a-progress.log"), "a", encoding="utf-8").write(line + chr(10))
m.close()
json.dump(rows, open(os.path.join(here, "bench", "a-history.json"), "w"), indent=1)
