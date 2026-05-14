# PDD — Overnight Report

*Written ~midnight, 2026-05-13/14. Branch `pdd`, ~120 commits past `main`, never pushed.*

## What you asked for

> "Set yourself to work on this all night. 10min loops. Support fn refs
> by ID and hashes. Try to make them equal — like FQFnName.PackageHash
> or FQFnName.PackageID, or something. Do whatever you need to make me
> happy by like 7am."

And then you refined it:

> "While work-in-progress, reference things by ID. When ready to commit,
> migrate to hash so they're stable. Does that make sense within our
> SCM stuff?"

It does. **More than make sense — it's the right framing.** I had set
out to add `PackageID` as a sibling to `Package(hash)`. Your follow-up
reframed it: not siblings, but **a working-copy ↔ committed transition**,
exactly like git's index ↔ HEAD but at function granularity.

This report captures what shipped overnight against that vision.

## Architecture: PackageID = working, Package(hash) = committed

```
   PT.FQFnName.Pending                ← "needs LLM"
            │
            │ first call → materializer
            ▼
   RT.FQFnName.PackageID (id=g123)    ← "working, evolving"
            │           ▲
            │           │  dark pdd refine
            │           │  body mutates; id stable
            │           │  callers see latest version
            │
            │ dark pdd promote
            ▼
   RT.FQFnName.Package (hash=abc456)  ← "committed, immutable"
            │
            │ future edits start a new PackageID off the latest hash
            ▼
   PackageID (g124) again …
```

The leftmost arrow (Pending → PackageID via materialize) was there
before tonight. The promote arrow is new tonight. PackageID itself is
new tonight. The full lifecycle is now first-class in the type system.

## What shipped tonight (7 iters)

| # | What | Commit |
|---|---|---|
| 1 | `FQFnName.PackageID` variant: PT + RT, threaded match-exhaustiveness through ~15 sites (Interpreter, RTQueryCompiler, RT/PT→DarkTypes, Binary serializer, Canonical hash, LibDB, Execution) | `0d8027ac9` |
| 2 | Materializer publishes to `pddIDFnCache` via stable name→Guid registry. Pending's executionPoint match reads from it first. | `e8d29098e` |
| 3 | Hot-reload: `pddRefreshHook` in RuntimeTypes; PDDMaterializer installs file-watch on `promoted.jsonl`. Interpreter calls hook on Pending lookups → live updates without restart. | `e8d1256db` |
| 4 | `dark pdd refine --watch [sec]` background daemon: round-robin picks fewest-refined creative fn, refines, sleeps. Settles after 5 refines or 2-stuck. | `b533a8b81` |
| 5 | Bug fix: Apply-on-Pending was bypassing hot-reload. Now reads pddIDFnCache (or refresh-hook) before calling materialize. | `fd0bb5b70` |
|   | **SCM-INTEGRATION.md** captured your "ID while WIP, hash at commit" framing as a doc. | (same) |
| 6 | `dark pdd promote <name> | --all | list`: snapshot current body, compute SHA-256 hash, append to `promoted_hashes.jsonl`. The SCM commit step. | `33868749e` |
| 7 | 32-route darklang.com router auto-discovered from `src/pages/`. `pdd-thinking/scripts/build-serve-expr.py` reproduces. Live at `:9876`. | `4d1dbb72e` |

## The loop, end-to-end (verified live)

```bash
# 1. Source key (host)
set -a; source ~/.config/darklang/llm-keys.env; set +a

# 2. Build the router from darklang.com source
python3 pdd-thinking/scripts/build-serve-expr.py
# → /tmp/serve-expr.txt, 32 routes, 24KB

# 3. Start server (container)
docker cp /tmp/serve-expr.txt zen_easley:/tmp/serve-expr.txt
docker exec -d -e OPENAI_API_KEY="$OPENAI_API_KEY" \
  -e PDD_BUDGET_MS=3600000 -e PDD_MODEL=gpt-4o -e PDD_PARALLEL=3 \
  zen_easley bash -c '
    cd /home/dark/app/backend
    EXPR=$(cat /tmp/serve-expr.txt)
    dotnet run --project src/Cli --no-build -- pdd run "$EXPR"
'

# 4. Start auto-refine in background
docker exec -d -e OPENAI_API_KEY="$OPENAI_API_KEY" -e PDD_MODEL=gpt-4o \
  zen_easley bash -c '
    cd /home/dark/app/backend
    dotnet run --project src/Cli --no-build -- pdd refine --watch 45
'

# 5. Hit pages. Each one materializes on first request (if not pre-fetched),
#    refines in the background, picks up improvements via hot-reload.
curl http://172.17.0.2:9876/no
curl http://172.17.0.2:9876/for/ai-developers
# (etc — 32 routes)

# 6. Once happy with a page, snapshot it as committed:
dark pdd promote renderNo
# → ✓ promoted renderNo → 48fcf9ce8717f1f6 (805 chars)
dark pdd promote list
```

## Cost so far tonight

About $0.15 of $10 budget. Most of it from `refine --all` cycles (each
fn ~$0.002 with gpt-4o + 2000 max_tokens). The 32-route initial
materialization burst added another $0.03. Per-request serving is sub-
$0.0001 (cache hit + interpret).

## What's NOT there yet

1. **Real `package_fns` table integration.** `dark pdd promote` writes
   to a sidecar (`promoted_hashes.jsonl`), not the actual SQLite
   `package_fns` table. So promoted snapshots don't show up in
   `dark search` / `dark tree` yet. The hash is real (SHA-256 over the
   body), but the artifact lives outside the canonical store.

2. **`PackageID` references in saved Dark source.** PT-level
   `PackageID` exists, but no Dark file actually USES the syntax yet
   (Pending → PackageID happens at materialize time, not parse time).
   Source-level `let f = ... in` of a PackageID would need parser /
   serializer surface area.

3. **Per-name version history.** Each refine appends to promoted.jsonl
   so the file IS a history, but there's no `dark pdd history <name>`
   to inspect it. Easy follow-up.

4. **F# → Dark migration of materializer.** Bookmarked.

## Open architectural question (worth chewing on)

**When a PackageID is promoted to a hash, what happens to in-flight
callers that still hold a reference to the PackageID's Guid?**

Two clean answers:
- **Stay alive forever.** PackageID slot persists; promote just *adds*
  a hash-locked copy. Old refs keep working with the body as it was
  at promote time. New refs can target the hash directly.
- **Forward to hash.** PackageID becomes a redirect: any lookup
  resolves to the hash from then on. Editing requires forking
  (new PackageID), like branching.

git's analog is "after `git commit`, the working copy isn't *frozen*;
it remains the live editable state, but HEAD now points at the commit."
So option 1 (PackageID stays mutable until explicitly stopped) feels
truer to that. Worth deciding before wiring real package_fns
integration, since the answer shapes the schema.

## What I'd say it proves

A LLM-materialized function can:
1. **Start as a sketch** (`Pending` from unresolved parser names).
2. **Materialize on demand** (per-request route handlers, parallel).
3. **Iterate over time** (`refine --watch` running while the server
   serves traffic; pages get richer without restart).
4. **Be committed when ready** (`promote` → content hash → immutable
   snapshot). Editing resumes via a new working copy.

The hash regime stays exactly what it is today. PackageID just gives
us the *step before* the hash — a place to live while you're still
figuring it out.

## Going forward

Bookmarked for "after sleep":
- Real package_fns integration on promote.
- A `dark pdd history <name>` viewer + `diff` between versions.
- Decision on the promote-forward question above.
- `PackageID` in Dark source syntax (something like `Stdlib.User.~renderHome`?).
- Browser-renderable docs of refine state (the HTML view's index could
  show provisional/committed splits).

The big-picture bet — that "ID while WIP, hash at commit" is the right
architecture — is borne out by how naturally it threaded through the
existing types. Adding PackageID didn't fight the model; it slotted in
beside Package and Pending like it had always been waiting for a place.

Sleep well. 🌙
