# Virtual files: Dark state as a filesystem

## The question, re-stated

Dark's state — package items, annotations, canvas deployments, traces,
commits — lives in SQLite tables and in-process data structures, and
today you reach it through `dark` subcommands or the LSP. A growing
class of consumers wants to reach it through **file paths and byte
streams** instead: `cat`, `rg`, VSCode, Claude Code, Obsidian-style
plugins, `jq`, `prettier`, `git` itself, ad-hoc shell pipelines, AI
agents that speak a `Read(path)/Edit(path)` vocabulary natively.

"Virtual files" is the shape that accommodates them: a presentation of
Dark's state that looks and behaves enough like files for
non-Dark-aware tools to work, while the authoritative storage stays in
Dark. "Virtual" because no bytes-on-disk is the source of truth — the
file view is projected from the SCM state, and any writes bounce back
through Dark's op pipeline before they become real.

This doc is a design exploration, not a commitment. It maps the
territory, compares implementation tiers, surveys prior art, and
identifies the places where Dark's semantics and POSIX's semantics pull
in different directions.

## Hard constraint: embeddable on all three platforms

The scope here is **what ships inside the Dark binary and works on
Linux, macOS, and Windows without the user installing anything else
first**. No third-party kernel extensions. No "first enable this
Windows Feature." No "brew install macfuse." Users who already have
Dark should not need a second download to get virtual files.

That constraint rules out most of the obvious heavyweight solutions
(macFUSE requires a kernel extension install + reboot on macOS;
`davfs2` isn't shipped on Linux by default; the Windows NFS Client is
an off-by-default optional feature). It ends up pointing at **two
tiers** — a projected working directory, and that same directory plus
a file watcher — as the cross-platform floor. Any fancier kernel-level
virtualization becomes a per-platform enhancement, not a primary shape.

The filesystem-portability concerns (case sensitivity, reserved names,
path length, symlinks, line endings) are addressed in their own
section below. They apply to every tier and shape the projection
layout.


---

## Who benefits, and how faithful do they need it to be?

The consumer determines the floor for "how close to real files" the
projection has to get. A read-only `cat` only needs byte-stream reads;
a two-way editing session needs a working-copy discipline; a full
`rsync` needs mtime stability.

| Consumer                                    | Min faithful-ness                    |
| ------------------------------------------- | ------------------------------------ |
| `cat`, `head`, `less`                       | Byte reads. Size doesn't have to match exactly. |
| `grep`, `rg`, `fd`, `find`                  | Directory enumeration + byte reads. Stable paths across a single run. |
| Editors (VSCode, vim, emacs)                | Read + write, reliable-enough fsync, reasonable mtime, inotify/fsevents helps but isn't required. |
| AI agents (Claude Code, Cursor)             | `Read(path)` / `Edit(path)` / `Write(path)` / `Glob` / `Grep`. Paths need to survive the session. |
| Shell pipelines                             | All of the above + predictable `ls -la` metadata. |
| `git` (as a way to version Dark items)      | Stable paths, stable file identity across "commits," handles hashing internally. |
| `rsync` / sync tools                        | Accurate mtime, byte-stable content, efficient readdir. |
| A running Dark app reading/writing          | Full POSIX incl. locking, atomic rename, fsync durability. (Almost certainly out of scope — this is Dark-as-OS territory.) |

The biggest payoff cluster is in the middle rows: editors + AI agents +
shell tools. That's the market for "virtual files" in Dark. The top row
is trivially satisfied by almost any implementation. The bottom rows
are either impractical (rsync at byte-identity against computed content)
or a different product entirely (Dark-as-runtime-filesystem).

**What an AI agent actually wants.** Agents like Claude Code don't need
perfect POSIX — they need stable paths across their session, they want
`Grep` to work, they want to open a file and write it back. They do
*not* need `fsync` to mean anything specific, they do *not* care about
inode numbers, and they handle write failures gracefully (retry,
diagnose, ask for help). This is a permissive consumer: projecting
reasonably shaped paths and text content gets them 90% of the way.

**What an editor wants.** Editors are pickier. They do `open()` →
`stat()` for mtime → `read()` → user edits → `write()` to a tempfile →
`rename()` over the original → `fsync()`. Some watch for external
changes via inotify. All of that has to either work, or fail in a way
the editor tolerates. The rename-over trick is common and worth testing
against before declaring a projection "editor-compatible."

**What `git` wants.** `git` wants filesystem semantics that round-trip
through hashing. If you project Dark as a directory and `git add .`
that directory, git treats each file as a blob, diffs them by content,
and tracks history in parallel to Dark's own SCM. This is useful for
backup and interop but dangerous as a workflow — two competing SCM
systems fighting over the same file tree will confuse humans. Worth
supporting as an *export* shape, not as a *sync* shape.


---

## The spectrum of implementation tiers

Ordered by how "real" the files feel, and annotated with the
install-burden on each OS. Under our embeddable constraint, only tiers
that require **nothing beyond the Dark binary** on every platform
qualify for the primary story.

| Tier  | What                              | Linux install | macOS install | Windows install | Embeddable? |
| ----- | --------------------------------- | ------------- | ------------- | --------------- | ----------- |
| 0     | CLI verbs (`dark cat`/`ls`)       | —             | —             | —               | ✅          |
| 1     | One-shot export                   | —             | —             | —               | ✅          |
| 2     | Projected working directory       | —             | —             | —               | ✅          |
| 3     | Projected dir + file watcher      | —             | —             | —               | ✅          |
| 4a    | FUSE                              | usually OK    | macFUSE kext  | FUSE-for-Win    | ❌ on macOS |
| 4b    | NFS loopback (EdenFS pattern)     | nfs-utils     | built-in      | optional feature | ❌ on Linux/Win |
| 4c    | WebDAV                            | davfs2        | built-in      | WebClient service | ❌ on Linux |
| 4d    | 9p mount                          | kernel v9fs   | no native     | no native       | ❌          |
| 4e    | ProjFS (Windows VFS API)          | n/a           | n/a           | built-in 10+    | Windows-only |

**Tiers 0–3 are the embeddable set.** Tier 4 is a grab-bag of
kernel-mediated options, none of which work on all three platforms
without user-side install. Tier 4 becomes a *per-platform optional
enhancement* in the longer-term arc, not part of the initial shape.

### Tier 0 — CLI as virtual filesystem

What exists today. `dark cat`, `dark ls`, `dark view`. Not files:
subcommands with filesystem-shaped verbs. Non-Dark tools don't benefit
unless they shell out.

**Cost:** zero new work.
**Ceiling:** no `rg`, no editor integration, no AI-agent file tools.

### Tier 1 — One-shot export

`dark fs export <dest>` materializes a directory snapshot. Read-only,
stale the moment it's produced, user responsible for disposal.

**Cost:** small. Just a "render everything to a directory" pass.
**Ceiling:** works once. Useful for sharing/handing-off a snapshot to
an external tool, useless as a working surface.
**Platform notes:** pure file-writing; works everywhere. Subject to
the portability concerns below (case, path length, reserved names).

### Tier 2 — Projected working directory (two-way, explicit sync)

Dark maintains a real directory on disk. `dark fs open <dir>` populates
it. User edits with any tool. `dark fs sync` scans for changes and
emits them as WIP ops. Branch switches regenerate. Explicit, not magic.

**Cost:** moderate. Projection, diff detection, WIP-op emission,
conflict surfacing. No kernel integration, no platform-specific code.
**Ceiling:** feels native to every tool. Sync is explicit, which is
clunky for editors that expect "save = durable" but fine for
batch/agent workflows. Closest analog: `git worktree`.
**Platform notes:** pure file IO; works on Linux, macOS, Windows
identically. The constraints are on the projection layout (see
§Filesystem portability), not on the implementation.

### Tier 3 — Projected working directory + file watcher

Same as Tier 2 plus a watcher that catches writes and syncs
continuously. Editors get near-real-time: save a file, it's in WIP
seconds later.

**Cost:** Tier 2 + a watcher + race-condition handling (partial
writes, rapid-fire saves, editor-temp-file dance).
**Ceiling:** the sweet spot for 95% of use cases. FUSE-quality
experience without FUSE.
**Platform notes:** the watcher backend differs per OS but is native
on each. Nothing to install:
  - Linux: **inotify** (kernel subsystem, always present).
  - macOS: **FSEvents** (kernel, always present).
  - Windows: **ReadDirectoryChangesW** (Win32, always present).

.NET's `System.IO.FileSystemWatcher` abstracts all three behind one
API and ships with the runtime Dark already depends on — no separate
install per platform, and no per-platform code in Dark itself beyond
the per-editor edge-case handling. Node's `chokidar`, Rust's `notify`,
etc. do the same if Dark grows a non-.NET watcher. The watcher is the
last embeddable piece; everything past here requires help.

### Tier 4 — Kernel-mediated VFS (per-platform, non-universal)

Each of the kernel-level options violates the embeddable constraint on
at least one platform:

- **FUSE** is the textbook answer on Linux. On macOS, **macFUSE** is a
  third-party kernel extension that requires the user to download,
  run an installer, approve it in System Settings → Privacy &
  Security, and reboot. Apple has been deprecating kexts broadly;
  the install friction is only going to grow. On Windows, "FUSE-for-
  Windows" projects exist (WinFsp, Dokan) but each requires a driver
  install.
- **NFS loopback** is how **EdenFS** (Sapling's virtual filesystem)
  solves macOS: spin up a local NFS server in-process and have the
  OS mount it. macOS ships `nfsd`; Linux generally has it via
  `nfs-utils` (not always installed); Windows NFS Client is an
  optional feature (off by default). Works, but not embeddable.
- **WebDAV** has the widest client coverage (macOS and Windows ship
  clients) but Linux typically needs `davfs2` installed.
- **9p** needs kernel `v9fs` on Linux (usually present) and has no
  native client on macOS or Windows.
- **ProjFS** is Windows-only, built into Windows 10 1809+. Embeddable
  on Windows but nowhere else.

If Dark ever wants a faster-than-projection experience, the realistic
story is: **Linux gets FUSE; Windows gets ProjFS; macOS stays on
Tier 3**. No unified kernel path works. This is the same compromise
Sapling/EdenFS live with.

### Tier 5 — Dark-hosted filesystem for running apps

Dark canvases expose a POSIX-shaped filesystem that *programs running
inside Dark* can read and write, as part of their execution. This is
Dark-as-OS. Fascinating, not this doc's subject. Mention only to bound
the scope: the tiers above are for **external tools reaching into
Dark**; Tier 5 would be for **Dark programs reaching out** through a
filesystem they own.


---

## What Dark could project

An inventory of Dark state by "how filelike is it, and how should it
appear." Designs that lump everything into one mount point fail
immediately because the shapes are genuinely different.

### Package items → `.dark` source files

The obvious case. Each package fn/type/value renders as its pretty-
printed source in a `.dark` file at its FQN path:

```
packages/
  Darklang/
    Stdlib/
      List.dark          # module-level items flattened into one file, OR...
      List/              # ...one file per item
        map.dark
        filter.dark
        head.dark
```

One-file-per-module mirrors how humans read code; one-file-per-item
mirrors how Dark actually stores things (items are independent,
modules are name prefixes). The per-item shape is probably right for
the projection — it matches Dark's internals, keeps edits narrow, and
makes agents' per-file vocabulary natural. Humans who want
"module-as-file" can view `packages/Darklang/Stdlib/List/index.dark`
(a synthetic concatenation).

Writes are meaningful: the user edits the source, the projection
parses it into a new fn/type/value, emits a WIP `AddFn` + `SetName`
(or a propagation if the hash is new and there are dependents).

### Annotations → frontmatter, sidecar, or a `.meta/` subtree

Three plausible shapes, differing in how deeply annotations integrate
with the source file view:

**A. Frontmatter.** YAML block at the top of the `.dark` file:

```
---
description: |
  Maps a function over a list.
deprecation: null
stability: stable
---
let map (l: List<'a>) (f: 'a -> 'b): List<'b> = ...
```

Feels natural for descriptions. Falls apart for annotations that
touch multiple elements (param-level descriptions, per-field
deprecations on records), since YAML gets baroque fast. Also blurs
the content-vs-annotation boundary the layers doc deliberately drew.

**B. Sidecar files.** Separate files per annotation per item:

```
packages/Darklang/Stdlib/List/
  map.dark
  map.description.md
  map.deprecation.yaml     # absent if not deprecated
```

Clean separation, matches the annotation layers, lets different tools
edit different axes. Costs: `ls` clutter, many-files-per-item, and
cross-file coordination when an edit implies both content and
annotation changes.

**C. Directory-per-item + one annotation per file.**

```
packages/Darklang/Stdlib/List/map/
  source.dark
  description.md
  deprecation.yaml
  hash
  history.log
```

Ultra-regular, but every item becomes a directory; depth blows up;
the "one file holds the code" intuition is gone. This is closest to
`/proc`-style where every entity is a directory of attributes.

**Probable split:** Frontmatter for description (conceptually part of
the code, reasonably shaped for YAML), sidecars for structured
annotations (deprecation, stability, purity), CLI for derived data
(not projected). Let's call this the **hybrid** approach — it's what
humans actually want to see.

### Canvas state → a different subtree with different semantics

Canvases aren't code; they're running configurations. Projecting a
canvas the same way as a package subtree would be a mistake. Instead:

```
canvases/
  my-app/
    handlers/
      GET-users.handler          # route → fn-hash binding
      POST-items.handler
    dbs/
      users.schema.dark          # the type declaration
      users.rows.jsonl           # read-only, possibly truncated, possibly streaming
    cron/
      nightly-cleanup.job
    secrets/                     # permission-gated; omitted by default
    deployed-at                  # commit hash the canvas is pinned to
```

Writable things: handler metadata (route, fn pointer), DB schema
changes (schema.dark is a type decl — writing it edits the type), cron
schedule. Not writable: DB rows (that's an insert, not a file write),
secrets (exposure hazard), `deployed-at` (SCM-level concern).

Canvas projection is a useful consistency check on the annotation
projection: if annotations don't work for handlers and DBs, the model
is incomplete.

### Traces → their own subtree, read-only, pinning via CLI

Traces live outside the package tree (high volume, own GC). File
projection mirrors that:

```
traces/
  recent/
    2026-04-22T14:23:15.1234Z-GET-users.json
    ...
  pinned/
    parseJson-happy-path.json             # symlink or rendered-inline
    auth-login-success.json
```

`recent/` is the rolling buffer — sampled, time-bounded, auto-GC'd.
`pinned/` is the curated set; items there are referenced by the
`example_trace_of` relationship table and never GC'd. Pinning is a
CLI operation, not a `mv` into `pinned/` (keep the SCM concern out of
the filesystem action).

Reads are fine; writes to `recent/` are meaningless and should be
rejected. Writes to `pinned/` likewise.

### Commit history → one file per branch

Read-only:

```
branches/
  main.log
  feature-x.log
  current -> main                       # symlink to the current branch's log
```

A `.log` file contains a commit-history dump in a shell-friendly
format (JSONL or plain lines). Useful for `git log`-style pipelines
but truly is a one-way projection.

### Derived data → also read-only, possibly omitted

Derived metrics (perf characterizations, usage counts, inferred
purity) are computed and branch-scoped. Filelike projection works but
the staleness is inherent:

```
derived/
  perf/
    Darklang.Stdlib.List.map.perf.json
  usage/
    Darklang.Stdlib.List.map.usage.json
```

Probably not worth surfacing in the first round — no editor workflow
needs it; agents can query via CLI. File projection adds visual
noise without enabling new workflows.

### What to omit

- **Secrets** by default. Only exposed behind an explicit flag and
  probably with a scarier separate mount.
- **Builtins.** Already non-editable, their Dark source doesn't exist,
  and cluttering the tree with read-only builtin signatures is
  editor-noise. Stash them under `builtins/` if needed for search;
  otherwise omit.
- **Ephemera** (raw traces below the pinning cut, error rates, etc.).
  Different subsystem, different shape, not files.


---

## The hard problems, in detail

### Identity versus path

Real files: identity is the inode on the device. Renames are cheap,
content doesn't change, stat stays meaningful.

Dark items: identity is the content hash. Renames are `SetName` ops
that change which location points at which hash. Content edits produce
*new* hashes; the "same item" is a fiction layered on top of the
op log.

**Implications for the projection:**

- An editor's open-file handle caches a path. If Dark renames the item
  underneath, the handle still works (OS keeps the inode alive) but
  subsequent `ls` won't show it at the old path. Editors sometimes
  reload from path; those get confused.

- `git diff` run against the projection will see every content edit as
  a new file (hash changed → if the projection names files by hash,
  every save is a new file). If the projection names files by FQN
  (path), `git diff` works intuitively.

- Rename detection: if Dark renames `Foo.bar → Foo.baz`, the
  projection should show one file moved, not one deleted and one
  added. With an FQN-keyed projection, this is natural. With a
  hash-keyed projection, rename is invisible.

**Resolution:** Name files by FQN, not hash. The hash is metadata
(stashed as xattr, sidecar, or in-frontmatter). Renames look like
renames to external tools. Content edits look like content edits.

The trade: the projection no longer reflects Dark's internal identity
model. For consumers, that's the right trade.

### Write equals parse

A `write()` on a `.dark` file has to be translated into a package op.
That op is either `AddFn`/`AddType`/`AddValue` (with the freshly-parsed
content), or — if the item exists — a supersede-and-re-set-location
sequence.

**The parse can fail.** Editors write bytes; they don't check that
those bytes parse. What does the projection do?

- Reject the write with an I/O error: editors handle this poorly,
  autosave loops, user loses work. Bad UX.
- Accept the write, store the bytes in a "unparsed draft" WIP state:
  the op log has a `DraftSource` kind whose payload is raw text that
  failed to parse. `status` shows the draft as a warning state. Next
  successful parse supersedes it. This is a real new op kind and needs
  design; it's the right shape.
- Accept the write silently, discard on parse failure: silent data
  loss, worst option.

The `DraftSource` option is the interesting one. It dovetails with
how humans actually edit code (type half a function, leave for coffee,
return to finish). The editor sees its write succeed; Dark shows "1
draft" in status; commit refuses until drafts resolve (either to a
real op or to a discard).

**Partial writes.** Editors sometimes write in chunks. The projection
should aggregate — rapid-fire `write()` calls within (say) 250ms
coalesce before the parse attempt. Matches inotify debounce patterns.

**Atomic rename-over.** Many editors write `foo.dark.swp` then
`rename()` it over `foo.dark`. The projection has to follow the rename
and treat the new file as "the new foo.dark" rather than an add +
delete. Standard FS-watching trap; well-documented solutions.

### Metadata semantics

Each `stat()` call expects a full set of fields. Dark doesn't have
most of them natively; we have to fabricate.

- **`mtime`.** "Last operation affecting this item's location on this
  branch." Available via `locations.created_at` on the latest
  non-unlisted row. Fine.
- **`ctime`.** Same as mtime, since Dark doesn't distinguish metadata
  vs content change times.
- **`atime`.** Meaningless. Always equal to mtime (many Linux mounts
  are `noatime` anyway).
- **`size`.** Pretty-printed byte length. Recomputed on demand; cached
  per hash.
- **`mode`.** `0644` uniformly. Upgrade when ACLs land.
- **`uid`/`gid`.** The caller's. No multi-user model.
- **`nlink`.** 1 for files. Directories: 2 + subdir count.
- **`ino`.** Synthetic, stable per-(branch, item). Use a hash of
  `(branch_id, fqn)` or a counter per branch. Stability matters for
  `find -inum` and for editors that compare inodes across stats.
- **`dev`.** Constant per mount.

**`rdev`**, **`blocks`**, **`blksize`** — synthesize defaults nobody
looks at.

One real gotcha: **POSIX says `mtime` can only move forward within the
same mount.** If Dark's clock skews, or if branch-switch makes
an "older" mtime visible, tools that rely on mtime-monotonicity (make,
rsync with `--update`) misbehave. Options: use `created_at` as source
of truth and take the max over the branch chain; or bump the
fabricated mtime to `max(stored_mtime, now())` on read.

### Branch scoping

Dark branches are first-class. Projected into the filesystem:

**Option A: per-branch roots.**

```
dark-mount/
  main/
    packages/...
  feature-x/
    packages/...
  current -> feature-x
```

Every branch is fully materialized. Switching branches is a pointer
flip, not a regeneration. Cost: disk space grows with branch count;
conceptually clean; tools that `cd` into `current/...` track whatever
branch is current.

**Option B: one tree that switches under you.**

```
dark-mount/
  packages/...     # content changes when `dark branch switch` runs
```

Less disk, but every branch switch invalidates every cached inode.
Editors with open files may or may not notice. Path-caching tools get
confused.

**Option C: per-branch subdir, symlink for current (git-worktree pattern).**

```
dark-mount/
  branches/
    main/...
    feature-x/...
  current -> branches/feature-x
```

A hybrid. Disk cost of A, path-switch semantics of B, explicit when
you care (`dark-mount/branches/main/packages/...`) and implicit when
you don't (`dark-mount/current/packages/...`).

**Preferred: C.** It matches the mental model (branches are real
parallel worlds) and degrades gracefully if tools cache paths via
`current`.

### Two-way conflicts

Editor saves `foo.dark` at the same moment Dark's op pipeline processes
a propagation that touched the same location. Who wins?

In a one-writer world (only the user is editing) this is trivial.
In reality:

- Another human on the same branch.
- Another agent on the same branch.
- A propagation running in response to someone's unrelated edit.
- A merge from a parent branch.

Dark already handles these as SCM-level conflicts. The projection has
to surface them in filelike form without losing the underlying conflict
machinery.

**Approach: conflict files mirror conflict state.**

```
packages/Darklang/Stdlib/List/map.dark           # current branch's HEAD
packages/Darklang/Stdlib/List/map.dark.yours     # the user's un-synced write
packages/Darklang/Stdlib/List/map.dark.theirs    # the conflicting incoming change
packages/Darklang/Stdlib/List/map.dark.base      # common ancestor
```

User resolves by editing `.dark` and deleting the three sidecars, same
as `git` conflict markers. Dark-side, `dark fs sync` sees the
resolution and emits the appropriate op.

Alternate: inline conflict markers (`<<<<<<<` etc.) in the main file.
Industry-standard, every editor handles them, but they make the file
temporarily unparseable — which routes back through the `DraftSource`
flow. Arguably a feature: the draft state makes the conflict visible
in `status`.

### Large-tree performance

Darklang's own package tree is thousands of items. Fully materializing
every `.dark` file on `dark fs open` is:

- Several thousand file creates.
- Several thousand pretty-prints.
- Several thousand SQL round-trips (or one big query + in-memory
  formatting).

Ballpark: on reasonable hardware, first-time materialization of the
whole tree is probably 5-30 seconds. Feasible but slow. After that,
steady state is cheap until a branch switch.

**Mitigations:**

- **Lazy readdir / open.** (FUSE territory.) Directory listings return
  names without materializing; `open()` triggers materialization. Only
  works in kernel-mediated tiers.
- **Eager with parallel pretty-print.** Batch fetch + parallel format.
  Cuts seconds off.
- **Differential update.** Keep a manifest of `(path, hash)` pairs.
  On sync, only re-materialize paths whose hash changed.
- **Skip builtins / unused modules.** Materialize only what the user
  has requested via `dark fs open <subpath>`.

**Branch switch** with Option C above: instead of regenerating, keep
per-branch materializations on disk and flip the `current` symlink.
Each branch re-materializes once; switching is instant.

**Caveat.** Projected directories are real disk — the Darklang tree
multiplied by each active branch is real GB. EdenFS solves this with
hardlinks to a content-addressed store; Dark could do the same
(content-hash-keyed backing store under `.dark-fs/objects/`, with
per-branch directories full of hardlinks). Worth it only when branch
count × tree size starts hurting.

### Filesystem portability (Linux / macOS / Windows)

Tiers 2 and 3 use real files on real disks. That's cheap universality
in the install sense, but it drags in a pile of concrete filesystem
differences that *the projection layout has to accommodate*. None of
these need a user install; they're Dark-binary-side bugs waiting to
happen.

#### Case sensitivity

- **Linux** (ext4 default): case-sensitive. `Foo.dark` ≠ `foo.dark`.
- **macOS** (APFS default, HFS+): case-preserving but case-**insensitive**
  by default. `Foo.dark` and `foo.dark` collide.
- **Windows** (NTFS): case-preserving, case-insensitive by default
  (per-directory case-sensitivity exists on NTFS + recent Windows, but
  opt-in).

Dark package names are case-sensitive internally; two items named
`Darklang.Foo.Bar` and `Darklang.Foo.bar` (type + fn convention) would
materialize into colliding files on macOS/Windows. In practice Dark's
naming conventions (type = PascalCase, fn/value = camelCase) prevent
identical names at the same path, but cross-kind collisions (a type
`Bar` and a value `bar` in the same module) could produce
name-clash-by-case.

**Mitigation:**
- Detect collisions at materialization time. If a path would collide
  case-insensitively with another, append a kind suffix (`Bar.type.dark`
  / `bar.val.dark`) and log a warning.
- Document: users on macOS/Windows who try to author two items whose
  names differ only in case get told to rename one.

#### Reserved filenames

- **Windows** reserves: `CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`,
  `LPT1`–`LPT9`, plus the same with any extension
  (`CON.dark`, `NUL.txt`). Attempting to create `CON.dark` fails or
  behaves weirdly (it resolves to the console device).
- **macOS/Linux** don't have reserved filenames in the same way.

Darklang legal identifiers include `con`, `nul`, `prn` (lowercase is
different from the reserved names, which are case-insensitive on
Windows — so `con.dark` is also bad).

**Mitigation:** at materialization, check the leaf name against the
Windows reserved set case-insensitively; if it collides, prefix or
suffix (`_con.dark` or `con_.dark`). Preserve the real name in
frontmatter or in a `.manifest` sidecar so sync-back can recover it.
Log once per collision.

#### Path length

- **Windows**: `MAX_PATH` is 260 characters by default. Longer paths
  work only if the application opts into long-path support (via
  manifest + registry `LongPathsEnabled`) **and** the user is on
  Windows 10 1607+.
- **Linux/macOS**: path length limits are per-filesystem and usually
  4096 characters. Practically unlimited.

A deeply nested Dark module path plus a projection root plus a branch
subdir plus a long identifier can blow past 260 chars. Example:

```
C:\Users\stachu\dark-mount\branches\deprecation-redesign\packages\Darklang\LanguageTools\LspServer\DocSync\handleTextDocumentDidSave.dark
```

That's ~140 chars. Fine. But scripts with deeper paths
(`prettyPrinter/runtime/analysis/something/another`) can push it over.

**Mitigation:**
- Opt into long-path support in the Dark binary's manifest (free on
  Windows 10+).
- Keep the projection root short by default (`%USERPROFILE%\dark\` not
  `%USERPROFILE%\Documents\My Files\dark-project\`).
- Document long-path-mode as a prerequisite on older Windows; fall
  back gracefully (materialize only the subtree under a MAX_PATH
  budget).

#### Forbidden filename characters

- **Linux**: allows anything except `/` and `\0`.
- **macOS**: allows anything except `/` and `\0`; `:` historically
  forbidden (HFS legacy) but APFS allows it.
- **Windows**: forbids `< > : " / \ | ? *` and control chars 0–31.

Dark's legal identifier charset is `[A-Za-z0-9_']`. The single
quote (`'`) is legal in Dark identifiers but uncommon; it's allowed on
all three platforms. Dots in module paths become directory separators
in the projection — no conflict.

**Verdict:** safe by construction. No mitigation needed unless the
identifier rules relax later.

#### Line endings

- **Linux/macOS**: `\n`.
- **Windows**: `\r\n` conventional, `\n` increasingly accepted.

Editors on Windows often write `\r\n` by default (though modern ones
default to `\n` in cross-platform projects). Dark's parser handles
both; the projection should **write `\n` only**, and the sync-back
normalizes incoming writes to `\n` before parsing so round-tripping is
stable. A `.gitattributes`-style hint file in the mount root
(`.editorconfig`) can request `end_of_line = lf` from IDEs that honor
it.

#### Symlinks

- **Linux**: unrestricted; any user can create symlinks.
- **macOS**: same.
- **Windows**: symlink creation requires **Developer Mode** (free,
  opt-in) or elevated privileges. Regular users on default-configured
  Windows 10/11 cannot create symlinks without one of these.

If the branch-switcher relies on a `current → branches/main` symlink,
Windows users without Developer Mode hit a dead end. Alternatives:

- **Junction points**: Windows-specific directory-symlink that doesn't
  require Developer Mode, but only works for directories (not files).
  Fine for `current → branches/main` since it's a directory reference.
- **Active branch indicator file**: write the current branch name to
  `.dark-fs/current-branch`, have tools resolve by reading it. More
  brittle but universally portable.
- **Per-branch mounts only**: skip the `current` symlink entirely;
  users `cd` into `branches/<name>/` directly.

**Preferred fallback order:** symlink (Linux/macOS, Windows with
Developer Mode) → junction (Windows without) → no-symlink layout that
uses a branch-indicator file (degraded but portable).

#### Hard links

- **Linux**: supported everywhere (ext4, xfs, btrfs).
- **macOS**: supported on APFS, HFS+.
- **Windows**: supported on NTFS, not on FAT/exFAT.

Matter only for Nix-store-style content-addressed backing (a possible
long-term optimization). If multiple branches share the same item
content, hardlink them to a backing store under
`.dark-fs/objects/<hash>`. Safe on NTFS-and-above, ext4+, APFS. FAT
external drives are out. Low risk.

#### File watching differences

All three platforms ship a native API that `System.IO.FileSystemWatcher`
(and most other cross-platform watcher libraries) multiplex behind one
interface:

- **Linux**: inotify. Per-process watch limit (default 8192 or 65536
  on modern distros). Large projections could run out — mitigate with
  `fs.inotify.max_user_watches` check + fallback to coarser polling.
- **macOS**: FSEvents. Per-directory granularity; has a known quirk
  where renames coalesce into "something changed in this dir" rather
  than specific rename events. The watcher has to re-scan on
  ambiguous events.
- **Windows**: ReadDirectoryChangesW. Reliable, with the quirk that
  moving a file *into* a watched dir shows as "create," and moving
  *out* shows as "delete" (not rename). Buffer size matters; default
  drops events on rapid fire.

All three are usable without any user install. The watcher
abstraction needs to handle the quirks above; .NET's built-in
`FileSystemWatcher` papers over most but not all of them (the macOS
"re-scan on ambiguous" case is a common pitfall — document in the
watcher code).

#### What "no install" actually buys us

Tiers 2 and 3 run on **stock Linux, stock macOS, and stock Windows 10+
(Developer Mode recommended but optional)** with no additional
downloads. Everything needed is already in the kernel or the .NET
runtime Dark already ships.

Anything past Tier 3 — FUSE on any platform, ProjFS on Windows —
requires either a driver install, a kernel-extension install, or an
opt-in Windows Feature. Those are out under the embeddable constraint
and get parked as "per-platform optional enhancements" for the
longer-term arc.

### Security

Projecting Dark state to the user's filesystem inherits that filesystem's
security model. Weak spots:

- **Secrets.** If secrets ever project to files, any process the user
  runs can `cat` them. The answer is to not project secrets by default;
  require an explicit mount flag and put them in a separately-permissioned
  subdirectory (0600, user-only).

- **Multi-user hosts.** POSIX uid/gid don't match Dark's identity model
  (which is, currently, "no identity model"). On a shared host, Dark's
  mount is owned by the user; Dark has no enforcement of finer-grained
  access. This is fine for a single-user dev tool; not fine for a
  hosted multi-tenant service.

- **Link following.** If the projection includes symlinks to external
  files (unlikely but worth stating), the projection can be tricked
  into exposing things outside Dark's control. Either forbid follow-
  links or realpath-sanitize on read.

- **fsync lies.** The projection necessarily lies about fsync durability
  (an `fsync()` on a projection file means "flush the op queue," not
  "force data to platter"). Document it; don't deploy Dark as the
  storage layer for software that depends on POSIX durability.


---

## Prior art, and what each suggests

### `/proc` (Linux)

Purely virtual, read-mostly (some writes tune kernel knobs). Files
don't exist on disk; each `read()` calls a kernel handler that formats
current state. File sizes are synthetic or zero; mtime is mostly
meaningless.

**Lesson:** *virtual filesystems where reads dominate are easy*. Dark's
analog is the read-only parts of the projection (log, derived,
pinned-traces). `/proc`'s approach suggests those don't need to be
backed by on-disk files — just compute on `read()`.

### `git` (working tree + object store)

Git has two worlds: an on-disk working copy (real files) and a
content-addressed object store (blobs in `.git/objects/`). The working
copy is a projection of a specific tree-ish from the object store.
Users edit the working copy; `git add` hashes and deposits into the
object store; `git commit` makes the change permanent.

**Lesson:** *the working-copy / object-store split maps cleanly onto
projected-directory / package-tree*. Dark's package store is the object
store; the projected directory is the working copy; `dark fs sync` is
the `git add` equivalent. The mental model users already have from git
transfers directly.

Mild difference: git objects are blobs the user pre-shaped; Dark items
are parsed content, so Dark's `git add` has to parse-and-validate, not
just hash.

### Sapling / EdenFS (Meta)

Virtual filesystem for massive monorepos. Lazy materialization via:
- **Linux:** FUSE.
- **macOS:** NFS loopback (EdenFS runs a local NFS server; the kernel
  mounts it as if it were a remote filesystem).
- **Windows:** ProjFS.

Content is never fully materialized on disk; `open()` fetches on
demand. Commands like `rg` work because `readdir` returns synthetic
entries with their real metadata, and file open populates content.

**Lesson:** *if you commit to Tier 4, lazy materialization is the
difference between tolerable and painful on large trees*. And NFS
loopback is a real pattern for macOS. If Dark goes to Tier 4, this
is the reference architecture.

### Nix store (`/nix/store`)

Content-addressed, immutable, symlinked from human-readable locations.
Each package is a directory whose path encodes its hash. Symlinks
like `/run/current-system/sw/bin/python` point into the store.

**Lesson:** *content-addressed immutable store + human-readable path
via symlinks* is exactly the shape Dark's package tree wants if we
ever go hybrid. Content-addressed storage under `.dark-fs/objects/`,
per-branch directories full of symlinks (or hardlinks) to the
content-hashed files. Branch switch = swap symlinks, O(1).

### Plan 9's `/dev`, `/net`, `/proc`

"Everything is a file" taken seriously. A TCP connection is a file you
open and write to. A window is a directory with control files.

**Lesson:** *the interesting design choice is what to file-ify, not
how*. Plan 9 file-ifies everything because it has a unified protocol
(9p). Dark doesn't need to go that far; it needs to file-ify the
things consumers already think of as files and leave the rest as CLI
verbs.

### `git-annex`

Large binary files stored out-of-band; the git tree contains symlinks
into a content-addressed store. Files look normal to the user; the
actual bytes live somewhere the user doesn't think about.

**Lesson:** *opaque content under addresable paths is a viable pattern
when the user doesn't need to see the payload*. For Dark, this is how
pinned traces could be exposed: a file at `traces/pinned/parseJson-
happy-path.json` that's really a symlink to the trace-subsystem's
storage.

### FUSE-based S3 mounts (`s3fs`, `goofys`, `rclone mount`)

Expose object storage as a filesystem. Famously fiddly: fsync
semantics are wrong, renames are non-atomic, random writes are
expensive, everything is "eventually consistent." Widely used anyway
because having `cat` and `ls` work is enough for most tools.

**Lesson:** *you can get enormous usability wins from a filesystem
that lies about POSIX semantics, as long as it lies consistently and
documents what it lies about*. Dark's projection will lie about
fsync, inode stability across branch switch, and mtime monotonicity.
Acceptable if those lies are named.

### Syncthing / Unison / `rsync`

Two-way sync between authoritative sources. Deals with conflict
explicitly: concurrent edits on both sides become a marked conflict,
not an overwrite.

**Lesson:** *two-way sync's hard part is conflict surface and user-
facing resolution*. Dark's projection-sync model should look at
Unison's reconciliation UI — it's a solved-enough problem that
reinventing it is wasteful.

### VSCode Virtual File System API

VSCode lets extensions register VFS providers under custom schemes
(`dark://packages/Darklang/Stdlib/List/map.dark`). Works entirely
in-editor — shell tools don't see it.

**Lesson:** *in-editor-only VFS is useful but doesn't address the
shell/agent use case*. Real projection still needed. But a VSCode
extension could be the "editor integration" shim that uses the real
projection underneath, or could be a first step before filesystem-
level work.

### Fossil / Mercurial checkouts

Same working-copy-plus-object-store pattern as git. Mercurial's
`.hg/store` is content-addressed; checkouts render it as files. Fossil
stores an entire repository in a single SQLite file (notable for Dark,
since Dark also uses SQLite) and renders checkouts as regular trees.

**Lesson:** *SQLite-backed VCS with filesystem checkouts is a proven
shape*. Fossil's choice to keep the store in SQLite matches Dark's
architecture exactly; checkouts are a render step, not a mirror.


---

## Recommended first step

Ship **Tier 2** with a clear promotion path to Tier 3.

### What Tier 2 looks like, concretely

```
dark fs open <dir>        # materialize current branch into <dir>
dark fs close <dir>       # clean up (regenerates on next open)
dark fs sync              # pick up external edits + pull in Dark-side changes
dark fs status            # show pending syncs, drafts, conflicts
dark fs diff              # show what would be emitted as ops
```

**Layout** (hybrid annotation approach, git-worktree-style branches):

```
<dir>/
  branches/
    main/
      packages/
        Darklang/
          Stdlib/
            List/
              map.dark              # pretty-printed source w/ frontmatter for description
              map.deprecation.yaml  # present when deprecated
              map.stability.yaml    # present when stability set
              ...
      canvases/
        my-app/
          handlers/
          dbs/
      traces/
        pinned/
          ...
      branch.log
    feature-x/
      ...
  current -> branches/main
  .dark-fs/
    manifest.json         # path → hash table for differential sync
    drafts/               # unparsed writes
```

**Materialization.** On `dark fs open`, enumerate every item in the
current branch, pretty-print it, write it to the corresponding path,
record `(path, hash)` in `manifest.json`. Do the same for every active
branch. Parallelize the pretty-print pass.

**Sync protocol.**
- `dark fs sync` walks the projection, diffs against `manifest.json`,
  and for each changed file: parse the new contents, compute the new
  hash, emit an `AddFn`/`AddType`/`AddValue` + `SetName` as WIP ops.
- For content that fails to parse, create a `.dark-fs/drafts/<path>`
  entry and leave the on-disk file alone. `dark fs status` shows
  "drafts: 1" so the user knows.
- For items that disappeared in Dark (renamed/deleted from another
  session), overwrite the projection and note in `dark fs status`.
- Conflicts (local write + Dark-side change to the same location)
  surface as `.yours`/`.theirs`/`.base` sidecars. User resolves by
  editing and removing the sidecars.

**Writes back only.** Annotations sidecars (`.deprecation.yaml`) on
write produce `Deprecate`/`Undeprecate` ops. Canvas files do their
kind-specific equivalent. Trace files are read-only.

### What this gets us

- Editors, `rg`, `fd`, `jq`, AI agents all work. No plugins, no
  platform-specific integration.
- The SCM discipline stays in the CLI (`status`, `commit`, `discard`).
  Writes land as WIP, not as phantom commits. Users keep the mental
  model they already have.
- No FUSE, no platform forking, no user-side install on any of Linux,
  macOS, or Windows. Everything ships inside the Dark binary.
- Branch switches are a symlink flip (Linux/macOS, Windows with
  Developer Mode) or a junction flip (Windows without), or a read of
  `.dark-fs/current-branch` on the safest-fallback path.
- Clear promotion path: a file watcher turns this into Tier 3 using
  native per-OS APIs (inotify/FSEvents/ReadDirectoryChangesW), still
  embeddable. Tier 4 (FUSE/ProjFS) becomes a platform-specific
  optional enhancement later, not a shared requirement.

### What it doesn't get us

- Editors don't see Dark-side changes until `dark fs sync` runs. With
  a watcher (Tier 3), closer to real-time. Without one, batchy.
- `fsync` lies silently. Probably fine for dev work, worth a warning
  in docs.
- Huge trees take O(tree size) to first materialize. EdenFS-style lazy
  materialization would fix this; Tier 2 doesn't.
- Running apps can't use the projection as a live filesystem. That's
  Tier 5 and out of scope.


---

## Longer-term arc

After Tier 2 is stable and being used. All steps 1–3 stay within the
embeddable-on-every-platform constraint; steps 4+ are per-platform
enhancements that require user install, only undertaken if the Tier
2/3 experience proves too weak.

1. **Tier 3: file watcher.** Use `System.IO.FileSystemWatcher` (or
   equivalent) so inotify/FSEvents/ReadDirectoryChangesW are driven by
   one code path. Eliminate the explicit `sync`. Biggest usability
   jump per unit work. Still embeddable, still no install.
2. **Annotation frontmatter UX polish.** Descriptions probably belong
   inline; decide whether deprecation/stability do too. Depends on
   what editing annotations-in-files actually feels like.
3. **Backing store hardlinks.** When branch × tree size starts hurting
   disk, adopt Nix-store-style content-addressed backing (per-hash
   files under `.dark-fs/objects/`, per-branch dirs full of hardlinks).
   Branch switch becomes O(1) inode flips instead of symlink flips.
   Supported on Linux (ext4+), macOS (APFS), Windows (NTFS). Still
   embeddable.
4. **A real draft-source op kind.** Formalize `DraftSource` as a PT
   op variant so unparsed writes have a real home. No platform
   implications.
5. **Per-platform kernel VFS (optional, each requires user install).**
   These only ship if Tier 3 is clearly too slow or too weak, and each
   is its own decision:
   - **Linux: FUSE.** libfuse is almost-always present; some distros
     need `fuse` / `fuse3` package. Opt-in per-install.
   - **Windows: ProjFS.** Built into Windows 10 1809+; may need to
     enable the feature on some SKUs. Opt-in per-user.
   - **macOS: no good option.** macFUSE requires kext + reboot;
     NFS-loopback (EdenFS pattern) is a heavier build. Probably stay
     on Tier 3 indefinitely on macOS unless EdenFS-style work becomes
     strategic.
6. **VSCode extension on top of Tier 2/3.** Not strictly needed
   (VSCode sees the projected dir already), but a light extension
   could add branch-switcher UI, conflict resolver, live dep graph
   overlays, etc. Works everywhere VSCode works.


---

## Open questions

1. **One file per item vs one file per module.** Per-item is what
   Dark's internals suggest; per-module is what humans often want to
   read. Supporting both (per-module as a synthetic concatenation)
   adds code but is probably the right answer.

2. **Frontmatter vs sidecar for annotations.** Lean toward frontmatter
   for description (conceptually part of the code, shows up inline
   where it belongs), sidecar for structured annotations (deprecation,
   stability). Test with real users before committing.

3. **Branch representation.** Worktree-style directories are the safer
   bet; single-tree-with-switching is simpler but breaks path caches.
   Users who hate the worktree shape can always `cd branches/current-
   branch-name/`.

4. **What's the conflict UX?** Sidecar files (`.yours`/`.theirs`) vs
   inline markers (`<<<<<<`). Inline markers are more familiar; sidecar
   files are clearer about structure. Maybe both, toggled.

5. **Should annotations be editable via files at all?** The alternative
   is "annotations are CLI-only; source projection is files." Editor-
   in-annotations is nice for descriptions; maybe unnecessary for
   deprecation-as-a-tiny-yaml.

6. **Do we expose WIP as files too?** One option: `packages/...`
   shows committed state; `wip/packages/...` overlays unstaged changes.
   Editors work on `wip/`; `commit` promotes. This is more legible
   than the single-tree approach where every edit silently lands in
   WIP. Consider.

7. **Trace pinning through files.** Should `cp traces/recent/foo.json
   traces/pinned/` be the pin action? Clean UX but means the FS write
   triggers an SCM op (new `example_trace_of` relationship row).
   Slippery slope for letting other `cp`/`mv` operations mean things.
   Probably safer: pinning stays CLI-only; filesystem is observation.

8. **Search over the projection.** `rg` over `packages/` is what users
   will reach for. That's fine, but Dark's own search knows about
   types, signatures, locations. The CLI `search` is richer than `rg`;
   agents that hit `rg` first will miss structured queries. Documenta-
   tion matters more than implementation here.

9. **How does `git` interact?** Putting the projected directory in a
   git repo is tempting (and it'll mostly work). But then you have
   two SCMs fighting over one tree. Probably advise: don't commit the
   projection to git; use Dark's own SCM. If you want a git export,
   that's a separate `dark fs git-export` verb.

10. **Identity for renames in Git-observed projections.** If someone
    *does* commit the projection to git, git's rename detection is
    heuristic (content similarity). Dark knows renames precisely.
    Bridging that — emitting a "rename" hint that git can pick up —
    is possible (recent git versions honor move-detection hints) but
    needs investigation.

11. **Permissions after ACLs land.** Mode bits aren't a long-term
    answer. Once Dark has a real identity/ACL model, the projection
    should reflect it: files the caller can read are readable, files
    they can't are 000 or absent. Design the permissions surface with
    this in mind.

12. **What about *writing* new files.** Creating a new file at
    `packages/Foo/bar.dark` means "create a new package item at FQN
    `Foo.bar`." That's an `AddFn` + `SetName`, straightforward. But
    what if the user creates a file that doesn't parse? Draft. What if
    they create a file in a location that already has a different
    item? Conflict. What if they create a directory first? Empty
    module — Dark has to decide if empty modules are a thing (they
    probably are now).

13. **Editor write-back dances.** VSCode's default save writes to
    `foo.dark` directly. Vim's `:w` does the rename-over dance. Emacs
    writes backup files. Each has a slightly different pattern. The
    sync watcher needs to handle all of them; the test suite should
    include "did a real editor write trigger the right op?" scenarios.

14. **A real use case test.** Early on, pick a specific workflow that
    doesn't work today and drive the projection at it: "Claude Code
    opens a Dark package item, refactors it, saves, commits." If the
    first round of Tier 2 doesn't make that workflow noticeably
    better than the current CLI-only path, the design is wrong.

15. **Windows-without-Developer-Mode baseline.** Decide whether the
    default Tier 2 layout degrades to "no symlinks at all" (for users
    on stock Windows without Developer Mode) or prompts them to turn
    Developer Mode on. Degrading is more portable; prompting is a
    better experience once they do. Probably: degrade silently, show
    a one-time hint that Developer Mode improves the branch UX.

16. **macOS case-insensitive-by-default trap.** The portability code
    handles name collisions, but the default APFS volume is
    case-insensitive. Someone creating a new item whose name differs
    only in case from an existing one will hit a cryptic error. The
    UX around this ("that name is too close to an existing one on
    macOS/Windows — please choose one that differs by more than
    case") needs to be friendly.

17. **Cross-platform test fixtures.** The test suite for the
    projection has to run on all three platforms, because almost
    every portability bug only appears on one. CI matrix the projection
    tests across Linux, macOS, Windows from the start; retrofitting
    is painful.
