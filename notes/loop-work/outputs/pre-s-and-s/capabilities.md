# Capabilities

LLM-generated and shared code will try to delete files, hit arbitrary endpoints, and
exfiltrate secrets. Capabilities gate that: "this code may do exactly these effects."

**Builtins are the only impure boundary** (pure Dark code can only compute), and `main`
already splits builtins into 9 effect assemblies (`Pure`, `Http.Client`, `Http.Server`,
`Random`, `Time`, `Cli`, `CliHost`, `Language`, `Matter`). So gating effects = gating
builtins, and the categories already exist. **Pure code is always free** — no `CapPure`
token, nothing to gate.

## Grants are per-instance settings — NOT ops

A grant ("this instance may write files / call `api.github.com`") is a **per-instance
setting**, not an op and not a projection. It does not sync, it is not in the op stream;
it is local config a user sets on a Dark install. (The *audit of what got used* can ride
the trace, which does sync — but the grant itself is a local setting.) Settings like
these are simple fields, so they live as a serialized Dark value / JSON blob in the
instance's local config, not as SQL rows.

## The Capabilities type — per-category, not a flat enum

Each category has its own shape; several are not yes/no:

```fsharp
type Capabilities =
  { httpClient : HttpClientCap option   // structured: allowed hosts, methods, secret-bearing?
    httpServer : HttpServerCap option   // structured: bind targets/ports (HttpClient-shaped)
    fileSystem : bool                   // yes/no now; future: per-dir, read/write, quota
    random     : bool                   // yes/no
    time       : bool }                 // yes/no
// Language & Matter need no grant token: their reflective surface is free, and
// anything that RUNS code (eval, materialize) PROPAGATES the caller's caps — never
// amplifies. eval cannot launder an effect the caller lacks.
// Cli/CliHost (stdin/args, subprocess spawn): boolean now, structured later — flagged.

and HttpClientCap = { hosts : HostPattern list; methods : Method list; allowSecrets : bool }
```

## Representation: model B (static category + dynamic check)

`Set<Capability>` on the builtin is wrong — a set-difference can't answer "is
`example.com` in the allowed hosts?", and it forces the interpreter to know every cap
shape. Instead a `BuiltInFn` carries **two** things:

- static **`caps : Set<CapCategory>`** — coarse, arg-independent ("could touch
  HttpClient"). Cheap to read; this is what gets unioned for effective-caps and shown to
  a prompt filter.
- dynamic **`checkCapabilities : granted -> args -> CapDecision`** — run only at the gate,
  with args in hand ("is *this host* allowed?").

The static set is a sound over-approximation (omitting `HttpClient` ⇒ provably never
needs it). A pure builtin has `caps = {}` and no `checkCapabilities`.

## The gate

The interpreter doesn't invent errors — at a builtin call it runs the builtin's
`checkCapabilities granted args`; on deny it produces a standard runtime conflict and
the resolution policy decides: **fail loudly** (strict default = today's behavior),
**substitute** a default, or **park** waiting for a grant. **No interactive grants for
now** (they make tests block on a human); the seam is left for an `AskHuman` resolution
that times out to a non-human fallback, parking on the event bus
([event-bus.md](event-bus.md)). Until a builtin declares `caps`, nothing is denied and
the call fast-paths.

## Effective caps for user / materialized fns (a projection)

User-defined and materialized fns don't declare caps; their **effective** set is a
projection over their call graph — union the static `caps` of every builtin reached:

```fsharp
effectiveCaps fn =                       // : Set<CapCategory>
  walkCalls fn.body |> unionOver (function
    | Builtin n  -> (lookupBuiltin n).caps
    | Package h  -> effectiveCaps (lookup h)   // immutable, memoize by hash
    | _          -> empty)
```

A user may *tighten* below the computed set (defense in depth) but never loosen it.

## CLI UX — set allowances, see failures

Grants are set and inspected from the CLI (the typed `Capabilities` value is the model;
these commands are its surface):

```text
$ print-md report.md
✗ blocked: HttpClient → fonts.googleapis.com (GET)
    print-md needs: FileSystem(read), Cli(spawn:weasyprint), HttpClient(GET fonts.*)
    grant with:  dark caps grant print-md --http-get 'fonts.googleapis.com' --fs read --spawn weasyprint

$ dark caps grant print-md --http-get 'fonts.googleapis.com' --fs read --spawn weasyprint
✓ granted (instance setting, local; not synced)

$ dark caps print-md
print-md  fs:read  spawn:weasyprint  http:GET fonts.googleapis.com

$ print-md report.md
✓ printed report.pdf
```

A denial is actionable: it names exactly what was needed and the one command to grant
it. `dark caps <app>` shows the current grant; `dark caps revoke …` removes it.

## Discovered

- The denial→resolution policy is the same "conflict dispatch" sync and runtime errors
  use. That shared dispatch may belong **below** S&S (a pre-S&S substrate primitive) so
  pre-S&S docs can reference it without an up-link — revisit bucket placement.
