# Binary Serialization Versioning

## Problem
Every PT change (new `Expr` variant, new `PackageOp` case, etc.) makes old binary blobs in `package_ops` unreadable. Unknown tags crash with `CorruptedData`. No versioning, no migration, no forward/backward compat.

## Current Format (from code review)

### Header (`BinaryFormat.fs`)
8 bytes total:
- **Version** (4 bytes, uint32): currently `1` — comment says _"this seems useless? at least until we start shipping non-alpha versions"_
- **DataLength** (4 bytes, uint32): payload size after header

Validation: `Validation.validateVersion` raises `UnsupportedVersion` if incoming version > `CurrentVersion`. `Validation.validateDataLength` raises `DataLengthMismatch` if remaining bytes don't match header.

### Body encoding
- **Union discriminators**: single byte (`w.Write 0uy` etc.) — max 256 variants per union
- **Records**: positional field encoding — no field names or tags, just values in order
- **Containers**: varint-encoded length prefix, then items (lists, maps, NEList)
- **Primitives**: native BinaryWriter types (int64, uint64, bool, etc.)
- **Special cases**: Float has sub-tags for NaN/Infinity (tags 0-3); Int128/UInt128 serialized as strings (with TODO: "what, why?"); Option uses 0=None/1=Some; DateTime as Unix ticks; Guid as raw 16 bytes

### Serialization API (`BinarySerialization.fs`)
- `makeSerializer<'T, 'ID>(writer)` — generic factory: write payload to MemoryStream, get length, write header+payload
- `makeDeserializer<'T, 'ID>(reader)` — generic factory: read header, validate version+length, call reader
- **Special case**: `RT.ValueType.serialize/deserialize` does NOT use the header — written raw without version. Would need header support added.

### Serializable types (11 top-level entry points)
**PT (Program Types):** PackageLocation, PackageType, PackageValue, PackageFn, PackageOp, Toplevel
**RT (Runtime Types):** PackageType, Dval, Instructions, PackageValue, PackageFn
Plus `RT.ValueType` (headerless special case)

### Total code: ~3,200 lines across `LibBinarySerialization/`

## Current Tag Registry

All unions currently use sequential 0-based tags with **no gaps**. This is the baseline for tag discipline going forward.

### Expr — 35 variants (tags 0–34)
`Serializers/PT/Expr.fs` lines 402–579

| Tag | Variant | Tag | Variant |
|-----|---------|-----|---------|
| 0 | EInt64 | 18 | ELambda |
| 1 | EUInt64 | 19 | ERecordFieldAccess |
| 2 | EInt8 | 20 | EVariable |
| 3 | EUInt8 | 21 | EApply |
| 4 | EInt16 | 22 | EList |
| 5 | EUInt16 | 23 | ERecord |
| 6 | EInt32 | 24 | ERecordUpdate |
| 7 | EUInt32 | 25 | EPipe |
| 8 | EInt128 | 26 | EEnum |
| 9 | EUInt128 | 27 | EMatch |
| 10 | EBool | 28 | ETuple |
| 11 | EString | 29 | EInfix |
| 12 | EChar | 30 | EDict |
| 13 | EFloat | 31 | EFnName |
| 14 | EUnit | 32 | EStatement |
| 15 | EValue | 33 | ESelf |
| 16 | ELet | 34 | EArg |
| 17 | EIf | | |

### PackageOp — 8 variants (tags 0–7)
`Serializers/PT/PackageOp.fs`

| Tag | Variant |
|-----|---------|
| 0 | AddType |
| 1 | AddValue |
| 2 | AddFn |
| 3 | SetTypeName |
| 4 | SetValueName |
| 5 | SetFnName |
| 6 | PropagateUpdate |
| 7 | RevertPropagation |

### TypeReference — 24 variants (tags 0–23)
`Serializers/PT/TypeReference.fs` (comment: "CLEANUP reorder these")

### MatchPattern — 21 variants (tags 0–20)
### InfixFnName — 13 variants (tags 0–12)
### Dval — 24 variants (tags 0–23)
### Instruction — 23 variants (tags 0–22)

## Strategy

### Repurpose the existing header version field
The header already has a version field (currently `1`, never meaningfully checked). Rather than adding a separate "format version" field, **repurpose this existing field** as the format version. It's already validated — `validateVersion` rejects unknown versions.

When the shape of any serialized type changes, bump `CurrentVersion` in `BinaryFormat.fs`. New header value = new reader.

### Keep all reader versions
Each format version gets its own reader. When format version N is current:
- Writer always writes format N
- Reader can read format 1 through N

Readers live in versioned modules (e.g., `Serializers.V1`, `Serializers.V2`). On read, dispatch by format version.

Old versions maintained indefinitely; user purges old ones manually as they see fit.

### Tag discipline
- Discriminated union tags are stable and never reused
- New variants get the next available tag number
- Removed variants keep their tag number reserved (reader can skip or error gracefully)
- The tag registry above becomes the reference — keep it updated

### Migration on read
When the reader encounters format version < current:
1. Deserialize with the old-version reader → old PT types
2. Map old PT → current PT (a simple function per version bump)
3. Optionally: rewrite the blob in the DB with the current format (lazy migration)

For now, keep the PT→PT mapping minimal. Most PT changes add variants (which old data never uses), so the mapping is often identity. Only structural changes (field additions, renames, type changes) need real migration logic.

### When to bump format version
- Adding a new union case: **no bump needed** (old blobs don't contain the new tag)
- Removing a union case: **no bump needed** (just reserve the tag)
- Adding a field to a record: **bump** (old blobs lack the field)
- Changing a field's type: **bump** (old blobs have wrong encoding)
- Reordering fields: **bump** (positional encoding breaks)

## Concrete refactoring plan

### Phase 1: Version infrastructure (~50 lines)
- Modify `makeDeserializer` to pass `header.Version` to reader functions
- Change reader signatures from `BinaryReader -> 'T` to `BinaryReader -> uint32 -> 'T`
- Add version dispatch at top level of each reader module
- Fix `RT.ValueType` to use header (currently headerless)

### Phase 2: First version bump (when needed, ~200 lines)
- Copy current readers as `readV1` functions
- Write new `readV2` functions for changed types
- Add V1→V2 mapping function (likely mostly identity)
- Bump `CurrentVersion` to `2u`

### Release purge fix (1 section of bash)
The release script (`scripts/deployment/publish-github-release`, lines 122–172) deletes ALL old releases on every publish. To fix: remove or comment out the "Delete old releases" section (lines 122–172). The existing code loops through all release IDs and deletes every one except the newest.

## TODOs
- [ ] Stop purging old CLI releases from GitHub (remove lines 122–172 in publish script)
- [ ] Add version-dispatching to `makeDeserializer` (~50 lines)
- [ ] Add header to `RT.ValueType` serialization (currently headerless)
- [ ] Document tag registry in code (copy table above into comment block in serializer files)
- [ ] On first format-breaking change: create V1 reader snapshot, bump to V2, add migration
- [ ] Tag CLI releases with which format version they use (embed in release notes or asset name)
- [ ] Add lazy migration (rewrite old blobs on read) — optional, can defer
