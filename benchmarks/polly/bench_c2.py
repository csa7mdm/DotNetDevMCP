# Benchmark C, re-scored: same mutants and the same full-suite failures (from c-mutants.json), selection by the current build.
# Every `missed` list is complete (all <= 10 names), so recall under the new selection is exact.
import sys, os, json
here = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, here); from mcpc import Mcp, server_cmd
repo = os.path.join(here, "bench", "Polly")
d = json.load(open(os.path.join(here, "bench", "c-mutants.json")))
m = Mcp(server_cmd(here), cwd=here)
m.call("SharpTool_LoadSolution", solutionPath=os.path.join(repo, "Polly.slnx"))
rows = []
for x in d["mutants"]:
    if x["full_failed"] == 0: continue
    for rep in range(2):  # first call is cold; report both
        dt, txt = m.call("dotnet_test_affected", changedFiles=[os.path.join(repo, x["file"])], dryRun=True)
        r = json.loads(txt); sel = {t["fullyQualifiedName"] for t in r["affectedTests"]}
        caught_before = x["caught"] if x["complete"] else None
        still_missed = [n for n in x["missed"] if n not in sel]
        # failing tests caught = previously caught (all still selected: selection only grew) + recovered misses
        row = dict(file=x["file"], rep=rep + 1, complete=r["selectionComplete"], selected=len(sel), seconds=round(dt, 1),
                   full_failed=x["full_failed"], caught=x["full_failed"] - len(still_missed) if len(x["missed"]) == x["full_failed"] - x["caught"] else None,
                   still_missed=still_missed)
        rows.append(row); print(row, flush=True)
m.close()
json.dump(rows, open(os.path.join(here, "bench", "c2-rescored.json"), "w"), indent=1)
