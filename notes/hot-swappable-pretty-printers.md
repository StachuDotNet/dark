# Hot-swappable pretty-printers — sketch

Callout from the trace-rewrite work (`notes/wrap-up/trace-replay/`). The
trace renderer was getting "what should this look like in the terminal?"
hard-coded throughout — sometimes JSON, sometimes a structured Dark
value, sometimes a one-line summary, sometimes a per-tree-node pretty
form. That fan-out is symptomatic; what we want instead is **one clear
seam where output formatting plugs in**, and the seam is the same for
every value in the system, not just traces.

## The thesis

Every `Dval` has a default printer. Every printer is just a Dark
function `Dval -> String`. Users can swap one in either:

- per-call:    `view --printer=Some.Custom.printer trace 7e2b…`
- per-session: `printer set Some.Custom.printer`
- per-type:    `printer for-type Stdlib.HttpClient.Response = Custom.httpResponsePrinter`

The runtime resolves the printer the same way it resolves any package
fn — by hash, by name. When you change the printer mid-session, *the
next render uses the new one*. No reload, no rebuild, no daemon
restart.

## Why this matters more than it looks

Three things converge:

1. **Trace UX.** Trace rows carry raw dvals (input, fn-call args,
   fn-call result). Today the renderer prints "the dval as
   pretty-JSON" by default, with a few baked-in special cases. Move
   to user-controlled printers and the renderer doesn't decide
   anything; it dispatches to a Dark fn.

2. **Agent UX.** The trace-replay write-up's central thesis is that
   agent UX is the optimization target. Agents need *dense plaintext*
   formatting, not JSON. Hot-swappable printers means the same trace
   stream can be rendered for an agent (`--printer=Agent.dense`) or
   for a human (`--printer=Pretty.color`) without the runtime caring.

3. **Generic display.** Every `Stdlib.print`, every REPL value echo,
   every `Builtin.debug`, every error-rendering path — they all want
   the same thing: a `Dval -> String` they can swap. We're building
   the same plumbing four times today.

## Mechanism (rough)

A `PrinterRegistry` keyed by either:

- `("default", Dval-shape-class)` — fallback printer per shape (record,
  list, custom-type, blob, …)
- `("for-type", FQTypeName)` — a printer chosen for a specific type
- `("override", PrinterName)` — global override that wins over both

```fsharp
type PrinterKey =
  | DefaultForShape of DvalShape
  | ForType of FQTypeName
  | NamedOverride of string

type PrinterRegistry = Map<PrinterKey, FQFnName.Package>
```

The registry lives on `ExecutionState` (or alongside it). A printer
lookup is: try `NamedOverride` first, then `ForType`, then
`DefaultForShape`. Builtins call:

```fsharp
let renderDval (state: ExeState) (dv: Dval) : Ply<string> =
  let fn = PrinterRegistry.resolve state.printers dv
  Execute.applyPackageFn fn [dv]
  |> Ply.map dvalToString
```

Builtin-side, `Builtin.debug` and friends become one-liners over
`renderDval`.

Dark-side, the standard library ships:

- `Stdlib.PrettyPrint.default      : Dval -> String`   — current behavior
- `Stdlib.PrettyPrint.compact      : Dval -> String`   — single-line
- `Stdlib.PrettyPrint.dense        : Dval -> String`   — agent-targeted
- `Stdlib.PrettyPrint.json         : Dval -> String`   — escape hatch

Users define their own as ordinary Dark fns and register them via
CLI commands.

## Why it's fast to ship

- Printers are *just package fns*. No new ABI, no new type, no AST
  changes. Resolve-by-hash + `Execute.applyPackageFn` already exists.
- The registry is a `Map` on `ExecutionState`. Swap-on-write.
- No persistence on the hot path — reload happens on next trace view,
  which is already on the human-latency budget.
- Default printer = current behavior, so the migration is
  fully-backward-compatible. We add the seam, default it to today's
  behavior, then replace one consumer at a time.

## Open questions

- **Printer cost ceiling.** A pathological printer can run forever.
  Wrap dispatch in a timeout (5 ms? 50 ms?) with a fallback to
  `Stdlib.PrettyPrint.default` and a warning row. Same shape as
  builtin watchdog timing.
- **Printer purity.** Should printers be `Pure` only? Probably yes —
  printing should never call HTTP or hit the DB. Enforce by accepting
  only `previewable = Pure` package fns at register time.
- **Stack depth.** A printer that prints a record whose field is a
  record whose field… needs guarded recursion. Default printer
  already handles this; user printers can call into it via
  `Stdlib.PrettyPrint.default` for sub-values.
- **Where's the registry persisted?** CLI session state, like the
  active branch. Could escalate to `~/.darklang/printer-prefs` for
  per-machine defaults; not v1.

## Where this connects

- **Trace rewrite (task #18 / `notes/wrap-up/trace-replay/`).** The new
  trace renderer should consume printers, not bake formats. Same seam
  on `view`, `inspect`, `tail`, `export`.
- **Agent UX.** `Agent.dense` printer ships with the same release as
  the `--format=plain` default for traces. Agents get sane defaults
  without flagging.
- **`Builtin.debug` / REPL.** Once the seam exists, point these at
  it. Removes a duplicate code path.
- **Errors.** Long term, error rendering wants the same seam — a
  `RuntimeError -> String` printer that's user-overridable. Out of
  scope for v1 but worth keeping the shape compatible.

## TL;DR

One typed `Dval -> String` seam, owned by Dark code, looked up by
package-fn-hash, swappable at runtime. Trace rendering and dval echo
both consume it. Agent-friendly defaults ship in stdlib. No new
runtime concept — printers are just package fns, dispatched via the
existing executor.
