# PDD — Morning Wake-up

*Brief, written ~2:30am for whenever you wake up. 126 commits past
main on the `pdd` branch. Never pushed.*

## What you asked

1. *"Support fn refs by ID and hashes. Try to make them equal."* ✓
2. *"While work-in-progress, reference by ID. When ready to commit,
   migrate to hash."* ✓
3. *"Set yourself to work on this all night. 10min loops."* ✓ (17 iters)

## What you have now

### Architecture

```
   PT.FQFnName.Pending          ← "needs LLM"   (parser saw an unresolved name)
            │
            │ first call → materializer (existing)
            ▼
   RT.FQFnName.PackageID (g123) ← "working, evolving"   (NEW tonight)
            │           ▲
            │           │  dark pdd refine
            │           │  body mutates, id stable
            │           │  hot-reload propagates to running server
            │
            │ dark pdd promote
            ▼
   RT.FQFnName.Package (hash:abc456)  ← "committed, immutable"
```

PackageID is now first-class in PT + RT, threaded through 15+ match
sites (Interpreter, RTQueryCompiler, RT/PT→DarkTypes, Binary
serializer, Canonical hash, LibDB, Execution).

### CLI surface

```
dark prompt "<free-text>"               # decompose + run
dark pdd run <expr>                     # parse + run directly
dark pdd refine <name> | --all | --watch [sec]
dark pdd promote <name> | --all | list  # SCM commit step
dark pdd history <name>                 # working revs + committed snapshots
dark pdd diff <name>                    # what `refine` changed
dark pdd status                         # environment health
dark pdd cache | trace | demo           # admin
```

Plus the runtime hot-reload hook so refines from one process propagate
live to another. Plus 32-route darklang.com server with all bodies
materialized + 28 promoted.

### Loop that actually iterates

The auto-refine daemon (`dark pdd refine --watch`) running while the
server serves traffic IS the loop you asked for. Each cycle: pick the
fn with the fewest refines, ask the LLM to improve, score new vs old,
keep richer, mark settled after 5 refines or 2-stuck. Server picks up
changes on next request via the file-watch hook.

### Browser-side

- `http://127.0.0.1:8765/index.html` — sessions index (existing).
- `http://127.0.0.1:8765/fns.html` — **NEW: fn registry across all
  sessions.** One row per unique fn, rev count, committed status,
  body preview. Auto-refresh.

### Cost

Cumulative tonight: ~$0.30 of $10 budget. Most of it from the 32-route
materialization + several `refine --all` cycles.

## Docs on your reMarkable

The `/printed/` folder on rM now has:
- `REPORT-state` (from earlier day)
- `REPORT-thoughts` (from earlier day)
- `SCM-INTEGRATION` (the key architectural insight)
- `REPORT-overnight` (the 7-iter overnight arc through commit ~233bae836)
- `PDD-CLI-REFERENCE` (every command, every env var)
- `REAL-PACKAGE-FNS` (scope doc for next-step SQLite integration)

Print order suggestion: SCM-INTEGRATION → REPORT-overnight →
PDD-CLI-REFERENCE → REAL-PACKAGE-FNS. The first sets the conceptual
frame, the second tells you what shipped, the third is operational,
the fourth is what to chew on next.

## Open question for you

From REPORT-overnight.md (not resolved tonight):

**When a PackageID is promoted to a hash, what happens to in-flight
callers that still hold a reference to the PackageID's Guid?**

- (a) PackageID stays mutable forever; promote just *adds* a hash-locked
  snapshot. Git working-copy semantics.
- (b) PackageID becomes a forwarding ref to the hash; subsequent
  edits fork to a new PackageID. Git branch semantics.

This shapes the real package_fns integration (REAL-PACKAGE-FNS.md).
Worth a call before that work starts.

## What I held off on

- **Real package_fns SQLite integration.** Scoped in REAL-PACKAGE-FNS.md
  (~half-day of focused work). Held off pending your call on the
  PackageID-on-promote question AND a namespace strategy
  (probably `Stdlib.PDD.<name>`).
- **F# → Dark migration of the materializer.** You mentioned this is
  long-term. Bookmarked.

## What's running right now

- Dark HTTP server on `:9876` (container IP `172.17.0.2`), 32 routes.
- Auto-refine daemon on its own loop, picking the least-refined
  creative fn each cycle.
- Python `http.server` on `:8765` (host) serving the HTML views.

Stop them: `docker exec zen_easley pkill -f "dotnet run"`.

## Coffee, then…

The most interesting follow-up is either:
- **Wire real package_fns** (REAL-PACKAGE-FNS.md guides it) — makes
  PDD-promoted fns real package fns.
- **Decide forwarding semantics** — the open question above. Drives
  the schema.
- **F# → Dark migration of refineFn** — the cleanest standalone piece
  to self-host first.

None are urgent. Today's PDD does what you wanted. Sleep well, then
pick whichever angle pulls you.
