# CLI NativeAOT + cold-start — session progress report

**Date:** 2026-05-12
**Branch:** `aot-and-cold-start-improvements` (14 commits ahead of `main`)
**Scope:** Tried to make AOT the publish path. Discovered the cross-OS
blocker; pivoted back to JIT. Took the size/perf/correctness wins on the
way out. AOT infrastructure stays in tree for the day multi-OS CI lands.

---

## Headline numbers (linux-x64)

| Metric                         | Pre-session         | End of session     | Delta             |
|--------------------------------|---------------------|--------------------|-------------------|
| Shipping path                  | JIT only            | **JIT (AOT parked)** | unchanged form  |
| JIT binary size                | 46–77 MB            | **46 MB**          | hovering at low  |
| JIT cold start                 | ~5.5 s              | **2.4 s**          | **−57%**          |
| JIT warm start                 | ~0.5 s              | ~0.5 s             | unchanged        |
| AOT binary size (parked)       | n/a                 | **27 MB**          | proves the path  |
| AOT cold start (parked)        | n/a                 | **1.5 s**          | −73% vs old JIT  |
| `seed.applyOps` phase          | 5.25 s              | **1.18 s**         | **−4.4×**         |
| AOT build warnings             | hidden behind rollup | 49 (all FSharp.Core, visible) | now actionable |
| Float ops under AOT            | crashed             | works              | sprintf fix       |
| FK integrity                   | silent corruption under AOT | verified clean | Guid→TEXT + check |

Two genuine bugs surfaced and fixed along the way — both were *latent
under JIT*; we only hit them faster because we tried AOT. The fixes
ship in the JIT path too.

---

## 1. The cross-OS blocker

Microsoft's docs are unambiguous:

> "Native AOT does not support cross-OS compilation. Cross-OS
> compilation with Native AOT requires some form of emulation, like a
> virtual machine or Windows WSL."
> — [cross-compile](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/cross-compile)

Same-OS cross-arch (linux x64↔arm64, etc.) is fine and we *could* light
up 4 of our 8 RIDs. Cross-OS (linux→osx, linux→win) needs runners on
those hosts.

[PublishAotCross](https://github.com/MichalStrehovsky/PublishAotCross)
(third-party) bridges Windows→Linux but explicitly cannot do →Windows
or →macOS due to MSVC/MinGW ABI differences.

**Practical implication:** the AOT branch's `--aot` flag works for
`linux-x64` today; the other unix RIDs are doable with toolchain bumps;
osx/win cannot be done from our Linux devcontainer at all.

---

## 2. SQLite static-linked into the AOT binary

Pre-session: AOT binary crashed at the first SQL call with
`DllNotFoundException: libe_sqlite3.so`. Picked the static-link path:
"we're not looking to poison the user's filesystem."

### What we built

**`scripts/build/build-sqlite.sh`** (revived from "not currently used")
— fetches the SQLite 3.46.0 amalgamation (SHA256-pinned) and
cross-compiles a static archive per target runtime via `zig cc`:

```
linux-x64       linux-musl-x64    linux-arm64    linux-arm
osx-x64         osx-arm64
```

Output: `backend/src/Cli/lib/libe_sqlite3-<rid>.a` (~3.6–5.2 MB each).
Gitignored — built locally / in CI on demand. The script's own
"all artifacts present" check makes it a cheap cache miss.

Flags:

```
-Os -fPIC -DSQLITE_THREADSAFE=1 -DSQLITE_DEFAULT_MEMSTATUS=0 -DSQLITE_DQS=0
-DSQLITE_ENABLE_COLUMN_METADATA       # SQLitePCL binds these unconditionally
-DSQLITE_ENABLE_MATH_FUNCTIONS
```

**`Cli.fsproj`** — under AOT-conditional ItemGroups:

```xml
<ItemGroup Condition="'$(PublishAot)' == 'true'">
  <DirectPInvoke Include="e_sqlite3" />
</ItemGroup>
<ItemGroup Condition="... '$(RuntimeIdentifier)' == 'linux-x64'">
  <NativeLibrary Include="lib/libe_sqlite3-linux-x64.a" />
</ItemGroup>
... (one per RID)
```

### Gotchas — and what to do about each

1. **`OMIT_LOAD_EXTENSION` breaks the AOT link.** SQLitePCLRaw binds
   `sqlite3_load_extension` + `sqlite3_enable_load_extension`
   unconditionally. **What to do**: keep extension loading compiled in;
   it's disabled at runtime by default. ~50 KB cost.

2. **`ENABLE_COLUMN_METADATA` is required** (off by default in stock
   SQLite). **What to do**: keep `-DSQLITE_ENABLE_COLUMN_METADATA`.
   Same reason; same cost.

3. **`OMIT_AUTOINIT` causes a segfault** on first `sqlite3_open`.
   SQLitePCL relies on SQLite's default auto-init-on-first-API-call.
   **What to do**: don't suppress AUTOINIT. To recover the ~5 KB you'd
   need to inject an explicit `sqlite3_initialize()` somewhere; not
   worth the wiring.

4. **`tee | tail -1` hides linker failures.** The pipe exit code is
   `tail`'s, not `dotnet publish`'s. **What to do**: always
   `set -o pipefail` around `dotnet publish` invocations.

### Bonus brainstorm — shrinking the `.a` further (not actioned)

- `-Oz` instead of `-Os` — usually 1–3% smaller.
- More `SQLITE_OMIT_*` candidates exist (TRIGGER, VIEW, SUBQUERY,
  WINDOWFUNC) — but each removes a symbol that SQLitePCL's
  `[DllImport]`s reference unconditionally, so removing them breaks the
  AOT link. **The real lever is a SQLitePCLRaw fork** that drops the
  unused `[DllImport]`s; otherwise the `OMIT_*` choices are constrained
  by what the provider binds. Not worth the fork for ~0.5–1 MB savings.

### Cross-compilation status

```
linux-x64        ✅  built + smoke-tested + AOT-published + JIT-validated
linux-musl-x64   ✅  archive built; AOT publish not tried
linux-arm64      ✅  archive built; needs nativeaot runtime nuget pack
linux-arm        ✅  archive built; needs runtime nuget pack
osx-x64          ✅  archive built; AOT publish impossible from Linux
osx-arm64        ✅  archive built; AOT publish impossible from Linux
win-x64          ❌  archive not built (`.lib`); cross-OS blocker anyway
win-arm64        ❌  same
```

### What about the bundle's `.so`?

Microsoft.Data.Sqlite → `SQLitePCLRaw.bundle_e_sqlite3` transitively
drops `libe_sqlite3.so` into the AOT `publish/` dir. **Per your
direction, leaving it.** Doesn't reach the user — build script moves
only the bare binary into `clis/`.

---

## 3. Trimming + AOT subsystem switches

### Re-added settings that the squash dropped

```xml
<IlcGenerateStackTraceData>false</IlcGenerateStackTraceData>
<IlcFoldIdenticalMethodBodies>true</IlcFoldIdenticalMethodBodies>
```

Per `1b32e0827`. Lost in the squash; restored. AOT-only effect.

### New .NET subsystem switches

CLI doesn't hot-reload, doesn't run startup hooks, doesn't use built-in
COM, isn't macOS-Cocoa, etc.:

```xml
<MetadataUpdaterSupport>false</MetadataUpdaterSupport>
<StartupHookSupport>false</StartupHookSupport>
<BuiltInComInteropSupport>false</BuiltInComInteropSupport>
<AutoreleasePoolSupport>false</AutoreleasePoolSupport>
<EnableUnsafeBinaryFormatterSerialization>false</EnableUnsafeBinaryFormatterSerialization>
<EnableUnsafeUTF7Encoding>false</EnableUnsafeUTF7Encoding>
<CustomResourceTypesSupport>false</CustomResourceTypesSupport>
```

Each prunes metadata + code from AOT publish; no effect on JIT.

### Warning visibility

Pre-session: `SuppressTrimAnalysisWarnings=true` — all warnings hidden
under one IL3053 rollup.

Now: `SuppressTrimAnalysisWarnings=false` + `TrimmerSingleWarn=false`.
49 warnings visible. **All originate inside FSharp.Core itself**
(`sformat.fs` 21, `printf.fs` 13, `reflect.fs` 13, `prim-types.fs` 1, +
1 from the FK-fix). Zero in our code.

```
IL2070 (14)  DAM annotation mismatch on `this`
IL3050 (9)   RequiresDynamicCode on a reachable method
IL2067 (9)   DAM on a parameter
IL2060 (5)   MakeGenericMethod with no static type info
IL2075 (5)   GetType() with no DAM annotation
IL2072/91/80/55 — smaller categories
```

Most are pessimism the trimmer can't reason through. The dangerous ones
are where our code happens to *call into* the flagged path. **Visibility
is load-bearing**: a new warning in CI is a signal to investigate
whether it's pessimism or a real landmine.

We hit one such landmine (§6). Without unsuppressing we'd never have
known to look.

### Warning hygiene (NU1510)

28 pre-session warnings were `NU1510` ("PackageReference X will not be
pruned") from transitive deps that the net10.0 shared framework now
provides. Suppressed globally via `Directory.Build.props`.

---

## 4. Cold start: 5.6 s → 1.5 s (AOT) / 2.4 s (JIT)

### Before

```
seed.applyOps        5252 ms   ← 97% of the cost
seed.generateRefs      24 ms
seed.evaluateValues   109 ms
seed.walCheckpoint      7 ms
─────────────────────────────
total                5551 ms
```

9845 ops × 0.53 ms/op. Bottleneck was Fumble's per-statement
connection-from-pool model: each of ~20k SQL statements ran on a fresh
connection in its own implicit transaction. ~20k tiny WAL commits.

### What changed

**Rewrote `LibDB/PackageOpPlayback.fs` against raw
`Microsoft.Data.Sqlite`** (no Fumble). All apply* helpers take a `Ctx`
bundling `SqliteConnection` + a `Dictionary<string, SqliteCommand>`
prepared-statement cache.

**`Seed.applyUnappliedOps`** opens one connection, sets
`PRAGMA synchronous=OFF` + `PRAGMA foreign_keys=OFF` for the duration
(standard SQLite bulk-load practice), wraps in one BEGIN/COMMIT, runs
all op-groups, then re-enables FK enforcement and runs
`PRAGMA foreign_key_check` — failing loudly if any dangling refs
slipped through.

### Prepared-statement cache

First call to a given SQL → build + `Prepare()`. Subsequent calls clear
the parameter collection and reuse the same command. Saves ~70 ms
(1251 → 1184 in `applyOps`). Smaller than hoped — SQLite's internal
C-side statement cache was already handling SQL-plan reuse.

### Batched dep inserts — tried, reverted

Collected `updateDependencies` calls into per-batch chunks (DELETE all
affected hashes + 1024-row chunked INSERT). Math suggested 20k SQL
statements → 20. **Actual result: 1184 ms → 2132 ms.** The new code
path is dominated by 6 KB SQL strings, `Parameters.Clear()` on
6144-param collections, and 6144 `AddWithValue` calls per chunk. The
per-item path's small parameter sets and SQLite's internal cache beat
the naive batched approach.

Reverted. If revisited: cache the chunk-size-N SQL once at module init,
use parameter slot reuse (`Parameters[N].Value`), try smaller chunks
(64 or 256).

### Final telemetry (AOT)

```
seed.applyOps        1184 ms   ← 4.4× faster
seed.generateRefs      21 ms
seed.evaluateValues   101 ms   (untouched)
seed.walCheckpoint     15 ms
─────────────────────────────
total                1494 ms
```

The remaining 1.18 s in `applyOps` is real work: deserializing 9845 op
blobs, computing hashes, PT→RT translation, raw SQL execution.

---

## 5. Package drops — 4 MB off the binary

A grep audit found **zero `open FSharpPlus`** anywhere in the backend,
despite the package being listed in 10 `paket.references` files. Same
for `FSharpx.Extras`. `FSharp.SystemTextJson` +
`NodaTime.Serialization.SystemTextJson` were stale carryovers from
when `Json.Vanilla` existed.

9 callsites used qualified names — all trivially replaced:

```
Prelude/Dictionary.fs        tryGetValue/keys/values → TryGetValue + .Keys + .Values
Prelude/Map.fs               Map.union (×2)            → Map.fold (...) ...
Prelude/ResizeArray.fs       iter/map/toList/toSeq     → for-loop / List.ofSeq / cast
Builtins.Pure/Libs/String.fs intersperse               → 4-line inline
```

Then a separate audit caught `FsRegEx` + `System.Diagnostics.DiagnosticSource`
listed in `paket.references` with zero callsites. Already trim-removed
from the binary; cleaning the manifest is tidy.

**Size impact**: 31.4 MB → 27.1 MB (AOT, **−14%**). The wins carry over
to JIT (smaller IL → less ReadyToRun expansion).

---

## 6. The two bugs we found by accident

Both were latent in JIT — we just hit them faster under AOT.

### sprintf "%.Ng" PrintfImpl crash on Float ops

Found by smoke-testing AOT:

```
NotSupportedException: MakeGenericMethod_NoMetadata,
  Microsoft.FSharp.Core.PrintfImpl+Specializations`3
    [Unit,String,String].CaptureFinal1[System.Double](...)
```

3 sites, all using `sprintf "%.Ng"` to format Float→string:

```
Builtins.Pure/Libs/Float.fs    Stdlib.Float.toString
Builtins.Pure/Libs/Json.fs     Stdlib.Json.serialize on DFloat
LibSerialization/DvalReprInternalQueryable.fs
```

`sprintf "%.Ng"` compiles to FSharp.Core PrintfImpl, which uses
`MakeGenericMethod` on `Double` at runtime — Native AOT can't satisfy
that once the type's metadata is trimmed. Replaced each with
`f.ToString("GN", InvariantCulture)`. Same canonical output, zero
reflection.

Would have been a latent JIT bug too if AOT had ever been turned on.
Now fixed for both paths.

### Guid bound as BLOB instead of TEXT → broken FK constraints

Found by JIT smoke-testing post-pivot. SQLite error 19, FK constraint
failed during `growIfNeeded`.

Root cause: `cmd.Parameters.AddWithValue("$branch_id", branchId)` where
`branchId` is `System.Guid`. Microsoft.Data.Sqlite's default type
mapping for Guid is **BLOB(16)**, not the canonical text representation.
Our schema stores branch_id (and location_id, deprecation_id) as TEXT.
SQLite's FK check compared 16-byte BLOB against the TEXT-stored
`'89282547-...'` UUID, never matched, raised.

**The AOT binary silently succeeded with this bug** because
Microsoft.Data.Sqlite's `Foreign Keys=True` default (set via the
connection-string parser) was apparently being trim-stripped — so AOT
skipped FK enforcement entirely and persisted ~5000 dangling
location→branch refs. The trimmer was *masking* the bug; JIT exposed
it.

Fix: a `pUuid` helper that binds `.ToString()` instead of the raw Guid
(matches what Fumble's `Sql.uuid` did internally). 14 callsites
converted. Plus `foreign_key_check` in `Seed.fs` now fires after every
grow, so this category of bug can't silently persist again.

---

## 7. JIT size breakdown (companion: cli-jit-size-breakdown-2026-05-12.md)

Detailed in the companion doc. Summary:

```
Total shipped JIT single-file       46 MB
Unbundled publish/                  51 MB (no .dbg)

Where it goes (unbundled, % of non-debug):
  .NET native runtime               19.6 MB   38%   libcoreclr/clrjit/etc.
  Our F# code (incl embedded seed)   9.3 MB   18%   3.6 MB is seed; ~6 MB code
  System.Private.CoreLib            5.7 MB    11%
  LibTreeSitter.dll                 4.7 MB    9%   embeds ALL RIDs' tree-sitter
  Other .NET framework DLLs         4.5 MB    9%   ~30 small files
  System.Private.Xml                1.5 MB    3%
  libe_sqlite3.so                   1.3 MB   2.5%
  NodaTime                          1.2 MB   2.3%
  Crypto                            1.1 MB    2%
  F# Core + Ply                     0.7 MB   1.5%
```

**Single biggest lever for JIT size beyond what we've done:**
`LibTreeSitter.dll` embeds all 9 RIDs' tree-sitter native libs as
resources. Making that RID-conditional would shave 3–4 MB. Cheap fix.

Second-biggest: the embedded `data.db.gz` is 3.6 MB. Could be ~2.5 MB
with zstd-19 instead of gzip-9.

---

## 8. AOT vs JIT — broad numbers

Recording the order-of-magnitude differences:

| Dimension | AOT | JIT | Note |
|---|---|---|---|
| Cold start | 1.5 s | 2.4 s | AOT 2–5× faster generally; ours is bound by grow |
| Warm start | 0.21 s | ~0.5 s | AOT wins on small invocations |
| Steady-state throughput | -0–20% | baseline | JIT can profile-guided-optimize; AOT can't |
| Memory at startup | -20–40% | baseline | No JIT working set |
| Binary size | 27 MB | 46 MB | ~40% smaller |

**For our CLI specifically:** the warm-start gap (-0.3 s) is what users
would feel most — ~100 invocations/day = 30 s saved. The cold-start gap
is small in absolute terms because both paths spend most time in
`growIfNeeded`.

---

## 9. Strategic recommendations — next moves

### (a) Mature `--aot` into a per-RID mode-picker

The flag exists. Next iteration: have the script *auto-select* the
right mode per RID rather than all-or-nothing.

```
default     per-RID best available (mix of AOT + JIT)
--all-jit   force JIT everywhere (today's behavior, kept for legacy)
--all-aot   force AOT everywhere (fails on osx/win from Linux)
--aot       only target RIDs that can AOT from current host
```

Per-RID matrix today (Linux-host):

```
linux-x64       AOT ✅ proven
linux-arm64     AOT — needs `Microsoft.NETCore.App.Runtime.nativeaot.linux-arm64` nuget
linux-musl-x64  AOT — zig may unblock the musl-cross failure from 1b32e0827
linux-arm       AOT — needs the runtime nuget
osx-x64         JIT — cross-OS not supported
osx-arm64       JIT — cross-OS not supported
win-x64         JIT — cross-OS not supported
win-arm64       JIT — cross-OS not supported
```

Cost: ~30 lines in `build-release-cli-exes.sh` + 1–2 paket entries +
testing each linux variant. **~hour of work.** Lights up 4 of 8 RIDs on
AOT.

### (b) Multi-OS CI — when and how

**Yes, you eventually need a Mac for code signing.** Apple Developer ID
Application cert ($99/yr) + `codesign` + `notarytool` only run on
macOS. Pre-existing TODO in `vault/.../Code-Signing.md` (issue #5307):
without it, macOS users get "killed: zsh" on download.

Options for adding macOS to CI:

1. **CircleCI macOS executor** — `macos: xcode: 16.x`. Native arm64
   since 2024. ~7× the per-minute cost of Linux. Rough budget:
   $50–200/month depending on PR volume. Simplest if you stay on
   CircleCI.

2. **GitHub Actions** — free macOS runners for public repos; cheaper
   paid tier for private. Strong multi-OS story is GHA's specialty.
   Migration from CircleCI is a non-trivial one-time cost but probably
   the right long-term move.

3. **Self-hosted Mac runner** — a Mac mini pointed at CircleCI/GHA.
   ~$600 one-time + minor maintenance. Viable if you already have one.

Once you have macOS CI, you get **macOS AOT publish for free** as a
side effect — and you've already solved the signing problem.

Windows CI is the same shape but ~3× Linux cost on CircleCI. Lower
priority — Windows users will accept "downloaded a big JIT binary"
more readily than macOS users will accept "binary, Gatekeeper killed
it."

### (c) Order of operations I'd suggest

1. **Land the mode-picker** (~1 hr). Lights up 4 linux AOT variants
   from the existing devcontainer. Validates cross-arch AOT without
   any CI changes.
2. **Decide on macOS CI** (decision, not work). Commit to the spend,
   or accept that macOS users live with JIT + manual codesign
   workarounds for the foreseeable future.
3. **If yes to macOS CI** → set up signing first (cert, keychain,
   notarytool config), then enable macOS AOT as a bonus.
4. **Windows AOT** stays deferred unless there's a specific
   user-visible reason to act.

---

## 10. Files changed on this branch

```
M  .circleci/config.yml                              (reverted to JIT)
M  .gitignore                                         (libe_sqlite3-*.a)
M  backend/Directory.Build.props                      (NoWarn NU1510)
M  backend/paket.dependencies + paket.lock            (4 packages dropped)
M  10 × backend/src/.../paket.references              (cleanup)
M  backend/src/Cli/Cli.fsproj                         (AOT items, subsystem switches)
M  backend/src/Prelude/{Dictionary, Map, ResizeArray}.fs  (inline replacements)
M  backend/src/Builtins/Builtins.Pure/Libs/{String, Float, Json}.fs  (sprintf fix)
M  backend/src/LibSerialization/DvalReprInternalQueryable.fs  (sprintf fix)
M  backend/src/LibDB/PackageOpPlayback.fs             (raw Sqlite + pUuid)
M  backend/src/LibDB/Seed.fs                          (one-tx grow + FK check)
M  scripts/build/{build-sqlite.sh, build-release-cli-exes.sh}
?? thinking/cli-aot-progress-2026-05-12.md            (this doc)
?? thinking/cli-aot-status-2026-05-12.md              (earlier snapshot)
?? thinking/cli-jit-size-breakdown-2026-05-12.md      (size companion)
```

## 11. Commit ledger

```
2ef0a59ad  ci: revert to JIT publish; add JIT size-breakdown doc
813d526c3  fix: bind UUIDs as TEXT, not BLOB, in raw-Sqlite playback
83418522a  docs: comprehensive progress report (superseded by this update)
e6a252b10  ci: build SQLite static archives + AOT-publish on PRs   ← reverted in 2ef0a59ad
6873bf210  build: drop unused FsRegEx + DiagnosticSource refs
a41741f97  perf: cache prepared SqliteCommands across the playback batch
a526c12cf  aot: unsuppress trim/AOT warnings so latent issues surface
bd9fb989b  aot: kill sprintf "%.Ng" float formatters — PrintfImpl trips trim
2298832dd  docs: AOT progress + status reports in thinking/
c615e5278  size: drop FSharpPlus / FSharpx.Extras / FSharp.STJ / NodaTime.STJ
a7b6a11b9  perf: rewrite package-op playback against raw Sqlite; one tx for cold start
474593fe0  build: silence NU1510 (unprunable transitive package refs)
375fe5b27  aot: static-link libe_sqlite3 + tighten AOT subsystem switches
064eb0e47  aot: build SQLite static archives via zig cc for static-link
```

---

## 12. Sources

- [SQLite amalgamation 3.46.0](https://www.sqlite.org/2024/sqlite-amalgamation-3460000.zip) (SHA256-pinned in build-sqlite.sh)
- [zig 0.11.0](https://ziglang.org/) — devcontainer at `~/zig/zig`
- [.NET Cross-compilation docs](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/cross-compile) — definitive "no cross-OS AOT" statement
- [SQLitePCL.raw](https://github.com/ericsink/SQLitePCL.raw) — supplies the `[DllImport]`s we satisfy
- [DirectPInvoke + NativeLibrary](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop)
- [PublishAotCross](https://github.com/MichalStrehovsky/PublishAotCross) — third-party, Windows→Linux only
- [pileofhacks SQLite-AOT walkthrough](https://pileofhacks.dev/post/a-native-static-binary-with-sqlite-support-in-c/) — the recipe we adapted
- [Trim analysis warnings reference](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-warnings/)
- Companion docs: `cli-aot-status-2026-05-12.md` (initial snapshot, superseded), `cli-jit-size-breakdown-2026-05-12.md` (size detail)
- Vault: `05.Implementation/CLI/The Executable/Code-Signing.md` (pre-existing TODO on macOS signing), `notes from trying to AOT-build.md` (2024 reconnaissance)
