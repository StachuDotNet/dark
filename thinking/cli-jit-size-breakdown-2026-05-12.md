# CLI JIT-publish — size breakdown report

**Date:** 2026-05-12
**Method:** Publish the CLI with `PublishSingleFile=false` so each
component lands as its own file in `publish/`, then `ls -lh` everything.
Same compile flags as a release JIT publish (`-c Release`,
`PublishTrimmed=true`, `PublishReadyToRun=true`, `--self-contained true`),
just unbundled.

**Target:** linux-x64. Numbers will look similar for the other runtimes
but with platform-specific native libs swapped in.

---

## Top-line numbers

| Form                                            | Size      |
|-------------------------------------------------|----------:|
| Publish dir, including `Cli.dbg` debug symbols  | **117 MB** |
| Publish dir, without `.dbg`                     | **51 MB** |
| Single-file bundled `Cli` binary (shipped)      | **46 MB** |
| For comparison: AOT publish from earlier today  | 27 MB     |

The 51 MB unbundled → 46 MB single-file delta is the bundler's
compression (not much; most assembly code is already tight).
`Cli.dbg` (PDB-equivalent debug symbols) is **never shipped** — it's
build-output noise that gets dropped by `build-release-cli-exes.sh`'s
`mv publish/Cli → clis/` step.

---

## What's in the 51 MB

Grouped by what each thing is, sorted by size:

| Category                                              | MB    | %    | Files |
|-------------------------------------------------------|------:|-----:|------:|
| **.NET native runtime** (libcoreclr, libclrjit, libmscordaccore, libmscordbi, libhostfxr, libhostpolicy, libcoreclrtraceptprovider, libclrgc, libclrgcexp, libSystem.IO.Compression.Native, ...) | 19.6 | 38 % | 12 |
| **Our F# code** (16 DLLs incl. embedded seed inside Cli.dll) | 9.3  | 18 % | 14 + 3 Builtins.Http |
| **.NET `System.Private.CoreLib.dll`** | 5.7  | 11 % | 1 |
| **`LibTreeSitter.dll`** (statically embedded tree-sitter .so) | 4.7  |  9 % | 1 |
| **Other .NET framework DLLs** (60+ of them — System.Collections, System.Linq, System.Net.*, System.Text.*, etc.) | 4.5  |  9 % | ~30 |
| **`System.Private.Xml.dll`** | 1.5  |  3 % | 1 |
| **`libe_sqlite3.so`** | 1.3  | 2.5% | 1 |
| **NodaTime.dll** | 1.2  | 2.3% | 1 |
| **.NET Crypto** (System.Security.Cryptography + transport security) | 1.1  | 2.2% | 3 |
| **F# Core + Ply** | 0.7  | 1.5% | 2 |
| Reflection / Linq / Collections etc. | 0.4  |  1 % | 8 |
| Microsoft.Data.Sqlite + Fumble + SQLitePCLRaw (managed) | 0.3  | 0.7% | 5 |
| `libSystem.Native.so` / `libSystem.Security.Cryptography.Native.OpenSsl.so` | 0.3  | 0.6% | 2 |
| Misc (locale stubs, netstandard shim, runtimeconfig, etc.) | ~0.2  | <1 % | ~15 |

### Our-F#-code breakdown

```
3.67 MB  Cli.dll                ← 3.64 MB is embedded data.db.gz! Real IL: ~30 KB
2.21 MB  LibExecution.dll       ← interpreter + Dvals + ProgramTypes
1.14 MB  Builtins.Pure.dll      ← Stdlib.Int*/String/List/Float/etc.
1.08 MB  LibDB.dll              ← Sqlite, PackageOpPlayback, Seed
0.55 MB  Builtins.Matter.dll    ← DB/Account/Toplevels builtins
0.43 MB  LibSerialization.dll   ← custom-binary serializers
0.26 MB  Builtins.Cli.dll       ← TUI primitives
0.15 MB  Builtins.Http.Client.dll
0.12 MB  Prelude.dll
0.11 MB  Builtins.CliHost.dll
0.10 MB  Builtins.Http.Server.dll
0.05 MB  LibCloud.dll           ← essentially empty post-pivot
0.03 MB  Builtins.{Language,Random,Time}.dll  (each tiny)
0.01 MB  LibConfig.dll
─────────
~9.95 MB total
−3.64 MB embedded data.db.gz (lives inside Cli.dll)
=  6.31 MB of actual managed F# code
```

**Embedded seed**: data.db.gz is currently **3.6 MB**. It dominates
`Cli.dll`. Compressed seed db is the single biggest single thing we
ship that isn't framework code.

---

## Where the levers are

In rough order of effort × payoff:

### 1. .NET native runtime (~20 MB) — mostly fixed-cost

`libcoreclr.so`, `libclrjit.so`, `libmscordaccore.so`, etc. are the
JIT runtime itself. With `PublishTrimmed=true` + `--self-contained
true`, .NET 10 already trims unused features here. To shave more
you'd need:
- `PublishReadyToRun=false` (smaller binary, slower cold start)
- Custom CoreCLR build (massively complex, not worth it)
- Native AOT (which is exactly what we just stepped back from for
  cross-OS reasons)

Going from JIT to AOT here saved 20 MB → ~10 MB compressed in the
27 MB AOT binary; that's the *biggest* win in switching publish
modes, and the only practical path to it is AOT.

### 2. Embedded `data.db.gz` (3.6 MB) — application-level lever

Single biggest non-framework chunk. Three approaches:

- **Smaller seed**. Today's seed has all packages
  (Darklang/Feriel/Stachu) including UI components, demo data, JSON-RPC,
  LSP, etc. A `darklang-core`-only seed (just `Darklang.Stdlib.*`)
  would be a fraction of the size. Trade: users without those
  packages on first run would need to fetch them. Could combine with
  network-on-demand package fetch.
- **Heavier compression**. data.db itself is 12 MB; gzip -9 gets to
  3.6 MB. zstd -19 typically beats gzip by 20-30% on databases
  (would shave ~1 MB). Trade: add zstd dependency.
- **Ship empty seed, populate on first run from network**. Trade:
  first run needs network; offline first-run breaks.

### 3. `LibTreeSitter.dll` 4.7 MB — embedded native libs

This DLL is a thin F# wrapper over the tree-sitter C library, but its
`.dll` size includes embedded tree-sitter .so/.dylib/.dll variants for
all 9 runtimes (because of how `LibTreeSitter.fsproj` packages them as
`EmbeddedResource`). At publish time, only one runtime's variant is
actually needed — but the build embeds all of them.

**Lever**: make the embedded-resource list runtime-conditional in
`LibTreeSitter.fsproj` so only the publish-target's tree-sitter blob
ships. Probably 3-4 MB saved on the bundled binary.

### 4. `System.Private.Xml.dll` 1.5 MB — possibly extractable

XML support is reachable for various reasons (XmlSerializer pulls
this in if anything in our trim-graph touches it). With trim-mode
already on, we're at the lower bound unless we audit *what* in our
code is pulling XML in. Could be NodaTime (it has some XML-format
parsing), could be transitive through Microsoft.Data.Sqlite (less
likely after .NET 10), could be FCS (parser project — though that's
not in the CLI closure).

**Lever**: `IlcGenerateMapFile=true`-equivalent for JIT — there
isn't one. The trim analyzer's per-assembly warning output (now
visible since we unsuppressed) can hint at what's being kept.

### 5. NodaTime 1.2 MB — load-bearing, hard to shrink

Used heavily in `Prelude` for `Instant` / `LocalDateTime` /
`ZonedDateTime`. The TZDB-free build flavor exists (`NodaTime.TzdbCompiler`)
but loses timezone support. Probably keep as-is.

### 6. `libe_sqlite3.so` 1.3 MB — close to floor for SQLite

This is the bundle's SQLite native lib. Comparable in size to our
own `libe_sqlite3-linux-x64.a` (~5 MB on-disk static archive
shrinks to ~1.3 MB compiled into the .so because of LTO and DCE
during the .so link). Can't really shrink it without forking
SQLitePCLRaw and dropping unused DllImports, which we've already
ruled out as not worth the complexity.

### 7. Other .NET framework DLLs (4.5 MB across 30 files)

System.Collections (multiple sub-DLLs), System.Net.*, System.Text.*,
System.IO.*, System.Diagnostics.DiagnosticSource, etc. Each is
small individually (10-100 KB). Trim is already pruning what it
can. Lots of small-fry; no single big lever.

---

## Comparison: JIT (46 MB) vs AOT (27 MB)

The AOT binary is **~40% smaller** than the JIT single-file
because:

```
   item                               JIT     AOT     delta
   -----------------------------------------------------------
   .NET native runtime                ~20 MB  ~10 MB  -10 MB
     (libcoreclr/clrjit not shipped;
      AOT-compiles only what's reached)
   Managed .NET framework DLLs        ~12 MB   ~5 MB  - 7 MB
     (trim is more aggressive in AOT;
      lots of methods get fold/dead-stripped)
   Our managed code                    ~6 MB   ~3 MB  - 3 MB
     (AOT compiles only reached methods;
      removes unreachable IL entirely)
   tree-sitter native (single RID)    ~5 MB   ~5 MB    0
   libe_sqlite3                       ~1 MB  static-linked into binary
   Embedded seed db                   ~3.6 MB ~3.6 MB  0
   Other .NET pieces                  ~2 MB  ~1 MB   - 1 MB
   AOT-specific overhead (dehydrated  
     data, generic hashtables)         0      ~5 MB   +5 MB
   --------------------------------------------------
   approximate total                  ~46 MB  ~27 MB
```

The AOT-only savings come almost entirely from **not shipping the
JIT runtime**. The cost: cross-OS limitations (today's blocker) and
slightly larger AOT-specific runtime structures.

---

## Sources

- `backend/Build/out/Cli/Release/net10.0/linux-x64/publish/` — the
  unbundled publish dir generated for this analysis.
- Vault `05.Implementation/CLI/The Executable/CLI size breakdown.md`
  — earlier (pre-FSharpPlus-drop) snapshot for context.
- `thinking/cli-aot-progress-2026-05-12.md` — the AOT-specific
  `IlcGenerateMapFile` analysis (companion report).
