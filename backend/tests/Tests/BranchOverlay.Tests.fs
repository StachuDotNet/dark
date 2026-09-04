/// Proof for the branches-as-overlays model (notes/fresh-arch/branches-concurrency.md).
/// A "branch" is its delta ops overlaid on a shared core PackageManager (PM.withExtraOps).
/// Two properties concurrent agents depend on:
///   - ISOLATION FROM CORE: a fn authored on a branch overlay resolves + EXECUTES there, but
///     is invisible to the shared core -- it never leaks into main.
///   - ISOLATION BETWEEN BRANCHES: two overlays over the SAME core see only their own defs;
///     neither can resolve OR fetch the other's, which is what lets N agents run N branches
///     concurrently over a shared read-only core.
module Tests.BranchOverlay

open Expecto

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude

open Fumble
open LibDB.Sqlite

module PT = LibExecution.ProgramTypes
module RT = LibExecution.RuntimeTypes
module PT2RT = LibExecution.ProgramTypesToRuntimeTypes
module Exe = LibExecution.Execution
module PM = LibDB.PackageManager
module HS = LibDB.HashStabilization
module Package = LibParser.Package
module NR = LibParser.NameResolver
module Branches = LibDB.Branches
module Queries = LibDB.Queries

/// A branch id for a test, derived from a readable label.
///
/// A branch id is a uuid, but a test that fails should name something a person can find. Deriving the
/// uuid from the label keeps both: the assertions read as `"chain-a"`, and re-running a test reuses the
/// same row rather than leaving a new branch behind each time.
let private testBranch (label : string) : PT.BranchId =
  use md5 = System.Security.Cryptography.MD5.Create()
  PT.BranchId.Id(
    System.Guid(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes label))
  )

module Seed = LibDB.Seed
module BS = LibSerialization.Binary.Serialization

open TestUtils.TestUtils


/// The names the parent has moved since a branch forked, asked of the DARK implementation.
///
/// `SCM.Branches.nameConflicts` is the merge gate the CLI actually consults; a test that called an F#
/// copy would be asserting about a second implementation rather than the one that runs. Same for
/// `darkRebase` below.
let private darkStringList (code : string) : Task<List<string>> =
  task {
    let! ptExpr = parsePTExpr code
    let! state = executionStateFor PM.pt false Map.empty
    let rtExpr = PT2RT.Expr.toRT Map.empty 0 None ptExpr

    match! Exe.executeExpr state rtExpr with
    | Ok(RT.DList(_, items)) ->
      return
        items
        |> List.map (fun d ->
          match d with
          | RT.DString s -> s
          | other -> $"{other}")
    | Ok(RT.DEnum(_, _, _, "Ok", [ RT.DList(_, items) ])) ->
      return
        items
        |> List.map (fun d ->
          match d with
          | RT.DString s -> s
          | other -> $"{other}")
    | Ok(RT.DEnum(_, _, _, "Error", [ RT.DString e ])) ->
      return failtest $"the Dark call failed: {e}"
    | Ok other -> return failtest $"unexpected result shape: {other}"
    | Error(rte, _) -> return failtest $"the Dark call raised: {rte}"
  }

let private darkBranch (branchId : PT.BranchId) : string =
  $"(Stdlib.Uuid.parse \"{branchId}\" |> Builtin.unwrap)"

let private nameConflicts (branchId : PT.BranchId) : Task<List<string>> =
  darkStringList $"Darklang.SCM.Branches.nameConflicts {darkBranch branchId}"

let private rebase (branchId : PT.BranchId) : Task<List<string>> =
  darkStringList $"Darklang.SCM.Branches.rebase {darkBranch branchId}"


/// Run a Dark call answering `Result<Unit, String>`, as the F# result these assertions expect.
let private darkUnitResult (code : string) : Task<Result<unit, string>> =
  task {
    let! ptExpr = parsePTExpr code
    let! state = executionStateFor PM.pt false Map.empty
    let rtExpr = PT2RT.Expr.toRT Map.empty 0 None ptExpr

    match! Exe.executeExpr state rtExpr with
    | Ok(RT.DEnum(_, _, _, "Ok", _)) -> return Ok()
    | Ok(RT.DEnum(_, _, _, "Error", [ RT.DString e ])) -> return Error e
    | Ok other -> return failtest $"unexpected result shape: {other}"
    | Error(rte, _) -> return failtest $"the Dark call raised: {rte}"
  }

let private resolveTakeTheirs
  (branchId : PT.BranchId)
  (fqn : string)
  : Task<Result<unit, string>> =
  darkUnitResult
    $"Darklang.SCM.Branches.resolveTakeTheirs {darkBranch branchId} \"{fqn}\""

let private resolveKeepMine
  (branchId : PT.BranchId)
  (fqn : string)
  : Task<Result<unit, string>> =
  darkUnitResult
    $"Darklang.SCM.Branches.resolveKeepMine {darkBranch branchId} \"{fqn}\""


/// Run a Dark call answering `Result<Int, String>`, as the count these assertions expect.
let private darkIntResult (code : string) : Task<int64> =
  task {
    let! ptExpr = parsePTExpr code
    let! state = executionStateFor PM.pt false Map.empty
    let rtExpr = PT2RT.Expr.toRT Map.empty 0 None ptExpr

    match! Exe.executeExpr state rtExpr with
    | Ok(RT.DEnum(_, _, _, "Ok", [ RT.DInt n ])) ->
      return int64 (RT.DarkInt.toBigInt n)
    | Ok(RT.DEnum(_, _, _, "Error", [ RT.DString e ])) ->
      return failtest $"the Dark call failed: {e}"
    | Ok other -> return failtest $"unexpected result shape: {other}"
    | Error(rte, _) -> return failtest $"the Dark call raised: {rte}"
  }

let private retagFrontierToParent
  (branchId : PT.BranchId)
  (parentId : PT.BranchId)
  : Task<int64> =
  darkIntResult (
    "Darklang.SCM.Branches.retagFrontierToParent "
    + darkBranch branchId
    + " "
    + darkBranch parentId
  )

let private markMergedEffective (branchId : PT.BranchId) : Task<int64> =
  darkIntResult $"Darklang.SCM.Branches.markMergedEffective {darkBranch branchId}"

/// The NAMES a branch has recorded a fork base for.
let private baseNames (branchId : PT.BranchId) : Task<List<string>> =
  darkStringList (
    "Darklang.SCM.Branches.nameBases "
    + darkBranch branchId
    + " |> Stdlib.List.map (fun (_o, _m, n, _h) -> n)"
  )

/// "owner\nmodules\nname=hash" per recorded base, as DARK computes the parent's current hash. Spelled
/// the same as the F# side in `parentHashesAgreeAcrossLanguages`, so the two lists compare directly.
let private parentHashLinesFromDark
  (parentId : PT.BranchId)
  (branchId : PT.BranchId)
  : Task<List<string>> =
  let key = "(Darklang.SCM.Branches.nameKey o m n)"

  let hashes =
    "(Darklang.SCM.Branches.parentHashesForBases "
    + darkBranch parentId
    + " "
    + darkBranch branchId
    + ")"

  darkStringList (
    "((Darklang.SCM.Branches.nameBases "
    + darkBranch branchId
    + " |> Stdlib.List.map (fun (o, m, n, _h) -> "
    + key
    + " ++ \"=\" ++ ((Stdlib.Dict.get "
    + hashes
    + " "
    + key
    + ") |> Stdlib.Option.withDefault \"\"))) |> Stdlib.List.sort)"
  )

open TestUtils.TestUtils

// A branch's source: one fn `foo` returning `answer`, computed via a CORE call
// (Stdlib.Int64.add), so executing its body ALSO proves the overlay resolves core names.
let private branchSource (answer : int) : string =
  $"""module Darklang.BranchTestOverlay

let foo (x: Int64) : Int64 = Stdlib.Int64.add {answer - 2}L 2L"""

let private fooLoc : PT.PackageLocation =
  { owner = "Darklang"; modules = [ "BranchTestOverlay" ]; name = "foo" }

// A source whose module name varies, so two branches can author DISTINCT fns.
let private namedSource (modName : string) (answer : int) : string =
  $"""module Darklang.{modName}

let foo (x: Int64) : Int64 = Stdlib.Int64.add {answer - 2}L 2L"""

let private fooLocIn (modName : string) : PT.PackageLocation =
  { owner = "Darklang"; modules = [ modName ]; name = "foo" }

/// Parse a branch source into stabilized ops: the real authoring path, SCC-aware hashes and all.
let private opsFor (source : string) : Task<List<PT.PackageOp>> =
  task {
    let builtins = localBuiltIns pmPT
    let! parsed =
      Package.parse builtins pmPT NR.OnMissing.ThrowError source |> Ply.toTask
    match parsed with
    | Ok ops -> return HS.computeRealHashes ops
    | Error errs ->
      return Exception.raiseInternal "branch parse failed" [ "errs", errs ]
  }

/// Execute the body of `foo` from a set of ops against `pm`. Proves the branch's code runs.
let private runFooBody
  (pm : PT.PackageManager)
  (ops : List<PT.PackageOp>)
  : Task<RT.Dval> =
  task {
    let body =
      ops
      |> List.tryPick (fun op ->
        match op with
        | PT.PackageOp.AddFn f -> Some f.body
        | _ -> None)
      |> Option.defaultWith (fun () ->
        Exception.raiseInternal "no AddFn op in branch ops" [])
    let! (state : RT.ExecutionState) = executionStateFor pm false Map.empty
    let rtExpr = PT2RT.Expr.toRT Map.empty 0 None body
    match! Exe.executeExpr state rtExpr with
    | Ok dv -> return dv
    | Error(rte, _) ->
      return Exception.raiseInternal "foo body errored" [ "rte", rte ]
  }


let isolationFromCore =
  testTask "a branch fn resolves + executes on its overlay but is INVISIBLE to core" {
    let! ops = opsFor (branchSource 42)
    let branch = PM.withExtraOps pmPT ops

    let! onBranch = branch.findFn fooLoc |> Ply.toTask
    Expect.isSome onBranch "foo resolves on the branch overlay"
    let! onCore = pmPT.findFn fooLoc |> Ply.toTask
    Expect.isNone onCore "foo does NOT leak into the shared core"

    let! dv = runFooBody branch ops
    Expect.equal dv (RT.DInt64 42L) "the branch fn's code runs -> 42"
  }

let isolationBetweenBranches =
  testTask
    "two overlays over one core see only their own defs (concurrent-branch isolation)" {
    let! opsA = opsFor (branchSource 42)
    let! opsB = opsFor (branchSource 99)
    let branchA = PM.withExtraOps pmPT opsA
    let branchB = PM.withExtraOps pmPT opsB

    let! hA = branchA.findFn fooLoc |> Ply.toTask
    let! hB = branchB.findFn fooLoc |> Ply.toTask
    Expect.isSome hA "A resolves its own foo"
    Expect.isSome hB "B resolves its own foo"
    Expect.notEqual
      hA
      hB
      "different bodies -> different content hashes (the branches diverge)"

    // Neither can fetch the other's def: an overlay holds only its own ops, falling back to core.
    let! aHasB = branchA.getFn (Option.get hB) |> Ply.toTask
    let! bHasA = branchB.getFn (Option.get hA) |> Ply.toTask
    Expect.isNone aHasB "branch A cannot fetch branch B's fn"
    Expect.isNone bHasA "branch B cannot fetch branch A's fn"

    let! dvA = runFooBody branchA opsA
    let! dvB = runFooBody branchB opsB
    Expect.equal dvA (RT.DInt64 42L) "branch A -> 42"
    Expect.equal dvB (RT.DInt64 99L) "branch B -> 99"
  }

/// Resolve a branch id by its name alias (first match), if any. Unfiltered: a lookup of a
/// specific past branch still wants to find it.
/// Resolve a branch id by its name alias (first match), if any. Unfiltered: a lookup of a
/// specific past branch still wants to find it.
let private branchIdForName (name : string) : Task<Option<PT.BranchId>> =
  Sql.query "SELECT id FROM branches WHERE name = @name LIMIT 1"
  |> Sql.parameters [ "name", Sql.string name ]
  |> Sql.executeRowOptionAsync (fun read ->
    PT.BranchId.ParseUnsafe(read.string "id"))

/// REBASE: accept the parent's current state as this branch's new base. Returns the names the
/// parent had changed since the fork; after this the branch's own ops layer on top (LWW by
/// origin_ts) and merge is unblocked.
/// Branch-registry lookups the assertions here need.
///
/// These used to live in `LibDB.Branches`, where nothing but this file and one builtin called them; the
/// production copies now live in Dark (`SCM.PackageOps`). Kept here as plain SQL rather than reaching
/// back through Dark, so an assertion about the store reads the store.
let private idForName (name : string) : Task<Option<PT.BranchId>> =
  Sql.query
    "SELECT id FROM branches
     WHERE name = @name AND archived_at IS NULL
     ORDER BY created_at DESC, rowid DESC LIMIT 1"
  |> Sql.parameters [ "name", Sql.string name ]
  |> Sql.executeRowOptionAsync (fun read ->
    PT.BranchId.ParseUnsafe(read.string "id"))

let private isMerged (branchId : PT.BranchId) : Task<bool> =
  task {
    let! found =
      Sql.query
        "SELECT 1 AS n FROM branches WHERE id = @id AND merged_at IS NOT NULL"
      |> Sql.parameters [ "id", Sql.string (string branchId) ]
      |> Sql.executeRowOptionAsync (fun read -> read.int64 "n")
    return Option.isSome found
  }


let private cleanupBranch (branchId : PT.BranchId) : Task<unit> =
  task {
    let del (sql : string) =
      Sql.query sql
      |> Sql.parameters [ "b", Sql.string (string branchId) ]
      |> Sql.executeStatementAsync

    do!
      del
        "DELETE FROM package_ops WHERE id IN (SELECT op_id FROM op_branches WHERE branch_id = @b)"
    do! del "DELETE FROM op_branches WHERE branch_id = @b"
    do! del "DELETE FROM branch_name_bases WHERE branch_id = @b"
    do! del "DELETE FROM branches WHERE id = @b"
  }

/// Pretend the parent moved every name this branch touched, by staling the recorded bases. Doing it
/// this way rather than actually changing the parent keeps the conflict tests off the shared main
/// projection every other test here reads concurrently.
let private staleNameBases (branchId : PT.BranchId) : Task<unit> =
  Sql.query
    "UPDATE branch_name_bases SET base_hash = 'stalehash' WHERE branch_id = @b"
  |> Sql.parameters [ "b", Sql.string (string branchId) ]
  |> Sql.executeStatementAsync

/// The parent's CURRENT hash per name is computed in both languages, and has to come out the same.
///
/// The two exist because the halves of the fork-base model run in different places: RECORDING a base
/// happens mid-author inside `scmAddOps`, which is F#, while DECIDING whether the parent has since
/// moved that name is `SCM.Branches.nameConflicts`, which is Dark. They read the same thing -- main's
/// `locations`, overridden by the parent chain's own rebinds when the parent is not main -- and if they
/// stop agreeing, a branch is either permanently conflicted or never conflicted, with nothing to say
/// which.
///
/// Exercised against a NON-MAIN parent, because that is the case with logic in it: for a main parent
/// both sides are one table read.
let parentHashesAgreeAcrossLanguages =
  testTask "F# and Dark compute the same parent hashes for a branch's bases" {
    let parent = testBranch "ph-parent"
    let child = testBranch "ph-child"
    do! cleanupBranch child
    do! cleanupBranch parent

    do! Branches.createBranch parent "ph-parent" PT.BranchId.Main
    let! parentOps = opsFor (namedSource "PhTest" 1)
    let! _ = Branches.storeDeltaOps parent parentOps
    do! Branches.recordNameBases parent PT.BranchId.Main parentOps

    do! Branches.createBranch child "ph-child" parent
    let! childOps = opsFor (namedSource "PhTest" 2)
    let! _ = Branches.storeDeltaOps child childOps
    do! Branches.recordNameBases child parent childOps

    let! bases =
      Sql.query
        "SELECT owner, modules, name FROM branch_name_bases WHERE branch_id = @b"
      |> Sql.parameters [ "b", Sql.string (string child) ]
      |> Sql.executeAsync (fun read ->
        read.string "owner", read.string "modules", read.string "name")
    Expect.isNonEmpty bases "the child recorded a base to compare"

    let! parentHashes = Branches.parentNameHashes parent

    let fromFSharp =
      bases
      |> List.map (fun (o, m, n) ->
        let hash = parentHashes |> Map.tryFind (o, m, n) |> Option.defaultValue ""
        $"{o}\n{m}\n{n}={hash}")
      |> List.sort

    let! fromDark = parentHashLinesFromDark parent child

    Expect.equal
      fromDark
      fromFSharp
      "Dark and F# agree on the parent's hash for every name the child based on"

    do! cleanupBranch child
    do! cleanupBranch parent
  }

/// An op this build cannot decode must be SKIPPED by the branch overlay, never raised on.
///
/// A synced store's own log holds ops a peer authored on a newer format. They are stored and left
/// unapplied on purpose, so a later build can apply them, which means they sit in the local log where
/// every local reader meets them -- including `chainOverlayOps`, which is what a process RESOLVES
/// through and loads at boot for whatever branch you are standing on. Raising there does not fail one
/// command, it fails the CLI, and that is exactly how `dark propagate pin` once died on a store holding
/// seven such ops and stayed dead.
///
/// The other half of the rule is that skipping one for READING never becomes dropping it for WRITING:
/// the junk op is still in the table at the end of this.
let undecodableBranchOpIsSkippedNotFatal =
  testTask
    "an op the build can't decode is skipped by the overlay, and survives in the log" {
    let bid = testBranch "undecodable-overlay"
    do! cleanupBranch bid

    do! Branches.createBranch bid "undecodable" PT.BranchId.Main
    let! ops = opsFor (namedSource "UndecodableTest" 3)
    let! _ = Branches.storeDeltaOps bid ops

    // A blob no build can read, tagged to the branch the way a peer's newer-format op arrives.
    let junkId = System.Guid.NewGuid()
    do!
      Sql.query
        "INSERT INTO package_ops (id, op_blob, effective, origin_ts)
         VALUES (@id, @blob, 0, '2099-01-01T00:00:00.000Z')"
      |> Sql.parameters
        [ "id", Sql.string (string junkId)
          "blob", Sql.bytes [| 0xFFuy; 0xFEuy; 0xFDuy; 0xFCuy |] ]
      |> Sql.executeStatementAsync
    do!
      Sql.query "INSERT INTO op_branches (op_id, branch_id) VALUES (@id, @b)"
      |> Sql.parameters
        [ "id", Sql.string (string junkId); "b", Sql.string (string bid) ]
      |> Sql.executeStatementAsync

    let! loaded = Branches.loadDeltaOps bid
    Expect.equal
      (List.length loaded)
      (List.length ops)
      "the readable ops load, and the unreadable one is not among them"

    let overlay = PM.withExtraOps pmPT loaded
    let! resolved = overlay.findFn (fooLocIn "UndecodableTest") |> Ply.toTask
    Expect.isSome resolved "and the branch still resolves its own fn"

    let! stillThere =
      Sql.query "SELECT count(*) AS n FROM package_ops WHERE id = @id"
      |> Sql.parameters [ "id", Sql.string (string junkId) ]
      |> Sql.executeRowAsync (fun read -> read.int64 "n")
    Expect.equal stillThere 1L "reading past it did not delete it"

    do! cleanupBranch bid
    do!
      Sql.query "DELETE FROM package_ops WHERE id = @id"
      |> Sql.parameters [ "id", Sql.string (string junkId) ]
      |> Sql.executeStatementAsync
  }

/// The overlay's SEARCH and its `findFn` must name the same version.
///
/// They are two foldings of the same ops, and they disagreed: `findFn` went through the location map
/// (last binding wins) while search enumerated the hash map, which is ordered BY HASH. Callers take the
/// head, so `dark view` on a branch showed whichever version happened to have the lowest hash. With two
/// versions that was right by luck; with three it showed the second-newest while `eval`, `diff` and
/// `log` all ran the newest -- reading one thing and running another.
///
/// Three versions, because two cannot tell a hash-ordered answer from a correct one.
let overlaySearchAgreesWithFindFn =
  testTask "the overlay's search names the version its location map binds" {
    let bid = testBranch "search-agrees"
    do! cleanupBranch bid
    do! Branches.createBranch bid "search-agrees" PT.BranchId.Main

    for answer in [ 1; 2; 3 ] do
      let! ops = opsFor (namedSource "SearchAgrees" answer)
      let! _ = Branches.storeDeltaOps bid ops
      ()

    let! loaded = Branches.loadDeltaOps bid
    let overlay = PM.withExtraOps pmPT loaded
    let loc = fooLocIn "SearchAgrees"

    let! bound = overlay.findFn loc |> Ply.toTask
    Expect.isSome bound "the branch binds the name"

    let query : PT.Search.SearchQuery =
      { currentModule = [ "Darklang"; "SearchAgrees" ]
        text = "foo"
        searchDepth = PT.Search.SearchDepth.OnlyDirectDescendants
        entityTypes = []
        exactMatch = true }

    let! (results : PT.Search.SearchResults) = overlay.search query |> Ply.toTask

    let found =
      results.fns
      |> List.filter (fun (f : PT.LocatedItem<PT.PackageFn.PackageFn>) ->
        f.location = loc)

    Expect.equal
      (List.length found)
      1
      "one hit per location, not one per version the branch has ever bound"

    Expect.equal
      (found |> List.map (fun f -> f.entity.hash))
      [ Option.get bound ]
      "and it is the version the location map binds"

    do! cleanupBranch bid
  }

/// A version a branch has edited PAST still has a name.
///
/// The live lookup folds last-wins per location, so it answers about the newest and nothing else, and
/// main's `getLocationsEverNamed` reads `locations`, which a branch never writes. Between them a
/// superseded branch version had no name at all, and `dark log` rendered it `<hash:...>` while the
/// newest in the same listing showed its name.
///
/// A fallback, not an alternative: the live name still wins while there is one.
let supersededBranchVersionsKeepTheirName =
  testTask
    "a version the branch has edited past is still named, not rendered as a hash" {
    let bid = testBranch "ever-named"
    do! cleanupBranch bid
    do! Branches.createBranch bid "ever-named" PT.BranchId.Main

    let! firstOps = opsFor (namedSource "EverNamed" 1)
    let! _ = Branches.storeDeltaOps bid firstOps
    let! secondOps = opsFor (namedSource "EverNamed" 2)
    let! _ = Branches.storeDeltaOps bid secondOps

    let hashOf (ops : List<PT.PackageOp>) =
      ops
      |> List.tryPick (fun op ->
        match op with
        | PT.PackageOp.SetName(_, PT.PackageFn h, _) -> Some h
        | _ -> None)

    let superseded = (hashOf firstOps).Value
    let live = (hashOf secondOps).Value
    Expect.notEqual
      superseded
      live
      "the two authorings really are different versions"

    let liveNames =
      PM.branchLocationsFor bid PT.ItemKind.Fn live |> List.map (fun l -> l.name)
    Expect.equal liveNames [ "foo" ] "the live version is named by the live lookup"

    Expect.isEmpty
      (PM.branchLocationsFor bid PT.ItemKind.Fn superseded)
      "and the superseded one is not -- that is what the fallback is for"

    let everNames =
      PM.branchLocationsEverNamed bid PT.ItemKind.Fn superseded
      |> List.map (fun l -> l.name)
    Expect.equal everNames [ "foo" ] "the fallback recovers the name it had"

    do! cleanupBranch bid
  }

/// The first item someone authors on a BRANCH counts as having items.
///
/// The check is an index seek on `locations`, which a branch never writes to, so on its own it says
/// "this owner has nothing" while their work sits on the branch -- and the workbench keeps offering
/// the "you have nothing yet" panel to someone who has just written something.
let branchAuthoringCountsAsHavingItems =
  testTask "an owner's first item on a branch counts as having items" {
    let bid = testBranch "owner-has-items"
    do! cleanupBranch bid
    do! Branches.createBranch bid "owner-has-items" PT.BranchId.Main

    // Store BEFORE asking. The overlay memoises per branch and this test writes ops behind its back
    // (`scmAddOps` refreshes it; `storeDeltaOps` on its own does not), so asking first would cache an
    // empty branch and then answer from that.
    let! ops = opsFor (namedSource "OwnerHasItems" 1)
    let! _ = Branches.storeDeltaOps bid ops

    Expect.isTrue
      (PM.branchOwnerHasItems bid "Darklang")
      "the branch's own binding counts"

    Expect.isFalse
      (PM.branchOwnerHasItems bid "SomeoneElse")
      "and it is scoped to the owner asked about"

    do! cleanupBranch bid
  }

let storeThenOverlay =
  testTask
    "a branch's ops round-trip through the store (effective=0) and overlay to resolve foo" {
    let branchId = testBranch "test-branch-store-1"
    do! cleanupBranch branchId

    do! Branches.createBranch branchId "store-proof" PT.BranchId.Main
    let! byName = branchIdForName "store-proof"
    Expect.equal byName (Some branchId) "branch resolves by its name alias"

    let! ops = opsFor (branchSource 42)
    let! stored = Branches.storeDeltaOps branchId ops
    Expect.isGreaterThan stored 0L "ops stored to the branch frontier"

    // stored effective=0 -> in the log, NOT folded into core, so core can't resolve foo.
    let! onCore = pmPT.findFn fooLoc |> Ply.toTask
    Expect.isNone onCore "foo is NOT folded into main (effective=0)"

    let! loaded = Branches.loadDeltaOps branchId
    let overlay = PM.withExtraOps pmPT loaded
    let! onBranch = overlay.findFn fooLoc |> Ply.toTask
    Expect.isSome onBranch "foo resolves via the branch loaded from the store"

    do! cleanupBranch branchId
  }

/// Count a branch's frontier ops at a given effective flag (cache-free, direct SQL).
let private countEffective (branchId : PT.BranchId) (eff : int) : Task<int64> =
  Sql.query
    "SELECT count(*) AS n FROM package_ops p
     JOIN op_branches b ON b.op_id = p.id
     WHERE b.branch_id = @b AND p.effective = @e"
  |> Sql.parameters [ "b", Sql.string (string branchId); "e", Sql.int eff ]
  |> Sql.executeRowAsync (fun read -> read.int64 "n")

let markMergedFlipsEffective =
  testTask
    "merge half-1: markMergedEffective flips a branch's ops effective 0->1 (fold does the rest)" {
    let branchId = testBranch "test-branch-flip-1"
    do! cleanupBranch branchId

    do! Branches.createBranch branchId "flip-proof" PT.BranchId.Main
    let! ops = opsFor (branchSource 42)
    let! _ = Branches.storeDeltaOps branchId ops

    let! pending = countEffective branchId 0
    Expect.isGreaterThan pending 0L "stored ops start effective=0 (branch-pending)"

    let! flipped = markMergedEffective branchId
    Expect.equal flipped pending "all pending ops flip to effective=1"
    let! stillPending = countEffective branchId 0
    Expect.equal stillPending 0L "none left effective=0"

    // (the fold -- Seed.applyUnappliedOps -- is what then brings foo into main; run in a fresh
    // process by `dark merge`, not here, so this test never pollutes the shared main projection.)
    do! cleanupBranch branchId
  }

/// Concurrent `resolveOrCreate` for one name yields ONE branch. The case `DARK_BRANCH` makes
/// ordinary: several agents start in their own shells with the same branch name exported and all
/// reach for it at once. If each minted its own, they would author into different branches while
/// believing they shared one.
let concurrentCreateYieldsOneBranch =
  testTask "racing to create one branch name produces one branch" {
    let name = "race-one-name"
    let! existing = idForName name
    match existing with
    | Some id -> do! cleanupBranch id
    | None -> ()

    let! results =
      Array.init 8 (fun _ -> Branches.resolveOrCreate name PT.BranchId.Main)
      |> System.Threading.Tasks.Task.WhenAll

    let ids = results |> Array.map fst |> Array.distinct
    Expect.equal ids.Length 1 "every caller got the SAME branch id"

    let createdCount = results |> Array.filter snd |> Array.length
    Expect.equal createdCount 1 "and exactly one of them reports having created it"

    // The store agrees, not just the return values.
    let! live =
      Sql.query
        "SELECT count(*) AS n FROM branches
         WHERE name = @name AND archived_at IS NULL AND merged_at IS NULL"
      |> Sql.parameters [ "name", Sql.string name ]
      |> Sql.executeRowAsync (fun read -> read.int64 "n")
    Expect.equal live 1L "one live row for the name"

    do! cleanupBranch ids[0]
  }


/// A merged branch stays addressable by name; a switch under that name starts a new one. A UX
/// contract, not an implementation detail: `dark branches` lists merged branches, so every verb
/// that takes a branch name has to accept the names it just printed.
let mergedBranchStaysAddressable =
  testTask "a merged branch resolves by name for reads, but not as a switch target" {
    do! cleanupBranch (testBranch "mergedName")
    do! cleanupBranch (testBranch "mergedName2")

    do! Branches.createBranch (testBranch "mergedName") "reuse-me" PT.BranchId.Main
    let! live = Branches.liveIdForName "reuse-me"
    Expect.equal
      live
      (Some(testBranch "mergedName"))
      "live branch resolves for both reads and writes"

    do!
      Sql.query "UPDATE branches SET merged_at = datetime('now') WHERE id = @b"
      |> Sql.parameters [ "b", Sql.string (string (testBranch "mergedName")) ]
      |> Sql.executeStatementAsync

    let! readSide = idForName "reuse-me"
    Expect.equal
      readSide
      (Some(testBranch "mergedName"))
      "merged branch still answers a read verb by name"

    let! writeSide = Branches.liveIdForName "reuse-me"
    Expect.isNone
      writeSide
      "but is NOT what `switch <name>` lands on -- its work is already merged"

    let! merged = isMerged (testBranch "mergedName")
    Expect.isTrue
      merged
      "and reports itself merged, so `merge` can say so instead of flipping nothing"

    // Reusing the name starts a separate branch, and reads then mean the NEW one.
    do! Branches.createBranch (testBranch "mergedName2") "reuse-me" PT.BranchId.Main
    let! afterReuse = idForName "reuse-me"
    Expect.equal
      afterReuse
      (Some(testBranch "mergedName2"))
      "the most recent branch wins the name"

    // Archiving is different from merging: it discards the ops, so there is nothing left to address.
    do!
      Sql.query "UPDATE branches SET archived_at = datetime('now') WHERE id = @b"
      |> Sql.parameters [ "b", Sql.string (string (testBranch "mergedName2")) ]
      |> Sql.executeStatementAsync
    let! afterArchive = idForName "reuse-me"
    Expect.equal
      afterArchive
      (Some(testBranch "mergedName"))
      "archived branches drop out of name resolution"

    do! cleanupBranch (testBranch "mergedName")
    do! cleanupBranch (testBranch "mergedName2")
  }


let processOverlaySelects =
  testTask
    "the branch a process is on resolves through its overlay; leaving it stops that" {
    PM.selectBranch PT.BranchId.Main // ensure clean start (process-global)
    let! before =
      (PM.ptForBranch (PM.currentBranchId ())).findFn fooLoc |> Ply.toTask
    Expect.isNone before "no branch active -> foo unresolved (core only)"

    let! ops = opsFor (branchSource 42)
    PM.selectBranch (testBranch "overlaySel")
    PM.setBranchOverlay ops
    let! during =
      (PM.ptForBranch (PM.currentBranchId ())).findFn fooLoc |> Ply.toTask
    Expect.isSome during "on the branch -> foo resolves through its overlay"

    // Main is still answered from core alone, with the branch's ops loaded and inert.
    let! fromMain = (PM.ptForBranch PT.BranchId.Main).findFn fooLoc |> Ply.toTask
    Expect.isNone fromMain "and main never sees a branch's binding"

    PM.selectBranch PT.BranchId.Main // leave clean for other tests (process-global)
    let! after = (PM.ptForBranch (PM.currentBranchId ())).findFn fooLoc |> Ply.toTask
    Expect.isNone after "back on main -> foo unresolved again"
  }

let branchesOffBranches =
  testTask "a branch off another sees its parent's frontier (branches off branches)" {
    do! cleanupBranch (testBranch "boB")
    do! cleanupBranch (testBranch "boA")
    do! Branches.createBranch (testBranch "boA") "chain-a" PT.BranchId.Main
    do! Branches.createBranch (testBranch "boB") "chain-b" (testBranch "boA") // B off A

    let! opsA = opsFor (namedSource "ChainA" 42)
    let! opsB = opsFor (namedSource "ChainB" 99)
    let! _ = Branches.storeDeltaOps (testBranch "boA") opsA
    let! _ = Branches.storeDeltaOps (testBranch "boB") opsB

    // B's overlay walks the parent chain: A's frontier + B's own.
    let! bOps = Branches.loadDeltaOps (testBranch "boB")
    let bOverlay = PM.withExtraOps pmPT bOps
    let! bSeesA = bOverlay.findFn (fooLocIn "ChainA") |> Ply.toTask
    let! bSeesB = bOverlay.findFn (fooLocIn "ChainB") |> Ply.toTask
    Expect.isSome bSeesA "B sees its parent A's fn (branches off branches)"
    Expect.isSome bSeesB "B sees its own fn"

    let! aOps = Branches.loadDeltaOps (testBranch "boA")
    let aOverlay = PM.withExtraOps pmPT aOps
    let! aSeesA = aOverlay.findFn (fooLocIn "ChainA") |> Ply.toTask
    let! aSeesB = aOverlay.findFn (fooLocIn "ChainB") |> Ply.toTask
    Expect.isSome aSeesA "A sees its own fn"
    Expect.isNone aSeesB "A does NOT see its child B's fn"

    // MERGE B INTO A (parent != main): retag B's frontier onto A. A's overlay now folds B's fn, but
    // main is still untouched (a non-main merge never flips effective / folds into main).
    let! parentOfB = Branches.parentOf (testBranch "boB")
    Expect.equal parentOfB (testBranch "boA") "B's parent is A"
    let! merged = retagFrontierToParent (testBranch "boB") parentOfB
    Expect.isGreaterThan merged 0L "B had frontier ops to merge"

    let! aOps2 = Branches.loadDeltaOps (testBranch "boA")
    let aOverlay2 = PM.withExtraOps pmPT aOps2
    let! aNowSeesB = aOverlay2.findFn (fooLocIn "ChainB") |> Ply.toTask
    Expect.isSome aNowSeesB "after merge, A sees B's fn (retagged onto A)"
    // B's OWN frontier tags are gone (moved to A). loadDeltaOps("boB") would still WALK to A, so we
    // check the direct tags, not the chain overlay.
    let! bOwnTags =
      Sql.query "SELECT count(*) AS n FROM op_branches WHERE branch_id = @b"
      |> Sql.parameters [ "b", Sql.string (string (testBranch "boB")) ]
      |> Sql.executeRowAsync (fun read -> read.int64 "n")
    Expect.equal
      bOwnTags
      0L
      "B's own frontier tags are empty after the retag (its ops are A's now)"
    let! stillNotMain = pmPT.findFn (fooLocIn "ChainB") |> Ply.toTask
    Expect.isNone stillNotMain "merge into a non-main parent does NOT leak into main"

    do! cleanupBranch (testBranch "boB")
    do! cleanupBranch (testBranch "boA")
  }

/// Main authoring's WipRefresh must NOT see a branch's ops: `getWipOps` excludes every
/// `op_branches`-tagged op. Let them back into WIP and the draft rewrite folds them
/// into main.
let getWipOpsExcludesBranch =
  testTask
    "getWipOps excludes branch-tagged ops (main authoring can't see branch state)" {
    let branchId = testBranch "test-wip-guard"
    do! cleanupBranch branchId
    do! Branches.createBranch branchId "wip-guard" PT.BranchId.Main
    let! ops = opsFor (branchSource 42)
    let! _ = Branches.storeDeltaOps branchId ops

    let! wip = Queries.getWipOps ()
    let! total =
      Sql.query "SELECT count(*) AS n FROM package_ops"
      |> Sql.executeRowAsync (fun read -> read.int64 "n")
    let! branchCount =
      Sql.query "SELECT count(*) AS n FROM op_branches WHERE branch_id = @b"
      |> Sql.parameters [ "b", Sql.string (string branchId) ]
      |> Sql.executeRowAsync (fun read -> read.int64 "n")
    // Every TAGGED op, not just this branch's: `getWipOps` excludes `op_branches` wholesale, so
    // subtracting only our own count assumes we're the only branch in the store, and we aren't.
    // DISTINCT because one op can be tagged to several branches.
    let! taggedCount =
      Sql.query "SELECT count(DISTINCT op_id) AS n FROM op_branches"
      |> Sql.executeRowAsync (fun read -> read.int64 "n")

    Expect.isGreaterThan branchCount 0L "the branch actually stored ops"
    Expect.equal
      (int64 (List.length wip))
      (total - taggedCount)
      "getWipOps returns every op EXCEPT branch-tagged ones (isolation)"

    do! cleanupBranch branchId
  }

/// `applyUnappliedOps`' final sweep is scoped to `applied=0 AND effective=1`, so merging branch M
/// leaves sibling S's still-pending ops applied=0. A wider sweep marks them applied without folding
/// them, they can never fold afterwards, and S's binding is silently lost by merge order.
let mergeDoesNotConsumeSiblingPendingOps =
  testTask
    "a merge leaves OTHER branches' pending ops applied=0 (applied-flag isolation)" {
    let bS = testBranch "test-sweep-sibling"
    let bM = testBranch "test-sweep-merging"
    do! cleanupBranch bS
    do! cleanupBranch bM
    do! Branches.createBranch bS "sweep-sibling" PT.BranchId.Main
    do! Branches.createBranch bM "sweep-merging" PT.BranchId.Main
    // Distinct modules so M's fold pollutes only its own unique name (cleaned up after).
    let! opsS = opsFor (namedSource "SweepSibling" 7)
    let! opsM = opsFor (namedSource "SweepMerging" 8)
    let! _ = Branches.storeDeltaOps bS opsS
    let! _ = Branches.storeDeltaOps bM opsM

    let pendingCount (b : PT.BranchId) : Task<int64> =
      Sql.query
        "SELECT count(*) AS n FROM package_ops
         WHERE applied = 0 AND id IN (SELECT op_id FROM op_branches WHERE branch_id = @b)"
      |> Sql.parameters [ "b", Sql.string (string b) ]
      |> Sql.executeRowAsync (fun read -> read.int64 "n")

    let! sBefore = pendingCount bS
    Expect.isGreaterThan sBefore 0L "sibling S starts with pending (applied=0) ops"

    // Merge M into main: flip its frontier effective=1, then fold (which runs the sweep).
    let! _ = markMergedEffective bM
    let! _ = Seed.applyUnappliedOps ()

    let! sAfter = pendingCount bS
    Expect.equal
      sAfter
      sBefore
      "S's pending ops are UNTOUCHED by M's merge (still applied=0)"

    do! cleanupBranch bS
    do! cleanupBranch bM
    do!
      Sql.query "DELETE FROM locations WHERE modules = 'SweepMerging'"
      |> Sql.executeStatementAsync
  }

/// Two branches bind the SAME name to DIFFERENT hashes, the second authored LATER. Merging older
/// then newer must land main on the NEWER binding (origin_ts LWW), so convergence does not depend on
/// merge order. Folds through the real path (markMergedEffective + applyUnappliedOps).
let sameNameMergesConvergeToLater =
  testTask
    "merging older-then-newer for one name lands on the NEWER binding (origin_ts LWW)" {
    let bOld = testBranch "test-cvg-old"
    let bNew = testBranch "test-cvg-new"
    let liveHash () : Task<Option<string>> =
      Sql.query
        "SELECT item_hash FROM locations
         WHERE owner = 'Darklang' AND modules = 'ConvergeWin' AND name = 'foo'
           AND unlisted_at IS NULL"
      |> Sql.executeRowOptionAsync (fun read -> read.string "item_hash")
    let mergeFold (b : PT.BranchId) : Task<unit> =
      task {
        let! _ = markMergedEffective b
        let! _ = Seed.applyUnappliedOps ()
        return ()
      }
    let setNameHash (ops : List<PT.PackageOp>) : string =
      ops
      |> List.pick (fun op ->
        match op with
        | PT.PackageOp.SetName(_, target, _) ->
          let (PT.Hash h) = target.hash
          Some h
        | _ -> None)

    do! cleanupBranch bOld
    do! cleanupBranch bNew
    do!
      Sql.query "DELETE FROM locations WHERE modules = 'ConvergeWin'"
      |> Sql.executeStatementAsync

    do! Branches.createBranch bOld "cvg-old" PT.BranchId.Main
    do! Branches.createBranch bNew "cvg-new" PT.BranchId.Main
    // Same module+name (ConvergeWin.foo), different bodies -> different hashes. bOld stored first, so
    // its ops get an EARLIER origin_ts than bNew's (storeDeltaOps stamps now() per call).
    let! opsOld = opsFor (namedSource "ConvergeWin" 5)
    let! opsNew = opsFor (namedSource "ConvergeWin" 6)
    let! _ = Branches.storeDeltaOps bOld opsOld
    let! _ = Branches.storeDeltaOps bNew opsNew
    let newerHash = setNameHash opsNew

    do! mergeFold bOld
    do! mergeFold bNew

    let! live = liveHash ()
    Expect.equal
      live
      (Some newerHash)
      "main lands on the NEWER binding after older-then-newer merge"

    do! cleanupBranch bOld
    do! cleanupBranch bNew
    do!
      Sql.query "DELETE FROM locations WHERE modules = 'ConvergeWin'"
      |> Sql.executeStatementAsync
  }

/// Locks the reload-stable rebase model: nameConflicts flags a name whose main hash diverged from
/// the branch's recorded base; rebase accepts main's state and clears it. Manipulates the base row
/// directly (no fold into main) so the test never pollutes the shared main projection.
let rebaseDetectsAndClearsConflicts =
  testTask "nameConflicts flags a diverged name; rebase clears it" {
    let bid = testBranch "test-rebase-gate"
    do! cleanupBranch bid

    do! Branches.createBranch bid "rebase-gate" PT.BranchId.Main
    let! ops = opsFor (namedSource "RebaseGate" 5)
    let! _ = Branches.storeDeltaOps bid ops
    do! Branches.recordNameBases bid PT.BranchId.Main ops

    // clean before divergence: the name isn't in main, so base "" == main's current "".
    let! c0 = nameConflicts bid
    Expect.isEmpty c0 "clean before any divergence"

    // simulate main having changed that name since the fork: stale the recorded base.
    do! staleNameBases bid
    let! c1 = nameConflicts bid
    Expect.isNonEmpty c1 "conflict detected (main's current hash != the stale base)"

    // rebase accepts main -> base := main's current -> no conflict, merge unblocked.
    let! _ = rebase bid
    let! c2 = nameConflicts bid
    Expect.isEmpty c2 "rebase cleared the conflict"

    do! cleanupBranch bid
  }

/// Locks the branch-transfer import path (what scmImportBranchOps does): register a branch, store
/// its ops effective=0 + tag, and re-derive the per-name bases against THIS instance's main. The
/// cross-instance invariant is that a branch stays a branch on the receiving side.
let branchTransferImportReDerivesBases =
  testTask
    "importing a branch's ops recreates it isolated + re-derives its per-name bases locally" {
    let dst = testBranch "test-xfer-dst"
    do! cleanupBranch dst

    // simulate scmImportBranchOps: register + store ops + re-derive bases.
    do! Branches.createBranch dst "xfer" PT.BranchId.Main
    let! ops = opsFor (namedSource "XferTest" 8)
    let! _ = Branches.storeDeltaOps dst ops
    do! Branches.recordNameBases dst PT.BranchId.Main ops

    let! loaded = Branches.loadDeltaOps dst
    let overlay = PM.withExtraOps pmPT loaded
    let! resolved = overlay.findFn (fooLocIn "XferTest") |> Ply.toTask
    Expect.isSome resolved "imported branch resolves its fn"
    let! onCore = pmPT.findFn (fooLocIn "XferTest") |> Ply.toTask
    Expect.isNone onCore "and it did NOT leak into core"

    // per-name bases were re-derived (against local main), so merge/rebase work on this instance.
    let! bases = baseNames dst
    Expect.isNonEmpty bases "per-name bases re-derived on import"

    do! cleanupBranch dst
  }

/// A name a branch binds ONLY by resolving a conflict still needs a per-name base: the base is what
/// the conflict detector needs to prove BOTH sides moved. Without one that name can never conflict
/// again, and `dark diff` renders it as `+ new` rather than a change. `bindingFromOp` counts
/// `Resolve` as a binding, and `recordNameBases` has to agree with it.
let resolveAloneRecordsANameBase =
  testTask "a name bound only by Resolve still gets a per-name base" {
    let b = testBranch "test-resolve-base"

    do! cleanupBranch b
    do! Branches.createBranch b "resolve-base" PT.BranchId.Main

    // Borrow a real (location, target) off a SetName rather than hand-building a hash: what is under
    // test is which op SHAPE gets counted, not what a Reference looks like.
    let! ops = opsFor (namedSource "ResolveBaseTest" 7)

    let binding =
      ops
      |> List.tryPick (fun op ->
        match op with
        | PT.PackageOp.SetName(loc, target, _) -> Some(loc, target)
        | _ -> None)

    Expect.isSome binding "the fixture produced a SetName to borrow a binding from"
    let (loc, target) = Option.get binding

    do!
      Branches.recordNameBases
        b
        PT.BranchId.Main
        [ PT.PackageOp.Decision(
            "decision-for-the-base-test",
            loc,
            "",
            PT.DecisionKind.Override target
          ) ]

    let! recorded = baseNames b
    Expect.equal
      recorded
      [ loc.name ]
      "the Resolve on its own recorded a base for the name"

    do! cleanupBranch b
  }

/// Locks per-name RESOLUTION (scm-spec 7). take-theirs untags the branch's SetName, so its overlay
/// falls back to the parent for that name; keep-mine leaves the branch binding it, re-stamped. Both
/// clear the conflict. The conflict is set up by staling the base, so nothing folds into shared main.
/// `resolve mine` must not rewrite any EXISTING op's `origin_ts`.
///
/// That stamp is portable: it is supposed to be byte-identical on every machine holding the op, because
/// it is what last-writer-wins compares. A branch's frontier ops travel, so re-stamping one locally makes
/// two peers resolve the same pair of bindings differently, permanently, with nothing to show why. The
/// resolution is recorded as a NEW `Decision`/`Override` instead, which carries its own stamp.
///
/// Asserted over EVERY op in the store rather than the ones we expect to be touched: the failure mode is
/// a stamp moving somewhere nobody was looking.
let resolveKeepMineDoesNotRestampSharedOps =
  testTask
    "resolve keep-mine authors an override and leaves every existing stamp alone" {
    let b = testBranch "test-resolve-no-restamp"
    let fqn = "Darklang.RestampTest.foo"

    do! cleanupBranch b
    do! Branches.createBranch b "restamp" PT.BranchId.Main
    let! ops = opsFor (namedSource "RestampTest" 7)
    let! _ = Branches.storeDeltaOps b ops
    do! Branches.recordNameBases b PT.BranchId.Main ops
    do! staleNameBases b

    let stamps () =
      Sql.query "SELECT id, origin_ts FROM package_ops"
      |> Sql.executeAsync (fun read -> (read.uuid "id", read.string "origin_ts"))

    let! before = stamps ()

    match! resolveKeepMine b fqn with
    | Error e -> failtest $"keep-mine failed: {e}"
    | Ok() -> ()

    let! after = stamps ()
    let afterById = Map.ofList after

    let moved =
      before
      |> List.filter (fun (id, ts) ->
        match Map.tryFind id afterById with
        | Some newTs -> newTs <> ts
        | None -> true)

    Expect.isEmpty
      moved
      "no op that already existed had its origin_ts rewritten (or vanished)"

    // And it did something: the resolution is a new op, not a no-op.
    Expect.isGreaterThan
      (List.length after)
      (List.length before)
      "keep-mine recorded the decision as a new op"

    do! cleanupBranch b
  }


let perNameResolutionMineTheirs =
  testTask
    "resolve take-theirs drops the branch's binding; keep-mine keeps it; both clear the conflict" {
    let bT = testBranch "test-resolve-theirs"
    let bM = testBranch "test-resolve-mine"
    let fqn = "Darklang.ResolveTest.foo"
    let setupConflict (b : PT.BranchId) =
      task {
        do! cleanupBranch b
        do! Branches.createBranch b "resolve" PT.BranchId.Main
        let! ops = opsFor (namedSource "ResolveTest" 5)
        let! _ = Branches.storeDeltaOps b ops
        do! Branches.recordNameBases b PT.BranchId.Main ops
        do! staleNameBases b
      }

    do! setupConflict bT
    let! c0 = nameConflicts bT
    Expect.isNonEmpty c0 "conflict present before take-theirs"
    match! resolveTakeTheirs bT fqn with
    | Error e -> failtest $"take-theirs failed: {e}"
    | Ok() -> ()
    let! c1 = nameConflicts bT
    Expect.isEmpty c1 "take-theirs cleared the conflict"
    let! loadedT = Branches.loadDeltaOps bT
    let overlayT = PM.withExtraOps pmPT loadedT
    let! resolvedT = overlayT.findFn (fooLocIn "ResolveTest") |> Ply.toTask
    Expect.isNone
      resolvedT
      "take-theirs: branch no longer binds foo (falls back to the parent)"

    do! setupConflict bM
    match! resolveKeepMine bM fqn with
    | Error e -> failtest $"keep-mine failed: {e}"
    | Ok() -> ()
    let! c2 = nameConflicts bM
    Expect.isEmpty c2 "keep-mine cleared the conflict"
    let! loadedM = Branches.loadDeltaOps bM
    let overlayM = PM.withExtraOps pmPT loadedM
    let! resolvedM = overlayM.findFn (fooLocIn "ResolveTest") |> Ply.toTask
    Expect.isSome resolvedM "keep-mine: branch still binds foo"

    do! cleanupBranch bT
    do! cleanupBranch bM
  }

/// Locks revive-on-reuse (createBranch upsert): re-creating an archived/merged branch id clears
/// archived_at + merged_at, so a review queue reused after reject/approve is active and visible
/// again. Parent stays first-write-wins.
let reuseBranchIdRevives =
  testTask
    "createBranch on an archived/merged id revives it; parent stays first-write-wins" {
    let b = testBranch "test-revive"
    do! cleanupBranch b
    do! Branches.createBranch b "revive" PT.BranchId.Main

    let flagsSet () : Task<int64> =
      Sql.query
        "SELECT count(*) AS n FROM branches
         WHERE id = @b AND (archived_at IS NOT NULL OR merged_at IS NOT NULL)"
      |> Sql.parameters [ "b", Sql.string (string b) ]
      |> Sql.executeRowAsync (fun read -> read.int64 "n")

    // Set the flags directly: this test is about createBranch's revive-on-reuse, and `archive` is
    // Dark now (SCM.Branches), so SQL keeps the setup on the test's actual subject.
    do!
      Sql.query
        "UPDATE branches SET archived_at = datetime('now'), merged_at = datetime('now')
         WHERE id = @b"
      |> Sql.parameters [ "b", Sql.string (string b) ]
      |> Sql.executeStatementAsync
    let! before = flagsSet ()
    Expect.equal before 1L "archived/merged flags are set before reuse"

    do! Branches.createBranch b "revive" PT.BranchId.Main
    let! after = flagsSet ()
    Expect.equal
      after
      0L
      "reuse revived the branch (archived_at + merged_at cleared)"

    // re-creating with a DIFFERENT parent must NOT change the recorded parent (first-write-wins).
    do! Branches.createBranch b "revive" (testBranch "some-other-parent")
    let! parent = Branches.parentOf b
    Expect.equal
      parent
      PT.BranchId.Main
      "parent stays first-write-wins across re-creation"

    do! cleanupBranch b
  }

/// ISOLATION: the branch author path folds a value's AddValue CONTENT into package_values (so eval
/// can read its rt_dval), but must NOT fold the SetName, so the NAME never lands in main's
/// `locations`. CORRECTNESS: after the content fold, `evaluateAllValues` must materialise an
/// EXPRESSION body's Dval into rt_dval, or it stays NULL and getValue returns nothing.
let branchValueContentFoldIsolatesName =
  testTask
    "folding a branch value's AddValue content populates package_values but NOT locations" {
    let source =
      "module Darklang.BranchValFoldTest\n\nval vv = Stdlib.Int64.add 3L 4L"
    let! ops = opsFor source
    let addValueOps =
      ops
      |> List.filter (fun op ->
        match op with
        | PT.PackageOp.AddValue _ -> true
        | _ -> false)
    Expect.isNonEmpty addValueOps "the val source produced an AddValue op"
    let valueHash =
      addValueOps
      |> List.pick (fun op ->
        match op with
        | PT.PackageOp.AddValue v ->
          let (PT.Hash h) = v.hash
          Some h
        | _ -> None)

    // Fold ONLY the AddValue (mirrors the branch author path's content-only fold).
    do! LibDB.PackageOpPlayback.applyOps addValueOps

    let! contentCount =
      Sql.query "SELECT count(*) AS n FROM package_values WHERE hash = @h"
      |> Sql.parameters [ "h", Sql.string valueHash ]
      |> Sql.executeRowAsync (fun read -> read.int64 "n")
    Expect.isGreaterThan
      contentCount
      0L
      "AddValue folded the content into package_values"

    let! locCount =
      Sql.query
        "SELECT count(*) AS n FROM locations WHERE name = 'vv' AND modules LIKE '%BranchValFoldTest%'"
      |> Sql.executeRowAsync (fun read -> read.int64 "n")
    Expect.equal
      locCount
      0L
      "the branch value's NAME is NOT in main locations (content-only fold keeps names branch-isolated)"

    // `applyOps` stores rt_dval NULL (see PackageOpPlayback.fs), so the branch author path must run
    // `evaluateAllValues` for an EXPRESSION-valued branch value to materialise its Dval.
    let builtins = Builtins.CliHost.Libs.Cli.builtinsToUse ()
    let! _ = Seed.evaluateAllValues builtins PM.rt
    let! (evaluated : Option<RT.PackageValue.PackageValue>) =
      LibDB.RuntimeTypes.Value.get (RT.Hash valueHash) |> Ply.toTask
    match evaluated with
    | Some pv ->
      Expect.equal
        pv.body
        (RT.DInt64 7L)
        "the expression-valued branch value materialised to 3+4=7 in rt_dval (bug #1 correctness guard)"
    | None ->
      Tests.failtest
        "rt_dval was NULL after evaluateAllValues -- expression-valued branch values would error (bug #1 regression)"

    do!
      Sql.query "DELETE FROM package_values WHERE hash = @h"
      |> Sql.parameters [ "h", Sql.string valueHash ]
      |> Sql.executeStatementAsync
  }

let branchExists =
  testTask
    "a branch is found by its registry row OR its op tags, and a typo is found by neither" {
    do! cleanupBranch (testBranch "beX")

    let! beforeAnything = Branches.exists (testBranch "beX")
    Expect.isFalse beforeAnything "an unknown branch does not exist"

    // Registered with no ops yet: this is what `dark switch` produces before you author anything, and
    // it has to count, or a fresh branch reads as a typo.
    do! Branches.createBranch (testBranch "beX") "" PT.BranchId.Main
    let! registeredOnly = Branches.exists (testBranch "beX")
    Expect.isTrue registeredOnly "registered with no ops still Branches.exists"

    // Tagged ops with no registry row: this is what a branch bundle from another machine looks like
    // before anything registers it locally. Also has to count.
    do! cleanupBranch (testBranch "beY")
    let! ops = opsFor (namedSource "BeY" 7)
    let! _ = Branches.storeDeltaOps (testBranch "beY") ops
    do!
      Sql.query "DELETE FROM branches WHERE id = @b"
      |> Sql.parameters [ "b", Sql.string (string (testBranch "beY")) ]
      |> Sql.executeStatementAsync
    let! taggedOnly = Branches.exists (testBranch "beY")
    Expect.isTrue taggedOnly "ops tagged with no registry row still Branches.exists"

    let! typo = Branches.exists (testBranch "beYY")
    Expect.isFalse typo "a prefix of a real branch is not that branch"

    do! cleanupBranch (testBranch "beX")
    do! cleanupBranch (testBranch "beY")
  }

let mergeCountsWhatItFlipped =
  testTask
    "markMergedEffective reports ops it flipped, not ops that were already effective" {
    do! cleanupBranch (testBranch "mcX")
    do! Branches.createBranch (testBranch "mcX") "" PT.BranchId.Main
    let! ops = opsFor (namedSource "McX" 11)
    let! _ = Branches.storeDeltaOps (testBranch "mcX") ops

    let! pending = countEffective (testBranch "mcX") 0
    Expect.isGreaterThan pending 0L "stored branch ops start effective=0"

    let! first = markMergedEffective (testBranch "mcX")
    Expect.equal first pending "the first merge reports exactly what it flipped"

    // Merging again flips NOTHING -- every tagged op is already effective -- so reporting the tag
    // count would be `MergeOutcome.merged` claiming work it did not do.
    let! second = markMergedEffective (testBranch "mcX")
    Expect.equal second 0L "a re-merge reports 0, not the number of tagged ops"

    do! cleanupBranch (testBranch "mcX")
  }

let importedOpsKeepTheirStamps =
  testTask
    "storeDeltaOpsStamped preserves an incoming op's origin_ts instead of re-stamping it" {
    do! cleanupBranch (testBranch "stX")
    do! Branches.createBranch (testBranch "stX") "" PT.BranchId.Main

    // A stamp from the far past, which the local authoring clock could never produce.
    let farPast = "2001-02-03T04:05:06.007Z"
    let! ops = opsFor (namedSource "StX" 13)
    let! _ =
      Branches.storeDeltaOpsStamped
        (testBranch "stX")
        (ops |> List.map (fun op -> (op, farPast)))

    let! stamps =
      Sql.query
        "SELECT DISTINCT p.origin_ts AS ts FROM package_ops p
         JOIN op_branches ob ON ob.op_id = p.id WHERE ob.branch_id = @b"
      |> Sql.parameters [ "b", Sql.string (string (testBranch "stX")) ]
      |> Sql.executeAsync (fun read -> read.string "ts")

    Expect.equal
      stamps
      [ farPast ]
      "every imported op keeps the stamp it arrived with -- re-stamping locally makes the IMPORTER \
       look like the author, so LWW resolves by who imported last rather than who edited last"

    do! cleanupBranch (testBranch "stX")
  }

let rebuildKeepsBranchPolicy =
  testTask "a projection rebuild re-folds branch-scoped propagation decisions" {
    do! cleanupBranch (testBranch "bpX")
    do! Branches.createBranch (testBranch "bpX") "" PT.BranchId.Main

    let loc : PT.PackageLocation =
      { owner = "Zz"; modules = [ "RebuildTest" ]; name = "pinned" }
    let decide =
      PT.PackageOp.Decision(
        "pin:RebuildTest.pinned:2026-01-02T00:00:00.000Z",
        loc,
        "deliberate",
        PT.DecisionKind.Propagation PT.PropagationPolicy.Pin
      )
    let! _ = Branches.storeDeltaOps (testBranch "bpX") [ decide ]

    let countPolicy () =
      Sql.query
        "SELECT count(*) AS n FROM propagation_policy
         WHERE branch_id = @b AND owner = 'Zz' AND modules = 'RebuildTest' AND name = 'pinned'"
      |> Sql.parameters [ "b", Sql.string (string (testBranch "bpX")) ]
      |> Sql.executeRowAsync (fun read -> read.int64 "n")

    let! stored = countPolicy ()
    Expect.equal
      stored
      1L
      "authoring the Decision on a branch folds a branch-scoped policy row"

    // A projection rebuild clears this table and re-folds only `effective = 1` ops, and branch ops
    // are `effective = 0` by design, so without an explicit re-fold the row never comes back.
    do! Sql.query "DELETE FROM propagation_policy" |> Sql.executeStatementAsync
    let! cleared = countPolicy ()
    Expect.equal cleared 0L "cleared, as a rebuild would"

    do! Branches.refoldBranchDecides ()
    let! restored = countPolicy ()
    Expect.equal
      restored
      1L
      "the rebuild path re-folds branch decisions from the log"

    do!
      Sql.query
        "DELETE FROM propagation_policy WHERE owner = 'Zz' AND modules = 'RebuildTest'"
      |> Sql.executeStatementAsync
    do! cleanupBranch (testBranch "bpX")
  }

let branchPMIsPerBranch =
  testTask "ptForBranch answers about a branch this process is NOT on" {
    do! cleanupBranch (testBranch "pfA")
    do! cleanupBranch (testBranch "pfB")
    do! Branches.createBranch (testBranch "pfA") "" PT.BranchId.Main
    do! Branches.createBranch (testBranch "pfB") "" PT.BranchId.Main

    let! opsA = opsFor (namedSource "PfA" 42)
    let! opsB = opsFor (namedSource "PfB" 99)
    let! _ = Branches.storeDeltaOps (testBranch "pfA") opsA
    let! _ = Branches.storeDeltaOps (testBranch "pfB") opsB

    // Sit on A. This is the state a `dark --branch pfA ...` process boots into.
    PM.selectBranch (testBranch "pfA")
    Expect.equal (PM.currentBranchId ()) (testBranch "pfA") "process is on pfA"

    // Asking about a branch we're NOT on is what a process-global overlay alone cannot do, and what
    // the LSP and any daemon will need.
    let! aFromA =
      (PM.ptForBranch (testBranch "pfA")).findFn (fooLocIn "PfA") |> Ply.toTask
    Expect.isSome aFromA "on pfA, pfA's fn resolves"

    let! bFromA =
      (PM.ptForBranch (testBranch "pfB")).findFn (fooLocIn "PfB") |> Ply.toTask
    Expect.isSome bFromA "while ON pfA, pfB's fn still resolves via ptForBranch"

    let! aFromB =
      (PM.ptForBranch (testBranch "pfB")).findFn (fooLocIn "PfA") |> Ply.toTask
    Expect.isNone aFromB "pfB's overlay does not contain pfA's fn"

    let! aFromMain =
      (PM.ptForBranch PT.BranchId.Main).findFn (fooLocIn "PfA") |> Ply.toTask
    Expect.isNone aFromMain "and main sees neither"

    // Switching is a process operation, not a restart: what `dark switch` needs in the REPL.
    PM.selectBranch (testBranch "pfB")
    Expect.equal
      (PM.currentBranchId ())
      (testBranch "pfB")
      "process moved to pfB without restarting"
    let! bAfterSwitch =
      (PM.ptForBranch (PM.currentBranchId ())).findFn (fooLocIn "PfB") |> Ply.toTask
    Expect.isSome bAfterSwitch "the active overlay followed the switch"
    let! aAfterSwitch =
      (PM.ptForBranch (PM.currentBranchId ())).findFn (fooLocIn "PfA") |> Ply.toTask
    Expect.isNone aAfterSwitch "and stopped answering about the branch we left"

    PM.selectBranch PT.BranchId.Main // leave clean for other tests (process-global)
    do! cleanupBranch (testBranch "pfA")
    do! cleanupBranch (testBranch "pfB")
  }

let branchNamesResolveButDontShadowMain =
  testTask
    "a branch supplies names for hashes main can't name, and never relabels ones it can" {
    do! cleanupBranch (testBranch "lnX")
    do! Branches.createBranch (testBranch "lnX") "" PT.BranchId.Main

    let! ops = opsFor (namedSource "LnX" 42)
    let! _ = Branches.storeDeltaOps (testBranch "lnX") ops

    let branchHash =
      ops
      |> List.tryPick (fun op ->
        match op with
        | PT.PackageOp.SetName(l, target, _) when l.name = "foo" -> Some target.hash
        | _ -> None)
      |> Option.get

    // Main cannot name this hash: a branch's SetNames never fold into `locations`, so without the
    // overlay the caller has nothing to render but 64 hex characters.
    let! fromMain = PM.pt.getFnLocations branchHash |> Ply.toTask
    Expect.isEmpty fromMain "main has no name for a branch-authored hash"

    PM.selectBranch (testBranch "lnX")
    let onBranch =
      PM.locationsFor (PM.currentBranchId ()) PT.ItemKind.Fn branchHash []
    Expect.equal
      (onBranch |> List.map (fun l -> l.name))
      [ "foo" ]
      "the overlay supplies the name main is missing"

    // ... but main WINS when it has an answer. Identical content is one item, so a hash is routinely
    // live at several names, and preferring the branch's renders a main item under a branch label.
    let mainLoc : PT.PackageLocation =
      { owner = "Darklang"; modules = [ "SomeMainModule" ]; name = "mainName" }
    let withMain =
      PM.locationsFor (PM.currentBranchId ()) PT.ItemKind.Fn branchHash [ mainLoc ]
    Expect.equal
      (withMain |> List.map (fun l -> l.name) |> List.tryHead)
      (Some "mainName")
      "main's name comes first, so a duplicated body is never relabelled to the branch's"

    let asType =
      PM.locationsFor (PM.currentBranchId ()) PT.ItemKind.Type branchHash []
    Expect.isEmpty asType "the overlay answers per kind, not per hash alone"

    PM.selectBranch PT.BranchId.Main
    let offBranch = PM.locationsFor PT.BranchId.Main PT.ItemKind.Fn branchHash []
    Expect.isEmpty offBranch "off the branch, the name is gone again (isolation)"

    do! cleanupBranch (testBranch "lnX")
  }


/// The three ways a run picks its branch. Each tier is scoped tighter than the one below on purpose:
/// the FLAG is this command, the ENV is this SHELL, the config is this machine. The env tier is what
/// lets several agents work on several branches at once without fighting over the single config key
/// `dark switch` writes.
let branchResolutionOrder =
  testTask "the flag beats DARK_BRANCH beats the stored branch" {
    let pick
      (flag : Option<string>)
      (env : Option<string>)
      (stored : Option<string>)
      =
      match flag with
      | Some f -> Some f
      | None ->
        match env with
        | Some e -> Some e
        | None -> stored

    Expect.equal (pick (Some "f") (Some "e") (Some "s")) (Some "f") "the flag wins"
    Expect.equal (pick None (Some "e") (Some "s")) (Some "e") "then the env"
    Expect.equal (pick None None (Some "s")) (Some "s") "then the stored branch"
    Expect.equal (pick None None None) None "and main is the absence of all three"
  }


/// A merge that does not travel: the merged OPS already cross (they are main ops once merged, and
/// the two mains converge on identical hashes), but without the event the FACT of the merge does
/// not, so a colleague's copy of the branch still lists as live work they could keep authoring on.
let branchEventMarksMerged =
  testTask "a BranchEvent(Merged) op folds to marking that branch merged" {
    let branchId = testBranch "test-branch-event-merged"
    do! cleanupBranch branchId
    do! Branches.createBranch branchId "event-proof" PT.BranchId.Main

    let! before = isMerged branchId
    Expect.isFalse before "not merged before the event"

    let op =
      PT.PackageOp.BranchEvent(branchId, PT.Merged [], "2026-01-01T00:00:00.000Z")
    let! _ = LibDB.Inserts.insertAndApplyOps [ op ]

    let! after = isMerged branchId
    Expect.isTrue after "the event folded, so the branch reads as merged"

    // Monotonic: this is what lets the event travel with no stamp on `branches` to arbitrate with, and
    // what makes re-receiving it on a third machine harmless.
    let! _ = LibDB.Inserts.insertAndApplyOps [ op ]
    let! twice = isMerged branchId
    Expect.isTrue twice "applying it again lands in the same place"

    do! cleanupBranch branchId
  }


/// An op is one row whatever authored it, so a branch's op and main's identical op share an id. Main
/// authoring it is main saying it runs here: the row flips effective and folds, and the tag goes. It used
/// to be `INSERT OR IGNORE`, so the author's op did nothing while the CLI said it had, which is how
/// `dark deprecate` came to report "Deprecated" over a fn that kept running.
let mainRetakesABranchsOp =
  testTask "authoring on main an op a branch already holds makes it effective, untagged, and live" {
    let branchId = testBranch "test-branch-main-retake"
    do! cleanupBranch branchId
    do! Branches.createBranch branchId "retake-proof" PT.BranchId.Main

    let! ops = opsFor (namedSource "MainRetake" 42)
    let ids = ops |> List.map (fun op -> string (LibDB.Inserts.computeOpHash op))
    let! _ = Branches.storeDeltaOps branchId ops
    let! onMainBefore = pmPT.findFn (fooLocIn "MainRetake") |> Ply.toTask
    Expect.isNone onMainBefore "held by the branch only, main cannot see it"

    let! inserted = LibDB.Inserts.insertAndApplyOps ops
    Expect.equal inserted (int64 (List.length ops)) "every op counted as taken, none as a duplicate"

    let! effectiveTagged =
      Sql.query
        "SELECT
           sum(p.effective) AS eff,
           (SELECT count(*) FROM op_branches WHERE branch_id = @b) AS tagged
         FROM package_ops p WHERE p.id IN (SELECT value FROM json_each(@ids))"
      |> Sql.parameters
        [ "b", Sql.string (string branchId)
          "ids", Sql.string (System.Text.Json.JsonSerializer.Serialize ids) ]
      |> Sql.executeRowAsync (fun read -> (read.int64 "eff", read.int64 "tagged"))
    Expect.equal effectiveTagged (int64 (List.length ops), 0L) "all effective, and no tag left on any"

    let! onMainAfter = pmPT.findFn (fooLocIn "MainRetake") |> Ply.toTask
    Expect.isSome onMainAfter "and main resolves it"

    // The tag is gone, so cleanupBranch would not find the rows; drop them by id.
    do!
      Sql.query
        "DELETE FROM locations WHERE op_id IN (SELECT value FROM json_each(@ids))"
      |> Sql.parameters [ "ids", Sql.string (System.Text.Json.JsonSerializer.Serialize ids) ]
      |> Sql.executeStatementAsync
    do!
      Sql.query "DELETE FROM package_ops WHERE id IN (SELECT value FROM json_each(@ids))"
      |> Sql.parameters [ "ids", Sql.string (System.Text.Json.JsonSerializer.Serialize ids) ]
      |> Sql.executeStatementAsync
    LibDB.Caching.invalidateAll ()
    do! cleanupBranch branchId
  }


/// A merged or archived branch is finished. Authoring on it used to go through `createBranch`, whose
/// upsert clears `merged_at`, so a process still holding the id after a merge elsewhere put its next
/// edit on a branch nothing would merge again, and listed it live. Now it refuses and says so.
let authoringOnAFinishedBranchRefuses =
  testTask "authoring on a merged branch is refused rather than reviving it" {
    let branchId = testBranch "test-branch-finished-refuses"
    do! cleanupBranch branchId
    do! Branches.createBranch branchId "finished-proof" PT.BranchId.Main
    do!
      Sql.query "UPDATE branches SET merged_at = datetime('now') WHERE id = @b"
      |> Sql.parameters [ "b", Sql.string (string branchId) ]
      |> Sql.executeStatementAsync

    // An empty op list reaches the guard before anything else, so it exercises exactly that.
    let! outcome =
      darkUnitResult $"Darklang.SCM.PackageOps.add {darkBranch branchId} []"
    match outcome with
    | Ok() -> failtest "the edit was accepted onto a merged branch"
    | Error e -> Expect.stringContains e "merged or archived" "and the refusal says why"

    let! still = isMerged branchId
    Expect.isTrue still "the branch stays merged"
    do! cleanupBranch branchId
  }


/// `SCM.PackageOps.liveBindingFor` is THE branch-aware read of a live binding: the chain overlay first,
/// then main's projection. Direct reads of `locations` answered about main from a branch, plausibly, and
/// that was the branch's recurring bug class.
let liveBindingReadsTheBranchThenMain =
  testTask "liveBindingFor answers the branch's binding, and main's where the branch is silent" {
    let branchId = testBranch "test-branch-live-binding"
    do! cleanupBranch branchId
    do! Branches.createBranch branchId "live-binding-proof" PT.BranchId.Main

    let! ops = opsFor (namedSource "LiveBind" 42)
    let! _ = Branches.storeDeltaOps branchId ops
    let branchHash =
      ops
      |> List.tryPick (fun op ->
        match op with
        | PT.PackageOp.AddFn f -> (let (PT.Hash h) = f.hash in Some h)
        | _ -> None)
      |> Option.defaultValue ""

    let hashOrNone (branch : string) (owner : string) (modules : string) (name : string) =
      $"(match Darklang.SCM.PackageOps.liveBindingFor {branch} "
      + $"(Darklang.LanguageTools.ProgramTypes.PackageLocation {{ owner = \"{owner}\"; "
      + $"modules = [ \"{modules}\" ]; name = \"{name}\" }}) with "
      + "| Some b -> b.hash | None -> \"none\")"
    let main = darkBranch PT.BranchId.Main

    let! answers =
      darkStringList (
        "[ "
        + hashOrNone (darkBranch branchId) "Darklang" "LiveBind" "foo"
        + ", "
        + hashOrNone main "Darklang" "LiveBind" "foo"
        + ", "
        + hashOrNone (darkBranch branchId) "Darklang" "Stdlib.List" "map"
        + ", "
        + hashOrNone main "Darklang" "Stdlib.List" "map"
        + " ]"
      )

    match answers with
    | [ onBranch; onMain; mainNameFromBranch; mainNameFromMain ] ->
      Expect.equal onBranch branchHash "on the branch, the branch's hash"
      Expect.equal onMain "none" "main does not have the branch's name"
      Expect.notEqual mainNameFromMain "none" "a name main has answers from main"
      Expect.equal mainNameFromBranch mainNameFromMain "and answers the same from the branch"
    | other -> failtest $"expected four answers, got {other}"

    do! cleanupBranch branchId
  }


/// The other half of `mainRetakesABranchsOp`: storing on a branch an op main already runs must not tag it.
/// Every draft query excludes tagged ids, so a tag on main's own op hid it from `status` and `commit`.
let aBranchNeverTagsWhatMainRuns =
  testTask "storing an op main already runs on a branch leaves it untagged" {
    let branchId = testBranch "test-branch-no-tag-on-main"
    do! cleanupBranch branchId
    do! Branches.createBranch branchId "no-tag-proof" PT.BranchId.Main

    let! ops = opsFor (namedSource "NoTagOnMain" 42)
    let ids = ops |> List.map (fun op -> string (LibDB.Inserts.computeOpHash op))
    let! _ = LibDB.Inserts.insertAndApplyOps ops
    let! _ = Branches.storeDeltaOps branchId ops

    let! tagged =
      Sql.query "SELECT count(*) AS n FROM op_branches WHERE branch_id = @b"
      |> Sql.parameters [ "b", Sql.string (string branchId) ]
      |> Sql.executeRowAsync (fun read -> read.int64 "n")
    Expect.equal tagged 0L "nothing main runs was tagged"
    let! draft = Queries.getDraftOps ()
    let draftIds = draft |> List.map (fun op -> string (LibDB.Inserts.computeOpHash op))
    for id in ids do
      Expect.contains draftIds id "and main's draft still lists its own op"

    // A fresh op on the same branch is tagged as before.
    let! fresh = opsFor (namedSource "NoTagOnMainFresh" 43)
    let! stored = Branches.storeDeltaOps branchId fresh
    Expect.equal stored (int64 (List.length fresh)) "fresh ops are stored"
    let! taggedNow =
      Sql.query "SELECT count(*) AS n FROM op_branches WHERE branch_id = @b"
      |> Sql.parameters [ "b", Sql.string (string branchId) ]
      |> Sql.executeRowAsync (fun read -> read.int64 "n")
    Expect.equal taggedNow (int64 (List.length fresh)) "and tagged"

    do!
      Sql.query "DELETE FROM locations WHERE op_id IN (SELECT value FROM json_each(@ids))"
      |> Sql.parameters [ "ids", Sql.string (System.Text.Json.JsonSerializer.Serialize ids) ]
      |> Sql.executeStatementAsync
    do!
      Sql.query "DELETE FROM package_ops WHERE id IN (SELECT value FROM json_each(@ids))"
      |> Sql.parameters [ "ids", Sql.string (System.Text.Json.JsonSerializer.Serialize ids) ]
      |> Sql.executeStatementAsync
    LibDB.Caching.invalidateAll ()
    do! cleanupBranch branchId
  }


/// Merging a branch into a non-main parent retags its ops onto the parent. Its name BASES have to move
/// too: a name without a base can never conflict again, so a grandparent moving one of the child's names
/// was invisible at the parent's merge.
let retagMovesTheBasesToo =
  testTask "retagging a child's frontier onto its parent carries the child's name bases" {
    let parent = testBranch "test-branch-bases-parent"
    let child = testBranch "test-branch-bases-child"
    do! cleanupBranch child
    do! cleanupBranch parent
    do! Branches.createBranch parent "bases-parent" PT.BranchId.Main
    do! Branches.createBranch child "bases-child" parent

    let! ops = opsFor (namedSource "BasesMove" 42)
    let! _ = Branches.storeDeltaOps child ops
    do!
      Sql.query
        "INSERT OR IGNORE INTO branch_name_bases (branch_id, owner, modules, name, base_hash)
         VALUES (@b, 'Darklang', 'BasesMove', 'foo', 'the-fork-hash')"
      |> Sql.parameters [ "b", Sql.string (string child) ]
      |> Sql.executeStatementAsync

    let! _ = retagFrontierToParent child parent

    let baseNames (b : PT.BranchId) =
      Sql.query "SELECT name FROM branch_name_bases WHERE branch_id = @b"
      |> Sql.parameters [ "b", Sql.string (string b) ]
      |> Sql.executeAsync (fun read -> read.string "name")
    let! onParent = baseNames parent
    let! onChild = baseNames child
    Expect.equal onParent [ "foo" ] "the parent now holds the child's base for the name"
    Expect.isEmpty onChild "and the child, finished, holds none"

    do! cleanupBranch child
    do! cleanupBranch parent
  }


/// `lookupRef` says WHY it missed, because only one kind of miss should start a branch. A `None` used to
/// stand for a foreign uuid, an ambiguous prefix and an unknown name alike, and `--branch <a peer's id>`
/// silently started a branch named after the id.
let refLookupSaysWhyItMissed =
  testTask "lookupRef distinguishes a foreign id, an ambiguous prefix and an unknown name" {
    let one = PT.BranchId.Id(System.Guid "aaaaaaaa-0000-4000-8000-000000000001")
    let two = PT.BranchId.Id(System.Guid "aaaaaaaa-0000-4000-8000-000000000002")
    do! cleanupBranch one
    do! cleanupBranch two
    do! Branches.createBranch one "ref-one" PT.BranchId.Main
    do! Branches.createBranch two "ref-two" PT.BranchId.Main

    let! byName = Branches.lookupRef "ref-one"
    Expect.equal byName (Branches.Found one) "a live name is found"
    let! byId = Branches.lookupRef (string one)
    Expect.equal byId (Branches.Found one) "a full id is found"
    let! byPrefix = Branches.lookupRef "aaaaaaaa"
    Expect.equal byPrefix (Branches.Ambiguous "aaaaaaaa") "a prefix two branches share is ambiguous"
    let foreign = PT.BranchId.Id(System.Guid.NewGuid())
    let! unknown = Branches.lookupRef (string foreign)
    Expect.equal unknown (Branches.UnknownId foreign) "a full id nobody has is a foreign id, not a name"
    let! noSuch = Branches.lookupRef "no-such-branch-here"
    Expect.equal noSuch (Branches.NoSuchName "no-such-branch-here") "and only a name nobody has is a name"

    do! cleanupBranch one
    do! cleanupBranch two
  }


/// The overlay pairs an item with its SetName by the hash the item carries, not by adjacency. Chain ops
/// are ordered by origin_ts across authors, so after a bundle import two authors' ops interleave, and
/// "the Add before this SetName" was the other author's: your name resolved to their body.
let overlayPairsByHashNotAdjacency =
  testTask "two authors' interleaved ops resolve each name to its own body" {
    let! opsA = opsFor (namedSource "InterleaveA" 42)
    let! opsB = opsFor (namedSource "InterleaveB" 99)
    let adds ops =
      ops |> List.filter (fun op -> match op with PT.PackageOp.AddFn _ -> true | _ -> false)
    let sets ops =
      ops |> List.filter (fun op -> match op with PT.PackageOp.SetName _ -> true | _ -> false)
    // [addA; addB; setA; setB]: adjacency pairs setA with addB.
    let interleaved = adds opsA @ adds opsB @ sets opsA @ sets opsB
    let overlay = PM.withExtraOps pmPT interleaved

    let bodyOf (m : string) =
      task {
        let! found = overlay.findFn (fooLocIn m) |> Ply.toTask
        let hash = Expect.wantSome found $"{m}.foo resolves"
        let! fn = overlay.getFn hash |> Ply.toTask
        let fn = Expect.wantSome fn $"{m}.foo's item is in the overlay"
        return! runFooBody overlay [ PT.PackageOp.AddFn fn ]
      }
    let! a = bodyOf "InterleaveA"
    let! b = bodyOf "InterleaveB"
    Expect.equal a (RT.DInt64 42L) "A's name runs A's body"
    Expect.equal b (RT.DInt64 99L) "and B's runs B's"
  }


/// A bundle op this build cannot decode is stored raw and inert beside the ones it can, the way main
/// sync stores such ops, rather than refusing the whole bundle. The next build that reads it applies it.
let anUndecodableBundleOpIsKeptNotRefused =
  testTask "a branch bundle with one unreadable op stores it inert and keeps the rest" {
    let branchId = testBranch "test-branch-raw-op"
    do! cleanupBranch branchId
    do! Branches.createBranch branchId "raw-proof" PT.BranchId.Main

    let! ops = opsFor (namedSource "RawOp" 42)
    let! stored = Branches.storeDeltaOpsStamped branchId (ops |> List.map (fun op -> (op, "2026-01-01T00:00:00.000Z")))
    let alien = System.Guid.NewGuid()
    let! storedRaw =
      Branches.storeDeltaBlobsStamped branchId [ (alien, [| 0xFFuy; 0x39uy; 0x07uy |], "2026-01-01T00:00:01.000Z") ]
    Expect.equal storedRaw 1L "the raw op is stored"

    let! (rows, tags) =
      Sql.query
        "SELECT
           (SELECT count(*) FROM package_ops WHERE id = @a AND effective = 0) AS r,
           (SELECT count(*) FROM op_branches WHERE op_id = @a AND branch_id = @b) AS t"
      |> Sql.parameters [ "a", Sql.string (string alien); "b", Sql.string (string branchId) ]
      |> Sql.executeRowAsync (fun read -> (read.int64 "r", read.int64 "t"))
    Expect.equal (rows, tags) (1L, 1L) "inert, and on the branch"

    let! loaded = Branches.loadDeltaOps branchId
    Expect.equal (int64 (List.length loaded)) stored "the readable ops load; the raw one is skipped, not fatal"

    do! cleanupBranch branchId
  }


/// The receiving side has no obligation to know every branch its peers have. Branch ids travel with
/// a bundle, so the branches you actually share match; the rest are none of this store's business.
let branchEventForUnknownBranchIsIgnored =
  testTask "a BranchEvent for a branch this store has never seen folds to nothing" {
    // A real id that no `branches` row carries. Since the op field became a `BranchId`, "not an id at
    // all" is no longer representable, so unknown-but-well-formed is the only case left to cover.
    let unknown = testBranch "test-branch-that-does-not-exist"
    let op = PT.PackageOp.BranchEvent(unknown, PT.Merged [], "2026-01-01T00:00:00.000Z")

    let! _ = LibDB.Inserts.insertAndApplyOps [ op ]

    let! rows =
      Sql.query "SELECT COUNT(*) as n FROM branches WHERE id = @b"
      |> Sql.parameters [ "b", Sql.string (string unknown) ]
      |> Sql.executeRowAsync (fun read -> read.int64 "n")
    Expect.equal rows 0L "no branch was conjured up to receive the event"
  }


/// The fold marks ops applied by PREDICATE, not by id, and folding an op can change that predicate.
///
/// A merge event arriving from another machine flips its branch's frontier to effective=1 mid-fold.
/// An applied=1 sweep running afterwards marks those ops applied without anything having folded
/// them, so the branch reads `[merged]` next to a main that does not have its code. The sweep runs
/// BEFORE the fold for exactly this reason.
let foldDoesNotStrandOpsItMadeEffective =
  testTask
    "an op that makes other ops effective does not leave them applied-but-unfolded" {
    let branchId = testBranch "test-branch-stranded"
    do! cleanupBranch branchId
    do! Branches.createBranch branchId "stranded-proof" PT.BranchId.Main

    let! ops = opsFor (namedSource "BranchTestStranded" 77)
    let! _ = Branches.storeDeltaOps branchId ops

    let! pending = countEffective branchId 0
    Expect.isGreaterThan pending 0L "the branch's ops start effective=0"

    // The event has to arrive the way a SYNC delivers it: inserted unapplied-and-effective, then
    // folded by `applyUnappliedOps`, which puts the flip and the sweep inside ONE pass. Authoring it
    // locally folds it in its own call, and a later pass picks the branch ops up regardless.
    // The event names what the merge moved: with an empty list it would, correctly, fold nothing.
    let mergedIds = ops |> List.map LibDB.Inserts.computeOpHash
    let event =
      PT.PackageOp.BranchEvent(branchId, PT.Merged mergedIds, "2026-01-01T00:00:00.000Z")
    let eventId = LibDB.Inserts.computeOpHash event
    let eventBlob = BS.PT.PackageOp.serialize eventId event
    do!
      Sql.query
        "INSERT OR IGNORE INTO package_ops (id, op_blob, applied, effective, origin_ts)
         VALUES (@id, @blob, 0, 1, @ts)"
      |> Sql.parameters
        [ "id", Sql.uuid eventId
          "blob", Sql.bytes eventBlob
          "ts", Sql.string "2026-01-01T00:00:00.000Z" ]
      |> Sql.executeStatementAsync

    let! _ = Seed.applyUnappliedOps ()

    // Asserted over the ids captured BEFORE the event, because the event clears the branch tags: a
    // query that looks them up by tag afterwards finds nothing and passes for the wrong reason.
    let ids = ops |> List.map (fun op -> string (LibDB.Inserts.computeOpHash op))
    let! unfolded =
      Sql.query
        $"""SELECT COUNT(*) as n FROM package_ops
            WHERE applied = 0 AND id IN ({ids |> List.map (fun i -> $"'{i}'") |> String.concat ", "})"""
      |> Sql.executeRowAsync (fun read -> read.int64 "n")
    Expect.equal unfolded 0L "every op the event made effective was actually folded"

    let! inMain = pmPT.findFn (fooLocIn "BranchTestStranded") |> Ply.toTask
    Expect.isSome
      inMain
      "the merged branch's fn resolves on main after the event folded"

    do! cleanupBranch branchId
  }



/// Dark code outside the SCM silos must not query `locations`. It is MAIN's projection and has no
/// `branch_id`, so a read that goes straight to it answers about main while the caller stands on a
/// branch, and answers plausibly. The overlay helpers in `SCM.PackageOps` are the branch-aware way.
///
/// Checked by reading the source, because the failure is invisible at run time on a single-branch
/// store. `matter.dark` is exempt: a relay holds no branches, so main's projection IS its answer.
let noDirectLocationsReadsOutsideTheSilos =
  testTask "only the SCM silos query `locations` from Dark" {
    let root = System.IO.Path.Combine("..", "packages", "darklang")

    // Two exemptions, both deliberate. The SCM silos OWN the table, so they are the code the rule
    // points everyone else at. `matter.dark` is the relay: it serves the public package browser and
    // its counts, which are main's by definition -- the relay has no branch to be standing on, so a
    // main-scoped read is the correct answer there rather than a drifted one.
    let exempt (path : string) : bool =
      let p = path.Replace("\\", "/")
      p.Contains "/scm/" || p.EndsWith "matter.dark"

    let offenders =
      System.IO.Directory.GetFiles(
        root,
        "*.dark",
        System.IO.SearchOption.AllDirectories
      )
      |> Array.filter (exempt >> not)
      |> Array.filter (fun path ->
        System.IO.File.ReadAllLines path
        |> Array.exists (fun line ->
          let t = line.Trim()
          not (t.StartsWith "//")
          && (t.Contains "FROM locations" || t.Contains "JOIN locations")))
      |> Array.map (fun p -> p.Replace("\\", "/"))
      |> List.ofArray

    Expect.isEmpty
      offenders
      "no Dark file outside packages/darklang/scm may read `locations` directly -- \
       it is main-only, so it answers about main while you stand on a branch. \
       Use the overlay helpers in SCM.PackageOps."
  }

/// No SQL in Dark may compare a branch column against the literal `'main'`.
///
/// Dead is the good case. The bad one is a comparison that half-fires, which is what those two did on
/// this dev store: it carries BOTH spellings in `branches.parent_id`, so the guard fired for the stale
/// rows and not the live ones.
///
/// Comments are exempt: explaining the trap is not falling into it.
let noMainLiteralInDarkSql =
  testTask "no Dark SQL compares a branch column against the literal 'main'" {
    let root = System.IO.Path.Combine("..", "packages", "darklang")

    let offenders =
      System.IO.Directory.GetFiles(
        root,
        "*.dark",
        System.IO.SearchOption.AllDirectories
      )
      |> Array.collect (fun path ->
        let shown = path.Replace("\\", "/")

        System.IO.File.ReadAllLines path
        |> Array.mapi (fun i line -> (i + 1, line))
        |> Array.filter (fun (_, line) ->
          let t = line.Trim()
          not (t.StartsWith "//") && t.Contains "'main'")
        |> Array.map (fun (n, _) -> shown + ":" + string n))
      |> List.ofArray

    Expect.isEmpty
      offenders
      "SQL in Dark must bind main's id as a parameter, never compare against the literal 'main'. \
       `branches.parent_id` holds a UUID, so the literal matches nothing and the comparison is dead. \
       Use `SCM.Ids.mainBranchId` and pass it as a bound parameter."
  }


/// A branch ID never reaches a person. Names do.
///
/// The rule Phase 1 is built on: ids everywhere inside, a NAME wherever a human reads it. It has been
/// broken three times, each time in a string nothing tests -- the conflict candidate labels, the
/// `BranchEvent` line in `dark show`, and the `rebase` and `resolve` messages, which greeted you with
/// `rebased onto "00000000-0000-0000-0000-000000000001"`.
///
/// Textual, and a tripwire rather than a proof: it knows the spellings we actually use for a branch id,
/// and only in the two forms that reach a person (`Error` and `Dval.string`). Resolve the name with
/// `SCM.PackageOps.branchName`, which falls back to the id for a branch this store has no row for.
let branchIdsNeverReachAPerson =
  testTask "no user-facing F# string interpolates a branch id" {
    // `..` is the REPO ROOT here, the same as the `locations` test above uses for `packages/`.
    let roots =
      [ System.IO.Path.Combine("..", "backend", "src", "LibDB")
        System.IO.Path.Combine("..", "backend", "src", "Builtins") ]

    // A root that is not there means this test scanned nothing and passed, which is worse than a
    // failure. It is how the first version of it "passed" against a bug sitting in the tree.
    for root in roots do
      Expect.isTrue
        (System.IO.Directory.Exists root)
        $"{root} does not exist, so this test would scan nothing"

    let pattern =
      System.Text.RegularExpressions.Regex(
        // `.*`, not `[^"]*`: these strings quote the branch themselves (`branch \"{name}\"`), so a
        // character class that stops at a quote stops before the interpolation every time. That is
        // how the first version of this test passed against the very bug it was written for.
        @"(Error|Dval\.string) \$"".*\{(branchId|parentId|sourceId|targetId|bid)\}"
      )

    let offenders =
      roots
      |> List.collect (fun root ->
        System.IO.Directory.GetFiles(
          root,
          "*.fs",
          System.IO.SearchOption.AllDirectories
        )
        |> Array.collect (fun path ->
          System.IO.File.ReadAllLines path
          |> Array.mapi (fun i line -> (i + 1, line))
          |> Array.filter (fun (_, line) -> pattern.IsMatch line)
          |> Array.map (fun (n, _) -> $"  {path.Replace('\\', '/')}:{n}"))
        |> List.ofArray)

    Expect.isEmpty
      offenders
      "These strings put a branch ID in front of a person:\n\
       Resolve it with `SCM.PackageOps.branchName` first. A person typed a name; showing them a uuid \
       back is the boundary this branch Branches.exists to draw."
  }


/// One LOCATION can hold a fn AND a value at once, so a conflict is identified by (name, item kind).
///
/// Raised in a coworker's review as B1 and tagged FIX; the fold's UPDATE matched on the name alone for
/// two months, so overriding one kind silently closed the other. The one it closed never got an answer:
/// it leaves `dark conflicts` while its binding is still contested.
let overrideClosesOnlyItsOwnKind =
  testTask
    "overriding a conflict on one kind leaves the other kind's conflict pending" {
    let loc : PT.PackageLocation =
      { owner = "Zz"; modules = [ "B1" ]; name = "shared" }
    let modules = String.concat "." loc.modules

    let insertConflict (id : string) (itemType : string) =
      Sql.query
        "INSERT OR REPLACE INTO conflicts
           (id, owner, modules, name, item_type, kind, candidates, auto_resolved_to, reason, status,
            origin_ts)
         VALUES (@id, @owner, @modules, @name, @itemType, 'same-name-different-hash', '[]', '', '',
                 'pending', '2026-01-01T00:00:00.000Z')"
      |> Sql.parameters
        [ "id", Sql.string id
          "owner", Sql.string loc.owner
          "modules", Sql.string modules
          "name", Sql.string loc.name
          "itemType", Sql.string itemType ]
      |> Sql.executeStatementAsync

    do! insertConflict "b1-fn" "fn"
    do! insertConflict "b1-value" "value"

    // Override the FN. The value's conflict is a different question and nobody has answered it.
    let target = PT.Reference.PackageFn(PT.Hash "b1fnhash")
    let op =
      PT.PackageOp.Decision(
        "b1-decision",
        loc,
        "taking mine",
        PT.DecisionKind.Override target
      )
    let! _ = LibDB.Inserts.insertAndApplyOps [ op ]

    let statusOf (id : string) =
      Sql.query "SELECT status FROM conflicts WHERE id = @id"
      |> Sql.parameters [ "id", Sql.string id ]
      |> Sql.executeRowAsync (fun read -> read.string "status")

    let! fnStatus = statusOf "b1-fn"
    let! valueStatus = statusOf "b1-value"

    Expect.equal fnStatus "overridden" "the fn conflict was the one answered"
    Expect.equal
      valueStatus
      "pending"
      "the value conflict at the same name is a separate question and stays open"

    do!
      Sql.query "DELETE FROM conflicts WHERE id IN ('b1-fn', 'b1-value')"
      |> Sql.executeStatementAsync
  }


let tests =
  // These mutate the process-global branch overlay AND delete from `package_ops`, either of which
  // can make a concurrent reader see the store mid-change. testSequenced, NOT testSequencedGroup:
  // the group form only stops the tests INSIDE it from running alongside each other, and still runs
  // in the parallel phase next to everything else, which is where the hazard is.
  testSequenced
  <| testList
    "BranchOverlay"
    [ branchResolutionOrder
      isolationFromCore
      branchExists
      mergeCountsWhatItFlipped
      importedOpsKeepTheirStamps
      rebuildKeepsBranchPolicy
      branchPMIsPerBranch
      branchNamesResolveButDontShadowMain
      isolationBetweenBranches
      parentHashesAgreeAcrossLanguages
      undecodableBranchOpIsSkippedNotFatal
      overlaySearchAgreesWithFindFn
      supersededBranchVersionsKeepTheirName
      branchAuthoringCountsAsHavingItems
      storeThenOverlay
      mergedBranchStaysAddressable
      concurrentCreateYieldsOneBranch
      markMergedFlipsEffective
      processOverlaySelects
      branchesOffBranches
      getWipOpsExcludesBranch
      mergeDoesNotConsumeSiblingPendingOps
      sameNameMergesConvergeToLater
      rebaseDetectsAndClearsConflicts
      perNameResolutionMineTheirs
      resolveKeepMineDoesNotRestampSharedOps
      resolveAloneRecordsANameBase
      reuseBranchIdRevives
      branchValueContentFoldIsolatesName
      branchEventMarksMerged
      branchEventForUnknownBranchIsIgnored
      foldDoesNotStrandOpsItMadeEffective
      branchTransferImportReDerivesBases
      noDirectLocationsReadsOutsideTheSilos
      noMainLiteralInDarkSql
      branchIdsNeverReachAPerson
      overrideClosesOnlyItsOwnKind
      mainRetakesABranchsOp
      authoringOnAFinishedBranchRefuses
      liveBindingReadsTheBranchThenMain
      aBranchNeverTagsWhatMainRuns
      retagMovesTheBasesToo
      refLookupSaysWhyItMissed
      overlayPairsByHashNotAdjacency
      anUndecodableBundleOpIsKeptNotRefused ]
