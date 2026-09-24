# Benchmark: DotNetDevMCP on Polly

Measured 2026-09-23 against [App-vNext/Polly](https://github.com/App-vNext/Polly) at `9a81fdc7` (2026-09-18): 801 C# files,
7 test projects multi-targeted to net8.0/net9.0/net10.0 (+ net481 on Windows), xUnit v3 on Microsoft.Testing.Platform.
The full suite is 12,262 test executions across all TFMs, 3,065 on net10.0 alone, from about 2,600 test methods.

Machine: Intel Core i7-10750H (6 cores, 12 threads), 32 GB, Windows 11 Pro, .NET SDK 10.0.401. Debug builds. The server was
driven over stdio by a scripted MCP client ([mcpc.py](mcpc.py)), the way an agent calls it. Raw results are in [results/](results/).

These numbers are from one machine and one repository. They show how the tool behaves; they are not a promise for your codebase.

## What `dotnet_test_affected` does

It walks Roslyn references from the symbols declared in the changed files until it reaches test methods, and runs only those.
The whole solution runs instead in two cases: the walk runs out of its time budget (default 10 s, `maxSelectionSeconds`), or the
selection is more than 20% of all test methods (`maxSelectedFraction`), where a filtered run is no faster. Either way the answer
is a superset, never a partial set, and the response says which happened.

## A. How many tests does a real change need? ([bench_a.py](bench_a.py))

Replay of Polly's last 40 commits that touched `src/**/*.cs`, selection only (`dryRun`).

Polly has 2,631 test methods.

| Setting | Filtered run | Full suite: selection > 20% | Full suite: out of budget | Median methods when filtered |
|---|---|---|---|---|
| Shipped defaults (depth 8, 10 s) | **16 of 40** | 6 | 18 | 82 |
| Depth 3, 20 s | 26 of 40 | 1 | 13 | 16 |

The commits that run the full suite are the broad ones: "Simplify code" (22 files), "Reduce async overhead" (40 files), SDK
updates, and edits to core plumbing such as `ScheduledTaskExecutor` that everything depends on. Depth 3 narrows more commits
but misses about 10% of the tests a change breaks (C); depth 8 is the default because a test selector has to be safe first.
Pass `maxDepth: 3` to trade that for speed.

## B. Wall clock ([bench_b.py](bench_b.py))

Selection plus run, no build, warm call; the full suite measured in the same session. Absolute times moved a lot between sessions
on this laptop (full net10.0 suite: 33.2 s in one, 48.1 s in another), so compare within a session.

Session 1, depth 3 (before the 20% rule), [results/b-timing-depth3.json](results/b-timing-depth3.json):

| Change | Methods selected | All TFMs | net10.0 only (`framework`) |
|---|---|---|---|
| Full suite | - | 49.4 s | 33.2 s |
| 1 file, `FaultGenerator` | 5 | 16.8 s | **4.6 s** (7.2x) |
| 3 files, cancellation propagation | 109 | 39.1 s | **10.0 s** (3.3x) |
| 1 busy file, test-flakiness fix | 589 | 64.9 s | 40.5 s (slower) |
| 40 files, out of budget | all | 70.0 s | 53.4 s |

Session 2, shipped 0.3.0 defaults, [results/b-timing.json](results/b-timing.json):

| Change | Selection | All TFMs | net10.0 only |
|---|---|---|---|
| Full suite | - | 94.9 s | 48.1 s |
| 1 file, `FaultGenerator` | 5 methods, filtered | 24.8 s | **5.1 s** (9.4x) |
| 3 files, cancellation propagation | 686 methods (26%), whole solution | 65.6 s | 34.5 s |
| 1 busy file, test-flakiness fix | out of budget, whole solution | 72.7 s | 42.8 s |
| 40 files | out of budget, whole solution | 70.8 s | 42.6 s |

Selection pays off for small changes, most of all with one target framework: each selected test project otherwise starts a
test host per TFM, and that fixed cost dominates small runs. Big selections were slower filtered than the full suite in session 1;
the 20% rule now runs the whole solution for them, so they cost about the same as not using selection at all.

## C. Is it safe? Fault injection ([bench_c.py](bench_c.py), [bench_c2.py](bench_c2.py))

A throwing statement was injected into 10 randomly chosen methods of files those 40 commits touched. For each, the full suite
ran to find every test the fault makes fail (only failures carrying the injected exception count, so flaky tests do not),
and the selection was checked against that list.

| Outcome | Mutants |
|---|---|
| Tests hung instead of failing (excluded: not attributable to test names) | 2 |
| No test fails even with the full suite (docs snippet) | 1 |
| Selection ran out of budget: full suite runs, so every failure is caught | 3 |
| Selection completed | 6 (in both settings below) |

Failing tests the completed selections included:

| Setting | Failing tests selected | Bugs caught by at least one selected test |
|---|---|---|
| Depth 3, before the MemberData fix | 101 of 112 (90%) | all |
| Shipped defaults | **111 of 112 (99.1%)** | all |

The misses at depth 3 were long overload chains (`ExecuteAsync(action, ct)` through several overloads to the implementation)
and xUnit `[MemberData]` fed by a static field, which the walk did not follow before 0.3.0. The one remaining miss constructs
the class through reflection (`BindingFlags`), which static analysis cannot see.

Cold caches matter: the first selection of a session can hit the budget and fall back where a warm one completes in 4 s.

## D. "Where is X used?" versus grep ([bench_d.py](bench_d.py))

| Symbol | Semantic references | `git grep -w` lines | Answer size, Roslyn / grep |
|---|---|---|---|
| `ResilienceContext.CancellationToken` | 137 | 1,354 | 6.5 KB / 202 KB |
| `Outcome<TResult>.Exception` | 113 | 1,537 | 6.6 KB / 216 KB |
| `TimeoutStrategyOptions.Timeout` | 15 | 439 | 4.2 KB / 59 KB |
| `RetryStrategyOptions<TResult>.Delay` | 49 | 153 | 6.1 KB / 21 KB |
| `RetryStrategyOptions<TResult>.MaxRetryAttempts` | 73 | 79 | 6.0 KB / 8 KB |

For names that are also common words, most grep hits are other symbols with the same name. For a distinctive name grep
is about as good. The Roslyn answer shows 20 references (relative path, the referencing line, the enclosing member) and the total.
Before 0.3.0 it was twice the size (12-14 KB) and counted each reference once per target framework (925 for `MaxRetryAttempts`).

## E. Things that did not work

A background warm-up that built every project's compilation after `SharpTool_LoadSolution`, meant to make the first selection
of a session fast. It had no measurable effect: 90 s after loading, the first selection on a busy file still hit the 10 s budget
([bench_e.py](bench_e.py), [results/e-improvements.json](results/e-improvements.json)). The cold cost is Roslyn binding the
documents a particular walk touches, which it caches per document afterwards (13.7 s the first time for one file, 1.7 s on
repeat), and a selection on another file first did not speed it up. The warm-up was removed; the first selection of a session
on busy code may fall back to the full suite.

## Reproduce

```bash
git clone https://github.com/App-vNext/Polly.git bench/Polly && git -C bench/Polly checkout 9a81fdc7
dotnet build bench/Polly/Polly.slnx
python bench_a.py && python bench_b.py && python bench_c.py 10 && python bench_c2.py && python bench_d.py && python bench_e.py
```

`DOTNETDEVMCP=DotNetDevMCP@<version>` picks the server version; a `feed/` folder next to the scripts is added as a package source.
