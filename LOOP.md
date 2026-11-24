# Bench loop: one round from scratch

Everything lives in `~/code/dark/bench-loop.bench/` on major (outside any checkout). The clone
it builds from is `~/code/dark/bench-loop` (make it with `~/code/dark/new-clone bench-loop --start`).

## Pieces

| File | What it does |
|---|---|
| `run-round.sh <binary> <label> <list> [attempts]` | Runs tasks against one binary, `PAR` (default 3) at a time, each under a box-wide slot. Rows go to `results/<label>.jsonl`, transcripts to `results/<label>/<task>-<n>/`, workspaces to `/tmp/bench-loop-ws/<label>/`. Ends with a summary. |
| `build-release.sh <label>` | The release musl CLI of the clone's HEAD (clean tree only): dev build, seed export, then AOT in the CI's own image (`bench-loop-musl:1`, made once by `musl-image.sh`). The AOT takes a slot. Output `bins/<label>/darklang`. |
| `build-commits.sh <branch>` | `build-release.sh` for the branch's base (`c0`) and each commit (`c1`..`cN`). |
| `summary.mjs a.jsonl [b.jsonl] [--tasks t1,t2]` | Per-task medians (calls, time, cost), failed runs, done; with two files, before > after. Counts only, safe on the measure set. |
| `dump.mjs <label> <task> [n]` | One tuning transcript, compact. Refuses measure-set tasks. |
| `cmds.mjs <label> <regex>` | Every Bash command in tuning transcripts matching a regex, to count a pattern. |
| `errors.mjs <label>` | Dark calls whose output looks like an error (noisy). |
| `attempt.mjs`, `one.sh`, `metrics.mjs`, `claude-iso` | One attempt: fresh store and HOME, the W467 runner's own prompt and sandbox code (`runner/`), an isolated claude with only the login. |
| `lists/tune.txt`, `lists/measure.txt` | Tune on orig 9 + train 25. Measure on held-out 8 + newer held-out 3. |
| `wins-r1.sh` | Round 1 chain: per-win before/after, then the final sets. Copy it, edit the `win` lines. |
| `run-ab.sh <binA> <labelA> <binB> <labelB> <list> [attempts]` | Two binaries with their attempts interleaved (A t1#1, B t1#1, ...), so box load hits both alike; ends with the before > after summary. Round 2 used it for every comparison. |
| `rescore.mjs <label>` | Recompute the metrics of a results file after `metrics.mjs` changes. |
| `srvsmoke.sh <binary>`, `seedcheck.sh <label>` | Quick checks: server permissions; the seeds still reproduce their fixtures. |

## A round

1. `cd ~/code/dark/bench-loop && git fetch upstream && git checkout -B bench-round-N upstream/main`
2. `cd ~/code/dark/bench-loop.bench && ./build-release.sh main` (about 3 min plus the slot wait).
3. Baseline: `./run-round.sh bins/main/darklang base-tune tune` then `... base-measure measure`.
   About $24 and $10 at one attempt each.
4. Read the tuning transcripts (`dump.mjs`, `cmds.mjs`). Count a pattern across all tasks
   before fixing it. Never open measure-set transcripts.
5. Commit one win per commit on the branch. Then `./build-commits.sh bench-round-N`.
6. Per win: `./run-round.sh bins/c<k-1>/darklang wK-before t1,t2 3` and
   `./run-round.sh bins/c<k>/darklang wK-after t1,t2 3`, then
   `node summary.mjs results/wK-before.jsonl results/wK-after.jsonl`.
7. Final: the tip's binary on `tune` and `measure`, compared with the baseline.
8. Full suite, `scripts/dev/exec scripts/formatting/format check`, push.

## Things that bite

- **The login.** The refresh token rotates. `claude-iso` copies the newest of
  `~/.claude/.credentials.json` and `cred/latest.json` in and carries a refreshed one forward
  in `cred/latest.json`. A login within 15 min of expiry is refreshed once under a lock first:
  in round 1, parallel sessions each spent the same refresh token and three attempts died with
  "OAuth session expired" (status `environment blocked`, 1 call). Rerun such rows. The user's own
  file on major is never written, so it goes stale.
- **Seeds rot.** `tasks/seeds/*/dark` is Dark code; when the stdlib renames something, setup
  fails and the attempt is recorded as `setup failed`. Run `seedcheck.sh` after a pull.
- **The sandbox.** Each Bash call gets its own network namespace, so a server started in one
  call is invisible to the next. The sandbox already sets `NO_PROXY=localhost,127.0.0.1,...`
  (round 2 also passes it from `attempt.mjs`). The `Microsoft-NetCore` 404 that agents blamed on
  the proxy in round 1 was Dark's own server: HttpListener refused `Host: localhost` on a
  127.0.0.1 server (fixed in round 2). `/tmp` outside `$TMPDIR` is read-only.
- **Slots.** Each attempt and each AOT takes one of the 4 box-wide slots, so a round also waits
  on other agents' suites.
- **One attempt is noise.** Single sessions move a task's cost by 30%; use 2-3 attempts and
  medians for anything you claim.
- **Builds wait.** `build-commits.sh` took two hours in round 1, almost all of it waiting for a
  slot behind other agents' suites. Start it as soon as the branch is final.
- **`docs for-ai` has a length test** (`packages/darklang/tests/cli/docsForAI.dark`, 7,300
  characters after round 1). A docs win must make room, not just add.
- **No foreign syntax.** The user won't widen the parser for other languages (`;`, `200L`);
  state the syntax in the docs instead.
- **Token totals.** A session that answers background-task notifications ends with several
  `result` events; each one's `usage` is that turn only. `metrics.mjs` `tokens()` reads the
  cumulative `modelUsage` instead (round 2), and `rescore.mjs` recomputes old rows.
- **Round 1 results** are in `results/` (`base-*`, `w1-*`..`w6-*`, `final-*`); the branch is
  `bench-round-1` on origin.

## Decisions after round 1 (Nov 16)
- Pair lambdas (fun (a, b) -> for one pair): docs only. Never widen the parser.
- delete with no terminal: keep refusing without --yes. Not a win to chase.
- From round 2 on, bench agents get NO_PROXY=127.0.0.1,localhost. Keep the environment one that is reasonable long-term: a real test of how well things build using only Dark, not harness tricks. Round 2 needs its own baseline on main with this set.

## Round 2 (Nov 16)
- Baseline `base2-tune` (main eb1bc904be, NO_PROXY set); per-win `r2w1-*`; final `r2final-before`
  (main) vs `r2final-after` (tip, `bins/r2tip`), measure set, 3 attempts each, interleaved.
- Branch `bench-round-2` on origin. Wins: localhost on a 127.0.0.1 server; did-you-mean one module
  down or the other side of a conversion; a `//` comment above a `fn`/`test`/`trait`/`impl`;
  reading a directory says "Is a directory"; a `module` save lists names it left behind.
- The hand-written lock trap from round 1 did not reproduce: mkdir, flock and closure locks all
  stay exclusive over 20 spawned read-modify-writes. The round-1 paste-service session itself
  ended on a bug in its own limiter, not the runtime.
- Seen, not fixed: `File.readText("")` raises "`` is protected host policy state" instead of
  returning an error; call stacks print a random `Lambda in Source (<id>)`; requests carry no
  client address (paste-service keys its rate limit on X-Forwarded-For).
- `metrics.mjs` notFound now matches only Dark's own unresolved-name errors (`Error: X not found`, `Not found: X`); before, a program's own "File not found" counted as a failed run. Round-2 files are rescored; round-1 files are not.

## Round 4 (Nov 18)
- Workspaces moved from /tmp to `/home/stachu/code/bench-loop-ws` (not under ~/code/dark: the runner refuses a workspace with a CLAUDE.md ancestor, and ~/code/dark has one). Delete `.batch-tools` there after a round (7 GB).
- Baseline `r4base-tune` (1 attempt; 4 setup timeouts under load ~100: markdown-lite, wav-info, webhook-receiver, word-count) and `r4base-measure` (3 attempts), main ae16a940bb, binary `bins/r4main`.
- One branch per win, each in its own clone; no bundled branch. Per-win benches were not run.

## Reflecting on a round (Nov 23)
- `node reflect.mjs <label>... [--seq] [--timeline label/task-n] [--md out.md] [--json out.json]` turns transcripts
  into: where turns go (each turn under its strongest call: orient, docs, lookup, probe, author, fix, run, test,
  store, perms, shell, talk), failure classes with episodes and the turns they cost, the first wrong turn, the
  first store save's outcome, tokens that tool output adds to every later turn, the largest outputs read,
  medians by task kind, docs parts read and names looked up. Several labels give an "Across runs" table;
  `a+b` pools runs. Measure-set tasks (every `lists/measure*.txt`) are counted, never quoted or timelined.
- Classes match Dark's own diagnostics only (a program's own `error:` lines and docs text don't count). When
  Dark's wording changes, check the `ERR` table at the top before comparing runs.
- `BENCH_LANG=Python` on `run-round.sh` runs the same tasks in Python for a floor (seeds use `<seed>/python`).
- `BENCH_YIELD=1` makes each attempt and the AOT wait until no suite is queued and IO is calm (`yield-gate.sh`);
  with `BENCH_NOSLOT=1` attempts then take no slot, so a suite queued later never waits behind the bench.
- `tasks/held-out-2` (`lists/measure2.txt`): kv-service, trace-bug, geo-rename, backup-tool, duration-lib. Held
  out like the measure set. `try-seed.sh <bin-label> <seed> [cmds]` sets a seed up in a throwaway store.
- Round `r10x.sh`: x116-measure vs xmain-measure (3, interleaved), xmain-new (held-out-2, 3), xpy-measure (1),
  xmain-tune (1). Binary `bins/m1123` is main d60c99c85d.
