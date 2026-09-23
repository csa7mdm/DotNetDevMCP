# Benchmark C: is affected-test selection safe? Inject a throwing statement into real Polly methods, run the FULL suite
# to find every test the mutant makes fail, and check whether dotnet_test_affected selected all of those tests.
import sys, os, json, subprocess, time, random, xml.etree.ElementTree as ET
here = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, here); from mcpc import Mcp, server_cmd
repo = os.path.join(here, "bench", "Polly"); sln = os.path.join(repo, "Polly.slnx")
K = int(sys.argv[1]) if len(sys.argv) > 1 else 10
NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
NL = chr(10)
MUTANT = '        if (!ReferenceEquals(typeof(object), null)) { throw new System.InvalidOperationException("dotnetdevmcp-mutant"); }'
OUT = os.path.join(here, "bench", "c-mutants.json")

def sh(*a, timeout=None):
    # subprocess.run(timeout=) kills only `dotnet`, then waits for pipes that its test hosts still hold open: forever on Windows.
    p = subprocess.Popen(list(a), cwd=repo, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    try:
        out, err = p.communicate(timeout=timeout)
    except subprocess.TimeoutExpired:
        subprocess.run(["taskkill", "/T", "/F", "/PID", str(p.pid)], capture_output=True)
        p.communicate()
        raise
    return subprocess.CompletedProcess(a, p.returncode, out, err)
def read(p): return open(p, encoding="utf-8", newline="").read()     # newline="": keep the file's own line endings
def write(p, s): open(p, "w", encoding="utf-8", newline="").write(s)

def failing_tests(output):
    """Unique Class.Method of tests the mutant made fail, from the TRX files `dotnet test` reports writing."""
    failed = set()
    for trx in {l.strip()[2:] for l in output.splitlines() if l.strip().startswith("- ") and l.strip().endswith(".trx")}:
        root = ET.parse(trx).getroot()  # local file written by our own test run
        names = {u.get("id"): f'{u.find(NS + "TestMethod").get("className")}.{u.find(NS + "TestMethod").get("name")}'
                 for u in root.iter(NS + "UnitTest")}
        for r in root.iter(NS + "UnitTestResult"):
            # Only failures the mutant caused: flaky timing tests fail on their own and are not the selection's fault.
            if r.get("outcome") == "Failed" and "dotnetdevmcp-mutant" in "".join(r.itertext()):
                failed.add(names.get(r.get("testId"), r.get("testName")).split("(")[0])
        os.remove(trx)
    return failed

def mutation_points(path):
    lines = read(path).split(NL)
    pts = []
    for i, l in enumerate(lines):
        if l.rstrip("\r") != "    {" or i == 0: continue
        prev = next((lines[j].rstrip("\r") for j in range(i - 1, -1, -1) if lines[j].strip()), "")
        if prev.endswith(")") and not prev.lstrip().startswith(("[", "//")):
            pts.append(i)
    return pts

def save(): json.dump({"mutants": results, "hangs": hangs, "no_build": no_build}, open(OUT, "w"), indent=1)

# Candidates: code touched by the replayed commits (benchmark A).
hist = json.load(open(os.path.join(here, "bench", "a-history.json")))
files = sorted({f for sha in [h["sha"] for h in hist]
                for f in sh("git", "diff-tree", "--no-commit-id", "--name-only", "-r", sha).stdout.splitlines()
                if f.startswith("src/") and f.endswith(".cs") and os.path.exists(os.path.join(repo, f))})
rng = random.Random(20260923)
cands = [(f, p) for f in files for p in mutation_points(os.path.join(repo, f))]
rng.shuffle(cands)
print(f"{len(files)} candidate files, {len(cands)} mutation points", flush=True)

m = Mcp(server_cmd(here), cwd=here)
m.call("SharpTool_LoadSolution", solutionPath=sln)
results, used_files, hangs, no_build = [], set(), [], []
if os.path.exists(OUT):  # resume: keep finished mutants, skip their files
    prev = json.load(open(OUT)); results, hangs, no_build = prev["mutants"], prev["hangs"], prev["no_build"]
    used_files = {r["file"] for r in results} | {h.rsplit(":", 1)[0] for h in hangs}
for f, line_no in cands:
    if len(results) >= K: break
    if f in used_files: continue
    path = os.path.join(repo, f); original = read(path)
    eol = "\r" if original.split(NL)[0].endswith("\r") else ""
    lines = original.split(NL); lines.insert(line_no + 1, MUTANT + eol)
    write(path, NL.join(lines))
    try:
        if sh("dotnet", "build", "Polly.slnx", "-nologo", "-v", "q").returncode != 0:
            no_build.append(f"{f}:{line_no + 1}"); print(f"skip {f}:{line_no + 1} (mutant does not build)", flush=True); continue
        used_files.add(f)
        t = time.perf_counter()
        try:
            full = sh("dotnet", "test", "--solution", "Polly.slnx", "--no-build", "--report-xunit-trx", timeout=300)
        except subprocess.TimeoutExpired:
            # The mutant made a test wait forever instead of failing: detected by the suite, but not attributable to test names.
            subprocess.run(["powershell", "-NoProfile", "-Command", "Get-Process Polly* -ErrorAction SilentlyContinue | Stop-Process -Force"])
            hangs.append(f"{f}:{line_no + 1}"); save(); print(f"hang {f}:{line_no + 1} (excluded)", flush=True); continue
        full_s = time.perf_counter() - t
        failed = failing_tests(full.stdout + full.stderr)
        dt, txt = m.call("dotnet_test_affected", changedFiles=[path], dryRun=True)
        d = json.loads(txt)
        selected = {x["fullyQualifiedName"] for x in d["affectedTests"]}
        caught = failed & selected
        row = dict(file=f, line=line_no + 1, complete=d.get("selectionComplete"), signature=lines[line_no - 1].strip()[:90],
                   full_seconds=round(full_s, 1), full_failed=len(failed), selected=len(selected), caught=len(caught),
                   missed=sorted(failed - selected)[:10], selection_seconds=round(dt, 1))
        if row["complete"] and failed:   # time a real affected run only where selection applies and something should fail
            dt2, txt2 = m.call("dotnet_test_affected", changedFiles=[path], noBuild=True, framework="net10.0")
            run = json.loads(txt2).get("run") or {}
            row.update(affected_run_seconds=round(dt2, 1), affected_run_failed=run.get("failedTests"), affected_run_total=run.get("totalTests"))
        results.append(row); save()
        print(f"{f}:{line_no + 1} full {full_s:.0f}s failed={len(failed)} | complete={row['complete']} selected={len(selected)} "
              f"caught={len(caught)} | run {row.get('affected_run_seconds')}s failed={row.get('affected_run_failed')}", flush=True)
    finally:
        write(path, original)
m.close()
sh("dotnet", "build", "Polly.slnx", "-nologo", "-v", "q")
save()
