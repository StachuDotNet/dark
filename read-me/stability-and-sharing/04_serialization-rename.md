# LibBinarySerialization → LibSerialization Rename

## Motivation

Adding JSON serialization for sync wire format. Rather than creating a separate project, rename `LibBinarySerialization` to `LibSerialization` with `Binary` and `Json` sub-namespaces.

## Current Structure

```
backend/src/LibBinarySerialization/
├── LibBinarySerialization.fsproj
├── BinaryFormat.fs                    → LibBinarySerialization.BinaryFormat
├── BinarySerialization.fs             → LibBinarySerialization.BinarySerialization (PUBLIC API)
└── Serializers/
    ├── Common.fs                      → LibBinarySerialization.Serializers.Common
    ├── PT/
    │   ├── Common.fs, TypeReference.fs, PackageType.fs, Expr.fs,
    │   │   PackageValue.fs, PackageFn.fs, PackageOp.fs, Toplevel.fs
    └── RT/
        ├── Common.fs, TypeReference.fs, ValueType.fs, PackageType.fs,
        │   Dval.fs, PackageValue.fs, Instructions.fs, PackageFn.fs
```

22 files total. Public API exposes 11 serialization entry points (6 PT, 5 RT) through `BinarySerialization.fs`.

## Proposed Structure

```
backend/src/LibSerialization/
├── LibSerialization.fsproj
├── Binary/
│   ├── BinaryFormat.fs                → LibSerialization.Binary.BinaryFormat
│   ├── BinarySerialization.fs         → LibSerialization.Binary  (PUBLIC API)
│   └── Serializers/                   (same internal structure, just namespace change)
│       ├── Common.fs
│       ├── PT/ ...
│       └── RT/ ...
└── Json/
    ├── JsonSerialization.fs           → LibSerialization.Json  (NEW PUBLIC API)
    └── Serializers/
        └── PT/
            └── PackageOp.fs           (only what sync needs initially)
```

## Impact

**5 projects depend on LibBinarySerialization:**
1. `LibPackageManager` — heaviest user (Inserts.fs, Queries.fs, PackageOpPlayback.fs)
2. `LibCloud` — Canvas.fs, Serialize.fs (Toplevel serialization)
3. `BuiltinPM` — package manager builtins
4. `BuiltinCloudExecution` — cloud execution runtime
5. `Tests` — serialization tests

**Import pattern change:**
```fsharp
// Before:
module BS = LibBinarySerialization.BinarySerialization
BS.PT.PackageOp.serialize id op

// After:
module BS = LibSerialization.Binary
BS.PT.PackageOp.serialize id op
// or for JSON:
module JS = LibSerialization.Json
JS.PT.PackageOp.serialize op  // no ID needed for JSON
```

**PT2DT is unaffected** — it lives in `LibExecution.ProgramTypesToDarkTypes`, not in LibBinarySerialization.

## JSON Serialization Design

For sync, we need JSON serialization of PackageOps. The path:

```
PT.PackageOp → PT2DT.PackageOp.toDT → Dval → DvalReprInternalRoundtrippable.toJsonV0 → JSON string
JSON string → DvalReprInternalRoundtrippable.parseJsonV0 → Dval → PT2DT.PackageOp.fromDT → PT.PackageOp
```

**This roundtrip is safe for all 8 op types.** PT expression trees (in AddValue/AddFn) are encoded as structural DEnum values, NOT as runtime closures. The lossy `DApplicable` issue in DvalReprInternalRoundtrippable only affects actual runtime lambda values, not PT AST nodes.

**Alternative approach:** Instead of going through DvalReprInternalRoundtrippable, write dedicated JSON serializers in `LibSerialization.Json` that directly serialize PT types to JSON using `System.Text.Json`. More work upfront but avoids the PT→DT→JSON double-conversion and gives full control over the wire format.

**Recommendation:** Start with the PT2DT + DvalReprInternalRoundtrippable path (it already works, all 8 variants covered). If performance or format control becomes an issue, add dedicated JSON serializers later.

## Effort

- Rename + namespace changes: ~2 hours (mechanical find-replace across 22 internal files + 5 dependent projects)
- Add Json sub-namespace with PackageOp serializer: ~1 day
- Total: ~1.5 days

This can be done as a standalone PR before any sync implementation.
