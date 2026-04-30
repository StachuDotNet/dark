# Cleanup pass — what shipped + why

Final pre-merge sweep on `blobs-and-streams`. 4 commits on top of the
pre-sweep tip; full backend tests green (10,188 / 10,188 passing).

## Tasks

- [x] Rename `thinking/` → `scratch/`; update `.gitignore` accordingly.
- [x] Delete `docs/blobs-and-streams.md`.
- [x] Move `emptyBlobHash` from `Dval.fs` to a sensible home.
- [x] Create `LibExecution/Blob.fs` with the blob-runtime helpers; tiny references in `Dval.fs`.
- [x] Create `LibExecution/Stream.fs` with the stream-runtime helpers; tiny references in `Dval.fs`.
- [x] Fix `equals` / `notEquals` (drop `promoteBlobs` path; state-aware structural compare; fix `DStream` reflexivity; extract shared body).
- [x] Re-evaluate `DvalReprInternalQueryable.fs` Blob/Stream changes (why JSON?). Either justify or rework.
- [x] Investigate + explain why values in `backend/testfiles/execution/stdlib/bytes.dark` changed.
- [x] Investigate + explain why values in `backend/testfiles/execution/stdlib/crypto.dark` changed.
- [x] Investigate + explain `backend/testfiles/execution/stdlib/string.dark` — why is there `toList` now?
- [x] Make tests prettier — LHS via `|>` instead of `()` nesting; newline before `=`/RHS.
- [x] Investigate + explain `backend/tests/Tests/LibExecution.Tests.fs` changes.
- [x] Benchmarks restructure: drop `latest.json` from tracked set, keep one tracked `.json`, generate `.md` from it (with history), drop `viewer.html`. Trim `README.md`.
- [x] HttpClient review: do we need a streaming AND a non-streaming Builtin?
- [x] Remove every `let private` / `let rec` from `.dark` files (those keywords aren't real in Dark).
- [x] Tidy `backend/src/BuiltinExecution/Libs/Stream.fs` if there's tightening to do.
- [x] Add a CLEANUP to `signatures.dark` saying it should fold into `tree`.
- [x] `stdlibOverview.dark` — make it actually useful, OR trim it down with a CLEANUP saying so.
- [x] Remove the "Orientation flow for an AI agent" preamble in for-ai docs.
- [x] Final formatting + tests pass; commit everything in sensible chunks; push.
- [x] `print-md todos.md`.

---

## Responses

### Rename `thinking/` → `scratch/`

Done. `thinking/` was already mostly empty in the working tree (the
old planning notes had been moved or deleted), and the gitignore line
`thinking/` was confusing because the folder name doesn't match how
the user (or future-me) thinks of it. Renamed via plain `mv` (git mv
choked because the dir was gitignored), updated the root `.gitignore`
so the line now reads `scratch/`. The blobs-and-streams plan files
moved with it: `scratch/blobs-and-streams/` now holds README,
critique, follow-ups, pr-description.

### Delete `docs/blobs-and-streams.md`

Done. The doc duplicated content already inlined as TODO/CLEANUP
comments in the code (per the L.* tasks in the README) plus
phase-1-results / phase-2-results in `rundir/measurements/`. There
was nothing in it that wasn't either better-placed or stale.

### `emptyBlobHash` placement

Lives in `LibExecution/Blob.fs:emptyHash` now. The user's suggestion
was "ProgramTypes next to the Blob type" — but that doesn't actually
work: PT depends on RT, and the empty-hash constant is consumed in
RT (specifically `readBytes`, which short-circuits when it sees the
zero-byte hash because Microsoft.Data.Sqlite's BLOB-column read
returns None on a zero-length row even when the row exists). PT can't
look up an RT constant.

Putting it in the new `LibExecution.Blob` module is the right home —
that file already owns the byte-store mechanics and the
ephemeral→persistent promotion, so the well-known empty-blob
constant living alongside is intuitive. The module-level docstring
calls out the SQLite quirk that forces the short-circuit.

### `LibExecution/Blob.fs` and `LibExecution/Stream.fs`

Done. Two new files between `ValueType.fs` and `Dval.fs` in the
fsproj:

- `Blob.fs` (~140 lines): `emptyHash`, `sha256Hex`, `newEphemeral`,
  `pushScope`, `popScope`, `readBytes`, `promote` (the recursive
  walk through every Dval shape). Module docstring explains why the
  Dval-shape walk lives here rather than in Dval.fs.
- `Stream.fs` (~110 lines): `disposeImpl`, `Finalizer` (GC-backed
  cleanup), `wrapImpl`, `newFromIO`, `newChunked`, `pullImpl`
  (private), `readNext`, `readChunk`. The chunked drain path
  (introduced in L.7) lives here too.

`Dval.fs` lost ~280 lines: it now contains primitive constructors,
`list`/`dict`/`option`/`result` helpers, `isPersistable` /
`nonPersistableReason`, `byteArrayToDvalList`, `dlistToByteArray`.
~80 caller sites across the codebase moved from `Dval.X` to
`Blob.X` / `Stream.X` (sed pass + module aliases).

### `equals` / `notEquals` rework

Done. The previous implementation in `NoModule.fs`:

1. Called `Blob.promote` (formerly `Dval.promoteBlobs`) on both
   sides of the comparison — this rebuilt entire Dval subtrees
   containing blobs, AND inserted ephemeral bytes into
   `package_blobs` as a side effect of structural equality.
2. Then ran a synchronous structural compare.

That was wrong on multiple axes:
- The `==` builtin shouldn't have a write-to-database side effect.
- Rebuilding Dval subtrees just to compare them allocates
  proportional to the structure, not to what's actually different.
- DStream was raising "cannot compare" instead of being reflexive
  for the same handle (so `s == s` was false for `s : Stream`,
  breaking pattern lookup if a stream was held in a DDict key).

The rewrite (`equals state a b : Ply<bool>`):
- Walks both Dvals in parallel; primitives compare by value;
  containers (list/tuple/dict/record/enum) recurse with VT.merge to
  shortcut cross-type cases.
- Blobs: `Persistent(h1,_) = Persistent(h2,_)` → cheap
  `h1 = h2` hash-compare. `Ephemeral id1 = Ephemeral id2` for the
  same handle returns true without dereferencing. Otherwise
  dereference both via `Blob.readBytes` and byte-compare. No
  Dval-tree rebuild, no SHA-256 of every ephemeral, no insert.
- Streams: reference equality on `lockObj` so same-handle is
  reflexive; cross-handle returns false (consuming one to compare
  would violate the single-consumer rule).

The `==` / `!=` builtins share `equalsBuiltinImpl`, which does the
VT.merge type-check and raises `EqualityCheckOnIncompatibleTypes`
on mismatch.

### `DvalReprInternalQueryable.fs` — why JSON?

The User-DB read/write path stores `package_values.rt_dval` (and the
canvas-side User DB rows in their own table) as JSON-in-Sqlite. This
is a long-standing model decision: Sqlite indexes JSON, our queries
filter on field paths, and the JSON shape gives us an obvious
human-readable serialization for debugging.

For Blob, the JSON serializer can't inline the bytes — the bytes
might be megabytes large and the JSON column would balloon.
Persisting via the small envelope `{"type":"blob","hash":"…","length":N}`
keeps User-DB rows tiny and points back into `package_blobs` for
the bytes. Streams can't be persisted at all (single-consumer; you
can't replay one), so they raise non-persistable on attempt — same
as `DApplicable` and any other live-only Dval.

Added a module-level docstring explaining the envelope choice so
the next reader doesn't have to reverse-engineer it.

### Why values in `bytes.dark`/`crypto.dark` changed

They didn't — only the test API form did. Old shape:

```
Stdlib.Bytes.hexEncode_v0 (Stdlib.String.toBytes_v0 "x")
```

New shape:

```
(Stdlib.Blob.fromString "x" |> Stdlib.Blob.toHex)
```

The hex output is byte-for-byte identical. The change was one of API
surface (Crypto/Base64/Bytes wrappers retyped to Blob in L.6) plus
the test-prettiness pass converting nested calls to pipe form.

### Why `string.dark` has `Blob.toList`

L.6 retyped `Stdlib.Base64.decode` to return `Result<Blob, String>`.
But `Stdlib.String.fromBytes_v0` and
`Stdlib.String.fromBytesWithReplacement_v0` still take
`List<UInt8>` — the L.6 carve-out kept those on the List shape
because Http, CLI file ops, and the LSP logger all consume that
type, and migrating them was much bigger than L.6's scope.

So the FromBytes / FromBytesWithReplacement test blocks now bridge
explicitly via `Stdlib.Blob.toList`:

```
((Stdlib.Base64.decode_v0 "w6I")
 |> Builtin.unwrap
 |> Stdlib.Blob.toList
 |> Stdlib.String.fromBytesWithReplacement_v0)
  = "â"
```

Header comment in the file flags the carve-out so the reason is
readable in-context.

### `LibExecution.Tests.fs` change

Single-line tweak: in the structural comparison of expected vs
actual Dvals during `.dark` test execution, both sides now go through
`Blob.promote` before comparison. Reason: a `.dark` test that
constructs an ephemeral blob on the LHS and another ephemeral blob
on the RHS would have UUID-distinct handles even though the bytes
match. The runner promotes both so the byte content backs onto the
content-addressed hash, after which structural equality is just
hash-compare. This is a test-runner concern; the production
`==`-builtin path now compares by content directly without any
promote side effect (see equals/notEquals item above).

### Test prettiness

Done across `crypto.dark`, `bytes.dark`, `string.dark`. Used
`(start |> step |> step) = expected` for the LHS and a newline
before `=` when the LHS spans multiple lines. The bare
`Stdlib.Blob.fromString "" |> X` form parses ambiguously in the F#
front-end (the parser treats `f x |> g` as a degenerate App when the
LHS is a function call without parens), so the seed expression is
parenthesised. `base64.dark` left as-is — its tests are mostly
single-call shapes that don't benefit from pipes.

### Benchmarks restructure

Done. New shape:

- `benchmarks/results/history.jsonl` — **the only tracked file**.
  Append-only on every `bench` run.
- `benchmarks/results/latest.json` — gitignored, just the most
  recent snapshot for tooling / piping.
- `benchmarks/results/local-*.json` — gitignored, machine-local
  experiments.
- `benchmarks/viewer.html` — **deleted**. `results.md` is the
  canonical view.
- `benchmarks/results/post-blob-stream.json` and other named
  snapshots — **deleted**. Promotion is just running `bench` on a
  clean tree and committing the new line in history.jsonl.
- `bench-promote` command — **deleted**.
- `bench-render` rewrites `results.md` from history.jsonl: latest
  entry is the headline (full per-scenario tables); older entries
  become a compact "Run history" table.
- README trimmed from 51 to 27 lines.

### HttpClient review

Both `httpClientRequest` (returns Response with Blob body) and
`httpClientStream` (returns StreamResponse with Stream body) are
genuinely useful — different API surfaces and different cost
profiles, and Dark callers want both shapes available without a
manual drain on the simple case.

There's ~70 lines of duplicated request-building between
`makeRequest` and `openStreamingRequest` (URL validation, header
collection, HttpRequestMessage construction). Two viable
consolidations, neither done in this pass:

  (a) Extract a shared `prepareRequest` helper returning
      `Result<HttpRequestMessage * CancellationToken, RequestError>`;
      both paths SendAsync on it. Small refactor.
  (b) Collapse the buffered builtin into a Dark-side wrapper around
      `httpClientStream + Stream.toBlob`. Removes the duplication
      entirely; one extra Stream allocation per non-streaming call;
      needs care around the telemetry differences
      (`telemetryInitialize` wraps the buffered path; streaming
      inlines the tags).

Module-level CLEANUP comment in `HttpClient.fs` records both paths
so the next person doesn't have to re-derive them.

### `let private` / `let rec` from `.dark` files

Done. Neither keyword is real in Dark — they're F#-isms that the
parser was tolerating. Stripped from `signatures.dark`,
`stdlibOverview.dark`, `docs.dark`. Each `let private foo =` became
`let foo =`; `let rec` (used incorrectly anyway, since Dark
functions are top-level and recursive by default) became `let`.

### Tidy `backend/src/BuiltinExecution/Libs/Stream.fs`

Reviewed; no surgery worth the diff churn. The shared
`resolveElemVT` / `resolveElemKT` helpers are factored out, the
lazy-transform comment clearly states the single-consumer
semantics, and the `Mapped`/`Filtered`/`Take`/`Concat` builtins
follow the same wrap-and-return pattern. Each builtin's `incorrectArgs`
fall-through is exhaustive.

One mild redundancy: each transform builtin builds an
`apply`/`pred` closure that calls `Exe.executeApplicable` and pipes
the `Error(rte, _) -> raiseRTE` branch. Could be a one-line helper.
Not worth the indirection.

### `signatures.dark` CLEANUP

Done. Header comment now reads:

> CLEANUP this command should fold into a more general `tree` /
> `view` flag rather than living on its own. The
> "format-tightly, scope-by-module, optional types, optional
> filter" shape is just a rendering of a package subtree —
> `tree --signatures [<filter>]` or `view <module> --signatures`
> would cover the same surface without the separate dispatch
> entry.

### `stdlibOverview.dark`

Trimmed from 127 lines to ~55. Removed the count-formatting machinery
and the per-module function-count tables — they were noise. The
command now just lists module names under `Stdlib`. Top-of-file
CLEANUP says it needs to be made actually useful (e.g. by surfacing
intent-to-API mappings, by integrating with `signatures` once that
folds into `tree`, etc.) before keeping it longer-term.

### "Orientation flow for an AI agent" preamble

Removed from `for-ai.dark`. The remaining content now starts at the
actual high-signal sections instead of a meta-prologue about how to
read the doc.

### Final formatting + tests + commits + push

`./scripts/formatting/format format` rewrote 6 files in place.
Backend tests: 10,188 passed / 4 ignored / 0 failed / 0 errored.

Committed in 4 chunks on top of `6575031cb`:

1. `aafdc1bb2` — blob/stream: extract LibExecution.Blob and Stream
   modules
2. `8d21f5e84` — bench: history.jsonl is the only tracked record
3. `b42fa4559` — stdlib tests: pipe form for nested blob/crypto
   calls
4. `945c0dc72` — cleanup: scratch rename, dark-side polish,
   pre-merge tidy

Pushed to `origin/blobs-and-streams`.

### `print-md todos.md`

You're reading the rendered output.

---

## Loop note

The `/loop` task ("execute the next incomplete chunk in
`scratch/blobs-and-streams/README.md`") had nothing to do — every
Phase 0/1/2 box and every Later (L.1–L.7) box is `[x]`. The hygiene
sweep above is a separate pre-merge pass; the loop has been left
without a wakeup so it stops naturally.
