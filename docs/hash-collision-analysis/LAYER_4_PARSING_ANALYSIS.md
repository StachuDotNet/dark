# Layer 4: Parsing Analysis

## Current State

### Overall Flow
WrittenTypesToProgramTypes converts user-written syntax to ProgramTypes:

1. **Parser** → WrittenTypes (WT)
2. **WT → PT** conversion (this module)
3. **ID Assignment** (via PackageIDs.fs)
4. **Name Resolution** (via NameResolver)
5. **PackageOp Generation**

### ID Assignment Strategy
Currently uses **location-based IDs** from PackageIDs.fs:

```fsharp
// Line 754
{ id = PackageIDs.Type.idForName pt.name.owner pt.name.modules pt.name.name
  description = pt.description
  declaration = declaration
  deprecated = PT.NotDeprecated }
```

Similar for values (line 780) and functions (line 832).

### Name Resolution
Uses `NameResolver` module:

```fsharp
// Line 81-83
let! t = NR.resolveTypeName pm onMissing currentModule t
let! typeArgs = Ply.List.mapSequentially toPT typeArgs
return PT.TCustomType(t, typeArgs)
```

Returns `NameResolution<FQTypeName>` which can be `Ok hash` or `Error reason`.

### Context Tracking
Parser maintains context for:

```fsharp
type Context = {
  currentFnName : List<string> option  // For ESelf detection
  isInFunction : bool                   // For scoping
  argMap : Map<string, int>            // Param name → index
}
```

### Self-Recursion Handling
Detects self-recursive calls (lines 318-322, 538-542):

```fsharp
match context.currentFnName with
| Some currentFnName ->
  let varQualifiedName = currentModule @ [ varName ]
  if varQualifiedName = currentFnName then
    return PT.EApply(id, PT.ESelf(id), processedTypeArgs, processedArgs)
```

### PackageIDs Integration
Uses hardcoded Guid→Hash mapping for known types:

```fsharp
// From PackageIDs.fs:
let result = p [ "Result" ] "Result" "c1cb018c-8264-4080-be7f-b06b8a0e1729"
let option = p [ "Option" ] "Option" "9ce3b596-968f-44c9-bcd4-511007cd9225"
```

On parse, checks if location matches known item and uses stable ID.

## Problems

### 1. Location-Based ID Assignment
**Problem**: IDs are computed from location, not content

```fsharp
// Lines 754, 780, 832
PackageIDs.Type.idForName pt.name.owner pt.name.modules pt.name.name
```

**Issue**:
- Renaming changes the ID
- Moving to different module changes the ID
- Same content, different location = different ID

**Needed**: Content-based ID assignment

### 2. Chicken-and-Egg Problem
**Problem**: Need ID before we have full content

**Current flow**:
1. Parse definition
2. Need to assign ID to create PackageType
3. But ID should be hash of PackageType
4. But PackageType needs ID to be created

**Impossible**: Cannot hash something that doesn't exist yet

### 3. Mutual Recursion in Parsing
**Problem**: Cannot resolve types that reference each other

```dark
type A = | Tag of B
type B = | Other of A
```

**Current flow**:
1. Parse A: needs hash of B (don't have it yet)
2. Parse B: needs hash of A (don't have it yet)
3. Deadlock

**Workaround**: Use `OnMissing.Allow` and let references be unresolved temporarily

### 4. Multi-Phase Parsing Required
**Problem**: Need multiple passes:

**Phase 1**: Parse with temporary IDs
- Use random Guids or location-based IDs
- Collect all definitions
- References might be unresolved

**Phase 2**: Compute content hashes
- Hash all definitions
- Detect SCCs
- Assign stable hashes

**Phase 3**: Re-parse with stable IDs
- Use actual content hashes
- Resolve all references
- Create final PackageOps

**Currently**: Only does Phase 1 (maybe Phase 3 with stabilization)

### 5. No SCC Detection
**Problem**: Parser doesn't detect or handle mutual recursion groups

**Needed**:
- Detect which types/functions reference each other
- Group into SCCs
- Hash each SCC as a unit
- Update all refs to use SCC hash

**Currently**: Each item parsed and hashed independently

### 6. Hash Timing Unclear
**Problem**: When should hashes be computed?

**Option A**: During WT→PT conversion
- Pro: Immediate
- Con: Might not have all dependencies

**Option B**: After all definitions parsed
- Pro: Have complete picture
- Con: Need to update everything

**Option C**: Separate pass
- Pro: Clean separation
- Con: Extra pass

**Current**: Option A (during conversion), but uses location-based IDs

### 7. OnMissing Strategy
**Problem**: Parser allows missing references with `OnMissing.Allow`

**Issue**:
- Creates `Error(NotFound)` in NameResolution
- Propagates to PT
- Maybe propagates to RT?
- When should errors be caught?

**Needed**: Clear policy on when unresolved names are acceptable

## Requirements for Ref-by-Hash

### R1: Multi-Phase Parsing
**Required flow**:

**Phase 1: Parse with placeholder IDs**
```fsharp
let! wtToP초기

 = parseFile file
let placeholderOps = WT2PT.toPT OnMissing.Allow placeholderPM wtToplevels
// placeholderOps have random/location-based IDs
```

**Phase 2: Compute content hashes**
```fsharp
let hashMapping = computeContentHashes placeholderOps
// Returns Map<PlaceholderID, ContentHash>
```

**Phase 3: Rewrite IDs**
```fsharp
let finalOps = rewriteIDs hashMapping placeholderOps
// Replaces all placeholder IDs with content hashes
```

**Phase 4: Validation**
```fsharp
validate finalOps
// Ensures all references are resolved
// Ensures no circular dependencies in hashing
```

### R2: SCC Detection in Parser
Need to detect mutual recursion during/after parsing:

```fsharp
module SCCDetector =
  type Node = {
    id : PlaceholderID
    definition : PackageOp
    dependencies : Set<PlaceholderID>
  }

  /// Build dependency graph from ops
  let buildGraph (ops : List<PackageOp>) : List<Node>

  /// Detect strongly connected components
  let detectSCCs (nodes : List<Node>) : List<NEList<Node>>
```

### R3: SCC-Aware Hash Computation
When computing hashes, handle SCCs:

```fsharp
module HashComputation =
  /// Compute hash for a single SCC
  let hashSCC (scc : NEList<PackageOp>) : Hash

  /// Compute hashes for all ops, handling SCCs
  let computeHashes (ops : List<PackageOp>) : Map<PlaceholderID, Hash> =
    let sccs = SCCDetector.detectSCCs ops
    sccs
    |> List.collect (fun scc ->
      let sccHash = hashSCC scc
      scc |> NEList.map (fun node -> node.id, sccHash))
    |> Map.ofList
```

### R4: ID Rewriting Pass
After hashing, rewrite all IDs in ops:

```fsharp
module IDRewriter =
  /// Rewrite all IDs in an op (definition ID and all references)
  let rewriteOp (mapping : Map<PlaceholderID, Hash>) (op : PackageOp) : PackageOp

  /// Recursively rewrite IDs in type references
  let rewriteTypeRef (mapping : Map<PlaceholderID, Hash>) (t : TypeReference) : TypeReference

  /// Recursively rewrite IDs in expressions
  let rewriteExpr (mapping : Map<PlaceholderID, Hash>) (e : Expr) : Expr
```

### R5: Placeholder ID Strategy
**Option A**: Random Guids
```fsharp
let placeholderID = System.Guid.NewGuid()
```
Pro: Guaranteed unique
Con: Non-deterministic

**Option B**: Location-based (current)
```fsharp
let placeholderID = hashLocation owner modules name
```
Pro: Deterministic
Con: Changes when moving

**Option C**: Sequential integers
```fsharp
let mutable nextID = 0
let placeholderID = nextID; nextID <- nextID + 1
```
Pro: Simple, deterministic
Con: Not globally unique

**Recommendation**: Option A (random) for Phase 1, replaced by content hashes in Phase 2

### R6: Error Handling Strategy
**Strict mode**: Fail fast on unresolved names
```fsharp
let! ops = WT2PT.toPT OnMissing.Error pm wtToplevels
// Fails if any names can't be resolved
```

**Lenient mode**: Allow unresolved, check later
```fsharp
let! ops1 = WT2PT.toPT OnMissing.Allow pm wtToplevels
// Phase 1: might have unresolved names

let! ops2 = WT2PT.toPT OnMissing.Error enrichedPM wtToplevels
// Phase 2: must resolve everything
```

**Recommendation**: Lenient for Phase 1, strict for Phase 3

### R7: Caching and Performance
Multi-phase parsing is expensive. Need caching:

```fsharp
type ParseCache = {
  /// Cache: source code → placeholder ops
  phase1Cache : Map<string, List<PackageOp>>

  /// Cache: ops → content hashes
  hashCache : Map<PackageOp, Hash>

  /// Cache: full file → final ops
  fullCache : Map<string, List<PackageOp>>
}
```

## Proposed Changes

### 1. New Multi-Phase Parser
```fsharp
module MultiPhaseParser =
  type ParseResult = {
    phase1Ops : List<PackageOp>        // Placeholder IDs
    hashMapping : Map<Hash, Hash>       // Placeholder → Content
    finalOps : List<PackageOp>         // Content IDs
  }

  let parse (pm : PT.PackageManager) (file : string) : Ply<ParseResult> =
    uply {
      // Phase 1: Parse with placeholders
      let! wtToplevels = parseFile file
      let! phase1Ops =
        WT2PT.toPackageOps
          OnMissing.Allow
          (withPlaceholders pm)
          wtToplevels

      // Phase 2: Detect SCCs and compute hashes
      let sccs = SCCDetector.detectSCCs phase1Ops
      let hashMapping = HashComputation.computeHashes sccs

      // Phase 3: Rewrite IDs
      let rewrittenOps = IDRewriter.rewriteOps hashMapping phase1Ops

      // Phase 4: Re-parse for validation (optional)
      let enrichedPM = withExtraOps pm rewrittenOps
      let! finalOps =
        WT2PT.toPackageOps
          OnMissing.Error
          enrichedPM
          wtToplevels

      return {
        phase1Ops = phase1Ops
        hashMapping = hashMapping
        finalOps = finalOps
      }
    }
```

### 2. SCC Detection
```fsharp
module SCCDetector =
  type OpNode = {
    placeholderId : Hash
    op : PT.PackageOp
    deps : Set<Hash>  // IDs this op references
  }

  let buildDependencyGraph (ops : List<PT.PackageOp>) : List<OpNode> =
    ops |> List.map (fun op ->
      { placeholderId = getOpId op
        op = op
        deps = findDependencies op })

  /// Tarjan's algorithm for SCC detection
  let detectSCCs (nodes : List<OpNode>) : List<NEList<OpNode>> =
    // Standard SCC detection
    ...
```

### 3. Hash Computation with SCCs
```fsharp
module HashComputation =
  let computeHashes (ops : List<PT.PackageOp>) : Map<Hash, Hash> =
    let graph = SCCDetector.buildDependencyGraph ops
    let sccs = SCCDetector.detectSCCs graph

    let mutable result = Map.empty

    for scc in sccs do
      match scc with
      | NEList.Singleton node ->
        // Simple case: no mutual recursion
        let hash = ContentHash.hashOp node.op
        result <- Map.add node.placeholderId hash result

      | NEList.Multiple nodes ->
        // Mutual recursion: hash as group
        let sccOps = nodes |> NEList.map (fun n -> n.op)
        let sccHash = ContentHash.hashSCC sccOps
        for node in nodes do
          result <- Map.add node.placeholderId sccHash result

    result
```

### 4. ID Rewriting
```fsharp
module IDRewriter =
  let rewriteOps
    (mapping : Map<Hash, Hash>)
    (ops : List<PT.PackageOp>)
    : List<PT.PackageOp> =
    ops |> List.map (rewriteOp mapping)

  let rewriteOp (mapping : Map<Hash, Hash>) (op : PT.PackageOp) : PT.PackageOp =
    match op with
    | PT.AddType typ ->
      PT.AddType {
        typ with
          id = Map.find typ.id mapping
          declaration = rewriteDeclaration mapping typ.declaration
      }

    | PT.AddFn fn ->
      PT.AddFn {
        fn with
          id = Map.find fn.id mapping
          body = rewriteExpr mapping fn.body
          returnType = rewriteTypeRef mapping fn.returnType
          // ... etc
      }

    | PT.SetTypeName(id, loc) ->
      PT.SetTypeName(Map.find id mapping, loc)

    // ... etc

  let rec rewriteTypeRef (mapping : Map<Hash, Hash>) (t : PT.TypeReference) : PT.TypeReference =
    match t with
    | PT.TCustomType(Ok(PT.FQTypeName.Package hash), args) ->
      let newHash = Map.tryFind hash mapping |> Option.defaultValue hash
      let newArgs = args |> List.map (rewriteTypeRef mapping)
      PT.TCustomType(Ok(PT.FQTypeName.Package newHash), newArgs)

    | PT.TList inner -> PT.TList(rewriteTypeRef mapping inner)
    | PT.TTuple(a, b, rest) ->
      PT.TTuple(
        rewriteTypeRef mapping a,
        rewriteTypeRef mapping b,
        List.map (rewriteTypeRef mapping) rest)
    // ... etc

  let rec rewriteExpr (mapping : Map<Hash, Hash>) (e : PT.Expr) : PT.Expr =
    // Deep traversal of expression tree, rewriting all hash references
    ...
```

### 5. Integration Point
```fsharp
// In LoadPackagesFromDisk.fs or similar:
let loadPackage (file : string) : Ply<List<PT.PackageOp>> =
  uply {
    let! parseResult = MultiPhaseParser.parse pm file

    // Return ops with content-based IDs
    return parseResult.finalOps
  }

// No more stabilization needed!
```

### 6. Validation Pass
```fsharp
module Validator =
  /// Validate that all references are resolved
  let validateOps (ops : List<PT.PackageOp>) : Result<unit, List<ValidationError>> =
    let errors = []

    for op in ops do
      // Check no unresolved names
      let unresolvedNames = findUnresolvedNames op
      if not (List.isEmpty unresolvedNames) then
        errors <- NameNotResolved(op, unresolvedNames) :: errors

      // Check no placeholder IDs remain
      let placeholders = findPlaceholderIds op
      if not (List.isEmpty placeholders) then
        errors <- PlaceholderIdRemaining(op, placeholders) :: errors

    if List.isEmpty errors then
      Ok ()
    else
      Error errors
```

## Code Impacts

### Files to Change

**Immediate**:
- `LibParser/WrittenTypesToProgramTypes.fs`:
  - Extract `toPackageOps` function
  - Make it work with placeholder IDs
  - Add Phase 2/3 after initial parse

- NEW: `LibParser/MultiPhaseParser.fs`:
  - Implement 3-phase parsing
  - SCC detection
  - Hash computation
  - ID rewriting

- `LibExecution/PackageIDs.fs`:
  - Update to support content hashing
  - Or remove entirely and compute hashes dynamically

**Second Order**:
- All code that calls the parser:
  - `LoadPackagesFromDisk.fs`
  - `TestModule.fs`
  - `Canvas.fs`
  - CLI tools

- `LibPackageManager/PackageManager.fs`:
  - Remove `stabilizeOps` function
  - No longer needed with content hashing

**Testing**:
- Test SCC detection
- Test hash computation
- Test ID rewriting
- Test full end-to-end parse
- Test mutual recursion cases

### Performance Considerations

**Concerns**:
- Three passes over code
- Dependency analysis overhead
- Hash computation for all items
- ID rewriting traversal

**Optimizations**:
- Cache parse results
- Parallelize independent SCCs
- Incremental re-parsing (only changed files)
- Lazy hash computation (only when needed)

### Migration Strategy

**Phase 1: Add multi-phase parsing (optional)**
- Implement alongside existing parser
- Use for new code only
- Test thoroughly

**Phase 2: Gradual rollout**
- Parse with both systems
- Compare results
- Validate hashes

**Phase 3: Switch default**
- Make multi-phase the default
- Keep old path as fallback
- Monitor for issues

**Phase 4: Remove old system**
- Delete single-phase parser
- Delete stabilization code
- Clean up

## Open Questions

### Q1: Parse Performance
**Question**: Will 3-phase parsing be too slow?

**Measurements needed**:
- Time to parse large package
- Time for SCC detection
- Time for hash computation
- Time for ID rewriting

**Mitigation**:
- Caching
- Parallelization
- Incremental parsing

**Recommendation**: Prototype and measure. Likely acceptable for CLI, might need optimization for editor.

### Q2: Placeholder ID Uniqueness
**Question**: How to ensure placeholder IDs don't collide with content hashes?

**Option A**: Different types
```fsharp
type ID = PlaceholderID of Guid | ContentID of Hash
```

**Option B**: Different ranges
```fsharp
// Placeholders use high bit set
// Content hashes use high bit clear
```

**Option C**: Don't worry
```fsharp
// SHA256 collision with random Guid is impossible
```

**Recommendation**: Option C - not worth complexity

### Q3: Caching Strategy
**Question**: What should be cached?

**Options**:
- Phase 1 results (placeholder ops)
- Hash mappings
- Final ops
- All of the above

**Recommendation**: Cache final ops keyed by (file content hash). Invalidate on file change.

### Q4: Error Messages
**Question**: How to report errors from Phase 2/3?

In Phase 1, we have WT positions. In Phase 3, we've lost that context.

**Solution**: Maintain source map from PT nodes back to WT positions throughout.

### Q5: Partial Updates
**Question**: If one type changes, do we re-parse everything?

**Option A**: Yes (simple)
**Option B**: Incremental (complex)

**Recommendation**: Option A for MVP. Option B for performance if needed.

### Q6: Cross-Module References
**Question**: How to handle references across files?

```dark
// File A
type Foo = | Bar of B.Baz

// File B
type Baz = Int64
```

**Current**: Parse files in dependency order

**With content hashing**: Parse A with placeholder for B.Baz, then resolve after B is parsed.

**Recommendation**: Parse all files in Phase 1, compute hashes in Phase 2 (cross-file), rewrite in Phase 3.

### Q7: SCC Across Files
**Question**: Can mutual recursion span files?

```dark
// A.dark
type A = | Tag of B.B

// B.dark
type B = | Other of A.A
```

**Answer**: Yes, must detect SCCs across files.

**Implication**: SCC detection operates on all ops from all files, not per-file.

## Summary

**Key Insights**:
1. Multi-phase parsing is mandatory for content-based IDs
2. SCC detection must work across files
3. ID rewriting is deep and pervasive
4. Performance needs measurement and optimization
5. Caching is critical for acceptable performance

**Biggest Risks**:
1. Parse performance (3 passes)
2. Complexity of ID rewriting (easy to miss a spot)
3. SCC detection bugs
4. Loss of error context between phases
5. Caching invalidation bugs

**Next Steps**:
1. Prototype 3-phase parsing on small example
2. Implement SCC detection
3. Implement hash computation with SCCs
4. Implement ID rewriting
5. Measure performance
6. Test on real packages
7. Optimize as needed
