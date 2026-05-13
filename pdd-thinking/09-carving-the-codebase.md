# 09 — Carving the Codebase

> Stachu's directive: "cut all corners as you see fit. any 'packages' or F# that's not needed for this experiment, cut. remove, delete, comment out, I don't care. tighten things so you can work efficiently"

This is intentionally a *first pass* — verify each disable in a build sweep before claiming it works.

## The current shape (verified 2026-05-13 ~01:15 EDT)

`backend/fsdark.sln` projects:

| Project | Keep? | Notes |
|---|---|---|
| `Prelude` | **Keep** | Everything depends on it |
| `LibConfig` | **Keep** | Trivially small, depended on |
| `LibDB` | **Keep** | SQLite playback, package store |
| `LibSerialization` | **Keep** | Serializing RT/PT |
| `LibExecution` | **Keep** | We're modifying this |
| `LibParser` | **Keep, *but uncertain***. Stachu 2026-05-13: not sure if we use LibParser or a new thing. See "Parser open question" below. |
| `LibPackageManager` | **Keep** | Package CRUD |
| `LibCloud` | **Disable** | GCP / cloud — unrelated to PDD spike |
| `LibTreeSitter` | **Maybe disable** | Verify nothing in keepers depends on it |
| `LocalExec` | **Keep (at least partially)** | Stachu confirms: needed to **reload packages from `.dark` files**. The CLI relies on it for the parse-Dark-source-and-import pipeline that PDD will lean on (materializations have to land in the package store, and re-loading after edits is a normal dev flow). |
| `Builtins.Pure` | **Keep** | Basic ops — needed everywhere |
| `Builtins.Cli` | **Keep** | CLI builtins |
| `Builtins.CliHost` | **Keep** (trimmed) | The orchestrator — keep but cut LibCloud dep |
| `Builtins.Http.Client` | **Keep** | Needed for LLM API calls (gate by capability) |
| `Builtins.Http.Server` | **Disable** | No server in spike |
| `Builtins.Language` | **Keep** | Parser / reflection builtins |
| `Builtins.Matter` | **Keep** | Package + branch ops (we save materializations as packages) |
| `Builtins.Random` | **Keep** | Cheap; useful |
| `Builtins.Time` | **Keep** | Cheap; useful |
| `Tests` | **Trimmed-keep** | Disable most subdirs; keep LibExecution-relevant |
| `TestUtils` | **Keep** | Support library |

## Dep graph (verified from `.fsproj` files)

- `Cli` → `LibExecution`, `LibDB`, `Builtins.CliHost`
- `Builtins.CliHost` → `Prelude`, `LibExecution`, `Pure`, `Http.Client`, `Language`, `Cli`-builtins, `Time`, `Random`, **`Http.Server`**, **`Matter`**, `LibDB`, **`LibCloud`** ← bold = candidate cuts
- `LibParser` → `Prelude`, `LibExecution`, `LibDB`

So the critical path-to-spike:
```
Cli → CliHost → LibCloud (CUT)
            → Http.Server (CUT)
```

We need to **trim `Builtins.CliHost.fsproj`** to remove the two cut deps, then **disable LibCloud + Http.Server** at sln level.

## The carving procedure (when you sit down)

```bash
# 0. Make sure we're on pdd, clean
cd /home/stachu/code/dark/main
git checkout pdd
git status

# 1. Edit fsdark.sln — comment out / remove these Project blocks:
#    - LibCloud
#    - Builtins.Http.Server
#    (search "LibCloud", "Http.Server" in fsdark.sln)
#
#    Equivalently, delete the lines using a text editor. The sln is regenerable.

# 2. Edit backend/src/Builtins/Builtins.CliHost/Builtins.CliHost.fsproj:
#    Remove the two ProjectReference lines:
#    - <ProjectReference Include="../Builtins.Http.Server/..." />
#    - <ProjectReference Include="../../LibCloud/LibCloud.fsproj" />
#
#    Then find references to LibCloud / Http.Server symbols in CliHost source and
#    stub them out (probably an init / builtin registration table; remove the
#    server/cloud entries).

# 3. Rebuild
./scripts/compile

# 4. Confirm CLI still works
./scripts/run-cli docs for-ai

# 5. Run a narrow test
./scripts/run-backend-tests --filter Tests.LibExecution
```

**Per the "Dark type changes need two F# build passes" memory:** if we touch any package-referenced types (we will, in Day 1), expect to `touch backend/src/LibExecution/package-ref-hashes.txt` and rebuild once. The first build regenerates the hashes; the second picks them up.

## Parser open question (Stachu 2026-05-13)

> *"I'm not sure if we should use LibParser or use some new thing"*

Three real options:

### Option P1 — Use LibParser as-is
- It exists. It parses Dark. Cli already depends on it.
- **But:** PDD wants to ingest "mostly garbage" — pseudocode that may fail Dark's parser. LibParser is strict by design.
- A possible bridge: catch the parser error, capture the offending region as a `Pending` reference at the AST level, continue with the surrounding code parsed normally. The parser stays unchanged; we add a *lenient post-processor*.

### Option P2 — A new tolerant parser
- Likely built with combinators (matches Stachu's `dl-mar15-advisor-call.md` thinking on combinators vs tree-sitter — "combinators written in Dark might be OK for our use case").
- Designed to return partial trees on failure rather than reject.
- Each unrecognized name becomes a `Pending` reference automatically.
- More work to build. Bigger surface area.

### Option P3 — Skip parsing for the LLM path entirely
- Have the LLM emit *already-shaped* PT, as JSON or structured output.
- The "parse pseudocode" step becomes "deserialize JSON."
- The "find" path can still consult LibParser-parsed `.dark` source.
- **Easiest** option for the spike. The LLM is the source of truth for shape; the JSON serializer guarantees well-formed PT.

### Recommendation

**Start with P3 for the spike.** Skip parsing pseudocode entirely; have the LLM emit JSON shaped like `Pending` references + body. Use LibParser only for loading existing `.dark` files (which it already does).

Move to P1 (LibParser + lenient post-processor) when you want users to actually *write* pseudocode in source files. Skip P2 until P1 proves insufficient.

This decision interacts with `02-libexecution-changes.md`'s `SignatureHint`: in P3, the LLM produces well-typed hints directly. In P1, the lenient post-processor has to guess.

## What about `LibTreeSitter`?

It's listed in `backend/src/` and as a sln project. None of our keepers reference it directly (verified via `grep "ProjectReference.*LibTreeSitter"`). Almost certainly safe to disable, but verify with a build sweep first. If something breaks, re-enable.

## What about `LocalExec`?

**Keep — at least the package-reload bits.** (Stachu confirmed 2026-05-13.)

LocalExec is what reloads packages from `.dark` files into the runtime package store. This is the normal "I edited a `.dark` file, re-import it" pipeline, and PDD will lean on it heavily: every materialization either comes from the package store (find path) or *becomes* a package store entry (after generation + commit). The reload path is therefore on the critical path.

What you can probably still trim inside LocalExec:
- One-shot execution scripts unrelated to package management (look for `Scripts/`, `Bench/`, etc.)
- Anything that runs a CronChecker / QueueWorker style flow (already gone from this sln but check)

But the parse-Dark-source → PT → import-into-package-store pipeline stays. When in doubt, leave it.

```bash
grep -r "LocalExec" backend/src/Cli backend/src/Builtins 2>/dev/null
# Map the references; keep the ones tied to package import/reload.
```

## Tests subset

Most tests will rot when LibCloud goes. To compile, **disable individual test files** rather than the whole `Tests` project:

In `backend/tests/Tests/Tests.fsproj`, comment out `<Compile Include="...">` lines for test files that import LibCloud or do HTTP-server testing. Keep the LibExecution-relevant tests.

Likely safe-to-keep:
- `Tests/LibExecution.Tests.fs` (the core interpreter tests)
- `Tests/Parser.Tests.fs` (if it doesn't pull anything cloud)
- `Tests/Builtin.Pure.Tests.fs`
- `Tests/Builtin.Time.Tests.fs`
- `Tests/Tests.fs` (the runner)

Likely safe to disable:
- `Tests/BwdServer.Tests.fs` (if exists)
- `Tests/HttpClient.Tests.fs` (or do we want it?)
- `Tests/CliTraces.Tests.fs` (already disabled per recent commit `9ca0d855d`)
- Anything mentioning HttpHandler

## What we're adding back (PDD bits)

Once carving is done, add:

### `backend/src/LibPDD/` (new project)

```
LibPDD/
  LibPDD.fsproj
  paket.references
  Materializer.fs       — find + generate coroutines + race
  TraceEvents.fs        — new event kinds
  Capability.fs         — capability tags + checks
  Find.fs               — corpus search (name + arity for v0)
  Generate.fs           — LLM call wrapper
  Defaults.fs           — defaultFor : TypeReference -> Dval (for tolerant mode)
```

Add it to `fsdark.sln`. Reference it from `Builtins.CliHost` and `Cli`.

### `LibExecution` additions

Per `02-libexecution-changes.md`:
- New `FQFnName.Pending` variant in `RuntimeTypes.fs`
- New `PackageManager.materializeFn` field
- New `VMState.pendingFrames` + scheduler in `Interpreter.fs`
- New `RTE.MaterializationFailed` error
- Trace event hooks

### `Cli` additions

- `dark pdd run <expr>` command
- `--tolerance strict|loose|debug` flag
- `--allow http,fileread,...` flag (capabilities)
- `dark trace show <id>` command

## What we are NOT touching

- `backend/src/Prelude/` — Don't even open it.
- `scripts/` — Don't touch the `_assert-in-container` shim (memory).
- `package-ref-hashes.txt` — embedded; touch only when forced by type changes.
- Dark `.dark` files — they parse fine; we may add new ones under a `pdd_demos/` subdir later.

## Stop-loss

If carving breaks something subtle and we burn an hour, **revert and proceed without carving**. The spike can run with the existing full F# build — just slower iteration cycles. The carving is an optimization, not a prerequisite.

```bash
# Escape hatch:
git checkout main -- backend/fsdark.sln backend/src/Builtins/Builtins.CliHost/Builtins.CliHost.fsproj
./scripts/compile
```

Done. We're back to a full build, with our `pdd-thinking/` notes still in place.
