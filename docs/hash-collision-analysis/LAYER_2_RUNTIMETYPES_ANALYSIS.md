# Layer 2: RuntimeTypes Analysis

## Current State

### Purpose and Design
RuntimeTypes is described as "lossy relative to ProgramTypes" (line 3-4):
- Used for execution only
- Format optimized for performance
- References back to PT via IDs when needed
- Comment: "CLEANUP: there's some useful 'reference things by hash' work to be done" (line 8)

### Identity Model
Uses Hash-based identity (same as PT):
```fsharp
FQTypeName.Package = Hash
FQValueName.Package = Hash
FQFnName.Package = Hash
```

### Type System

**TypeReference** (lines 183-209):
```fsharp
type TypeReference =
  | TUnit | TBool | TInt64 | ... // primitives
  | TTuple of TypeReference * TypeReference * List<TypeReference>
  | TList of TypeReference
  | TDict of TypeReference
  | TFn of NEList<TypeReference> * TypeReference
  | TCustomType of
      NameResolution<FQTypeName.FQTypeName> *
      typeArgs : List<TypeReference>
  | TVariable of string
  | TDB of TypeReference
```

**Notable differences from PT**:
- No location information
- Still has NameResolution (can be Error)
- Used for type checking during execution

**ValueType/KnownType** (lines 104-179):
```fsharp
type KnownType =
  | KTUnit | KTBool | KTInt64 | ...
  | KTList of ValueType
  | KTTuple of ValueType * ValueType * List<ValueType>
  | KTFn of args : NEList<ValueType> * ret : ValueType
  | KTDB of ValueType
  | KTCustomType of FQTypeName.FQTypeName * typeArgs : List<ValueType>
  | KTDict of ValueType

and ValueType =
  | Unknown
  | Known of KnownType
```

Key insight: Runtime tracks `Unknown` for gradual/partial type information

### Instructions and Execution

**No location tracking in instructions**:
```fsharp
type Instruction =
  | CreateRecord of
      createTo : Register *
      typeName : FQTypeName.FQTypeName *  // Just hash, no location
      typeArgs : List<TypeReference> *
      fields : List<string * Register>

  | CreateEnum of
      createTo : Register *
      typeName : FQTypeName.FQTypeName *  // Just hash, no location
      typeArgs : List<TypeReference> *
      caseName : string *
      fields : List<Register>

  | LoadValue of createTo : Register * FQValueName.FQValueName
```

### Dval (Runtime Values)

**DRecord and DEnum** (lines 576-591):
```fsharp
| DRecord of
    sourceTypeName : FQTypeName.FQTypeName *
    runtimeTypeName : FQTypeName.FQTypeName *
    typeArgs : List<ValueType> *
    fields : DvalMap

| DEnum of
    sourceTypeName : FQTypeName.FQTypeName *
    runtimeTypeName : FQTypeName.FQTypeName *
    typeArgs : List<ValueType> *
    caseName : string *
    fields : List<Dval>
```

**Question** (lines 579-582): "Do we need to split this into sourceTypeArgs and runtimeTypeArgs? What are we even using the source stuff for? error-reporting?"

### Package Definitions (RT versions)

**PackageType** (lines 1051-1052):
```fsharp
type PackageType = {
  id : Hash
  declaration : TypeDeclaration.T
}
```

**PackageValue** (lines 1054-1055):
```fsharp
type PackageValue = {
  id : Hash
  body : Dval
}
```

**PackageFn** (lines 1057-1067):
```fsharp
type PackageFn = {
  id : Hash
  typeParams : List<string>
  parameters : NEList<Parameter>
  returnType : TypeReference
  body : Instructions  // Note: compiled to instructions
}
```

**Key differences from PT**:
- No description, no deprecation
- Function body is Instructions, not Expr
- Much simpler, execution-focused

### PackageManager (RT)

```fsharp
type PackageManager = {
  getType : FQTypeName.Package -> Ply<Option<PackageType.PackageType>>
  getValue : FQValueName.Package -> Ply<Option<PackageValue.PackageValue>>
  getFn : FQFnName.Package -> Ply<Option<PackageFn.PackageFn>>
  init : Ply<unit>
}
```

**Notable**:
- Only `get` methods (by hash)
- No `find` methods (no location lookup)
- No search
- No ops application (read-only from RT perspective)

## Problems

### 1. Location Information Loss
**Problem**: RT has no location information at all

**Why this matters**:
- Error messages: "Type not found: [hash abc123...]" vs "Type not found: Darklang.Stdlib.Option"
- Stack traces: Need to show function names, not hashes
- Debugging: Need to map runtime state back to source
- Profiling: Need human-readable names

**Current workaround**: Reference back to PT by ID? But that requires having PT available at runtime.

### 2. Source vs Runtime Type Name Confusion
**Problem**: DRecord/DEnum store BOTH sourceTypeName and runtimeTypeName

**Why**: Type aliases mean source code might say `MyAlias` but runtime needs the `RealType`

**Issue**:
- Duplicates information
- Unclear when they differ
- Code comment questions if this is needed
- May be over-engineered

### 3. NameResolution in RT TypeReference
**Problem**: RT.TypeReference still has `NameResolution<FQTypeName>` which can be Error

**Why this is weird**:
- RT is for execution - shouldn't have unresolved names
- By the time we're executing, all names should be resolved
- Or execution should fail earlier during PT→RT

**Possible reasons it exists**:
- Gradual migration from PT
- Error preservation for better messages
- Legacy from before PT/RT split

### 4. No Metadata Access at Runtime
**Problem**: RT has no access to:
- Descriptions (for error messages)
- Deprecation warnings
- Source locations
- Author information

**Impact**:
- Can't generate helpful error messages
- Can't warn about deprecated usage
- Can't provide source references in stack traces

**Question**: Should RT have ANY metadata? Or should ExecutionState bridge to PT?

### 5. Instructions Have No Source Mapping
**Problem**: Instructions don't track:
- Which PT.Expr they came from
- Source line numbers
- Original code

**Impact**:
- Stack traces are just instruction pointers
- Can't map errors back to source
- Debugging is hard

**Possible solution**: Source maps in VMState?

### 6. ValueType Lacks Location Info
**Problem**: `KTCustomType of FQTypeName * typeArgs` - just a hash

**Why this matters**:
- Type errors say "Expected Type abc123, got Type def456"
- No human-readable names
- Can't show "Expected Option<Int64>, got Result<Int64, String>"

**Current**: Must look up in PM to get name? But that's async and might fail.

## Requirements for Ref-by-Hash

### R1: Location Hints for Error Messages
RT needs SOME location information for quality errors:

**Option A**: Store minimal metadata in RT types
```fsharp
type PackageType = {
  id : Hash
  declaration : TypeDeclaration.T
  displayName : string  // Just for errors: "Option"
}

type PackageFn = {
  id : Hash
  displayName : string  // "Darklang.Stdlib.List.map"
  ...
}
```

**Option B**: ExecutionState provides metadata lookup
```fsharp
type ExecutionState = {
  ...existing...
  getTypeDisplayName : Hash -> string
  getFnDisplayName : Hash -> string
}
```

**Recommendation**: Option B - keep RT lean, let ExecutionState bridge to PT

### R2: Source vs Runtime Type Resolution
**Simplify or remove?**

If we keep sourceTypeName vs runtimeTypeName:
```fsharp
DRecord of
  sourceTypeName : Hash *      // What the source code said
  runtimeTypeName : Hash *     // What it resolved to (after aliases)
  typeArgs : List<ValueType> *
  fields : DvalMap
```

But: Do we really need both?
- Error messages want sourceTypeName
- Type checking wants runtimeTypeName
- But we could just store runtimeTypeName and look up source via PT

**Recommendation**: Keep only runtimeTypeName in Dval, look up source via ExecutionState when needed for errors

### R3: Resolved Names Only
RT.TypeReference should not allow `Error` in NameResolution:

**Current**:
```fsharp
TCustomType of
  NameResolution<FQTypeName.FQTypeName> *
  typeArgs : List<TypeReference>
```

**Should be**:
```fsharp
TCustomType of
  FQTypeName.FQTypeName *  // Always Ok, never Error
  typeArgs : List<TypeReference>
```

**Why**: By the time PT→RT conversion happens, names must be resolved or compilation fails

### R4: Source Maps for Instructions
Add source mapping to Instructions:

```fsharp
type SourceLocation = {
  exprId : id
  tlid : Option<tlid>
}

type InstrData = {
  instructions : Instruction array
  resultReg : Register
  sourceMap : Map<int, SourceLocation>  // instruction index → source
}
```

Then error messages can reference back to PT.Expr.

### R5: Metadata via ExecutionState
```fsharp
type ExecutionState = {
  ...existing...

  // NEW: Metadata queries (cached)
  getTypeDisplayName : Hash -> string
  getFnDisplayName : Hash -> string
  getTypeSourceLocation : Hash -> Option<PackageLocation>
  getFnSourceLocation : Hash -> Option<PackageLocation>
}
```

This allows RT to stay lean while still having access to metadata when needed.

### R6: Clear PT vs RT Boundary
Clarify what crosses the boundary:

**PT → RT happens when**:
- Loading a package
- Compiling a function
- Preparing for execution

**At this point**:
- All names MUST be resolved
- All types MUST be known
- All IDs MUST be valid hashes

**If not**: PT→RT fails with clear error, don't propagate unresolved names to RT

## Proposed Changes

### 1. Remove NameResolution from RT.TypeReference
```fsharp
type TypeReference =
  | TUnit | TBool | ... // unchanged
  | TCustomType of
      typeName : FQTypeName.FQTypeName *  // Always resolved, never Error
      typeArgs : List<TypeReference>
  | TFn of NEList<TypeReference> * TypeReference
  | TVariable of string
  | TDB of TypeReference
```

### 2. Simplify Dval Type Names
```fsharp
| DRecord of
    typeName : FQTypeName.FQTypeName *  // Resolved runtime type
    typeArgs : List<ValueType> *
    fields : DvalMap

| DEnum of
    typeName : FQTypeName.FQTypeName *  // Resolved runtime type
    typeArgs : List<ValueType> *
    caseName : string *
    fields : List<Dval>
```

Remove `sourceTypeName` - look up via PT if needed for errors.

### 3. Add Source Mapping
```fsharp
type InstrData = {
  instructions : Instruction array
  resultReg : Register

  // NEW: Map instruction index to source expression
  sourceMap : Map<int, SourceInfo>
}

type SourceInfo = {
  exprId : id
  tlid : Option<tlid>
  description : string  // e.g., "function call to map"
}
```

### 4. Enhance ExecutionState
```fsharp
type ExecutionState = {
  ...existing...

  // NEW: Metadata access (for error messages)
  metadata : MetadataProvider
}

type MetadataProvider = {
  getTypeDisplayName : Hash -> string
  getFnDisplayName : Hash -> string
  getValueDisplayName : Hash -> string

  getTypeLocation : Hash -> Option<PackageLocation>
  getFnLocation : Hash -> Option<PackageLocation>
  getValueLocation : Hash -> Option<PackageLocation>
}
```

Implementation can cache lookups, query PT.PackageManager, etc.

### 5. Strict PT→RT Validation
```fsharp
module ProgramTypesToRuntimeTypes =
  /// Convert PT to RT, failing if any names are unresolved
  let toRT (pt : PT.PackageFn) : Result<RT.PackageFn, ConversionError> =
    // Validate all type references are resolved
    // Validate all function references are resolved
    // Validate all value references are resolved
    // Convert to instructions
    // Build source map

    match validate pt with
    | Ok () -> Ok (convert pt)
    | Error e -> Error e
```

### 6. PackageManager API
No changes needed - RT.PackageManager already only has `get` methods:

```fsharp
type PackageManager = {
  getType : Hash -> Ply<Option<PackageType>>
  getValue : Hash -> Ply<Option<PackageValue>>
  getFn : Hash -> Ply<Option<PackageFn>>
  init : Ply<unit>
}
```

This is correct for RT - only needs content lookup by hash.

## Code Impacts

### Files to Change

**Immediate**:
- `LibExecution/RuntimeTypes.fs`:
  - Remove `NameResolution` wrapper from `TCustomType`
  - Remove `sourceTypeName` from `DRecord`/`DEnum`
  - Add `SourceInfo`/`sourceMap` to `InstrData`
  - Add `MetadataProvider` to `ExecutionState`

- `LibExecution/ProgramTypesToRuntimeTypes.fs`:
  - Add validation for all NameResolutions being Ok
  - Fail conversion if any Error NameResolutions
  - Build source maps during conversion

- `LibExecution/Execution.fs`:
  - Provide MetadataProvider implementation
  - Use source maps for error reporting

**Second Order**:
- All runtime error generation (use metadata provider for names)
- All type error messages (look up display names)
- Stack trace generation (use source maps)
- Debugging tools (use source maps)

**Testing**:
- Test PT→RT fails on unresolved names
- Test error messages include readable names
- Test source maps are built correctly

### Compatibility Considerations

**Breaking change**: Remove NameResolution wrapper
- Need to update all pattern matches on TCustomType
- Need to update all constructors
- PT→RT must validate before conversion

**Breaking change**: Remove sourceTypeName
- Need to update all pattern matches on DRecord/DEnum
- Error reporting must use metadata provider instead

**Non-breaking**: Add source maps
- Pure addition to InstrData
- Optional, can be empty map initially

**Non-breaking**: Add MetadataProvider to ExecutionState
- Pure addition
- Can start with fallback implementation that returns hashes

## Open Questions

### Q1: Performance of Metadata Lookups
**Question**: Will looking up display names for every error be too slow?

**Options**:
- Cache in MetadataProvider
- Pre-populate ExecutionState with all names
- Accept the overhead (errors are rare)

**Recommendation**: Cache in MetadataProvider with LRU eviction

### Q2: Source Map Granularity
**Question**: Should source maps track every instruction or just function calls?

**Options**:
- Track everything (precise but large)
- Track only interesting instructions (calls, errors)
- Track only function boundaries

**Recommendation**: Track function calls, variable bindings, and any instruction that can error

### Q3: Metadata in RT Types
**Question**: Should RT.PackageType include ANY metadata?

**Current**: No metadata at all
**Alternative**: Include `displayName : string` for fast access

**Recommendation**: No metadata in RT types - keep them pure. Let MetadataProvider handle it.

### Q4: PT Access at Runtime
**Question**: Should RuntimeTypes have access to ProgramTypes at all?

**Current**: Somewhat - via IDs and manual lookup
**Alternative**: Completely separate, bridge via ExecutionState

**Recommendation**: Completely separate. ExecutionState bridges the gap when needed.

### Q5: ValueType Display
**Question**: How to render `ValueType` in error messages?

```fsharp
let vt = KTCustomType(hash, [KTInt64])
// Error message should say "Option<Int64>", not "Type abc123<Int64>"
```

**Need**: `renderValueType : MetadataProvider -> ValueType -> string`

**Recommendation**: Implement in error formatting code, not in RT itself

## Summary

**Key Insights**:
1. RT should be pure and lean - no metadata, no unresolved names
2. ExecutionState bridges RT to PT for display/debugging
3. Source maps enable error messages to reference original code
4. MetadataProvider allows RT to get display names without coupling to PT
5. PT→RT must validate and fail on unresolved names

**Biggest Risks**:
1. Performance overhead of metadata lookups
2. Breaking changes to Dval and TypeReference
3. Building/maintaining source maps correctly
4. Migration path from current state

**Next Steps**:
1. Prototype MetadataProvider interface
2. Remove NameResolution from RT.TypeReference
3. Build source maps in PT→RT conversion
4. Update one error path to use metadata provider
5. Measure performance impact
