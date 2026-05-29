# Iter 14 — Test infrastructure for the daemon world

Today's tests share global SQLite tables (`toplevels_v0`,
`user_data_v0`) and rely on test-time wipes between cases (see
`backend/tests/Tests/LibExecution.Tests.fs:120-123`). Single-
instance Dark made that workable. The new architecture — per-
account ops.db files, projections, sessions, traces — needs a
different test story.

This iter is the practical one: what does a test *look* like in
the new world, how does it run fast enough to be tolerable, and
how do today's ~10K tests migrate without a months-long freeze?

## What's wrong with today's tests

- **Shared global state.** Tests that touch `toplevels_v0` or
  `user_data_v0` must run sequentially (`testSequenced`).
  Concurrency is left on the table.
- **No isolation between accounts.** Single-instance Dark
  collapsed all canvases into one DB; tests that exercise
  multi-account behavior can't.
- **Fragile setup.** A test that forgets to wipe leaves rows
  for the next test. The wipe-then-populate idiom is repeated
  everywhere.
- **No multi-peer testing.** Sync, conflicts, branch divergence
  — fundamentally about >1 daemon — can't be tested at all.
- **Heavy F# scaffolding.** Each new test category needs new
  Expecto plumbing in F#. The barrier to writing a test is too
  high.

The new architecture makes this easy if we do the test infra
right.

## What a test looks like

Pure test (today's bread and butter):

```dark
module Mycorp.Tests.UserUtils

let validateName_acceptsValidName () : Test.Result =
  Test.assertEq (Mycorp.UserUtils.validateName "alice") (Ok "alice")

let validateName_rejectsEmpty () : Test.Result =
  Test.assertEq (Mycorp.UserUtils.validateName "") (Error "empty")

Test.registerAll [
  validateName_acceptsValidName
  validateName_rejectsEmpty
]
```

Or with light syntactic sugar (parser-side):

```dark
module Mycorp.Tests.UserUtils

[<Test>] let validateName_acceptsValidName () : Test.Result =
  Test.assertEq (Mycorp.UserUtils.validateName "alice") (Ok "alice")
```

The `[<Test>]` annotation is sugar over `Test.register`. Picked
up at parse time; daemon's projection of the package store
includes a "registered tests" list.

DB-touching test:

```dark
[<Test>] let createUser_persistsRow () : Test.Result =
  Test.withScopedAccount (fun acct ->
    Test.eval acct "Mycorp.UserDB.create { name = \"alice\" }"
    let user = Test.eval acct "Mycorp.UserDB.get \"alice\""
    Test.assertEq user (Some { name = "alice" })
  )
```

`Test.withScopedAccount` allocates a fresh ephemeral account
within the test daemon; teardown drops its ops.db. ~ms-fast.

Handler test:

```dark
[<Test>] let api_returnsOk () : Test.Result =
  Test.withScopedApp Mycorp.MyApp (fun app ->
    let resp = Test.simulateRequest app {
      method = "GET"
      path = "/health"
      headers = []
      body = ""
    }
    Test.assertEq resp.statusCode 200L
  )
```

`Test.withScopedApp` allocates an account, registers the app's
handlers, simulates an HTTP request through them in-process.
No real network.

Multi-peer test (the new affordance):

```dark
[<Test>] let sync_resolvesConflictsConsistently () : Test.Result =
  Test.withTwoPeers (fun peerA peerB ->
    Test.eval peerA "DB.set Counter \"x\" 1"
    Test.eval peerB "DB.set Counter \"x\" 2"
    Test.sync peerA peerB
    let a = Test.eval peerA "DB.get Counter \"x\""
    let b = Test.eval peerB "DB.get Counter \"x\""
    Test.assertEq a b  // same value; resolution converged
    Test.assertConflictRecorded peerA "Counter:x"  // surfaced
  )
```

This kind of test is the unique payoff. Sync, branches,
projections — all testable in-process, deterministically.

Property test:

```dark
[<Test>] let serialize_roundtrip (gen: Gen<Dval>) : Test.Result =
  Test.forAll gen (fun dv ->
    let bytes = Stdlib.Binary.serialize dv
    let restored = Stdlib.Binary.deserialize bytes
    restored == dv
  )
```

Hypothesis-style: define a generator, assert an invariant
across thousands of randomly-generated inputs. Failures
shrink to minimal repros automatically.

Trace test (auto-generated; per iter 13):

```dark
// Auto-emitted from `dark trace as-test 8a3f2dd`:
[<Test>] let regression_8a3f2dd () : Test.Result =
  Test.replayTrace "8a3f2dd" {
    expectResult = Ok ...
    sideEffects = SideEffectMode.Pure
  }
```

The trace recording is the test fixture; the assertion is
"output matches what was recorded after the fix." Run on every
build to prevent regression.

## Test layers and their costs

| Layer | Setup cost | Parallelism | Use for |
|-------|-----------|-------------|---------|
| Pure | ~10μs | full | most logic |
| Scoped-account | ~1ms | full | DB / handler tests |
| Scoped-app | ~5ms | full | HTTP handler integration |
| Multi-peer | ~10ms | full | sync/conflict |
| Property (1K cases) | ~100ms | per-case | invariants |
| Trace replay | ~1-100ms | full | regression |
| Full-daemon spawn | ~200ms | limited | daemon startup, edge cases |

The daemon shares one process across all test cases; per-test
cost is dominated by ops.db creation + projection initialization,
both fast. Full-daemon spawn is reserved for tests that
specifically test daemon lifecycle (rare).

## How the runner works

```
$ dark test
Discovering tests... 8421 tests across 142 modules
Running...
✔ Mycorp.Tests.UserUtils.validateName_acceptsValidName  (87μs)
✔ Mycorp.Tests.UserUtils.validateName_rejectsEmpty      (84μs)
✔ Mycorp.Tests.UserApi.createUser_persistsRow          (1.2ms)
✗ Mycorp.Tests.UserApi.deleteUser_cascadesPosts        (3.4ms)
  Expected: List.length posts == 0
  Actual:   List.length posts == 3
  At: backend/packages/mycorp/tests/userApi.dark:84
  Trace: dark trace show test-run-9f3a/test-1183
...
8420 passed, 1 failed (4.3s)
```

Discovery: query the daemon's package projection for "all
registered test fns." No file scan needed.

Execution: launch tests via `Test.execute` builtin. Daemon
runs them in a worker pool; results stream back as test-result
ops.

Failure trace: every test failure is a recorded trace. The CLI
output points at it; user can `dark trace show <id>` to step
through the failing test.

`dark test --filter Mycorp.Tests.UserApi`: glob filter on test
fn name. Standard.

`dark test --watch`: re-runs affected tests on save. The
daemon's projection knows which tests touch which fns
(via trace data from previous runs); when a fn changes, only
re-run tests that exercise it. ~10× speedup on iterative work.

## What runs where

```
host process (test runner CLI)
   ↓ JSON-RPC to daemon
daemon (one shared instance per test run)
   ↓ launches per-test scoped accounts
   ↓ executes Dark test fns
   ↓ aggregates results into test-results stream
results streamed back to CLI; printed, exit code set
```

This is the same machinery as `dark repl` and `dark eval`.
No special test runtime; tests are just Dark code that calls
`Test.assertEq` etc.

The daemon process is shared across the entire test run. Boot
once (200ms), execute thousands of tests, tear down. Per-test
cost = scoped account creation, not daemon spawn.

## Migrating today's ~10K tests

Side-by-side migration:

### Phase A. New tests use new infra

From day 1 of the new arch, new test files use the new style.
Old test infra continues to work in parallel.

### Phase B. Wrap old test files

Today's `.dark` testfiles in `backend/testfiles/execution/`
have a specific shape:

```dark
[<DB Person>]
type Person = { name: String }

let test1 = (DB.set Person { name = "alice" }) = ()
let test2 = (DB.get Person "alice") = Some { name = "alice" }
```

A migration shim parses these into the new format:

- `[<DB Person>]` → registers the type, becomes a CreateType op
  in the test's scoped-account ops.db.
- `let testN = X = Y` → becomes `[<Test>] let testN () =
  Test.assertEq X Y`.

The shim runs at test-run time; .dark files don't need rewrites.
Eventually we rewrite them to use the new annotation syntax for
clarity, but it's not blocking.

### Phase C. Migrate F# Expecto tests

F# tests in `backend/tests/Tests/*.fs` test internals: parser,
type-checker, codec roundtrip, etc. These are testing F# code,
so they stay in F# Expecto. They don't need to migrate.

What does need migration: F# tests that exercise *Dark* code
through F# scaffolding (e.g., HttpClient.Tests.fs that runs
Dark http handlers). These should be ported to Dark. ~50 such
files; ~2 weeks of work, parallel to other migration tracks.

### Phase D. Delete old test infrastructure

Once all tests use the new infra:
- Drop the migration shim.
- Drop `setupDBs` / `setupWorkers` / shared-state-wipe
  scaffolding from F# test runner.
- F# Expecto runner remains for F# unit tests; new infra is
  the dominant path.

Total migration: ~2-3 months calendar time, low risk if done
side-by-side. None of it is "stop the world."

## Test concurrency

The daemon's worker pool runs tests in parallel:

- **Embarrassingly parallel** if scoped-account tests touch
  different accounts (default).
- **Same-account tests serialized** within the daemon, even
  though scoped accounts are isolated — because two tests
  using the same scoped account share state.

The runner allocates scoped accounts per-test; `withScopedAccount`
creates a one-off account, runs, drops. Each test thinks it
owns the world.

Resource caps: daemon's worker pool is sized at N (CPU count).
Tests above that queue. Long-running tests (>10s) get warned;
>60s killed.

## Determinism

Test failures should be reproducible. Sources of non-determinism:

- **Time.** `DateTime.now` differs between runs. Use
  `Test.withFrozenTime` to freeze time within a scope.
- **Random.** Same. `Test.withSeed` to control RNG.
- **Concurrency.** Async tasks running in non-deterministic
  order. Tests that need ordering use explicit
  `Test.awaitAll`.
- **HTTP.** Real outbound calls. Default forbidden in tests;
  tests opt in via `Test.allowOutboundHttp` and use a
  recorded-response harness.

The test framework has these primitives. Test authors get
deterministic behavior unless they explicitly opt out.

## Test results as ops

Test results stream into the daemon's `test-results` stream:

```dark
type TestResult =
  { testName: String
    runId: Uuid
    status: TestStatus  // Pass | Fail of String | Skip of String
    durationMs: Int64
    traceId: Option<TraceId>  // for failures
    timestamp: DateTime
    fnHash: Bytes
    branch: String }
```

Per iter 11, projections over this stream give:

- **Flake detector**: tests that pass/fail intermittently —
  flag as flaky.
- **Trend dashboard**: which tests have gotten slower over
  time, which have started failing recently.
- **Coverage projection**: `(fnHash → tests that exercise it)`
  for the `--watch` mode.
- **CI dashboard**: per-PR test status; per-branch latest run.

`dark test --history Mycorp.Tests.UserApi.deleteUser_cascadesPosts`:

```
2026-05-09 14:22  Pass    3.4ms
2026-05-09 13:11  Fail    3.5ms   (cascade missed)
2026-05-09 13:08  Fail    3.5ms   (cascade missed)
2026-05-08 18:00  Pass    3.4ms
...
```

This kind of insight comes for free when results are ops.

## Property test machinery

Property tests need:
- Generators (`Gen<T>` for any type).
- Shrinking on failure.
- Statistics (how many cases ran; how often each branch hit).

Implementation:
- `Stdlib.Test.Gen` namespace with generators for each Stdlib
  type. User-defined types: derive from struct shape.
- Shrinking: standard depth-first shrink + verify, halt on
  smallest fail.
- Stats: optional `Test.observe` calls within the property fn.

~500 LOC of Dark, none of it daemon-side. Just stdlib.

## Test fixtures

Today's tests sometimes need elaborate fixtures: pre-populated
DBs, fake users, sample data. New idiom:

```dark
let withSampleUsers (test: Account -> Test.Result) : Test.Result =
  Test.withScopedAccount (fun acct ->
    Test.applyOps acct [
      Op.CreateType "User" ...
      Op.DBWrite "users" "alice" ...
      Op.DBWrite "users" "bob" ...
    ]
    test acct
  )

[<Test>] let listUsers_returnsAll () : Test.Result =
  withSampleUsers (fun acct ->
    let users = Test.eval acct "Mycorp.users.listAll ()"
    Test.assertEq (List.length users) 2L
  )
```

Fixtures are just Dark fns that wrap `Test.withScopedAccount`.
Composable, typed, reusable.

For really large fixtures (a "production-like" test environment),
the fixture is itself an ops.db file checked into the repo:

```dark
[<Test>] let analyticsQuery () : Test.Result =
  Test.withFixtureDb "fixtures/large-analytics.opsdb" (fun acct ->
    let result = Test.eval acct "Mycorp.analytics.report ()"
    Test.assertEq (List.length result) 1024L
  )
```

The fixture file is binary, content-addressed, ~MB-scale.
Versioned alongside source. Daemon loads it as the scoped
account's initial state.

## CI integration

`dark test --output junit-xml` for Jenkins/GitLab/etc.
`dark test --output github-actions` for inline annotations.
`dark test --output json` for arbitrary tools.

The test runner is just Dark code; output formatters are pluggable
fns (`registerOutputFormat "junit-xml" formatJunit`). User can
add their own.

CircleCI / GitHub Actions / etc. each run `dark test` with the
appropriate format. Daemon spins up, tests run, daemon exits
when test session done.

## Open questions

1. **Tests that are themselves daemon-related.** Testing the
   daemon's startup, sync logic, etc. requires either
   spawning real daemons (slow) or mock-daemoning. Recommendation:
   minimal real-daemon spawning in a `tests/daemon-integration/`
   tier; mostly mock at the Dark level for unit-style tests.
2. **Snapshot testing.** Today's golden-file tests
   (`backend/testfiles/execution/...`) are essentially snapshot
   tests. New infra: `Test.assertSnapshot` writes the value to
   a file on first run; subsequent runs compare.
3. **Test selection by code change.** `dark test --since main`
   runs only tests affected by changes since main. Uses the
   `(fnHash → tests)` coverage projection. ~10× speedup on
   iterative work.
4. **Test data privacy.** Tests can include sensitive fixtures
   (PII, secrets). Default: tests run in scoped accounts that
   never sync. Opt-in to "sync test results" if desired.
5. **F# tests of internals.** Stay in F# Expecto. The migration
   doesn't touch them.
6. **Dark-side test framework for the test framework itself.**
   Bootstrapping issue (the test framework's own tests). Run
   the framework against itself once it's stable; before that,
   F# tests of the test runner.
7. **Performance regression tests.** Capture timing data per
   test; alert when fn-hash X gets slower by Y%. Cheap to add
   given the test-results stream.
8. **Distributed test execution.** Long-term: multiple daemons
   sharing a test workload. Each daemon claims a subset of
   tests. Useful for >100K-test repos. v2.

## TL;DR

- Tests are Dark fns annotated `[<Test>]`. `Test.assertEq`,
  `Test.withScopedAccount`, `Test.withTwoPeers` are the main
  primitives.
- Daemon runs tests in a shared process; per-test cost is
  scoped-account creation (~ms).
- Multi-peer / sync / conflict tests become trivially writable
  for the first time.
- Trace-replay tests fall out of iter 13 — every bug becomes a
  regression test in one command.
- Migration is side-by-side: new tests use new infra; old .dark
  testfiles work via a shim; F# unit tests stay in F#.
- Test results are ops; flake/trend/coverage projections come
  for free.

The shape of a test changes — for the better. Multi-peer and
sync tests being feasible is a step-change in what we can
verify.
