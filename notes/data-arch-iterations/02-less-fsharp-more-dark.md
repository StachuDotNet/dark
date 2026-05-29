# Iter 02 — less F#, more Dark

The user's prompt: "dream of less F# and more dark." The existing
docs touch this — the rewrite doc § 14 has a sketch of new
`Stdlib.Sqlite` / `Stdlib.App` / `Stdlib.Sync` surface — but
they don't draw the migration line. Here's where I'd put it,
and what gets us across.

## The cut: F# is a runtime, Dark is the product

Today the line is fuzzy. `LibCloud`, `LibDB`, half of `Builtins.*`
are doing application-level work in F# — package management,
trace recording, account lookups, scripts CRUD, branch operations.
Each of them is also exposed back into Dark as a builtin, so
Dark code uses them through a thin wrapper. That layer is the
seam: F# does the work, Dark "wraps" it.

In the new world, **F# does the things F# is uniquely good at:
running the interpreter, talking to the OS, holding sockets.
Dark does everything else.** Concretely, F# stays for:

- The interpreter (`LibExecution.Interpreter`).
- Type checker (until there's a Dark type checker — long-term goal,
  not a v1 concern).
- Binary serialization — only because Dark can't yet emit its own
  bytecode. Once it can, this could move too.
- SQLite plumbing (`LibDB.Sqlite`) — opens connections, runs raw
  queries. Maybe ~150 lines.
- Cryptography (`LibSerialization.Hashing`) — calls into
  `System.Security.Cryptography`. ~50 lines after cleanup.
- Process supervision (the daemon's subprocess host).
- The HTTP listener (`Builtins.Http.Server.HttpListener`).
- The WebSocket client to the hub.
- The unix-socket RPC server (`darkd`'s control plane).

That's the "thin runtime." Everything above it is Dark.

## What moves to Dark, and why

| Today (F#) | Where it goes | Why |
|---|---|---|
| `LibDB.PackageManager` (lookups + chain walk) | `Stdlib.PackageStore` (Dark) over a Sqlite builtin | It's just SQL queries. Putting it in Dark lets Dark fns introspect their own package tree without crossing a builtin boundary. |
| `LibDB.Inserts` (op append + projection update) | `Stdlib.Ops.append` + `Stdlib.Projections.catchUp` (Dark) | Once we have raw SQL access, "INSERT INTO ops" is one line. Branch-aware updates are pure logic. |
| `LibDB.PackageOpPlayback`, `BranchOpPlayback` | `Darklang.Projections.Package.builder` etc. (Dark) | Unified-model § 4: every `ProjectionBuilder` is `applyOp conn op` — ~50-200 lines per stream. Trivially Dark. |
| `LibDB.Tracing` (594 lines) | `Darklang.Projections.Traces.builder` (Dark) + a thin F# event emitter | Today it's a recorder + a writer + a session. As ops, it's `emitOp("traces", trace_id, RecordCall {...})` from F# (one-line builtin call) and `applyOp` in Dark. |
| `LibDB.Branches`, `Rebase`, `Merge` | `Darklang.SCM.*` | We already have most of `Darklang.SCM` in Dark; finish the migration. |
| `LibDB.Queries` (817 lines, branch-aware SQL) | `Darklang.SCM.PackageStore.find*` + per-projection queries written as Dark | Per-projection queries don't need branch-aware filtering anymore (the projection IS the chain). Most of these queries collapse to "SELECT FROM <projection> WHERE name = ?". |
| `LibDB.UserDB` | `Darklang.AppStore.*` | Same shape — KV access on an app's `data.db`. Dark calls one builtin to open/connect. |
| `LibCloud.Account` | `Darklang.Auth.Account` (Dark) | All it does is "SELECT/INSERT on accounts table." Trivially Dark. |
| `LibCloud.File` | `Stdlib.File.*` (Dark + Cli builtins) | Mostly there already. |
| `LibCloud.Serialize.fs` | dies | This was the canvas-era serialize layer; absorb survivors into the projection builder. |
| `LocalExec.LocalExec` (550-line entry-point CLI) | `Darklang.Bootstrap.*` (Dark) + a 50-line F# main | Once the daemon owns bootstrap, LocalExec is a debug-only tool. Most of its commands (migrations, sweep-blobs, export-seed) become Dark fns invoked through `dark debug …`. |
| `LocalExec.LoadPackagesFromDisk`, `BenchmarkScenarios`, `Migrations.fs` | `Darklang.Tooling.*` | Tooling-shaped work; perfect for Dark. |
| `Builtins.Matter.Libs.PM.*` (large) | thin builtins + Dark `Darklang.SCM.*` | Most are wrapper-level; move the wrappers up to Dark, leave a tiny "open SQLite" builtin family. |
| `Builtins.Cli.Libs.{File,Directory,Process,Stdin,Terminal,Posix}` | stays — these ARE the OS bindings | F# stays the OS-binding layer. But many of the *fns* using these are pure transforms that should be Dark. |
| `Builtins.Pure.Libs.{Int8,Int16,…UInt128}` (10 files, 5400 lines) | mostly stays, but per the audit, templated/CG'd | The arithmetic itself can't be Dark (it IS the int support); but the redundant per-width files can collapse via templating. |
| `Builtins.Pure.Libs.Json` (769 lines) | stays | JSON ser/deser is hot-path; performance matters. |
| `Builtins.Language.Libs.LanguageTools` (introspection) | stays in spirit but shrinks to a few primitives | Once Dark can read its own package store directly, language tools that "list all builtins" can be Dark queries. |
| `Cli.Cli` (entry point) | stays — it IS the F# entry | Tiny. ~80 lines today. |

## Order to migrate

The constraint: every step must leave the tree green and shippable.
The migration follows the unified-model phasing (slices 0-9 in the
rewrite doc), but with one extra discipline: **whenever a slice
introduces a new F# subsystem, ask "could this be Dark?"** Below
is when each F# subsystem retires.

**During slice 0-3 (split DB, no daemon yet):**
- `LibDB.UserDB` rewrites to take a connection arg. F#-side stays;
  the per-app routing happens at the call site.
- `LibCloud.Serialize` retires; survivors fold into `LibDB`.

**During slice 4 (per-branch projections):**
- `LibDB.Queries` shrinks dramatically. Each projection has trivial
  per-key queries; no recursive CTE. ~600 of 817 lines die.
- `LibDB.PackageOpPlayback` becomes the Dark `Darklang.Projections.
  Package.applyOp` — F# side becomes ~50 lines of "load typed op,
  call Dark fn." Critical: this is the first Dark fn that's on
  the daemon's hot path.

**During slice 5 (the daemon):**
- The daemon's command dispatcher is Dark from day one. F# does
  socket termination + thread the request to a Dark fn.
- `LibCloud.Account` retires. `Darklang.Auth.Account` replaces it.
- The "`builtinsToUse`" `let rec/and` chain in `Builtins.CliHost.
  Libs.Cli` simplifies — daemon owns one set of builtins for its
  process lifetime; per-request state is held in `ExecutionState`.

**During slice 6 (apps):**
- `Stdlib.App.*`, `Stdlib.Sqlite.*`, `Stdlib.Datastore.*` — all
  Dark. Builtins underneath are tiny: `sqliteOpenForApp` (returns
  a handle), `sqliteExec` (runs SQL on a handle), `sqliteClose`.
  Maybe 5 builtins, ~150 lines.
- App supervisor: F# subprocess management. ~200 lines, stays F#.

**During slice 7 (sessions):**
- `Stdlib.Session.*` is Dark. The "current session" check is a
  builtin lookup against a daemon-side dict.

**During slice 8 (sync):**
- The hub WS client is F# (System.Net.WebSockets bindings).
  ~150 lines.
- The sync state machine — push/pull, retries, watermarks,
  backoff — is Dark. Maybe ~400 lines of Dark.
- Per-stream sync policy lookup is a Dark fn.

**During slice 9 (subprocess apps):**
- App-host subprocess is a tiny F# main (~30 lines: connect to
  daemon over stdin/stdout, run a Dark fn, ship results).

## What this changes about the codebase

Today: ~52,500 LOC in `backend/src/`. Audit-projected ~5,000 lines
of low-risk cleanup. The migration above takes another **roughly
20,000-25,000 lines** out of F# and into Dark over slices 4-9.

Rough breakdown of what dies in F# (approximations):

```
LibCloud.Account                ~100  → Darklang.Auth.Account
LibCloud.Serialize              ~200  → folded
LibDB.PackageOpPlayback         ~520  → Darklang.Projections.Package
LibDB.BranchOpPlayback          ~180  → Darklang.Projections.Branches
LibDB.Tracing                   ~594  → Darklang.Projections.Traces
LibDB.Queries                   ~600  (of 817) → projection-local
LibDB.PackageManager            ~300  → Darklang.SCM.PackageStore
LibDB.Branches                  ~250  → Darklang.SCM.Branch
LibDB.Rebase                    ~140  → Darklang.SCM.Rebase
LibDB.Merge                     ~120  → Darklang.SCM.Merge
LibDB.Inserts                   ~280  → Darklang.Ops.Append
LibDB.WipRefresh                ~180  → dies
LibDB.Caching                   ~60   → dies (projections ARE the cache)
LibDB.Propagation               ~150  → Darklang.SCM.Propagate
LibDB.HashStabilization         ~?    → Dark
LibDB.UserDB                    ~574  → Darklang.AppStore (~150 lines, much shorter)
Builtins.Matter.Libs.PM/*       ~2200 → ~600 thin builtins; ~1600 lines move up to Dark
Builtins.Matter.Libs.DB         ~663  → ~200 thin builtins
Builtins.Matter.Libs.Traces     ~960  → ~200 thin builtins (ProjectionBuilder is in Dark)
LocalExec.LocalExec             ~550  → ~80 line entry; rest is Darklang.Bootstrap
LocalExec.LoadPackagesFromDisk  ~?    → Dark
LocalExec.Migrations            ~120  → after this PR, mostly dies
WrittenTypesToProgramTypes      ~981  (parser) — long-term Dark, but stays F# for v1
                                 ──────
                                 ~9,500 lines of F# directly retire
                                 plus ~10,000 lines of "could be Dark"
```

A meaningful chunk of `backend/src` retires. The Dark side grows
correspondingly — but Dark code is more amenable to being read by
agents, by the LSP, and by the user.

## "Could be Dark, should be Dark" — the discipline

A useful test: when adding a new module, ask:

1. **Does this need OS bindings?** (file, socket, timer, crypto)
   → F#, ideally one tiny builtin.
2. **Does this need the interpreter internals?** → F#.
3. **Is this performance-critical to the inner loop?** (per-call,
   per-op) → F# probably; profile first.
4. **Is this *application* work — schemas, queries, state machines,
   policies?** → **Dark.**

The friction today: Dark can't easily do (4) because every
trivial helper requires a new builtin. Two interventions break the
logjam:

- **Raw SQLite access** (per Stdlib.Sqlite). Once Dark can do
  arbitrary SQL on its own files, ~80% of `LibDB.*` can move up.
- **Hot reload** — once the daemon hosts long-lived state and Dark
  fns can be hot-swapped, "iterate on a Dark fn" is faster than
  "iterate on an F# fn." That changes incentives.

## The fnRenames audit again, in this lens

The `fnRenames` machinery (audit § 2) is a great example of "F# code
that should never have existed in F#." It's a generic
"old-name-deprecates-to-new-name" mapping with a fold and a Map.
Pure logic. No OS, no perf concern. In Dark this would be a
50-line `Darklang.Builtins.RenameMap.applyRenames` fn over a
declarative `[(oldName, newName)]` list. The fact that nobody's
defined a single rename in 12 months tells us we never needed
this code anywhere — but if we had, it would have been Dark code.

## "What stays in F# forever"

Long-term, F# is:
- Interpreter (`LibExecution`).
- Bytecode binary serialization.
- OS bindings (file, sockets, processes, signals).
- Crypto wrappers.

Everything else can be Dark. The dream end state is
`backend/src/` is ~5,000 lines of "F# runtime." Today: 52,500.
Five-to-tenfold reduction is achievable over a year of slicing.

## Risks I see

- **Cold start of the runtime.** A thinner F# means more bootstrap-
  time Dark execution. If the daemon's first request runs through
  ten Dark fns to dispatch, we pay tens of milliseconds of
  interpreter time per request. Today we pay zero (F# is direct).
  Mitigation: AOT-compiled hot paths in Dark (hash → instructions
  cache); skip dispatch when same-shape request is a known route.

- **Debuggability.** F# has structured exceptions, .NET debuggers,
  stack traces. Dark fn errors land as `RuntimeError` records —
  worse signal-to-noise on day one. Mitigation: surface call stacks
  better, ship `dark debug` tooling that reads the daemon's
  recent-trace ring buffer.

- **Type-system gaps.** Dark's type system is less expressive than
  F#'s today (no F#-style discriminated unions in arbitrary places,
  weaker exhaustiveness checking). Some F# code we want to move
  needs Dark to grow first. Mitigation: prioritize the Dark
  features that unblock migration. (`Result.collect` for
  fold-with-error patterns, exhaustive-match warnings, etc.)

- **Self-hosting hazard.** The runtime is F# *and* it runs Dark
  code that operates on the runtime. A bug in Dark code that
  manages projections could leave the projection broken AND
  prevent us from running the Dark code that fixes it. Mitigation:
  keep an F# "minimal mode" that can replay ops without going
  through any Dark fn — the escape hatch is `dark --minimal
  rebuild-projections`.

## Imagining the end state

By the end of slice 9:

- `dark` CLI: F# entry → Dark dispatch. Maybe 5 builtin call sites
  on a typical command's path. The rest is Dark.
- `darkd` (daemon): F# main loop. Owns the unix socket, the WS to
  the hub, the SQLite connections, the subprocess fan-out. Holds
  ~10 Dark fns it calls hot (op-append, projection-update, sync
  push/pull tick, app supervisor heartbeat, conflict scan).
  Everything else is Dark via the package store.
- `dark.run` (hub): runs in a daemon as an `App` of kind HTTP.
  Routes are Dark fns. The Postgres-backed identity store is
  accessed through a typed Dark Datastore.
- VS Code LSP: Dark code running in the daemon. The "F# language
  server" that exists today retires.
- Tests: F# Expecto for things that need F# (interpreter,
  serialization). Dark tests in `.dark` files for everything else
  — ~80% of the test corpus moves to Dark.

A user installing Dark for the first time gets ~80MB of binary
(AOT'd F# + the embedded ops snapshot). After login, all the
"interesting" code they read is Dark. The agent that helps them
write their app reads Dark to understand "how does the trace
recorder work." We never had to teach the agent F#.

That's the dream.
