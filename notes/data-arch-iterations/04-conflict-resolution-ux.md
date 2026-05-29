# Iter 04 — conflict resolution UX, in concrete terms

The unified-model § 8 says "conflicts are projection-level data;
resolutions are ops." Right answer at the storage layer; doesn't
say what the user sees. This iteration draws screens, commands,
and the Dark fn surface for the resolution flow.

## The conflict shapes that actually exist

Going through every stream:

**Packages — `SetName(loc) → hash`.** Two devices bind the same
location to different hashes. Common ancestor exists if both
descended from the same prior `SetName`. ~80% of real conflicts.

**Packages — propagation conflict.** A `PropagateUpdate(old →
newA)` and `PropagateUpdate(old → newB)` both target the same
old hash. Treated as a SetName conflict at every consumer
location.

**Branches — `RenameBranch`.** Two devices rename the same branch
to different names. Trivial LWW.

**Branches — archive vs commit.** One device archives, another
commits. Genuine semantic conflict; user picks "the archive
wins (drop the new commits)" or "the commit wins (un-archive)."

**Traces — none.** Trace ops are append-only and identified by
trace_id; no two ops compete on the same key. Skip.

**Sessions — field-level updates.** Two devices update
`intent` (or `cwd`, etc.) at the same wall-clock window. Field-
level LWW is right; whole-record LWW would lose the other field.

**Sessions — end vs continue.** Device A ends a session; device
B attaches a new trace to it. Mostly: ignore B's attach (the
session is over). Surface as info, not a hard conflict.

**App data — `DB.set("k", ...)` race.** The app's most common
conflict shape. Resolution depends on the app's declared
`mergePolicy[<table>]`. Default LWW.

**App data — schema migration race.** Two devices both run a
migration that mutates a row's structure. Detect at apply-time:
the second migration sees a row already in the new shape and
no-ops, or surfaces a conflict if shapes diverged.

**Grants — concurrent grant + revoke.** Granter grants; from a
different device, revokes. Sequence determined by `created_at`;
LWW. (We're not building Byzantine consensus.)

That's the universe. ~7-10 conflict types, mostly resolved by
LWW with a few that need user judgment.

## The top-level surface — `dark conflicts`

Lists open conflicts across every stream the user owns:

```
$ dark conflicts
Stream    Key                                      Kind          Detected
packages  Darklang.Stdlib.List.map                 SetName       2026-05-09 03:14
packages  Darklang.Stdlib.HttpClient.get           SetName       2026-05-09 03:42
sessions  s_01H9C… ("debug Cli completion")        UpdateField   2026-05-09 03:50
app:blog  posts/draft-001                          KvSet         2026-05-09 04:02
branches  feat-auth                                RenameBranch  2026-05-09 04:10

5 conflicts. Run `dark resolve <stream> <key>` or `dark resolve --auto` to
apply default policies.
```

- No grouping by default (flat is faster to scan for ≤20).
- `--grouped` for streams with many conflicts.
- `--mine` filters to streams owned by self (vs streams shared in).
- `--since 1h` filters by detection time.

The list is just a SELECT against each projection's `conflicts`
table, unioned. Fast (~few ms even with hundreds).

## The drill-in — `dark resolve <stream> <key>`

This is the screen that decides whether the UX is good or bad.

### SetName conflict (packages)

```
$ dark resolve packages Darklang.Stdlib.List.map

Conflict on Darklang.Stdlib.List.map (packages stream)
──────────────────────────────────────────────────────

Two SetName ops disagree on the hash to bind:

  [a] op 7d3f…  by stachu-laptop (you)   2026-05-09 02:48
      → hash 4a7b1c…
      // version with explicit accumulator
      let map (xs: List<'a>) (f: 'a -> 'b) : List<'b> =
        let mutable acc = []
        for x in xs do acc <- (f x) :: acc
        List.reverse acc

  [b] op 9c12…  by stachu-major (you)    2026-05-09 03:01
      → hash 8e2f4d…
      // version with fold
      let map (xs: List<'a>) (f: 'a -> 'b) : List<'b> =
        Stdlib.List.fold xs [] (fun acc x -> Stdlib.List.append acc [f x])

Common ancestor: op 5511…  → hash 1abc…
                   (version before both edits)

Choices:
  [a]      keep stachu-laptop's version
  [b]      keep stachu-major's version
  [diff]   show diff between [a] and [b]
  [view a|b]   open the function source in editor
  [c]       create a third version (drops you into the editor with both shown)
  [skip]    don't resolve now (conflict stays open)

Choice:
```

The 3-line "preview source" snippet for each side comes from
running the projection's `pkg_functions[hash].pt_def` through
the existing pretty-printer. Cheap.

`[c]` opens the editor with both versions side-by-side as
comments above an empty body, so the user can hand-merge.

### Session field conflict

```
$ dark resolve sessions s_01H9C…

Conflict on session "debug Cli completion" (field: intent)

  [a] op 8af…  by stachu-laptop  2026-05-09 03:48
      intent = "fix Tab completion in `dark fn`"

  [b] op 91b…  by stachu-major   2026-05-09 03:50
      intent = "fix Tab completion + cursor pos in `dark fn`"

Auto-LWW would pick [b] (newer).

  [a] / [b] / [skip]
Choice: [b]

Resolved. Wrote resolution op (cb33…).
```

For sessions, `[c]` (third value) is also offered for `intent`
since users sometimes want to merge ("fix Tab completion in
`dark fn`, including cursor pos").

### App data KV conflict (with merge fn)

```
$ dark resolve app:blog posts/draft-001

Conflict on key "posts/draft-001" (app:blog stream)

This app declares a 3-way merge fn for table "posts":
  Darklang.Examples.Blog.Post.merge3way

  [a] op aa1…  by stachu-laptop  2026-05-09 03:55
      {"title": "Migrating Dark", "body": "...lots of text…", "draft": true}

  [b] op bb2…  by stachu-major   2026-05-09 03:58
      {"title": "Migrating Dark to ops", "body": "...different text…", "draft": true}

  [common ancestor]
      {"title": "Migrating Dark", "body": "...earlier text…", "draft": true}

Running merge3way([common], [a], [b])…

Result preview:
  {"title": "Migrating Dark to ops", "body": "...3-way merged text…", "draft": true}

  [accept]   write merge result as resolution
  [a] / [b]  override merge with one side
  [diff]     show what merge3way produced vs [a], [b]
  [skip]
```

The merge fn is just a Dark fn the app author wrote. `[accept]`
mints a fresh op containing the merged value and writes it as
the resolution. Auto-resolution does this without prompting (per
stream config).

### Branch archive vs commit (semantic)

```
$ dark resolve branches feat-auth

Conflict on branch "feat-auth"

  [a] op 11a…  by stachu-laptop   2026-05-09 04:08
      ArchiveBranch — branch was marked archived

  [b] op 22b…  by stachu-major    2026-05-09 04:10
      CreateCommit("WIP on auth flow")
      — added 3 ops to the branch (after archive)

This is a semantic conflict: archive means "I'm done with this
branch"; the commit happened anyway.

Choices:
  [archive]   honor the archive; drop the post-archive commits
              (commits stay in `ops.db` as orphans, projection ignores)
  [commit]    un-archive; integrate the commit
  [skip]
```

Clear-language buttons ("archive" / "commit") rather than [a]/[b]
when the choice has real intent.

### --auto bulk resolution

```
$ dark resolve --auto
Auto-resolving 5 conflicts using stream defaults…
  packages: 2 conflicts
    Darklang.Stdlib.List.map         → LWW: stachu-major (newer)
    Darklang.Stdlib.HttpClient.get   → LWW: stachu-laptop (newer)
  sessions: 1 conflict
    s_01H9C… field "intent"          → LWW: stachu-major
  app:blog: 1 conflict
    posts/draft-001                  → merge3way: succeeded
  branches: 1 conflict (SKIPPED)
    feat-auth                        → no auto policy for archive-vs-commit

4 resolved, 1 remaining. Run `dark resolve branches feat-auth` interactively.
```

`--dry-run` prints the same output without writing resolution
ops. Good for "what would auto do?"

`--prefer mine` and `--prefer peer` override the per-stream LWW
policy globally for one bulk pass.

## The Dark fn surface — `Stdlib.Conflicts.*`

Conflict resolution should be a Dark API, not just CLI commands.
Apps want to programmatically resolve their own data; the LSP
wants to display conflicts in editor; tests want to assert
conflict shapes.

```dark
module Darklang.Stdlib.Conflicts


type Conflict =
  { stream: String
    key: String
    kind: ConflictKind
    conflictingOps: List<Op>
    commonAncestor: Stdlib.Option.Option<Op>
    detectedAt: Int64 }


type ConflictKind =
  | SetName
  | RenameBranch
  | ArchiveVsCommit
  | UpdateField of fieldName: String
  | KvSet of table: String
  | Other of label: String


type Resolution =
  { winnerOp: Hash
    reason: ResolveReason }


type ResolveReason =
  | AutoLWW
  | AutoMergeFn of fnHash: Hash
  | Human of userId: Uuid * comment: Stdlib.Option.Option<String>
  | ResolveAsThird of newOpHash: Hash


/// Every open conflict the current user can see.
let list () : List<Conflict> = ...

/// Conflicts on one stream.
let listForStream (stream: String) : List<Conflict> = ...

/// Drill in.
let get (stream: String) (key: String) : Stdlib.Option.Option<Conflict> = ...

/// Resolve interactively (writes a resolution op).
/// Returns the new Resolution.
let resolve
  (stream: String) (key: String)
  (winner: Hash) (reason: ResolveReason)
  : Stdlib.Result.Result<Resolution, String> = ...

/// Resolve with a freshly-minted op (the [c] / 3-way merge case).
let resolveWithNew
  (stream: String) (key: String)
  (newPayload: Bytes) (reason: ResolveReason)
  : Stdlib.Result.Result<Resolution, String> = ...

/// Bulk auto-resolve using each stream's declared policy.
let autoResolveAll () : List<(Conflict, Stdlib.Result.Result<Resolution, String>)> = ...


/// For app authors: register a merge fn for a per-table conflict shape.
type MergeFn = (oldValue: Bytes) -> (leftValue: Bytes) -> (rightValue: Bytes) -> Bytes

let registerMergeFn
  (stream: String) (table: String) (fn: MergeFn)
  : Unit = ...
```

The `dark resolve` CLI is a thin wrapper over `Conflicts.list` +
`Conflicts.resolve`. The LSP is the same. A user script that
auto-resolves conflicts a particular way is the same.

## Conflict warning before edit (prevention)

The user opens a function for editing:

```
$ dark fn Darklang.Stdlib.List.map

⚠ This fn currently has an unresolved conflict from earlier sync.
  Two versions exist: 4a7b1c… (stachu-laptop) and 8e2f4d… (stachu-major).
  Editing now will create a third version that supersedes both.

  Resolve first? [y/N/r=resolve interactively]
```

Default is `N` (proceed) — that's `[c]` ("create a third
version") implicitly. `r` jumps to the resolve flow.

Implementation: `dark fn` looks up `Conflicts.get(packages,
<location>)` before opening the editor. If `Some _`, prompts.

Same applies to `dark val`, `dark type`, anything that writes a
SetName op.

## Conflict surface in `dark ls` / `dark tree`

```
$ dark ls Darklang.Stdlib.List
List/
  add        : List<a> -> a -> List<a>
  map        ⚠ : List<a> -> (a -> b) -> List<b>     [conflict]
  indexedMap : List<a> -> (Int -> a -> b) -> List<b>
  …
```

The `⚠` glyph is queried per item from `Conflicts.list`. Cached
per-stream so `ls` doesn't run a SQL query per fn.

## VS Code / LSP integration

The same `Stdlib.Conflicts.*` API drives:

- A "Conflicts" view in the sidebar (counts per stream, drill-in).
- Code lenses above conflicted functions:
  `⚠ 1 unresolved conflict — Resolve…`
- Quick-pick palette: `Dark: Resolve next conflict`.
- A diff editor that opens with both sides side-by-side, with
  "accept left" / "accept right" / "merge" affordances on each
  hunk for SetName conflicts whose preview can be diffed.

Implementation: the LSP server is Dark code (per iter 02), so
its conflict views are direct calls into `Stdlib.Conflicts`. No
extra plumbing.

## Conflicts on resolutions

A resolution is itself an op. Two devices both auto-resolve the
same conflict at the same time:

- Both run the same deterministic LWW. They produce the same
  resolution op — content-hashed, dedups. No new conflict.
- One device runs LWW, the other runs human-pick. Different
  resolution ops. Now `(stream, key)`'s conflict has *two*
  resolutions, both with the original conflict as their
  parent.

Detection: the `conflicts` projection table tracks "what was the
last applied resolution?" If two resolutions both descend from
the same conflict, they themselves form a conflict — but at the
"resolution" level, not the original op level.

Surface:

```
$ dark resolve packages Darklang.Stdlib.List.map

This conflict was resolved twice:

  [r1] op cb33…  by stachu-laptop  2026-05-09 03:30
       resolution: AutoLWW (winner: 8e2f4d…)

  [r2] op cc44…  by stachu-major   2026-05-09 03:35
       resolution: Human (winner: 4a7b1c…, comment: "the laptop one is right")

Choices:
  [r1] / [r2] / [diff]
```

Sort by `created_at` if no human override; latest wins. Surface
the multi-resolution case in `dark conflicts --history` for
auditability.

This is the recursive case the unified-model gestures at. It's
not deep — at most a few levels of resolution-of-resolution
before someone says "fine I'll pick" — and it falls out of the
same machinery.

## Notifications when sync brings in conflicts

When a `darkd` pull lands new ops that introduce conflicts, the
daemon emits:

- A log line in `daemon.log`:
  `[sync] +3 conflicts (2 packages, 1 sessions)`
- An entry in `events.db` (an LRU of recent daemon events for
  the CLI to read).
- A banner on the user's next CLI command:

  ```
  $ dark fn Some.Other.Fn
  ℹ 3 unresolved conflicts since last command — first:
    Darklang.Stdlib.List.map [packages]
    Run `dark conflicts` to view, or `dark resolve --auto` to apply defaults.
  ```

The banner shows once per CLI invocation, then suppresses for
60 seconds (don't be annoying).

## Auto-resolution policy declaration

Per-stream defaults live in `stream_config`:

```sql
ALTER TABLE stream_config ADD COLUMN
  default_resolver TEXT;     -- 'lww' | 'lww-per-field' | 'manual' | <fn-hash>
```

Defaults out of the box:

| Stream | Default resolver |
|---|---|
| packages | `lww` |
| branches | `manual` (semantic conflicts; rare; user pick) |
| sessions | `lww-per-field` |
| app:* | `lww` unless app declares per-table merge fn |
| account | `lww-per-field` |
| traces | n/a (no conflicts) |

App authors override via Dark code at app init:

```dark
module Examples.Blog

let app : App =
  App
    { name = "blog"
      router = Examples.Blog.Routes.router
      mergePolicies =
        Stdlib.Dict.empty
        |> Stdlib.Dict.set "posts" (Examples.Blog.Post.merge3way)
        |> Stdlib.Dict.set "drafts" Stdlib.Conflicts.lwwPerField }
```

The daemon reads the app manifest at start, registers each merge
fn under `(stream='app:<id>', table=<name>)`. Conflict resolution
on app data looks up the merge fn for the table; falls back to
LWW if none.

## What this changes

- `LibDB.Inserts` — no change at the storage layer; conflicts are
  detected in the projection-builder, not at insert.
- New `Builtins.Matter.Libs.Conflicts` (~5 builtins thinly
  wrapping projection conflict tables): `list`, `get`,
  `resolve`, `resolveWithNew`, `registerMergeFn`.
- New `packages/darklang/stdlib/conflicts.dark` (the API above).
- New `packages/darklang/cli/commands/conflicts.dark` and
  `resolve.dark` — the CLI surface.
- `dark fn` / `dark val` / `dark type` get a pre-edit conflict
  check (~10 lines added per command).
- `dark ls` / `dark tree` get the `⚠` glyph (~20 lines added).

Total new code: a few hundred lines of Dark, a few dozen of F#.
The hard work is the design, not the implementation.

## Open questions

1. **Conflict freshness window.** A conflict from 6 months ago that
   nobody resolved — is it still relevant? Probably yes for
   packages (the code is what it is), no for sessions (sessions
   end). Per-stream "auto-stale" policy: sessions auto-resolve
   stale conflicts to the last writer + log, packages never
   stale.

2. **What if a merge fn is itself the subject of a conflict?**
   `Examples.Blog.Post.merge3way` is a function in the package
   tree. Two devices both edit it differently. The app is
   currently using one version; they conflict on which version.
   Resolve order: package conflicts first, then app data
   conflicts (which now have a definite merge fn). Daemon does
   this naturally since `app:*` projections depend on
   `packages` having converged.

3. **Privacy of conflict data.** Stachu shares branch with
   Feriel. They both edit `Stdlib.List.map`, conflicting. Does
   Feriel see the conflict? Yes — she has a write grant; the
   conflict is on a key she has access to. Both devices show
   it; either can resolve. The resolution op is signed by
   whichever device resolves; the other device sees "Stachu
   resolved this at 03:30."

4. **Conflict UX in tightly-collaborative editing.** If two users
   are pair-programming with both writing to `feat-cli`, they'll
   constantly create LWW conflicts. The fix is upstream — make
   the editor *show pending peer ops* before they land
   ("Feriel is editing List.map") so users serialize. That's
   another iteration.

## TL;DR

`dark conflicts` lists. `dark resolve <stream> <key>` drills in.
`dark resolve --auto` applies defaults. The Dark fn surface
(`Stdlib.Conflicts.*`) drives both CLI and LSP. App authors
register per-table 3-way merge fns. Resolution-of-resolution
falls out naturally as ops on ops.

Concrete enough to build.
