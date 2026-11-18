# scripts/perf

Everything here measures the runtime. This file is the map: what each tool is, which workload it
runs, and the recipe for a round of perf work. The writing lives in `docs/perf/`: `playbook.md` is
how to do this work and the traps, `roadmap.md` is what is worth doing next, `history.md` is the
numbers round by round.

## The one rule

**Compare allocation, and only compare AOT against AOT.** Allocation for a fixed workload repeats to
the byte and does not care how loaded the box is. Wall time on a dev box drifts ~30% between passes
on the same binary, Debug and Release disagree by up to 3x, and AOT and R2R by ~5x on startup. Every
number that goes in a PR, a document or a message comes from an AOT Release binary; Debug is for
iterating.

`build-release-cli-exes.sh` writes the mode to `clis/<binary>.mode`. Read it rather than assuming.

## Asking what one thing costs

Measure from outside the process: run the work at 0 and at N iterations and take the difference
of the two runs' exit telemetry (`DARK_TELEMETRY=1`). Startup, parsing and package loading are in
both runs and cancel. `suite` does this for its workloads and `probe` for each row of a probe in
`workloads/probes/`; to ask about something new, add a row to the probe closest to it, or a new
probe, and run

    scripts/perf/probe scripts/perf/workloads/probes/costs.dark

Every reading carries the build that produced it, its mode (`debug`, `release-r2r`,
`release-aot`) and its commit, because the same workload has three different right answers. `gate`,
`suite` and `probe` print that pair for the binary they ran, and `suite --record` stores it.

## What the interpreter counts

With `DARK_TELEMETRY=1` every VM counts, and the exit dump in `logs/telemetry.jsonl` (under
`rundir/` for the dev CLI, `~/.darklang/` for a published one) totals them: `vm.stats` has
instructions, builtin and package calls, frame pushes, trait dispatches and misses, register slots
allocated and type-symbol-table sizes; `opcode.*` has bytes and count per opcode;
`builtinAlloc.*` allocation and calls per builtin by name; `applyStage.*` bytes per region of the
Apply path (read the fine-grained stages; the three `*.total` ones bracket nested execution and can
exceed what the process allocated); `gc.stats` the bytes the process allocated and its collections.
`view-telemetry.py` reads it.

Per-package-fn time is the exception: `view.dark` and `route.dark` turn on detailed timing with
`InterpreterStats.enableDetailedTiming true` and print `InterpreterStats.get ()`, whose
`packageFnTiming` `fnprofile` turns into a ranked table with names.

## The recipe

1. `scripts/dev/build` and get the thing working first.
2. `scripts/perf/gate` for a quick debug reading (it reports, it does not gate).
3. Pick a workload that is the shape you are changing, from `workloads/` below.
4. Probe with `alloc-profile` (what type is eating the heap) or `fnprofile` (which fn the time is
   in). Once nothing is above ~5%, stop profiling and build a before-and-after instead.
5. Publish AOT: `scripts/run-local-exec export-seed rundir/seed.db` then
   `scripts/build/build-release-cli-exes.sh --mode=aot`.
6. `scripts/perf/gate --published` for the budgeted number, `scripts/perf/suite --release` for the
   seven shapes, `scripts/perf/bench ab <a> <b> <scenario>` to compare two binaries.
7. Nothing in CI checks the numbers. They are tracked over releases by `measure-release` and
   `track` (below); the budget is the baseline they compare against.
8. Publish, THEN run the gates. `scripts/testing/gates first-day` refuses an artifact older than
   the tree, which reads as a failure if you do it the other way round.

## The tools

- `gate` checks every reading against `budget.json`; CI does not run it. `workloads/steady.dark`, allocation against `budget.json`, warm and
  on the cold first run (with its peak memory). The warm number is net of startup
  (`workloads/startup.dark`), which has a budget of its own. Every workload in `workloads/suite/`
  has one too, on allocation per iteration as `suite` measures it. So do the interactive CLI's
  first frame and its keys, on VM instructions, allocation and SQL statements per key (`_ui.py`). Then
  CPU for `workloads/fib.dark`,
  `workloads/lists.dark` and `ls` against the newest release, paired (`_paired.py`); offline,
  against the one `budget.json` pins. `--published` is the real one; bare is debug and does not
  gate.
- `measure-release <tag | binary>` runs `gate --published --report` on one release binary, with
  this tree's workloads and CPU against the release `budget.json` pins, and prints every reading as
  one JSON line. Runs on the host. Because the instrument is fixed, an old release measures as
  well as a new one, so a history can be backfilled.
- `track` measures each release a history file does not have yet (through `measure-release`),
  appends the rows, draws a chart per metric over all of it, writes a dated `perf-<date>.md` and
  prints it. Run it on whatever schedule suits; nothing in it assumes one.
- `suite` runs the seven shapes in `workloads/suite/`, allocation and time per iteration, with
  startup differenced out. `gate --published` runs it too, and budgets each one's allocation.
- `bench` (and `_bench.py`) is repeatable CLI timing from a pinned store fixture. `bench ab`
  interleaves two binaries and reports the paired median, which is drift-proof.
- `http` is allocation and latency per request under concurrency: the only tool here that sees the
  async half of the runtime.
- `probe` is allocation and VM instructions per iteration for each row of a probe, from runs at 0
  and at N iterations.
- `keystroke` is one keypress in the interactive CLI, end to end. Needs a TTY, so it drives
  `expect`. `gate` budgets the headless half of it (see `workloads/ui/` below).
- `alloc-profile` is allocation by type name, from GC allocation-tick events.
- `fnprofile` ranks per-package-fn time and resolves hashes to names. Feed it three runs: it takes
  the minimum per call.
- `crosslang` runs the reference workload in node and python, for scale.
- `view-telemetry.py` shows where a run's time went, from `rundir/logs/telemetry.jsonl`.
- `_common` holds the shared shell helpers: where a published binary is, which mode it is, whether
  it is stale.
- `_total-alloc.py` is the one parser for "bytes this run allocated", used by `gate`, `suite` and
  `http`.

## The workloads

Different contracts, which is why they are in different places:

- `workloads/steady.dark`, `workloads/startup.dark` and `workloads/http-server.dark` are owned by a
  tool. `steady.dark` is the gated workload and `startup.dark` the baseline taken off it, both
  named in `budget.json`, so changing either changes what the budget means. Do not edit them to
  make a number look better.
- `workloads/suite/` is the differenced set. Each file reads `PERF_ITERS` and gets run twice, at 0
  and at N, so startup cancels and the result is per-iteration. A new file here is picked up
  automatically, and `gate` reports it without failing until `gate --published --update` records
  its budget.
- `workloads/ui/workbench.dark` is the interactive CLI with no terminal, at 120x40: at
  `PERF_ITERS=0` process start to the workbench's first frame painted to a string, at N that plus
  N keys, which `PERF_UI` picks: Down/Up at `List.map` (`keypress`), Down through `List`
  (`keyWalk`, every item new), Down/Up on Home (`homeKey`), Down through Stdlib's modules
  (`moduleKey`). `_ui.py` runs it at 0, then each at 1 and 1+N, and `gate` budgets the first
  frame and the difference per key: VM instructions, which repeat exactly, allocation, which sees
  the builtins where most of a keypress goes, and SQL statements. Wall time is printed beside them
  and gates nothing.
- `workloads/bench/` is staged into `rundir/perf-workloads/` by `bench`. These use raw `Builtin.`
  calls rather than the Dark wrappers on purpose: an A/B runs the same source against an older
  published binary, whose store may not carry today's wrapper.
- `workloads/probes/` is hand-run. One file per question (what a view costs, what a lambda
  application costs, where routing time goes, where spreading starts to pay). Run them with
  `./scripts/run-cli run <file>` for time, or `scripts/perf/probe <file>` for allocation and
  instructions per row; each file's header says what it answers and how to read it. The `perf-probes-run` gate checks every probe with fewer repetitions by
  running a copy with its `let smoke = false` line set to true. It keeps every workload row,
  list size, canvas size and workbench view; the operation probes use inputs on both sides of
  their conditional examples. It also runs the profiling sections, and `probe` over one probe. Smoke output
  is labelled and must not be used as a performance measurement. Run by hand, a probe keeps
  its full repetitions. The switch is a constant rather than an environment variable because
  reading one needs `env read`, which the default instance policy prompts for. The gate
  reports elapsed seconds per probe to help identify slow checks.

## What is NOT here

Correctness checks. Golden-output comparisons for the interpreter, operator fast paths, SGR parsing,
grapheme handling and rendered views are package tests, in `packages/darklang/tests/`, asserted by
`dark test run`. If you want to check that a perf change
kept behaviour identical, run the package tests.

## Reading a number without fooling yourself

The playbook has the long version. The four that matter most:

- **A published binary older than the tree measures the previous build.** `gate` and
  `gates first-day` refuse one rather than reporting; believe them.
- **The store is half the measurement.** The same binary reads a couple of percent higher against a
  working dev store than against one the published binary grew itself. CI is in the second case.
- **A zero-second run is a lock, not a result**, and a suspiciously good number usually means the
  thing you think produced it did not run.
- **A whole run is mostly startup.** `startup.dark` (one `printLine`) is about two thirds of a
  whole `steady.dark` run, so a tolerance on the total hides a regression in the loop under test.
  So `gate` budgets the two apart: startup at 3%, and `steady.dark` minus startup (parsing the
  file and the loop) at 5%, each the median of three warm runs. It prints the
  total too. `suite` differences 0 against N iterations, so its numbers leave out parsing as well.
