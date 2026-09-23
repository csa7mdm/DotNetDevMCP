# Benchmark: DotNetDevMCP on Polly

Measured 2026-09-23 against [App-vNext/Polly](https://github.com/App-vNext/Polly) at `9a81fdc7` (2026-09-18): 801 C# files,
7 test projects multi-targeted to net8.0/net9.0/net10.0 (+ net481 on Windows), xUnit v3 on Microsoft.Testing.Platform.
The full suite is 12,262 test executions across all TFMs, 3,065 on net10.0 alone, from about 2,600 test methods.

Machine: Intel Core i7-10750H (6 cores, 12 threads), 32 GB, Windows 11 Pro, .NET SDK 10.0.401. Debug builds. The server was
driven over stdio by a scripted MCP client ([mcpc.py](mcpc.py)), the way an agent calls it. Raw results are in [results/](results/).

These numbers are from one machine and one repository. They show how the tool behaves; they are not a promise for your codebase.

## What `dotnet_test_affected` does

It walks Roslyn references from the symbols declared in the changed files until it reaches test methods, and runs only those.
The walk is capped by time (default 10 s, `maxSelectionSeconds`). If it runs out, the change reaches too much code for selection
to pay off, and the whole solution runs instead: the answer is then a superset, never a partial set.

## A. How many tests does a real change need? ([bench_a.py](bench_a.py))

Replay of Polly's last 40 commits that touched `src/**/*.cs`, selection only (`dryRun`).

| Setting | Complete selections | Median test methods selected | Median / max selection time |
|---|---|---|---|
| Shipped defaults (depth 8, 10 s) | 22 of 40 | 272 | 0.7 s / 6.1 s |
| Depth 3, 20 s (earlier setting) | 27 of 40 | 19 | 0.2 s / 13.7 s |

The other commits fall back to the full suite. They are the broad ones: "Simplify code" (22 files), "Reduce async overhead"
(40 files), SDK updates, and edits to core plumbing such as `ScheduledTaskExecutor` that everything depends on.
Depth 8 selects more tests than depth 3; C shows why that is the right default.

## B. Wall clock ([bench_b.py](bench_b.py))

Selection plus run, no build, second (warm) call; full suite measured the same session.

| Change | Methods selected | All TFMs | net10.0 only (`framework`) |
|---|---|---|---|
| Full suite | - | 49.4 s | 33.2 s |
| 1 file, `FaultGenerator` | 5 | 16.8 s | **4.6 s** |
| 3 files, cancellation propagation | 109 | 39.1 s | **10.0 s** |
| 1 busy file, test-flakiness fix | 589 | 64.9 s | 40.5 s |
| 40 files (falls back) | all | 70.0 s | 53.4 s |

Selection pays off for small and medium changes, most of all when running one target framework: each selected test project
otherwise starts a test host per TFM, and that fixed cost dominates small runs. For changes that reach hundreds of tests,
filtered runs are no faster than running everything. A fallback costs its selection budget on top of the full run.

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
| `ResilienceContext.CancellationToken` | 137 | 1,354 | 12 KB / 202 KB |
| `Outcome<TResult>.Exception` | 113 | 1,537 | 14 KB / 216 KB |
| `TimeoutStrategyOptions.Timeout` | 15 | 439 | 9 KB / 59 KB |
| `RetryStrategyOptions<TResult>.Delay` | 49 | 153 | 13 KB / 21 KB |
| `RetryStrategyOptions<TResult>.MaxRetryAttempts` | 73 | 79 | 11 KB / 8 KB |

For names that are also common words, most grep hits are other symbols with the same name. For a distinctive name grep
is as good and smaller. The Roslyn answer shows 20 references with context and the total count.

## Reproduce

```bash
git clone https://github.com/App-vNext/Polly.git bench/Polly && git -C bench/Polly checkout 9a81fdc7
dotnet build bench/Polly/Polly.slnx
python bench_a.py && python bench_b.py && python bench_c.py 10 && python bench_c2.py && python bench_d.py
```

`DOTNETDEVMCP=DotNetDevMCP@<version>` picks the server version; a `feed/` folder next to the scripts is added as a package source.
