# Benchmark D: "where is X used?" - Roslyn FindReferences vs grep -w on the same Polly symbols.
# Measures how many grep hits are noise, and how much text each answer puts in an agent's context.
import sys, os, json, subprocess, time, re
here = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, here); from mcpc import Mcp, server_cmd
repo = os.path.join(here, "bench", "Polly"); sln = os.path.join(repo, "Polly.slnx")
SYMBOLS = [
    "Polly.ResilienceContext.CancellationToken",
    "Polly.Retry.RetryStrategyOptions<TResult>.Delay",
    "Polly.Timeout.TimeoutStrategyOptions.Timeout",
    "Polly.Outcome<TResult>.Exception",
    "Polly.RetryResiliencePipelineBuilderExtensions.AddRetry",
    "Polly.Retry.RetryStrategyOptions<TResult>.MaxRetryAttempts",
]

def grep(name):
    out = subprocess.run(["git", "grep", "-n", "-w", name, "--", "*.cs"], cwd=repo, capture_output=True, text=True).stdout
    return out.count("\n"), len(out)

m = Mcp(server_cmd(here), cwd=here)
m.call("SharpTool_LoadSolution", solutionPath=sln)
rows = []
for fqn in SYMBOLS:
    name = re.sub(r"<.*?>", "", fqn).rsplit(".", 1)[1]
    dt, txt = m.call("SharpTool_FindReferences", fullyQualifiedSymbolName=fqn)
    try:
        d = json.loads(txt); refs = d.get("totalReferences")
    except json.JSONDecodeError:
        refs = None
    lines, chars = grep(name)
    row = dict(symbol=fqn, name=name, roslyn_refs=refs, roslyn_seconds=round(dt, 2), roslyn_chars=len(txt),
               grep_lines=lines, grep_chars=chars, error=None if refs is not None else txt[:200])
    rows.append(row); print(row, flush=True)
m.close()
json.dump(rows, open(os.path.join(here, "bench", "d-references.json"), "w"), indent=1)
