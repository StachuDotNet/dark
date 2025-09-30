# LibMatter notes

## Op




## Patch

```fsharp
module Patch = 
    let create (intent: String) (author: String) : Patch =
        // Business logic in Darklang packages
        let patch = Patch.create (Uuid.generateV4()) intent author
        PatchDB.save patch
        patch


/// Create a new patch
let createPatch (intent: string) (author: string) : Task<Patch>
  

let savePatch (patch: Patch) : Task<unit>

let addOpToPatch (patchId: uuid) (op: Op) : Result<unit, SomeError>

let getPatch (patchId: uuid) : Task<Patch option>

/// Get operations for a patch
let getPatchOps (patchId: uuid) : Task<List<Op>>


module State = 
    let getState = ... TODO


    /// Detect conflicts between operations
let detectConflicts (ops: List<Op>) : Task<List<Conflict>> 

let checkConflictsWithCurrent (ops: List<Op>) : Task<List<Conflict>>



let executePatch (patch : Patch.T) : Ply<Result<unit, string>> =
  uply {
    // TODO: Wrap in database transaction
    let mutable errors = []
    
    for op in patch.ops do
      let! result = executeOp op
      match result with
      | Ok _ -> ()
      | Error err -> errors <- err :: errors
    
    if List.isEmpty errors then
      return Ok ()
    else
      let errorMsg = String.join "; " errors
      return Error $"Patch execution failed: {errorMsg}"
  }

  let validatePatch (patch : Patch.T) : Ply<Result<unit, string>> =
    uply {
        // Basic validation checks:
        // 1. All content hashes are valid
        // 2. All name locations are valid
        // 3. No conflicts with existing names
        // TODO: Implement validation logic
        return Ok ()
    }

```


## Session

```fsharp
module Session = 
  let switchSession (sessionId: Uuid) : Result<CliState, String> =
    TODO


/// Create a new session
let createSession (name: string) (owner: string) : Task<Session>

/// Save session to database
let saveSession (session: Session) : Task<unit>

/// Switch to a different session
let switchToSession (sessionId: SessionId) : Task<Session option>

/// Get session by ID
let getSession (sessionId: SessionId) : Task<Session option>
```





## Conflict-Detection



### Package: Darklang.ConflictDetector
```darklang
module Darklang.ConflictDetector =

  type Conflict = {
    type: ConflictType
    description: String
    location: PackageLocation
    resolutionOptions: List<String>
  }

  let analyzeOps (ops: List<Op>) : List<Conflict> =
    let conflicts = []

    // Group operations by location
    let opsByLocation = List.groupBy .location ops

    // Check for conflicting operations on same location
    opsByLocation
    |> List.filter (fun (_, locationOps) -> List.length locationOps > 1)
    |> List.map detectLocationConflict
    |> List.append conflicts

  let detectLocationConflict (location: PackageLocation, ops: List<Op>) : Conflict =
    // Logic to detect specific conflict types
    Conflict.create
      ConflictType.MultipleUpdates
      ("Multiple operations on " ++ PackageLocation.toString location)
      location
      ["keep-first"; "keep-last"; "manual-merge"]
```


### Some higher-level commands that will result in multiple ops
```fsharp

/// Create a new function and add it to the current patch
let createFunction 
  (location : PackageLocation.T) 
  (definition : PackageFn.PackageFn) 
  : Ply<Result<string, string>> =
  uply {
    // 1. Generate content hash
    let contentHash = hashPackageFn definition
    
    // 2. Create the ops
    let addContentOp = AddFunctionContent(contentHash, definition)
    let createNameOp = CreateName(location, contentHash)
    
    // 3. Execute the ops
    let! result1 = executeOp addContentOp
    match result1 with
    | Error err -> return Error err
    | Ok _ ->
      let! result2 = executeOp createNameOp  
      match result2 with
      | Error err -> return Error err
      | Ok _ -> return Ok contentHash
  }
```



we can probably get away without any conflict-resolution for a bit.
Detection-only; if it fails, just say where it fails and require the patch to be altered.






### Core Matter Operations API
**Current State**: Types exist in ProgramTypes.fs, but no implementation  
**Needed**: Complete implementation of Matter operations

```fsharp
// Content hashing
let hashContent (content: string) (contentType: ContentType) : string
let validateHash (content: string) (hash: string) : bool

// Op execution  
let executeOp (op: Op.T) : Ply<Result<unit, string>>
let executeOps (ops: List<Op.T>) : Ply<Result<unit, string>>

// Session management
let createSession (name: string) (basePatch: uuid option) : Ply<Result<Session.T, string>>
let switchSession (sessionId: uuid) : Ply<Result<Session.T, string>>
let getCurrentSession () : Ply<Option<Session.T>>
let listSessions () : Ply<List<Session.T>>

// Patch operations
let createPatch (ops: List<Op.T>) (metadata: Patch.Metadata) : Ply<Result<Patch.T, string>>
let applyPatch (patchId: uuid) : Ply<Result<unit, string>>
let validatePatch (patch: Patch.T) : Ply<Result<unit, ValidationError>>

// Content resolution
let resolveLocation (location: PackageLocation.T) : Ply<Option<string * ContentType>>
let getContentByHash (hash: string) : Ply<Option<Content>>
```

### Database Integration
**Current State**: Schema exists, but no queries  
**Needed**: Complete CRUD operations for all Matter tables

```fsharp
// In LibMatter.Database.fs
module LibMatter.Database

// Content operations
let insertContent (hash: string) (contentType: ContentType) (content: bytes) : Ply<Result<unit, string>>
let getContent (hash: string) : Ply<Option<Content>>
let contentExists (hash: string) : Ply<bool>

// Name resolution operations  
let createName (location: PackageLocation.T) (hash: string) : Ply<Result<unit, string>>
let updateNamePointer (location: PackageLocation.T) (newHash: string) : Ply<Result<unit, string>>
let resolveName (location: PackageLocation.T) : Ply<Option<string>>
let deleteName (location: PackageLocation.T) : Ply<Result<unit, string>>

// Session operations
let insertSession (session: Session.T) : Ply<Result<unit, string>>
let updateSession (session: Session.T) : Ply<Result<unit, string>>
let getSession (sessionId: uuid) : Ply<Option<Session.T>>
let listSessionsByStatus (status: Session.Status) : Ply<List<Session.T>>

// Patch operations
let insertPatch (patch: Patch.T) : Ply<Result<unit, string>>
let getPatch (patchId: uuid) : Ply<Option<Patch.T>>
let listPatches () : Ply<List<Patch.T>>
let insertPatchOp (patchId: uuid) (sequenceNum: int) (op: Op.T) : Ply<Result<unit, string>>
```

---




### Matter-Aware Package Browsing
**Current State**: Package navigation exists but doesn't understand sessions  
**Needed**: Session-aware package viewing

```fsharp
// Enhanced packages/darklang/cli/packages/nav.dark
let buildSessionState (location: PackageLocation) (session: Option<Session.T>) : State =
  let results = 
    match session with
    | Some s ->
      // Get packages visible in this session (includes session changes + base)
      Search.searchContentsInSession s.id location
    | None ->
      // Get packages in current global state
      Search.searchContents location
  
  // Mark items that are modified in current session
  let markedResults = 
    match session with
    | Some s ->
      markSessionModifications results s.id
    | None ->
      results
  
  // Build navigation state with session awareness
  buildNavigationState location markedResults

// Show session modifications in package view
let displaySessionAwareItem (item: NavItem) (session: Option<Session.T>) : String =
  let baseDisplay = formatNavItem item
  match session, item.modificationStatus with
  | Some _, Modified hash -> $"{baseDisplay} ✏️ (modified in session)"
  | Some _, New -> $"{baseDisplay} ✨ (new in session)"
  | Some _, Deleted -> $"{baseDisplay} 🗑️ (deleted in session)"
  | _, Unchanged -> baseDisplay
```


### Enhanced Search with Session Context
**Current State**: Basic package search  
**Needed**: Session-aware search that includes local changes

```fsharp
// Enhanced packages/darklang/cli/packages/search.dark
let searchWithSession (query: String) (session: Option<Session.T>) : SearchResults =
  let globalResults = Search.searchGlobal query
  
  match session with
  | Some s ->
    let sessionResults = Search.searchSession s.id query
    // Merge results, prioritizing session changes
    Search.mergeResults globalResults sessionResults
  | None ->
    globalResults

// Show search results with session context
let displaySearchResults (results: SearchResults) (session: Option<Session.T>) : Unit =
  match session with
  | Some s ->
    Stdlib.printLine $"Search results (session: {s.name}):"
    Stdlib.printLine ""
    
    if not (Stdlib.List.isEmpty results.sessionResults) then
      Stdlib.printLine "📝 In your session:"
      displayResults results.sessionResults
      Stdlib.printLine ""
    
    Stdlib.printLine "🌐 Global packages:"
    displayResults results.globalResults
  | None ->
    Stdlib.printLine "Search results (global):"
    displayResults results.globalResults
```
