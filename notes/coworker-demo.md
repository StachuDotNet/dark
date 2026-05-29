# What's new in the CLI (since `main`)

A walkthrough of the user-facing wins on
`remove-bwdserver-in-favor-of-dark-impl`. Everything below is something you
can actually type at a `darklang` prompt today.

Two themes:

1. **HTTP serving is now first-class in the CLI.** Any package fn that
   shapes like a router can be served with one command — no F# rebuilds,
   no canvas concept, no hardcoded list.
2. **Tracing got a real CLI.** Recordings from `eval`, `run`, *and* HTTP
   handlers now share one storage shape. ~25 new `traces` subcommands
   for browsing, filtering, replaying, exporting, and (the favorite)
   inlining recorded values back into your source.

## `darklang serve` — HTTP for any router fn

```
$ darklang serve Darklang.DemoData.HttpServerTest.router --port 8080
Serving Darklang.DemoData.HttpServerTest.router on http://localhost:8080
^C
Shutting down... done.
```

That's it. The fn-path argument is resolved at runtime via the same
build-and-eval that powers `eval` and `run`, so:

- Any router fn works. Not in some hardcoded list — just any fn whose
  signature shapes like a router.
- No F# rebuild between routers. Edit Dark, restart `darklang serve`,
  done.
- `Ctrl+C` exits cleanly. In-flight requests finish.
- Body-size cap, `Server: darklang`, HSTS, X-Forwarded-Proto URL
  canonicalization all wired in.
- Multi-handler conflicts are detected and reported (no more
  whoever-registered-last-wins).
- `Stdlib.Http.faviconResponse` exists for the no-handler favicon
  path.

Per-request logging lands on stdout in a readable shape:

```
[HttpServer] GET /hello → 200 (3 ms)
[HttpServer] POST /echo → 200 (12 ms)
```

## `traces` — the CLI-first trace surface

Every `eval`, `run`, and HTTP request now writes a trace. Same storage
shape regardless of source. Commands are designed to feel like `git`:
short verbs, content-first.

### Browse

```
$ darklang traces list
b91 GET /user/42                12ms   2026-05-04 09:22
8cf POST /echo                   3ms   2026-05-04 09:21
9d5 @Stdlib.Bool.and true false  0ms   2026-05-04 09:20
```

```
$ darklang traces list --route /user --limit 5
$ darklang traces list --fn helloHandler
$ darklang traces list --json | jq
$ darklang traces tail              # most recent one in detail
$ darklang traces tail 3            # 3rd-most-recent
$ darklang traces tail --route /user
$ darklang traces follow            # live tail; new traces stream as they land
$ darklang traces follow --route /user --json
```

Every list-shaped command supports `--json` for piping into other
tools. Tab-completion knows about flags and pattern positions
(after `--fn`/`--route` it expects a value, not another flag).

### View

```
$ darklang traces view b91
GET /user/42 → 200  (12 ms total, 4 fn calls)
├── @Darklang.MyApp.userHandler  (12 ms)
│   ├── Stdlib.DB.get …          (3 ms)
│   └── Http.responseJson …      (1 ms)
…
```

```
$ darklang traces view b91 --depth 2
$ darklang traces view b91 --filter Stdlib.DB
$ darklang traces view b91 --exclude Stdlib.Http
$ darklang traces view b91 --slow 5      # only fns that took ≥5 ms
```

Trace IDs accept any **unambiguous prefix**, so `b91` is fine as long
as nothing else starts with `b91`. Filters are case-insensitive.

### Find

```
$ darklang traces find "alice"            # search inputs + results
$ darklang traces find "alice" --view     # find + immediately view first match
$ darklang traces find "alice" --json
```

SQL `LIKE` wildcards (`%`, `_`, `\`) in your pattern are escaped
properly — `traces find %` returns nothing instead of every trace.

### Replay

```
$ darklang traces replay b91
Replaying GET /user/42 against current code...
Result: { id="42"; name="alice"; … }

$ darklang traces replay b91 --diff
- "name": "alice"
+ "name": "Alice"
- "active": false
+ "active": true
```

`replay --diff` re-runs the handler with the recorded input and shows
where the result diverges from what was originally captured. Works
for HTTP traces and plain `eval` / `run` traces.

### Export / import

```
$ darklang traces export b91 > bug-report.json
$ darklang traces import bug-report.json
Imported trace b91 (GET /user/42).
```

Round-trips include `trace_expr_values` (the per-AST-node values),
so the imported trace is fully usable for `view --with-trace` (next
section).

### Stats / hotspots

```
$ darklang traces stats
Recent 100 traces:
  GET /user/:id      42 traces   avg 8ms   max 31ms
  POST /echo         18 traces   avg 4ms   max  9ms
  @Stdlib.Bool.and    9 traces   avg 0ms   max  1ms

$ darklang traces hotspots
Across last 100 traces (top 50 fns by total time):
  Stdlib.DB.get          152ms   38 calls
  Http.responseJson       41ms   42 calls
  Stdlib.String.split     12ms   91 calls
```

### Inspect / values / gen-test

`inspect` adds a request-context banner + result footer to a single
trace — handy for "what was the input shape again?":

```
$ darklang traces inspect b91
GET /user/42
Request: { url=…; method="GET"; headers=…; … }
…
Result: { status=200; body=…; headers=… }
```

`values` dumps every recorded `(exprId → value)` for a trace. Mostly
useful for debugging the trace pipeline itself.

`gen-test` materializes a trace as a paste-ready test fn:

```
$ darklang traces gen-test b91
let testGetUser42 () : Test.Result =
  let req = Http.Request.{ method = "GET"; url = "/user/42"; … }
  let actual = Darklang.MyApp.userHandler req
  let expected = … // captured result
  Test.equal actual expected
```

### Maintenance

```
$ darklang traces delete b91
$ darklang traces prune --keep 50      # keep most recent 50
$ darklang traces clear                # everything
$ darklang traces clear --before 7d    # everything older than 7 days
```

### Self-hosted dashboard

`darklang serve Darklang.DemoData.TraceDashboard.app --port 9000` brings
up a tiny HTML dashboard built in Dark on top of the same trace-reader
builtins. It eats its own dog food — the dashboard's own requests show
up in the trace list.

## `view --with-trace` — values inline with your code

This is my favorite. `view <fn-path>` already pretty-prints the source.
Add `--with-trace [<id>]` and every annotated AST node gets `// = <val>`
inlined from the trace.

```
$ darklang view Darklang.MyApp.getUser --with-trace
fun req ->
  let userId = req.url.params.id           // = "42"
  let user = Stdlib.DB.get userId          // = Some User { name="alice"; active=true }
  match user with
  | Some u -> Http.responseJson u          // = { status=200; body=…; … }
  | None -> Http.notFound ()
```

Currently annotates: `let` bindings, `match` arm results, `if`
branches, pipe stages. Long values truncate to 80 chars; if you want
the full shape, `traces view <id>` still has it.

If `--with-trace` produces zero annotations (usually because the source
changed since the trace was recorded), you get a friendly hint instead
of silent empty output.

```
$ darklang view Darklang.MyApp.getUser --with-trace b91
$ darklang view Darklang.MyApp.getUser --with-trace b91 --depth 2
```

This is the dark-classic killer feature in text form. Pair it with
`traces follow` and you can literally watch a request land, then ask
the CLI to show you every intermediate value the handler computed —
all without leaving your terminal.

## Quality-of-life sweep

A bunch of small things that make the CLI feel less surprising:

- **Tab-completion** — flag names complete after a subcommand;
  flag *values* complete to nothing (so tab gets out of the way
  instead of suggesting `--json` as your fn name); positional args
  after a flag-with-value complete properly.
- **Focused error messages** — instead of generic "Unknown flag",
  malformed forms now say the actual problem ("`--depth` requires a
  value (e.g. `--depth 3`)", "tail expects a positive integer or
  flags", etc).
- **Empty/whitespace rejection** — `traces find ""` no longer matches
  every trace in the database.
- **Case-insensitive `--route` / `--filter` / `--exclude`** — match
  the way humans actually type.
- **Trace-ID prefix support** — `b91` is enough as long as it's
  unambiguous; ambiguous prefixes get a focused list of matches.
- **Flag-vs-trace-id distinguishing** — `traces view --json` no longer
  treats `--json` as a trace id and surfaces a misleading "ambiguous"
  error. It tells you `--json` isn't a trace id.
- **"Unknown subcommand"** lands first instead of dumping the full
  help block.
- **Singular/plural** — "1 trace" vs "2 traces", not "1 traces" or
  "1 trace(s)".
- **Empty-store hint** — `traces list` with no recordings yet tells
  you how to record one (`darklang eval`, `run`, or HTTP handler
  under `serve`).
- **`traces help`** lists examples for the less-obvious commands
  (`inspect`, `gen-test`, `values`).

## Try it (5 minutes)

```bash
# In one terminal — start an HTTP server.
darklang serve Darklang.DemoData.HttpServerTest.router --port 8080

# In another — make some requests.
curl localhost:8080/hello
curl -X POST localhost:8080/echo -d 'hi'
curl localhost:8080/user/42

# Browse the traces.
darklang traces list
darklang traces tail
darklang traces stats
darklang traces hotspots

# Pick a trace and look at it three ways.
darklang traces view <id>
darklang traces inspect <id>
darklang view Darklang.DemoData.HttpServerTest.helloHandler --with-trace <id>

# Replay it.
darklang traces replay <id> --diff

# Live tail while you make more requests.
darklang traces follow
```

Have fun. If anything looks wrong or surprising, tell me — the polish
pass found a lot of small things, and I'd bet there's more.
