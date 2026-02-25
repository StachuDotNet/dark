# Hot Reloading for Darklang CLI Applications

## Vision

A developer writes a CLI app in Darklang using an Elm-like architecture (State,
Update, View). The app is running. They change a function, type, or value in the
package tree (via `fn`, `type`, `val`, or VS Code). The running app immediately
picks up the new code. If the State type changed, the runtime migrates the
in-memory state to the new shape. The view re-renders. No restart. Smalltalk-
style live programming for the terminal.

## Non-Goals (for now)

- Hot reloading the `dark` CLI binary itself
- Distributed/multi-process hot reload
- Complex TUI framework (start simple, iterate)

---

## How Things Work Today

### Content-Addressed Storage + ID Stabilization

Package items are stored in three content tables — none are branch-scoped:

| Table | Key | Content |
|---|---|---|
| `package_functions` | UUID (PK) | `pt_def` (source), `rt_instrs` (compiled bytecode) |
| `package_types` | UUID (PK) | `pt_def` (source), `rt_def` (compiled) |
| `package_values` | UUID (PK) | `pt_def` (source), `rt_dval` (evaluated, nullable) |

The `locations` table maps `(owner, modules, name) → item_id` per branch. This
is the only branch-scoped content table.

**When you edit a function at the same location**, `stabilizeOpsAgainstPM` reuses
the existing UUID. So:

1. You `fn Foo.bar ...` → function gets UUID `abc123`, stored in
   `package_functions`
2. You `fn Foo.bar ...` again (new body) → ID stabilization finds `abc123` at
   that location → reuses it
3. `INSERT OR REPLACE INTO package_functions (id='abc123', pt_def=<new>,
   rt_instrs=<new>)`

The UUID is the same. The content behind it is new. This is the key property
that makes hot reload possible: **callers hold UUID references, and those
references remain valid even after the function body changes.**

### The Two PackageManagers

There are two module-level singleton PMs in `PackageManager.fs`:

**RT (Runtime)** — used during execution:
```fsharp
let rt : RT.PackageManager =
  { getType = withCache PMRT.Type.get
    getFn = withCache PMRT.Fn.get
    getValue = withCache PMRT.Value.get
    init = uply { return () } }
```

**PT (Program Types)** — used for editing, type checking, name resolution:
```fsharp
let pt : PT.PackageManager =
  { findType = fun (branchId, location) ->
      let chain = getBranchChain branchId
      withCache (PMPT.Type.find chain) location  // NOTE: see below
    getType = withCache PMPT.Type.get
    // ... etc
  }
```

### How Caching Works (and Doesn't)

`withCache` wraps a lookup function with a `ConcurrentDictionary`:

```fsharp
let withCache (f : 'key -> Ply<Option<'value>>) =
  let cache = ConcurrentDictionary<'key, 'value>()
  fun (key : 'key) ->
    uply {
      let mutable cached = Unchecked.defaultof<'value>
      let inCache = cache.TryGetValue(key, &cached)
      if inCache then return Some cached
      else
        let! result = f key
        match result with
        | Some v -> cache.TryAdd(key, v) |> ignore<bool>
        | None -> ()
        return result
    }
```

The `get*` caches (3 RT + 3 PT = 6 total) are created at module initialization
and persist for the entire process lifetime. They map UUID → content. **These
never invalidate.** Once a function body is cached by UUID, the cached version
is served forever, even if the DB row was updated via `INSERT OR REPLACE`.

The `find*` caches in PT are actually created inside lambdas (a new
`withCache` closure per call), so they don't persist across calls. This is
correct but wasteful — each `findType` call creates a fresh
ConcurrentDictionary, does one lookup (always a miss), then throws it away.

### The Execution Pipeline

When Darklang code runs:

1. `Execution.executeFunction` builds instructions and calls `executeExpr`
2. `executeExpr` creates a fresh `VMState` with empty `packageFnInstrCache`
3. The interpreter's main loop processes instructions
4. On encountering a package function call, the interpreter:
   a. Checks `vm.packageFnInstrCache` (per-VMState) — on hit, uses cached
      instructions
   b. On miss, calls `exeState.fns.package uuid` → goes through `rt.getFn`
      → `ConcurrentDictionary` → on miss, reads from DB
   c. Caches the result in `vm.packageFnInstrCache`

**For hot reload, two caches need invalidation:**
- The RT PM's `ConcurrentDictionary` (module-level, persists forever)
- The VMState's `packageFnInstrCache` (per-execution, but a long-running app
  loop runs in a single VMState)

### The HTTP Server Pattern

`Builtin.httpServerServe` is the template for "thin F# + Darklang logic":

```fsharp
// F# side: ~50 lines of infrastructure
| exeState, _, _, [ DInt64 port; DApplicable(AppNamedFn handlerFn) ] ->
  // Start ASP.NET Core server
  // For each request:
  //   1. Marshal HTTP request → Darklang Dval
  //   2. Call: executeNamedFn exeState handlerFn requestDval
  //   3. Marshal Darklang Dval → HTTP response
```

```dark
// Darklang side: all business logic
let serve (port: Int64) (handler: Http.Request -> Http.Response) : Unit =
  Builtin.httpServerServe port handler
```

The HTTP server has the same stale-cache problem: it captures `exeState` at
startup and never invalidates. Once generation-based caching exists, the HTTP
server gets hot reload for free between requests.

### Builtin Function Signature

Builtins receive `(ExecutionState * VMState * typeArgs * args)`:

```fsharp
and BuiltInFnSig =
  (ExecutionState * VMState * List<TypeReference> * List<Dval>) -> DvalTask
```

This means a builtin can mutate `vm.packageFnInstrCache` directly. This is
critical — a `pmClearCaches` builtin can clear both the module-level PM caches
AND the current VMState's instruction cache.

---

## Layer 1: Generation-Based Cache Invalidation

This is the foundation. Everything else builds on this. It's independently
valuable — fixes stale caches in LSP, HTTP server, and any long-running process.

### SQL: Generation Counter

New migration:

```sql
CREATE TABLE IF NOT EXISTS package_generation (
  id INTEGER PRIMARY KEY CHECK (id = 1),
  generation INTEGER NOT NULL DEFAULT 0
);
INSERT OR IGNORE INTO package_generation (id, generation) VALUES (1, 0);
```

Note: SQLite's `PRAGMA data_version` increments on any write from another
connection, but doesn't detect writes from the same connection/pool. An explicit
counter is more reliable.

### Inserts.fs: Bump on Write

Every path that writes to package tables bumps the generation:

```fsharp
let bumpGeneration () =
  Sql.query "UPDATE package_generation SET generation = generation + 1"
  |> Sql.executeStatementAsync
```

Called at the end of `insertAndApplyOps` (which handles both WIP and committed
ops), `commitWipOps`, `discardWipOps`, and any merge/rebase operations.

### Caching.fs: Expose Clear Functions

Rather than checking generation on every cache lookup (which would add a DB
read per PM operation), restructure `withCache` to return a clear handle:

```fsharp
type CachedLookup<'key, 'value> =
  { lookup : 'key -> Ply<Option<'value>>
    clear : unit -> unit }

let withCache (f : 'key -> Ply<Option<'value>>) : CachedLookup<'key, 'value> =
  let cache = ConcurrentDictionary<'key, 'value>()
  { lookup =
      fun (key : 'key) ->
        uply {
          let mutable cached = Unchecked.defaultof<'value>
          let inCache = cache.TryGetValue(key, &cached)
          if inCache then
            return Some cached
          else
            let! result = f key
            match result with
            | Some v -> cache.TryAdd(key, v) |> ignore<bool>
            | None -> ()
            return result
        }
    clear = fun () -> cache.Clear() }
```

### PackageManager.fs: Wire Up Clearing

```fsharp
let private rtGetType = withCache PMRT.Type.get
let private rtGetFn = withCache PMRT.Fn.get
let private rtGetValue = withCache PMRT.Value.get

let private ptGetType = withCache PMPT.Type.get
let private ptGetFn = withCache PMPT.Fn.get
let private ptGetValue = withCache PMPT.Value.get

let rt : RT.PackageManager =
  { getType = rtGetType.lookup
    getFn = rtGetFn.lookup
    getValue = rtGetValue.lookup
    init = uply { return () } }

// ... pt similarly ...

/// Clear all 6 module-level caches. Called when generation changes.
let clearAllCaches () =
  rtGetType.clear()
  rtGetFn.clear()
  rtGetValue.clear()
  ptGetType.clear()
  ptGetFn.clear()
  ptGetValue.clear()

/// Read current generation from DB
let getGeneration () : Task<int64> =
  Sql.query "SELECT generation FROM package_generation WHERE id = 1"
  |> Sql.executeAsync (fun read -> read.int64 "generation")
  |> Task.map (fun rows -> rows |> List.head)
```

### Two New Builtins

| Builtin | Signature | What it does |
|---------|-----------|--------------|
| `Builtin.pmGetGeneration` | `() -> Int64` | Reads `generation` from `package_generation` table |
| `Builtin.pmClearCaches` | `() -> Unit` | Calls `PackageManager.clearAllCaches()` AND sets `vm.packageFnInstrCache <- Map.empty` |

The `pmClearCaches` builtin is the only one that needs VMState access:

```fsharp
fn =
  (function
  | _exeState, vm, _, [] ->
    uply {
      PackageManager.clearAllCaches()
      vm.packageFnInstrCache <- Map.empty
      return DUnit
    }
  | _ -> incorrectArgs ())
```

### Why This Is Sufficient

When you `fn Foo.bar ...` to change a function:

1. `insertAndApplyOps` → `INSERT OR REPLACE INTO package_functions` with same
   UUID, new `rt_instrs` → `bumpGeneration()`
2. Runner's next loop iteration: `Builtin.pmGetGeneration()` returns new value
3. Runner calls `Builtin.pmClearCaches()`:
   - Clears RT ConcurrentDictionaries (so `rt.getFn uuid` will re-read from DB)
   - Clears `vm.packageFnInstrCache` (so interpreter won't use stale bytecode)
4. Next call to `app.update` → interpreter looks up UUID → PM cache miss →
   DB fetch → new `rt_instrs` → new behavior

### Impact Beyond Hot Reload

This fixes real bugs today:
- **LSP server**: long-running process, PM cache goes stale when packages are
  modified via CLI
- **HTTP server builtin**: same stale-cache problem (could check generation
  between requests)
- **Interactive CLI**: future-proofs against any caching-related staleness

---

## Layer 2: The App Type

Following the HTTP server pattern: define a type, write a runner, expose thin
builtins.

### The Parallel

| | HTTP Server | App (MVU) |
|---|---|---|
| **Type** | `HttpServer.Handler` | `App.App<'s, 'm>` |
| **Runner** | `HttpServer.serve` (Darklang) | `App.run` (Darklang) |
| **Builtin** | `Builtin.httpServerServe` | `Builtin.pmGetGeneration` + `Builtin.pmClearCaches` |
| **F# does** | Start server, HTTP ↔ Dval, call handler | Read generation, clear caches |
| **Darklang does** | Routing, response building | MVU loop, input, rendering, hot reload |

### The Type

```dark
module Darklang.Stdlib.App

type App<'state, 'msg> =
  { init: 'state
    update: 'state -> 'msg -> 'state
    view: 'state -> List<String>
    subscriptions: 'state -> List<Subscription<'msg>> }

type Subscription<'msg> =
  | OnKeyPress of (Stdlib.Cli.Stdin.KeyInfo -> Option<'msg>)
  | OnTimer of { intervalMs: Int64; msg: 'msg }
```

Darklang already supports calling function-typed record fields — `handler.handler
req` in httpserver.dark, `tool.handler` in the MCP server.

Start with `List<String>` for view. A richer `View` type (styled text, layouts)
can come later without changing the architecture.

### The Runner (Darklang)

```dark
module Darklang.Stdlib.App.Runner

let rec loop
  (state: 'state)
  (app: App<'state, 'msg>)
  (generation: Int64)
  : Int64 =

  // 1. Check for package changes
  let currentGen = Builtin.pmGetGeneration ()
  let generation =
    if currentGen != generation then
      Builtin.pmClearCaches ()
      currentGen
    else
      generation

  // 2. Render
  let lines = app.view state
  Stdlib.printLine "\u{001b}[2J\u{001b}[H"
  lines |> Stdlib.List.iter (fun line -> Stdlib.printLine line)

  // 3. Wait for input
  let keyInfo = Stdlib.Cli.Stdin.readKey ()
  let msgs =
    (app.subscriptions state)
    |> Stdlib.List.filterMap (fun sub ->
      match sub with
      | OnKeyPress handler -> handler keyInfo
      | _ -> Stdlib.Option.Option.None)

  // 4. Update
  let newState =
    msgs |> Stdlib.List.fold state (fun s msg -> app.update s msg)

  // 5. Loop
  loop newState app generation

let run (app: App<'state, 'msg>) : Int64 =
  let generation = Builtin.pmGetGeneration ()
  loop app.init app generation
```

### Why Hot Reload Works

When the user changes a function body:

1. `insertAndApplyOps` writes new `rt_instrs` for the same UUID (ID
   stabilization preserves the UUID) and bumps the generation counter
2. Next loop iteration: `pmGetGeneration()` returns new value
3. `pmClearCaches()` clears:
   - 6 module-level ConcurrentDictionaries (RT + PT get caches)
   - `vm.packageFnInstrCache` (the current VMState's instruction cache)
4. `app.view state` calls a function by UUID → interpreter checks
   `packageFnInstrCache` → miss → calls `rt.getFn uuid` → ConcurrentDictionary
   miss → DB fetch → gets new `rt_instrs`
5. New behavior takes effect immediately

The `app` record itself doesn't need re-resolving. It holds UUID references.
UUIDs are stable (ID stabilization). Only the content behind them changes.

### CLI Command

```dark
module Darklang.Cli.Commands.RunApp

let execute (state: Cli.AppState) (args: List<String>) : Cli.AppState =
  match args with
  | [valuePath] ->
    match Builtin.cliEvaluateExpression state.currentBranchId valuePath with
    | Ok appValue ->
      Stdlib.App.Runner.run appValue
      state
    | Error msg ->
      Stdlib.printLine (Colors.error msg)
      state
  | _ ->
    Stdlib.printLine "Usage: run-app <value-path>"
    state
```

---

## Layer 3: State Migration (Future)

### When Is Migration Needed?

Only when the **State type definition** changes. Function body changes don't
require migration.

Detection: track the `rt_def` blob of the state type's UUID. On generation
change, re-fetch and compare. If different, the type shape changed and the
in-memory state needs migration.

### Structural Auto-Migration

For simple changes (add field with default, remove field):

```dark
module Darklang.Stdlib.App.Migration

let autoMigrate
  (oldState: 'oldState)
  (defaults: Dict<Dval>)
  : Result<'newState, String> =
  // Needs builtins:
  //   Builtin.dvalToDict : Dval -> Dict<String, Dval>
  //   Builtin.dvalCoerceToType : Dval -> TypeId -> Dict<String, Dval> -> Result<Dval, String>
  ...
```

### User-Provided Migration

Convention: if the app record has a `migrate` field:

```dark
type App<'state, 'msg> =
  { ...
    migrate: Option<Dict<String, Dval> -> 'state> }
```

---

## Example: A Counter App

```dark
module Darklang.MyProject.Counter

type State = { count: Int64; label: String }

type Msg =
  | Increment
  | Decrement
  | Quit

let app : Stdlib.App.App<State, Msg> =
  Stdlib.App.App
    { init = State { count = 0L; label = "Counter" }

      update = fun state msg ->
        match msg with
        | Increment -> { state with count = state.count + 1L }
        | Decrement -> { state with count = state.count - 1L }
        | Quit -> state

      view = fun state ->
        [ state.label
          ""
          $"  Count: {Stdlib.Int64.toString state.count}"
          ""
          "  [up] Increment  [down] Decrement  [q] Quit" ]

      subscriptions = fun _state ->
        [ Stdlib.App.Subscription.OnKeyPress (fun key ->
            match key.key with
            | UpArrow -> Stdlib.Option.Option.Some Msg.Increment
            | DownArrow -> Stdlib.Option.Option.Some Msg.Decrement
            | Q -> Stdlib.Option.Option.Some Msg.Quit
            | _ -> Stdlib.Option.Option.None) ] }
```

Running it:

```
$ dark run-app Darklang.MyProject.Counter.app
```

Now in another terminal:

```
$ dark fn "Darklang.MyProject.Counter.update (state: State) (msg: Msg): State =
    match msg with
    | Increment -> { state with count = state.count + 10L }
    | Decrement -> { state with count = state.count - 10L }
    | Quit -> state"
```

The running app immediately starts incrementing/decrementing by 10. No restart.

---

## Implementation Phases

### Phase 0: Generation Counter + Cache Invalidation

1. **SQL migration**: Add `package_generation` table
2. **Inserts.fs**: Call `bumpGeneration()` at the end of `insertAndApplyOps`,
   `commitWipOps`, `discardWipOps`, and merge/rebase paths
3. **Caching.fs**: Restructure `withCache` to return `{ lookup; clear }`
4. **PackageManager.fs**: Wire up `clearAllCaches()` and `getGeneration()`
5. **Builtins**: Add `Builtin.pmGetGeneration` and `Builtin.pmClearCaches`
   (the latter clears both module-level PM caches and `vm.packageFnInstrCache`)

Independently valuable: fixes stale caches in LSP server, HTTP server, etc.

### Phase 1: App Type + Basic Runner

1. **Dark packages**: Define `App<'s, 'm>`, `Subscription<'m>` types
2. **Dark packages**: Write the runner loop with generation checking
3. **CLI**: Add `run-app` command
4. **Demo**: Build a counter app, verify hot reload works

### Phase 2: State Migration

1. **Builtins**: Add `Builtin.dvalToDict`, `Builtin.dvalCoerceToType`
2. **Dark packages**: Write `App.Migration.autoMigrate`
3. Wire type-change detection into the runner loop

### Phase 3: VS Code Integration

Live preview pane, state inspector, migration assistant.

---

## Open Questions

1. **Exit mechanism**: How does an app signal "I'm done"? Options:
   - `update` returns `('state, ShouldContinue)` tuple
   - Runner catches a specific error as "exit"
   - Convention: an `Exit` variant in the msg type

2. **Error handling during reload**: If new code has a runtime error, show error
   overlay, keep old state, wait for next generation bump. Elm/Smalltalk
   approach — the app doesn't crash, it shows the error until you fix it.

3. **Timer subscriptions**: `readKey` blocks. Timers need non-blocking key
   reading with a timeout. Probably needs `Builtin.stdinReadKeyWithTimeout`.

4. **WIP vs committed**: Should uncommitted WIP changes trigger hot reload?
   Probably yes — that's the Smalltalk model. `insertAndApplyOps` handles both
   WIP and committed ops, so generation bumps cover both.

5. **PT find cache inefficiency**: The PT PM's `find*` operations currently
   create a new `withCache` closure per call (inside the lambda), so they
   never actually cache. This is correct but wasteful. Could be fixed by
   caching at the module level (like `get*`), but then invalidation matters.
   Separate concern from hot reload.

6. **Ecosystem around App type**:
   - `App.withLogging` — wraps update to log every message
   - `App.withDevTools` — adds a state inspector overlay
   - `App.test` — runs an app with scripted inputs, asserts on state/view
   - `App.compose` — combine sub-apps (like Elm's `Browser.application`)
   - VS Code live preview
   - Record/replay for debugging
