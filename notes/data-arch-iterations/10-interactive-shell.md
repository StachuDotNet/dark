# Iter 10 — `dark repl` as a first-class entrypoint

The CLI commands today (`eval`, `run`, `commit`, `branch`, etc.)
are one-shot: arg → action → exit. Useful, but they leave the
shell-like ergonomics on the table. With the daemon owning
state, a real REPL becomes cheap — and the REPL is the most
direct way to make Dark *feel* alive to a new user.

This iter sketches what `dark repl` looks like and what makes it
non-trivially better than `python3` or `node`.

## The 30-second tour

```
$ dark repl
Darklang 0.7.4 — connected to daemon (~/.darklang/daemon.sock)
Branch: main · Account: stachu · Session: ephemeral

> 1 + 1
2

> let users = [{name = "alice"}; {name = "bob"}]
users : List<{name: String}>

> users |> Stdlib.List.map (fun u -> u.name)
["alice"; "bob"] : List<String>

> :help
... shows :commands ...

> :branch feat-foo
Switched to branch feat-foo

> :attach my-blog
Attached to running app `my-blog` (pid 84321)
Subsequent evals run in its execution context

> Mycorp.MyBlog.Posts.list ()
[{id = 1; ...}; {id = 2; ...}] : List<Post>

> :exit
```

Each line is a few characters of typing; each gives back
something concrete; nothing requires a project setup, an
imports file, or a build step.

## What makes the REPL different from a one-shot CLI

The daemon means the REPL session is **stateful** without being
**fragile**. Specifically:

### Persistent variables across commands

```
> let xs = [1; 2; 3]
xs : List<Int64>

> let doubled = xs |> Stdlib.List.map (fun x -> x * 2)
doubled : List<Int64>

> doubled
[2; 4; 6]
```

The session has a context map of `(name → Dval)` that the
daemon owns. The map persists across cmds, across reconnects
(if the user opted in: `dark repl --keep`), and can be
introspected with `:vars`.

### Tab completion driven by the daemon's projection

```
> Stdlib.Stri<TAB>
String          (module)
> Stdlib.String.<TAB>
append           contains          endsWith        replaceAll
... (all defined fns, fetched from the daemon's projection)
```

Completion is over the *user's actual* package state at the
current branch. If the user just defined `Mycorp.foo`, that
appears in completion immediately — no restart needed.

Implementation: REPL talks to daemon via the same JSON-RPC the
LSP uses (`textDocument/completion`). Latency target <50ms.

### Inline trace display

```
> Mycorp.processUser u
[trace] Mycorp.processUser u={name="alice"} → Result.Ok ...
        ↳ Mycorp.validateName "alice" → Ok "alice"
        ↳ Mycorp.lookupUser "alice" → Ok {id=42; ...}
{id = 42; name = "alice"; ...} : User
```

Toggle off with `:traces off`. Default: on but compact (only
top-level fn calls + final return). `:traces full` for every
function call.

### History across sessions (and shareable)

```
> :history
  1  let xs = [1; 2; 3]
  2  let doubled = xs |> Stdlib.List.map (fun x -> x * 2)
  3  doubled

> :replay 2
let doubled = xs |> Stdlib.List.map (fun x -> x * 2)
doubled : List<Int64>
```

History is per-session by default. `dark repl --keep --name foo`
attaches a name to the session; it survives daemon restarts;
`dark repl --resume foo` brings it back. The session log is an
op stream like everything else (`history` stream, keyed by
session id). It can be exported as a notebook (see below).

### Multiple concurrent REPLs

```
[terminal A]
> let counter = 0
> :share counter --as remote-counter

[terminal B]
> :access remote-counter
remote-counter : Int64 = 0

> :poke remote-counter (fun n -> n + 1)
remote-counter : Int64 = 1

[terminal A]
> remote-counter
remote-counter : Int64 = 1
```

Two REPLs on the same daemon can share variables explicitly —
useful for pair-programming, demos, or piping data between
windows without intermediate files.

This is a 20-line feature on top of the daemon's session map +
op stream, but it's something neither a Python REPL nor a Node
REPL nor any other REPL has done well.

### Branch context

```
> :branch
main

> :branch list
* main
  feat-foo
  bugfix-x

> :branch feat-foo
Switched to feat-foo (3 commits behind main)

> Mycorp.foo
... uses feat-foo's def of Mycorp.foo ...
```

The REPL's branch context is independent from the user's CLI
default; switching here doesn't affect other terminals' branch
contexts. Useful: try a fn at HEAD of feat-foo without
checking it out.

### App attach (live debugging!)

```
> :apps
my-blog              running   pid 84321  uptime 4h 12m
async-tasks          stopped
my-cli-tool          running   pid 84422  uptime 3d

> :attach my-blog
Attached to my-blog. Eval will run in its execution state.
Available DBs: posts, users, sessions
Available Modules: Mycorp.MyBlog.*
```

After `:attach`, eval runs **inside the running app's process**
(or rather, the app's execution state — same packages, same
DBs, same per-app state). You can:

- Read DBs: `Stdlib.DB.queryAll posts`
- Send signals: `Mycorp.MyBlog.flushCache ()`
- Inspect state: `:state` shows app's per-process Dvals
- Override fns ad-hoc: `let Mycorp.MyBlog.shouldRetry _ = false`
  (scoped to the attached session, gets cleared on detach)

Detach with `:detach`. The app keeps running.

This is the big one — Smalltalk-style live debugging without
giving up the type system or the persistence story. Today's
"debug a running production app" requires SSH + log statements +
hope. Here it's `dark repl --on prod-1.dark.run; :attach my-app`.

### Hot-edit becomes "write code in the REPL, save later"

```
> let Mycorp.foo (x: Int64) : Int64 = x * 3
WIP: created fn Mycorp.foo

> Mycorp.foo 5
15

> :wip
1 fn (Mycorp.foo)

> :commit "add foo"
Committed as 8a3fdd2 on feat-foo

> :wip
(no WIP)
```

Defining a fn in the REPL emits the same WIP op that an editor
save would. The REPL becomes a tiny editor for one-off scripting.
Power users will live here.

### REPL is itself Dark code

Per iter 02, the REPL's read-eval-print loop is `Darklang.Repl`.
Public surface:

```dark
module Darklang.Repl

/// Register a custom REPL command (`:foo`).
let registerCommand
  (name: String)
  (handler: ReplState -> List<String> -> Stdlib.Result.Result<ReplState, String>)
  : Unit = ...

/// Override how a Dval renders in the REPL output.
let registerPrinter (typeName: TypeName) (fn: Dval -> String) : Unit = ...
```

Now any user can:

```dark
// In ~/.darklang/repl-init.dark:
Repl.registerCommand "explain" (fun state args ->
  // :explain Mycorp.foo → show docstring + types + usage examples
  ...
)
```

The next REPL launch picks up `:explain`. Hot-swappable. Same
machinery as iter 09's hot-swap LSP.

## Multi-mode

```
> :mode sql
sql> SELECT * FROM posts WHERE author = 'alice'
... runs against the user's app DBs via the SQL-compiler ...

sql> :mode dark
> ...
```

Modes for SQL, shell (`:mode sh`), JSON (paste JSON, get a
parsed Dval back), markdown notebook editing. Each mode is
just a registered Dark module (`Repl.Modes.Sql`, etc.).

Maybe overkill but cheap to ship and unlocks affordances like
"the SQL panel in DBeaver but for Dark DBs."

## Notebook export

```
> :save-notebook ~/scratch/2026-05-09-debugging-flaky-test.dark.md
Saved 14 commands as 6 cells (auto-grouped by 60s gaps)
```

The session log (cmds + outputs + traces) gets written as a
`.dark.md` file with interleaved markdown + code fences:

````markdown
# 2026-05-09 — debugging flaky test

Ran the failing test in isolation and traced the cache layer.

```dark
let cache = Stdlib.Dict.empty<String, User>
let withCache = Mycorp.cachedLookup cache "alice"
```

```dark
> withCache
{name = "alice"; ...}
```

The cache is empty on first call but populated on second...
````

Re-runnable: `dark repl --replay <file>`. This makes Dark
notebooks-on-Github plausible — share a debugging trail or a
demo as one file.

## Connection model

The REPL talks to the daemon over the unix socket — same
JSON-RPC as the LSP and the CLI. Specifically a sub-protocol:

```
client → server:  { method: "repl/eval", params: { sessionId, expr } }
server → client:  { result: { dval, traces, wipOps } }
                  { method: "repl/notify", params: ... }  // async events
```

Cancellation: client sends `repl/cancel { sessionId }`. Server
preempts the executing task. The task's partial trace is still
recorded (per iter 04).

Remote: `dark repl --on stachu-laptop` opens the unix socket on
the local daemon, which forwards the JSON-RPC to the remote
daemon over the hub WS. ~50ms latency overhead. Otherwise
identical UX.

## Pretty-printing

REPL output uses the registered pretty-printer for each Dval's
type. Per the hot-swappable-pretty-printers note, this is
controlled by the user. Defaults are sensible (Lists wrap at 80
columns, Records show field names, Dicts sorted by key).

User overrides:

```dark
PrettyPrinter.register Mycorp.User (fun u ->
  $"{u.name} <{u.email}>"
)
```

Subsequent REPL prints of `Mycorp.User` use the new format.

## Implementation outline

If iter 02 has been delivered, the F# side is:

```
backend/src/Builtins/Builtins.Matter/Libs/Repl.fs
```

This gives:

- `replSessionStart` / `replSessionEnd` / `replSessionList`
- `replEval` (running an expr in the session's context)
- `replSetVar` / `replGetVar` / `replListVars`
- `replAttachApp` / `replDetachApp`

Roughly 200 LOC of F# (most logic is dispatch into existing
ExecutionState machinery).

Dark side:

```
packages/darklang/repl/
  state.dark           # ReplState type
  loop.dark            # the read-eval-print loop
  commands/            # :branch, :attach, :wip, ...
  notebook.dark        # save / replay
  modes/               # SQL, shell, JSON
```

~1500 LOC of Dark. Doable in a week of focused work after iter
02 lands.

The terminal-side binary:

```
backend/src/DarkRepl/Program.fs
```

A tiny binary that opens the unix socket, runs a line-editor
(linenoise/rustyline equivalent in F# — or a small shim out
to a real one), forwards lines as `repl/eval` messages, prints
responses with ANSI formatting. ~500 LOC.

Total: ~2200 LOC for a class-leading REPL. Cheap relative to
how much it changes the on-ramp story.

## What this beats

- **Python REPL / IPython.** Better completion (real types).
  Better trace integration (we know what each call returned).
  Live attach to running apps. Hot-edit + commit.
- **F# fsi.** Faster (no F# compile step). Branch-aware. Editor
  integration via shared daemon.
- **Lisp REPLs (Slime, etc.).** Comparable hot-edit story but
  with the type system and persistence machinery Lisp doesn't
  have.
- **Smalltalk image.** Comparable live-image-editing feel but
  with proper SCM and multi-machine sync (the things that
  killed Smalltalk).
- **Jupyter.** Comparable notebook story (via notebook export)
  but tied to the same daemon as everything else; no "kernel
  vs file system" disconnect.

The combination — type-checked + persisted in ops + branch-aware
\+ live-attach + multi-window — is unique. Each feature exists in
some prior tool; the union is what we get from the daemon.

## Open questions

1. **Resource limits.** A REPL session can hold large Dvals in
   its var map. Cap at, say, 100MB per session; warn on insert
   that would push over. Unbounded is footgun.
2. **Security on attach.** `:attach` lets you run arbitrary
   code in another app's context. Same auth as the app-stop
   API: only the app's owner (or grant-holder) can attach.
3. **Eval-isolation across sessions.** Two sessions running
   long fns in parallel — daemon needs to fairly schedule.
   Per-session worker pool, max-concurrent-evals = 1 per
   session, queue beyond.
4. **Type errors as REPL UX.** When `let foo : Int = "abc"`
   fails, the error needs to be REPL-friendly (point at the
   offending span; suggest a fix). Same renderer as the LSP's
   diagnostics — already in our court.
5. **First-line latency.** Startup target: <100ms from launching
   `dark repl` to first prompt. Means: no cold daemon spawn
   (assume daemon is running); pre-warmed REPL session
   (cheap — empty state).
6. **Editor REPL panel parity.** The VS Code panel from iter 09
   uses the same `repl/eval` messages. Same UX. Differences:
   editor renders rich Dvals (collapsible JSON-like), terminal
   uses ANSI. Common formatter, two final-output stages.

## TL;DR

`dark repl` is a daemon-aware REPL with persistent variables,
branch context, app-attach, hot-edit, custom commands, multi-
mode, notebook export, and remote-daemon connect. ~2200 LOC of
Dark + F#. Same JSON-RPC as the LSP. Builds on iter 02 (Dark
REPL loop) and iter 04 (pretty-printers, conflict surfacing).

The deeper point: the REPL isn't a separate tool — it's just
another client of the daemon, talking the same protocol as VS
Code, the LSP, and the CLI. Same machinery, three UIs. New users
land in the REPL; power users live in it.
