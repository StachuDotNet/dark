# PDD — CLI Reference

*Concise reference. Every command on the branch.*

## Running

All commands run via the project's CLI:

```
dark <subcommand> ...
```

In practice inside the dev container:

```
cd /home/dark/app/backend
dotnet run --project src/Cli --no-build -- <subcommand> ...
```

OpenAI key sourced once on the host before invoking:

```
set -a; source ~/.config/darklang/llm-keys.env; set +a
```

Pass into the container via `docker exec -e OPENAI_API_KEY=...`.

## Commands

### `dark prompt "<free-text>"`

High-level entrypoint. Decomposes the free-text request into a Dark
expression (one LLM call), then parses with `OnMissing.AllowPending`,
materializes pendings, and executes.

```
$ dark prompt "compute fibonacci of 8 plus factorial of 5"
[pdd] decomposing via gpt-4o-mini...
[pdd] decomposed → Stdlib.Int64.add (fibonacci 8L) (factorial 5L)
[pdd] kick-off 2 pendings in parallel
[pdd] ✓ real fibonacci 5329ms
[pdd] ✓ real factorial 5128ms
[pdd] result: DInt64 141L
```

### `dark pdd run "<dark-expression>"`

Skips the decompose step. Parses the user-typed Dark with
`AllowPending`, materializes pendings, and executes. Used for HTTP
servers, multi-fn pipelines, anything where you want to write the Dark
yourself.

```
$ dark pdd run "factorial 6L"
DInt64 720L
```

For long-lived programs (HTTP server), set a large `PDD_BUDGET_MS`:

```
$ PDD_BUDGET_MS=3600000 dark pdd run "Builtin.httpServerServe 9876L (fun req -> ...) ..."
```

### `dark pdd demo <fnName> <Int64-arg>`

Hand-built `Apply` of a Pending fn with a single Int64 argument. Used
as the smallest possible test surface for the wire.

### `dark pdd cache (list | clear | paths)`

Admin for the PDD cache files (`rundir/pdd-cache/promoted.jsonl` and
`decomposed.jsonl`).

- `list`: dump every entry.
- `clear`: delete both cache files (next run materializes fresh).
- `paths`: print absolute paths.

### `dark pdd trace (list | last)`

Lookup HTML view sessions (`rundir/pdd-view/<id>.html`).

- `list`: all sessions, newest first.
- `last`: print path to the newest session HTML (chain with `open
  $(dark pdd trace last)`).

### `dark pdd refine <fnName> | --all | --watch [intervalSec]`

The "iterate the fn body" loop.

- `<fnName>`: one-shot. Calls the LLM with "improve this body" + the
  cached version; keeps the new body if it scores richer (more
  semantic tags + length).
- `--all`: refines every creative-named fn (by `render*`/`generate*`/
  ... prefix) once.
- `--watch [intervalSec]` (default `30`): background loop. Each tick:
  pick the fn with FEWEST refines so far, refine, sleep, repeat.
  Settles a fn after 5 successful refines or 2 stuck-in-a-row.

Pair with a running server: hot-reload (see iter 3/5) means refines
land in the live server on next request without restart.

### `dark pdd promote <fnName> | --all | list`

The SCM commit step. Snapshots a PackageID-stage fn's current body,
computes a SHA-256 content hash (truncated to 16 chars), appends to
`rundir/pdd-cache/promoted_hashes.jsonl`.

- `<fnName>`: promote one.
- `--all`: promote every creative-named fn.
- `list`: show every committed snapshot (newest first), with hash,
  name, timestamp, body length.

```
$ dark pdd promote renderHome
  ✓ promoted renderHome → 4290f2f3cde3279d (584 chars)

$ dark pdd promote list
  4290f2f3cde3279d renderHome 04:30:24 (584 chars)
```

### `dark pdd history <fnName>`

Show every body this fn has had. Reads both:
- `promoted.jsonl` (working revs, append-only)
- `promoted_hashes.jsonl` (committed snapshots)

Working revs marked with `~`; committed snapshots with `✓ + hash`.

```
$ dark pdd history renderHome
[pdd] history for renderHome
[pdd]   → 3 working revs, 1 committed snapshots

  ~ rev 1  (584 chars)
    "<html>" ++ ...
  ~ rev 2  (1076 chars)
    "<html><head><style>..." ++ ...
  ~ rev 3  (1481 chars) (current)
    "<html><head><nav><ul><li>..." ++ ...

  ✓ 4290f2f3cde3279d 2026-05-14T04:30Z  (584 chars)
    "<html>" ++ ...
```

### `dark pdd diff <fnName>`

Show what `refine` changed between the latest two working revs.
Tokenizes on `++` (Dark's string-concat operator) and surfaces added
(green `+`) / removed (red `-`) tokens.

Not a true LCS-based unified diff — but useful for seeing structural
changes (e.g. "rev 2 → rev 3 added `<nav>` with anchored sections").

```
$ dark pdd diff renderForAI
[pdd] diff renderForAI (rev 2 → rev 3)
[pdd]   1056 → 1524 chars (+468)

  - "    <section>\n"
  + "  <meta charset=\"UTF-8\">\n"
  + "    <nav>\n"
  + "        <li><a href=\"#overview\">Overview</a></li>\n"
  + ...
```

### `dark pdd status`

One-glance health snapshot:

- Unique fns × total revs in working cache
- How many have been refined more than once
- Committed snapshots
- Decompose-cache size
- Session HTML count + latest path
- Top 5 most-refined fns

```
$ dark pdd status
[pdd] PDD status
[pdd] fns in working cache (promoted.jsonl):
[pdd]   44 unique fns, 74 total revs
[pdd]   15 fns have > 1 rev (have been refined)
[pdd] committed snapshots: 29
[pdd] sessions: 119 (latest: rundir/pdd-view/index.html)
[pdd] most-refined fns:
[pdd]    4 mul
[pdd]    4 renderForPython
[pdd]    3 fact_helper
```

## Env vars

| Var | Default | Meaning |
|---|---|---|
| `OPENAI_API_KEY` | — | required for any materialization or refine |
| `PDD_MODEL` | `gpt-4o-mini` | LLM for body + test gen. Set to `gpt-4o` for picky syntax (lists, lambdas) |
| `PDD_BUDGET_MS` | `300000` (5 min) | wall-clock ceiling per `pdd run` / `prompt`. Set to `3600000` for HTTP servers |
| `PDD_PARALLEL` | `3` | max in-flight concurrent materializations (avoids OpenAI 429s) |
| `PDD_SKIP_QA` | unset | if set, skip QA-test-gate for ALL fns (default: only skip for creative fns) |

## Files

| Path | Role |
|---|---|
| `rundir/pdd-cache/promoted.jsonl` | working-copy stream. Every materialization + refine appends. Last entry per name wins on cache reload. |
| `rundir/pdd-cache/promoted_hashes.jsonl` | committed snapshots from `dark pdd promote`. Immutable history. |
| `rundir/pdd-cache/decomposed.jsonl` | free-text → Dark-expr cache for `dark prompt`. |
| `rundir/pdd-view/<id>.html` | per-session HTML view. Two-pane: fn cards + event log. Self-refreshes via meta-refresh. |
| `rundir/pdd-view/index.html` | sessions index page (browse all). |
| `rundir/logs/pdd-materialize.jsonl` | every LLM call recorded (name, sig, body, error). |

## A full demo loop

```bash
# 1. start server with 32 darklang.com routes
python3 pdd-thinking/scripts/build-serve-expr.py
docker cp /tmp/serve-expr.txt zen_easley:/tmp/serve-expr.txt
docker exec -d -e OPENAI_API_KEY="$KEY" -e PDD_BUDGET_MS=3600000 \
  -e PDD_MODEL=gpt-4o -e PDD_PARALLEL=2 zen_easley bash -c '
    cd /home/dark/app/backend
    dotnet run --project src/Cli --no-build -- pdd run "$(cat /tmp/serve-expr.txt)"
'

# 2. background refine watcher
docker exec -d -e OPENAI_API_KEY="$KEY" -e PDD_MODEL=gpt-4o zen_easley bash -c '
    cd /home/dark/app/backend
    dotnet run --project src/Cli --no-build -- pdd refine --watch 60
'

# 3. hit pages — each materializes on first request, refines in background,
#    picks up improvements via hot-reload
curl http://172.17.0.2:9876/
curl http://172.17.0.2:9876/for/ai-developers
curl http://172.17.0.2:9876/no

# 4. observe progress
dark pdd status
dark pdd history renderHome
dark pdd diff renderForAI

# 5. once happy, commit
dark pdd promote --all
dark pdd promote list
```
