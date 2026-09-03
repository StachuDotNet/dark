module LibDB.PackageManager

open Prelude
open LibExecution.ProgramTypes

module RT = LibExecution.RuntimeTypes
module PT = LibExecution.ProgramTypes

open LibDB.Caching

module PMPT = ProgramTypes
module PMRT = RuntimeTypes


// Cache of Harmful fn hashes, as underlying hex strings: PT.Hash and RT.Hash are distinct CLR
// types, and storing strings avoids threading either wrapper through the cache layer. Not
// branch-scoped, since `deprecations` isn't. `invalidateHarmful` is for a long-lived process that
// mutates deprecation state and must not keep answering from a stale Set.
let mutable private harmfulCache : Option<Set<string>> = None

let private loadHarmful () : Set<string> =
  match harmfulCache with
  | Some cached -> cached
  | None ->
    let harmful =
      Queries.getHarmfulFnHashes ()
      |> Async.AwaitTask
      |> Async.RunSynchronously
      |> Set.map (fun (PT.Hash h) -> h)
    harmfulCache <- Some harmful
    harmful

/// Drop the Harmful set so the next lookup re-reads `deprecations`.
let invalidateHarmful () : unit = harmfulCache <- None

// Registered so a fold drops it along with everything else: unregistered, `deprecate --kind
// harmful` in a REPL session halts nothing until the process restarts.
Caching.register invalidateHarmful


// TODO: bring back eager loading
let rt : RT.PackageManager =
  { getType = withCache PMRT.Type.get
    getFn = withCache PMRT.Fn.get
    getValue = withCache PMRT.Value.get
    getBlob = PMRT.Blob.get
    persistBlob = PMRT.Blob.insert

    // A deprecation is a fact about a hash, and a hash means the same thing on every branch, so
    // the set needs no branch key.
    isHarmful = fun (RT.Hash h) -> Set.contains h (loadHarmful ())

    init =
      uply {
        //eagerLoad
        return ()
      } }


/// The PT PackageManager for MAIN: name resolution against `locations`, which by design holds only
/// main's bindings. A branch is this plus its delta ops -- branch-aware callers want `ptForBranch`.
let pt : PT.PackageManager =
  // `withCache` allocates a fresh `ConcurrentDictionary` per invocation, so hoist the cached
  // lambdas out here to reuse one dict. Caching by location is safe precisely because this PM only
  // ever answers about main; a branch's answers come from the overlay in front of it, built per
  // branch id and never sharing this dict.
  let findTypeCached = withCache (fun location -> PMPT.Type.find location)
  let findValueCached = withCache (fun location -> PMPT.Value.find location)
  let findFnCached = withCache (fun location -> PMPT.Fn.find location)

  { findType = findTypeCached
    findValue = findValueCached
    findFn = findFnCached

    getType = withCache PMPT.Type.get
    getFn = withCache PMPT.Fn.get
    getValue = withCache PMPT.Value.get

    // A CLI script's declarations are never in the store, so without a fallback
    // they render as hashes. Only as a fallback, though: hashes are content
    // addressed, so a script's private name for some shape is also a name for
    // every stored declaration of that shape, and `pickLocation` breaks ties by
    // shortest path, which a script's one-segment path always wins. Consulted
    // ahead of the store, `type MyErr = | BadFormat` in a script would rename
    // `Stdlib.Int.ParseError` for the rest of the process.
    getTypeLocations =
      fun id ->
        uply {
          match! PMPT.Type.getLocations id with
          | [] -> return EphemeralPackages.typeLocations id
          | stored -> return stored
        }
    getValueLocations =
      fun id ->
        uply {
          match! PMPT.Value.getLocations id with
          | [] -> return EphemeralPackages.valueLocations id
          | stored -> return stored
        }
    getFnLocations =
      fun id ->
        uply {
          match! PMPT.Fn.getLocations id with
          | [] -> return EphemeralPackages.fnLocations id
          | stored -> return stored
        }

    search = fun query -> PMPT.search query

    init = uply { return () } }


/// An in-memory PackageManager built by applying `ops` in sequence. Used for transient state
/// during parsing, testing, and branch overlays.
let createInMemory (ops : List<PT.PackageOp>) : PT.PackageManager =
  let typeLocations = ResizeArray<PT.PackageLocation * Hash>()
  let valueLocations = ResizeArray<PT.PackageLocation * Hash>()
  let fnLocations = ResizeArray<PT.PackageLocation * Hash>()

  for op in ops do
    match op with
    | PT.PackageOp.SetName(loc, target, _) ->
      match target with
      | PT.PackageType h -> typeLocations.Add(loc, h)
      | PT.PackageValue h -> valueLocations.Add(loc, h)
      | PT.PackageFn h -> fnLocations.Add(loc, h)
    | PT.PackageOp.AddType _
    | PT.PackageOp.AddValue _
    | PT.PackageOp.AddFn _ -> ()

    // None of these change what a name points at -- an ack or a policy records what a person decided ABOUT a
    // name, a BranchEvent is about the branch -- so an overlay of bindings has nothing to do here.
    | PT.PackageOp.Deprecate _
    | PT.PackageOp.Undeprecate _
    | PT.PackageOp.Decision(_,
                            _,
                            _,
                            (PT.DecisionKind.Ack _ | PT.DecisionKind.Propagation _))
    | PT.PackageOp.BranchEvent _ -> ()

    // An override binds a name like a SetName does; the overlay only cares about the binding.
    | PT.PackageOp.Decision(_, loc, _, PT.DecisionKind.Override target) ->
      match target with
      | PT.PackageType h -> typeLocations.Add(loc, h)
      | PT.PackageValue h -> valueLocations.Add(loc, h)
      | PT.PackageFn h -> fnLocations.Add(loc, h)

  // Items are keyed by hash. The ops come as Add*(item) followed by Set*Name(hash, loc), so pair
  // them up to build Hash -> item maps.
  let typeMap =
    let mutable map = Map.empty<Hash, PT.PackageType.PackageType>
    let mutable pendingType : Option<PT.PackageType.PackageType> = None
    for op in ops do
      match op with
      | PT.PackageOp.AddType t -> pendingType <- Some t
      | PT.PackageOp.SetName(_, PT.PackageType hash, _) ->
        match pendingType with
        | Some t ->
          map <- Map.add hash { t with hash = hash } map
          pendingType <- None
        | None -> ()
      | _ -> ()
    map

  let fnMap =
    let mutable map = Map.empty<Hash, PT.PackageFn.PackageFn>
    let mutable pendingFn : Option<PT.PackageFn.PackageFn> = None
    for op in ops do
      match op with
      | PT.PackageOp.AddFn f -> pendingFn <- Some f
      | PT.PackageOp.SetName(_, PT.PackageFn hash, _) ->
        match pendingFn with
        | Some f ->
          map <- Map.add hash { f with hash = hash } map
          pendingFn <- None
        | None -> ()
      | _ -> ()
    map

  let valueMap =
    let mutable map = Map.empty<Hash, PT.PackageValue.PackageValue>
    let mutable pendingValue : Option<PT.PackageValue.PackageValue> = None
    for op in ops do
      match op with
      | PT.PackageOp.AddValue v -> pendingValue <- Some v
      | PT.PackageOp.SetName(_, PT.PackageValue hash, _) ->
        match pendingValue with
        | Some v ->
          map <- Map.add hash { v with hash = hash } map
          pendingValue <- None
        | None -> ()
      | _ -> ()
    map

  let typeLocMap = Map.ofSeq typeLocations
  let valueLocMap = Map.ofSeq valueLocations
  let fnLocMap = Map.ofSeq fnLocations

  // Build reverse multi-maps (id -> all locations)
  let typeIdToLocs =
    typeLocations
    |> Seq.fold
      (fun acc (loc, id) ->
        let existing = Map.tryFind id acc |> Option.defaultValue []
        Map.add id (loc :: existing) acc)
      Map.empty
  let valueIdToLocs =
    valueLocations
    |> Seq.fold
      (fun acc (loc, id) ->
        let existing = Map.tryFind id acc |> Option.defaultValue []
        Map.add id (loc :: existing) acc)
      Map.empty
  let fnIdToLocs =
    fnLocations
    |> Seq.fold
      (fun acc (loc, id) ->
        let existing = Map.tryFind id acc |> Option.defaultValue []
        Map.add id (loc :: existing) acc)
      Map.empty

  { findType = fun loc -> Ply(Map.tryFind loc typeLocMap)
    findValue = fun loc -> Ply(Map.tryFind loc valueLocMap)
    findFn = fun loc -> Ply(Map.tryFind loc fnLocMap)

    getType = fun id -> Ply(Map.tryFind id typeMap)
    getValue = fun id -> Ply(Map.tryFind id valueMap)
    getFn = fun id -> Ply(Map.tryFind id fnMap)

    getTypeLocations =
      fun id -> Ply(Map.tryFind id typeIdToLocs |> Option.defaultValue [])
    getValueLocations =
      fun id -> Ply(Map.tryFind id valueIdToLocs |> Option.defaultValue [])
    getFnLocations =
      fun id -> Ply(Map.tryFind id fnIdToLocs |> Option.defaultValue [])

    search =
      fun query ->
        // Query-aware in-memory search so a BRANCH overlay's items show up in ls/view/tree/search,
        // not just eval. Locations here come from SetName ops' PackageLocation, which is cleanly
        // structured (owner separate, modules a proper list) unlike the `locations` table's
        // owner-in-modules ambiguity. `combine` appends these to main's results, so this only
        // contributes the overlay's matching items.
        let cm = query.currentModule
        let text = query.text
        let rec isPrefix (p : List<string>) (l : List<string>) =
          match p, l with
          | [], _ -> true
          | ph :: pt, lh :: lt when ph = lh -> isPrefix pt lt
          | _ -> false
        let fullModule (loc : PT.PackageLocation) = loc.owner :: loc.modules
        let moduleMatches (loc : PT.PackageLocation) =
          let fm = fullModule loc
          match cm, query.searchDepth with
          | [], PT.Search.SearchDepth.AllDescendants -> true
          | [], PT.Search.SearchDepth.OnlyDirectDescendants -> List.length fm = 1
          | _, PT.Search.SearchDepth.OnlyDirectDescendants -> fm = cm
          | _, PT.Search.SearchDepth.AllDescendants -> fm = cm || isPrefix cm fm
        // Match the QUALIFIED path as well as the bare name. Main's SQL tests the query against
        // `owner`, `modules` and `owner || '.' || modules`, so `search Probe.Ctx` finds what lives
        // under it. Matching only the bare name here meant the overlay answered that query with
        // nothing while main answered it with main's items, so a branch's own work was invisible to
        // exactly the search someone types to find it.
        let qualified (loc : PT.PackageLocation) =
          String.concat "." (fullModule loc @ [ loc.name ])
        let nameMatches (loc : PT.PackageLocation) =
          if text = "" then
            true
          elif query.exactMatch then
            loc.name = text
          else
            let t = text.ToLowerInvariant()
            loc.name.ToLowerInvariant().Contains t
            || (qualified loc).ToLowerInvariant().Contains t
        let itemMatches (loc : PT.PackageLocation) =
          moduleMatches loc && nameMatches loc
        // One entry per LOCATION, bound to what that location currently binds -- never one per hash.
        //
        // Enumerating the hash map instead returned every version a branch had ever bound, in HASH
        // order, and callers take the head: `dark view` on a branch showed whichever version happened
        // to have the lowest hash. Two versions in, that was right by luck; three versions in, it
        // showed the second-newest while `eval`, `diff` and `log` all ran the newest. Reading one
        // thing and running another is the worst shape that bug could take.
        //
        // Going through the location maps is also what makes search agree with `findFn` BY
        // CONSTRUCTION rather than by two pieces of code happening to fold the same ops the same way,
        // and it matches main, whose SQL search reads `locations` and so only ever sees live bindings.
        let liveAt
          (locations : ResizeArray<PT.PackageLocation * Hash>)
          (locMap : Map<PT.PackageLocation, Hash>)
          (items : Map<Hash, 'item>)
          : List<PT.LocatedItem<'item>> =
          locations
          |> Seq.map fst
          |> Seq.distinct
          |> Seq.toList
          |> List.choose (fun loc ->
            Map.tryFind loc locMap
            |> Option.bind (fun hash -> Map.tryFind hash items)
            |> Option.map (fun item ->
              ({ entity = item; location = loc } : PT.LocatedItem<_>)))

        let typesWithLocs = liveAt typeLocations typeLocMap typeMap
        let valuesWithLocs = liveAt valueLocations valueLocMap valueMap
        let fnsWithLocs = liveAt fnLocations fnLocMap fnMap

        // Submodules = the direct child module (cm ++ next segment) of any overlay item strictly
        // below cm. Only surfaced when browsing (empty text): a text search returns items, not
        // folders. Main's SQL search still contributes its own submodules via the fallback.
        let allLocs =
          (typesWithLocs |> List.map (fun i -> i.location))
          @ (valuesWithLocs |> List.map (fun i -> i.location))
          @ (fnsWithLocs |> List.map (fun i -> i.location))
        let submodules =
          if text <> "" then
            []
          else
            allLocs
            |> List.choose (fun loc ->
              let fm = fullModule loc
              if isPrefix cm fm && List.length fm > List.length cm then
                Some(List.truncate (List.length cm + 1) fm)
              else
                None)
            |> List.distinct

        Ply
          { PT.Search.SearchResults.submodules = submodules
            types = typesWithLocs |> List.filter (fun i -> itemMatches i.location)
            values = valuesWithLocs |> List.filter (fun i -> itemMatches i.location)
            fns = fnsWithLocs |> List.filter (fun i -> itemMatches i.location) }

    init = uply { return () } }


/// Combine two PackageManagers: check `overlay` first, then fall back to `fallback`.
/// This is used to layer transient/uncommitted definitions on top of persistent ones.
let combine
  (overlay : PT.PackageManager)
  (fallback : PT.PackageManager)
  : PT.PackageManager =
  { findType =
      fun loc ->
        uply {
          match! overlay.findType loc with
          | Some id -> return Some id
          | None -> return! fallback.findType loc
        }

    findValue =
      fun loc ->
        uply {
          match! overlay.findValue loc with
          | Some id -> return Some id
          | None -> return! fallback.findValue loc
        }

    findFn =
      fun loc ->
        uply {
          match! overlay.findFn loc with
          | Some id -> return Some id
          | None -> return! fallback.findFn loc
        }

    getType =
      fun id ->
        uply {
          match! overlay.getType id with
          | Some t -> return Some t
          | None -> return! fallback.getType id
        }

    getValue =
      fun id ->
        uply {
          match! overlay.getValue id with
          | Some v -> return Some v
          | None -> return! fallback.getValue id
        }

    getFn =
      fun id ->
        uply {
          match! overlay.getFn id with
          | Some f -> return Some f
          | None -> return! fallback.getFn id
        }

    getTypeLocations =
      fun id ->
        uply {
          let! overlayLocs = overlay.getTypeLocations id
          let! fallbackLocs = fallback.getTypeLocations id
          return overlayLocs @ fallbackLocs
        }

    getValueLocations =
      fun id ->
        uply {
          let! overlayLocs = overlay.getValueLocations id
          let! fallbackLocs = fallback.getValueLocations id
          return overlayLocs @ fallbackLocs
        }

    getFnLocations =
      fun id ->
        uply {
          let! overlayLocs = overlay.getFnLocations id
          let! fallbackLocs = fallback.getFnLocations id
          return overlayLocs @ fallbackLocs
        }

    search =
      fun query ->
        uply {
          // OVERLAY WINS: a name the overlay rebinds (a branch override of a main item) must
          // appear ONCE, as the branch's version. Overlay results come first, so
          // distinctBy-location keeps them over the fallback's stale entry.
          let! overlayResults = overlay.search query
          let! fallbackResults = fallback.search query
          let locKey (i : PT.LocatedItem<'a>) =
            (i.location.owner, i.location.modules, i.location.name)
          let dedup items = items |> List.distinctBy locKey
          return
            { PT.Search.SearchResults.submodules =
                List.append overlayResults.submodules fallbackResults.submodules
                |> List.distinct
              types = dedup (List.append overlayResults.types fallbackResults.types)
              values =
                dedup (List.append overlayResults.values fallbackResults.values)
              fns = dedup (List.append overlayResults.fns fallbackResults.fns) }
        }

    init =
      uply {
        do! overlay.init
        do! fallback.init
      } }


/// `basePM` with `ops` overlaid on top: the branch overlay, and the parse-time PM for tests and
/// from-disk parsing.
let withExtraOps
  (basePM : PT.PackageManager)
  (ops : List<PT.PackageOp>)
  : PT.PackageManager =
  let opsPM = createInMemory ops
  combine opsPM basePM


// BRANCH OVERLAYS.
//
// A branch is not a copy: it is delta ops (stored `effective = 0`, tagged in `op_branches`)
// overlaid on core, so a branch's PM is `withExtraOps pt ops`. That derivation needs nothing but
// the branch id, so it works for ANY branch on demand: `opsForBranch` / `ptForBranch`.
//
// The process also has "the branch I am on", resolved once at the CLI entry point from `--branch` /
// `DARK_BRANCH` / `current_branch`. That is a DEFAULT, not the mechanism: the entry point hands it
// to `ExecutionState.branchId` and it is passed from there. Nothing deep in the stack reads it
// ambiently, which is what lets a long-lived process answer about a branch it is not sitting on,
// and lets `switch` change branch without restarting.

let mutable private branchOverlayOps : List<PT.PackageOp> = []

/// The active branch's ID (for authoring routing), or None = author to main.
let mutable private currentBranchIdOpt : Option<PT.BranchId> = None

/// Delta ops for branches OTHER than the active one, loaded on demand. Bounded by how many
/// branches a process actually asks about, which for a CLI is one or two.
let private otherBranchOps =
  System.Collections.Concurrent.ConcurrentDictionary<PT.BranchId, List<PT.PackageOp>>()

// Dropped on every fold, like the rest: DB-derived state held for the life of the process with no
// other way to expire. Defensive rather than load-bearing today, since the user-visible readers of
// another branch (`diff`, `conflicts branch`) query SQLite directly.
Caching.register (fun () -> otherBranchOps.Clear())

/// Select the active branch's delta ops for this process (empty = main/core only). Prefer
/// `selectBranch`, which loads them; this is for callers already holding an explicit op list.
let setBranchOverlay (ops : List<PT.PackageOp>) : unit = branchOverlayOps <- ops

/// Drop the memoized op list for <branchId>, so the next read of it goes back to the store.
///
/// `opsForBranch` memoizes every branch that isn't the current one, and that memo is otherwise
/// only cleared by a fold. Authoring to a branch you aren't sitting on is supported, and a branch
/// write with no content ops folds nothing, so without this a process that had read that branch
/// once would keep serving the pre-write list for the rest of its life.
let forgetBranch (branchId : PT.BranchId) : unit =
  otherBranchOps.TryRemove branchId |> ignore<bool * List<PT.PackageOp>>

/// The branch this process is on. Main when nothing else was selected.
let currentBranchId () : PT.BranchId =
  currentBranchIdOpt |> Option.defaultValue PT.BranchId.Main

/// Delta ops for ANY branch, walking its parent chain. The active branch answers from the process
/// overlay (already loaded); any other is loaded once and memoized.
///
/// Main has none by construction -- its ops ARE the core, and an overlay is what a branch adds on top --
/// so it answers empty without a query. Hence a plain `BranchId` rather than an Option: main is a
/// branch id like any other, and wrapping it would make `None` and `Some main` two spellings of one thing.
let opsForBranch (branchId : PT.BranchId) : List<PT.PackageOp> =
  if branchId.IsMain then
    []
  elif currentBranchIdOpt = Some branchId then
    branchOverlayOps
  else
    otherBranchOps.GetOrAdd(branchId, (fun id -> (Branches.loadDeltaOps id).Result))

/// The PT PM for <branchId>: core with that branch's overlay, or plain core on main.
/// Used at parse/lowering time so a branch fn resolves name->hash.
let ptForBranch (branchId : PT.BranchId) : PT.PackageManager =
  match opsForBranch branchId with
  | [] -> pt
  | ops -> withExtraOps pt ops

/// Where a branch binds <param hash>, for hash-to-NAME lookups.
///
/// `locations` holds main's bindings only; a branch's SetNames deliberately never fold into it,
/// which is the isolation guarantee. So anything resolving a hash back to a name has to ask the
/// overlay too, or a branch-authored item has no name at all and renders as `<hash:d6f972b3>`.
///
/// Latest binding wins within the overlay (ops arrive oldest-first), and a name the branch REBOUND
/// to something else no longer counts as a location for the old hash, same as main.
let branchLocationsFor
  (branchId : PT.BranchId)
  (kind : PT.ItemKind)
  (hash : Hash)
  : List<PT.PackageLocation> =
  opsForBranch branchId
  |> List.fold
    (fun (acc : Map<string, PT.PackageLocation * Hash>) op ->
      match op with
      | PT.PackageOp.SetName(loc, target, _)
      | PT.PackageOp.Decision(_, loc, _, PT.DecisionKind.Override target) when
        target.kind = kind
        ->
        let modules = String.concat "." loc.modules
        let key = $"{loc.owner}/{modules}/{loc.name}"
        Map.add key (loc, target.hash) acc
      | _ -> acc)
    Map.empty
  |> Map.toList
  |> List.choose (fun (_, (loc, h)) -> if h = hash then Some loc else None)

/// Every location this branch has EVER bound <param hash> to, live or since superseded.
///
/// `branchLocationsFor` folds last-wins per location, so it answers "what does this hash hold NOW",
/// which is what naming a live item wants. It cannot answer for a version that has been edited past,
/// and on a branch nothing else can either: main's `getLocationsEverNamed` reads `locations`, which a
/// branch never writes to. Without this, `dark log` on a branch renders every superseded version as
/// `<hash:...>` while the newest shows its name -- one listing, two spellings, and the hash says
/// nothing about what you are looking at.
///
/// Strictly a FALLBACK, for when the live lookup found nothing. Offering these alongside live names
/// would let a version the branch has moved off keep answering to the name that moved on.
let branchLocationsEverNamed
  (branchId : PT.BranchId)
  (kind : PT.ItemKind)
  (hash : Hash)
  : List<PT.PackageLocation> =
  opsForBranch branchId
  |> List.choose (fun op ->
    match op with
    | PT.PackageOp.SetName(loc, target, _)
    | PT.PackageOp.Decision(_, loc, _, PT.DecisionKind.Override target) when
      target.kind = kind && target.hash = hash
      ->
      Some loc
    | _ -> None)
  |> List.distinct


/// Locations for <param hash>: main's, then any the branch adds.
///
/// Main FIRST, deliberately: callers render a label and take the head, and identical content is
/// one item, so a hash is routinely live at several names. Branch-first would render a MAIN item
/// whose body happens to match something you wrote on a branch under the branch's name, the right
/// content under the wrong label. This way it is purely additive: the branch supplies names for
/// hashes main cannot name at all, and changes nothing main could already answer.
let locationsFor
  (branchId : PT.BranchId)
  (kind : PT.ItemKind)
  (hash : Hash)
  (fromMain : List<PT.PackageLocation>)
  : List<PT.PackageLocation> =
  match branchLocationsFor branchId kind hash with
  | [] -> fromMain
  | branchLocs ->
    fromMain @ (branchLocs |> List.filter (fun l -> not (List.contains l fromMain)))

/// Make <branchId> the branch this process is on: load its delta ops and set both globals. Used at
/// boot and by `ops switch`, so a long-lived process changes branch without a restart. Drops the
/// on-demand memo, since a re-select is the moment a stale overlay would show.
let selectBranch (branchId : PT.BranchId) : unit =
  otherBranchOps.Clear()

  if branchId.IsMain then
    branchOverlayOps <- []
    currentBranchIdOpt <- None
  else
    branchOverlayOps <- (Branches.loadDeltaOps branchId).Result
    currentBranchIdOpt <- Some branchId
