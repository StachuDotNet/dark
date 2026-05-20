# The F# / Dark Line

> Loop T20 (2026-05-20). Per subsystem: what's the irreducible F#
> bit, what's Dark code on top. The line moves over time —
> capture v1 split (now) and v2 split (post Dark-interpreter-
> in-Dark).

Per FRONTIER's "what F# should grow / what F# should stop knowing"
framing. This doc makes the split concrete.

## What `main` looks like today

**11 F# projects** under `backend/src/`:

| F# project | Role |
|---|---|
| `Prelude` | basic types + utilities. Foundation. |
| `LibSerialization` | binary serialization (PT/RT/canonical hashing) |
| `LibConfig` | config + env vars + paths |
| `LibTreeSitter` | tree-sitter Dark grammar bindings |
| `LibParser` | parse `.dark` source → PT AST. Uses tree-sitter. |
| `LibExecution` | the interpreter. PT/RT types, eval loop, Stream<T>, Tracing. ~the heart. |
| `Builtins` | the 9 builtin assemblies (Pure / Http.Client / Http.Server / Random / Time / Cli / CliHost / Language / Matter) |
| `LibDB` | SQLite access. PackageManager, BranchOpPlayback, PackageOpPlayback, Rebase, Merge, Queries, Inserts. |
| `LibCloud` | Cloud-side helpers (toplevels, file io) |
| `LocalExec` | local-CLI orchestration. Migrations, LoadPackagesFromDisk, Benchmarks. |
| `Cli` | the CLI executable's entry point |

**Dark packages** under `packages/darklang/`:

- `stdlib/` — Stdlib in Dark
- `cli/` — the entire CLI app (MVU framework + commands + apps/{outliner, review, views})
- `scm/` — SCM commands (branch, commit, merge, rebase, ...)
- `tracing/` — trace tooling
- `languageTools/` — language tooling
- `languageServerProtocol/` — LSP plumbing
- `llm/` — LLM agent code (agent.dark + examples)
- `modelContextProtocol/` — MCP server (Darklang exposes ops as MCP tools)
- `vscode/`, `lsp-extensions/`, `prettyPrinter/`, `wip/`, etc.

Much more Dark-side than the sketches assumed. SCM, tracing,
LSP, the entire CLI app are already Dark code.

## Per-subsystem split — v1 (current/near-term)

### Interpreter / type checker / parser

| Concern | Side | Notes |
|---|---|---|
| AST type defs (PT/RT) | **F#** | `LibExecution.ProgramTypes` + `RuntimeTypes`. Cross-language Rosetta source; staying F# in v1. |
| Eval loop | **F#** | `LibExecution.Interpreter`. The substrate. Stays. |
| Type checker | **F#** | `LibExecution.TypeChecker`. Stays. |
| Parser | **F#** (with tree-sitter grammar in Dark dir) | `LibParser`. Used only at edit-time after BOOTSTRAP Phase 1. |
| **Conflict dispatch** *(T14)* | **F#** infra + **Dark** policy | F# provides the dispatch field on ExecutionState; Dark code installs auto-rules + policies per session. |
| **Errors-as-conflicts** *(T14 ext)* | **F#** glue | The raiseRTE → dispatch shim stays F#. Dark code can install per-error policies. |
| **EventBus** *(T16)* | **F#** | `LibExecution.EventBus`. The scheduler + RuntimeBuses live on ExecutionState. |
| **Parking + scheduler** *(T16)* | **F#** | Built on Ply + the scheduler. Frame park/wake is F# infra. |
| **Dark-side `Promise<T>` + `!`** | **Dark** | Compiles to a `Stdlib.Promise.force` builtin. |
| Stream<T> (existing) | **F#** | `LibExecution.Stream`. Stays. |

### Storage / SCM / packages

| Concern | Side | Notes |
|---|---|---|
| SQLite schema definition | **(declarative SQL)** | `backend/migrations/schema.sql`. Not Dark, not F# — its own thing. |
| Migration runner | **F#** | `LocalExec.Migrations`. Kill-and-fill mechanic. |
| Op apply (PackageOpPlayback, BranchOpPlayback) | **F#** | Performance-sensitive; stays F#. |
| Op definitions (PackageOp / BranchOp variant) | **F#** PT types | Cross-language source. |
| **CLI commands (SCM: branch, commit, merge, rebase, etc.)** | **Dark** ✓ | Already in `packages/darklang/cli/scm/`. |
| **Conflict detection (Rebase.getConflicts)** | **F#** today | Performance + DB-join-heavy. Could move to Dark via builtins later; not urgent. |
| **Conflict resolution dispatch policy** | **Dark** | Per IDENTITY: per-agent / per-session policies installed via Dark code. |

### Identity, capabilities, sharing

| Concern | Side | Notes |
|---|---|---|
| `accounts_v0` table access | **F#** via `LibDB.PackageManager` | One query is a builtin; Dark code uses it. |
| **`account_identities` lookup** *(T11)* | **F#** builtin | HTTP request → header → account lookup is performance-sensitive on every request. |
| **Delegation creation / verification** *(T12)* | **Dark** | Most logic; F# only for the chain-walk perf path. |
| **Cap-check in Apply** *(T15)* | **F#** | Inline in the interpreter's builtin-call path. Hot loop. |
| **Cap-grant UX (install-time + interactive)** | **Dark** | `packages/darklang/cli/cap.dark` (new). |
| **Cap-deny → ask-human flow** | **Dark** subscriber | Event bus subscriber in the viewer (Dark MVU app). |
| **Wire protocol (sync endpoints)** | **Dark** ✓ | HTTP server is `Builtins.Http.Server` (F#) + Dark handlers in `packages/darklang/sync/` (new). |
| **Auth (Tailscale header parsing)** | **Dark** | `Stdlib.Tailscale.fromHeaders` (new). |

### Bootstrap

| Concern | Side | Notes |
|---|---|---|
| Schema bootstrap | **F#** | `LocalExec.Migrations`. Pre-language. |
| **Seed export/grow** | **F#** ✓ | `LibDB.Seed` already exists. |
| **`.dark` file parsing** | **F#** | `LocalExec.LoadPackagesFromDisk`. Moves to a separate `LibBuildTools` project per BOOTSTRAP bootstrap-6, only linked in build-seed mode. |
| **Snapshot download (matter.darklang.com)** | **Dark** | `Stdlib.Install.fetchSnapshot` (new), called by `dark install`. |

### Apps + UI

| Concern | Side | Notes |
|---|---|---|
| MVU loop runtime (~500 LoC) | **F#** | New `LibExecution.Mvu`. Tick + Subs + Effects. |
| `SubApp` shape + MVU types | **Dark** ✓ | Already in `packages/darklang/cli/core.dark`. |
| Apps (outliner, review, views) | **Dark** ✓ | Already in `packages/darklang/cli/apps/`. |
| **The PDD viewer** | **Dark** | `packages/darklang/cli/apps/viewer/` (new); subscribes to EventBuses. |
| **The conflict-resolution UI** | **Dark** | `packages/darklang/cli/apps/conflict/` (new). |
| View tree (`View` ADT) | **Dark** | `Stdlib.UI.*` package (new), per assembly's per-target renderer. |
| Terminal rendering | **F#** | Reads `View` tree, emits ANSI. |
| Web rendering (later) | **F#** | Reads `View` tree, emits HTML. |
| Voice rendering (later) | **F#** + accessibility framework | Reads `View` tree, narrates. |

### Materializer / PDD / LLM

| Concern | Side | Notes |
|---|---|---|
| LLM HTTP call | **F#** today (via `Builtins.Http.Client`) | Could be `Stdlib.LLM.complete` Dark fn calling Http.Client. |
| **Agent runtime** *(T22)* | **Dark** | Already in `packages/darklang/llm/agent.dark` — refine into substrate-integrated app. |
| Materializer policy | **Dark** | `Stdlib.PDD.materialize` (eventually, per spike-end thinking). |
| Body parser (LibParser invocation) | **F#** | Stays for now. |
| Pending-handle registry | **F#** | Maps Guid → state. Hot path. |

### Tracing

| Concern | Side | Notes |
|---|---|---|
| Trace recording (in-eval) | **F#** | `LibExecution.Tracing` + `LibDB.Tracing`. Per-call performance critical. |
| **Trace viewer + replay** | **Dark** ✓ | `packages/darklang/tracing/`. |
| Cross-instance trace sync | **Dark** | New; uses sync wire protocol. |

## Per-subsystem split — v2 (post Dark-interpreter-in-Dark)

Once a Dark interpreter exists in Dark itself (FRONTIER's
long-term goal), the line moves dramatically:

| Concern | v1 side | v2 side |
|---|---|---|
| Eval loop | F# | **Dark** (with F# as the bootstrap-interpreter) |
| Type checker | F# | **Dark** |
| Parser | F# (tree-sitter binding) | **Dark** (tree-sitter binding stays F#; lexer/parser logic moves) |
| Builtins | F# only | F# (effectful, perf-critical) + Dark (pure shims for testing) |
| Materializer policy | F# substrate + Dark policy | Pure **Dark** |
| Hot-reload semantics | F# infra | **Dark** infra (with F# scheduler primitives exposed) |
| MVU runtime | F# | **Dark** |

v2 is **years out**. Don't design it now; just leave space.

## What stays F# forever (probably)

These are *substrate of the substrate* — Dark code calling Dark
code interpreting Dark code would bottom out somewhere:

- **`Prelude`** — F#-language utilities.
- **The lowest layer of the interpreter** — even with a
  Dark-in-Dark interpreter, the very bottom has to be F# to
  execute it.
- **Database driver** — `Microsoft.Data.Sqlite` lives in F#.
- **Tree-sitter binding** — C library + F# binding.
- **Network primitives** — `System.Net.Http` calls live in F#.
- **OS interaction** — file system, processes, signals.
- **Capability *check* mechanism** — even if the policies are
  Dark code, the gate at builtin-call has to be F# inline.
- **Scheduler primitives** — frame park/wake is F# Ply + closure.

Everything else is a candidate for Dark-side migration over time.

## Migration shape

The migration is **gradual and per-subsystem**. Examples:

- `Builtins.Http.Client.HttpClient` — F# implementation calls
  .NET HttpClient. Wrap in a Dark fn `Stdlib.Http.get`/`post`
  that calls the builtin. Now Dark code uses
  `Stdlib.Http.get`, not the builtin directly. Refactoring the
  internals (e.g., add retries, add tracing, add cap checks)
  becomes a Dark code change, not an F# rebuild.
- `Rebase.getConflicts` — F# query today. Wrap in
  `Stdlib.SCM.detectConflicts` Dark fn. Add cases for new conflict
  kinds in Dark. F# stays as the perf-critical SQL.
- `LoadPackagesFromDisk` — moves *out* of the runtime path
  entirely (per BOOTSTRAP bootstrap-6). Only invoked by the
  build-seed tool.

The line moves whenever it becomes cheaper to move it. No
big-bang.

## Open decisions

- **(Q-fd-1) When does the materializer become a Dark fn?**
  Plausible only after Phase 2 (caps + dispatch) so the
  materializer can ask for grants via the dispatch.
- **(Q-fd-2) When does the parser run only at edit-time?**
  Phase 1 (BOOTSTRAP) drops `.dark` parsing from the runtime
  path. The editor still uses it.
- **(Q-fd-3) Per-builtin `capabilities` field — F# or Dark?**
  Both worlds: F# declares the field; Dark code reads + uses
  it for refactoring and audit.
- **(Q-fd-4) Stdlib organization.** As more is in Dark, the
  `Stdlib.*` namespace grows. Substrate vs user — at what
  point do we have a "Stdlib core" boundary? Probably:
  Stdlib-substrate.* (cap-checked) vs Stdlib.* (sugar). Open.
