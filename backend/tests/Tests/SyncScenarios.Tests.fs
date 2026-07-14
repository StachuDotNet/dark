/// Cross-instance SCM + sync scenarios, exercised through the REAL receive path (Seed.receiveOps /
/// receiveBranchOps / Resolutions.receiveResolutions) + the invisible fold — fast, in-process, no CLI.
///
/// The model: an "instance" IS its op set. Two instances converge iff folding the same op set (in any order,
/// under the origin_ts LWW + the MIN-reconcile on receive) yields the same projection. So the convergence
/// tests here apply an op multiset in different orders and assert an identical `locations` outcome — that
/// order-independence IS the two-instance convergence guarantee. testSequenced + self-cleanup so these ride
/// the shared store safely (they only ever touch their own `Test.*` names/branches).
///
/// The lightest of three sync-test layers: this proves the fold is order-independent (one store, op-set
/// replay); `MultiInstance` runs two real isolated stores over the in-process wire; `SyncE2E` runs real
/// processes over HTTP.
module Tests.SyncScenarios

open Expecto

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Fumble
open LibDB.Sqlite

module Seed = LibDB.Seed
module Inserts = LibDB.Inserts
module Resolutions = LibDB.Resolutions
module PT = LibExecution.ProgramTypes
module BS = LibSerialization.Binary.Serialization
module Hashing = LibSerialization.Hashing.Hashing

// ── helpers: build wire events + inspect/reset the projection, all keyed to a test-only name ──

/// A SetName package-op event tuple, exactly as it crosses the wire / arrives at receiveOps.
let private setNameEvent
  (loc : PT.PackageLocation)
  (fnHash : string)
  (commitHash : string)
  (ts : string)
  : System.Guid * byte[] * System.Guid * string * string =
  let op = PT.PackageOp.SetName(loc, PT.Reference.PackageFn(PT.Hash fnHash))
  let opId = Inserts.computeOpHash op
  (opId, BS.PT.PackageOp.serialize opId op, PT.mainBranchId, commitHash, ts)

let private recv
  (event : System.Guid * byte[] * System.Guid * string * string)
  : Task<int64> =
  Seed.receiveOps [] [ event ]

/// The live binding hash(es) for a location — the thing that must converge.
let private liveHash (loc : PT.PackageLocation) : Task<List<string>> =
  Sql.query
    "SELECT item_hash FROM locations WHERE owner = @o AND modules = @m AND name = @n AND unlisted_at IS NULL"
  |> Sql.parameters
    [ "o", Sql.string loc.owner
      "m", Sql.string (String.concat "." loc.modules)
      "n", Sql.string loc.name ]
  |> Sql.executeAsync (fun read -> read.string "item_hash")

/// Wipe a test name's ops + projection rows (+ any recorded conflict/resolution) so a scenario can replay it
/// in a different order from a clean slate.
let private wipe
  (loc : PT.PackageLocation)
  (opIds : List<System.Guid>)
  : Task<unit> =
  task {
    let modulesStr = String.concat "." loc.modules
    for opId in opIds do
      do!
        Sql.query "DELETE FROM package_ops WHERE id = @id"
        |> Sql.parameters [ "id", Sql.uuid opId ]
        |> Sql.executeStatementAsync
    do!
      Sql.query
        "DELETE FROM locations WHERE owner = @o AND modules = @m AND name = @n"
      |> Sql.parameters
        [ "o", Sql.string loc.owner
          "m", Sql.string modulesStr
          "n", Sql.string loc.name ]
      |> Sql.executeStatementAsync
    let locStr = $"{loc.owner}.{modulesStr}.{loc.name}"
    do!
      Sql.query "DELETE FROM sync_conflicts WHERE location = @l"
      |> Sql.parameters [ "l", Sql.string locStr ]
      |> Sql.executeStatementAsync
    do!
      Sql.query "DELETE FROM resolutions WHERE location = @l"
      |> Sql.parameters [ "l", Sql.string locStr ]
      |> Sql.executeStatementAsync
  }

let private twoFns () : Task<string * string> =
  task {
    let! hs =
      Sql.query "SELECT hash FROM package_functions LIMIT 2"
      |> Sql.executeAsync (fun read -> read.string "hash")
    match hs with
    | a :: b :: _ -> return (a, b)
    | _ -> return Exception.raiseInternal "seed needs >=2 functions" []
  }

let private aCommit () : Task<string> =
  Sql.query "SELECT hash FROM commits LIMIT 1"
  |> Sql.executeRowAsync (fun read -> read.string "hash")

/// A BranchOp wire event (id, blob, origin_ts) as it arrives at receiveBranchOps.
let private branchEvent (op : PT.BranchOp) : string * byte[] * string =
  let (PT.Hash h) = Hashing.computeBranchOpHash op
  (h, BS.PT.BranchOp.serialize h op, "2099-01-01T00:00:00.000Z")

/// A SetName package event on a SPECIFIC branch (setNameEvent above hardcodes main).
let private setNameEventOn
  (loc : PT.PackageLocation)
  (fnHash : string)
  (branchId : System.Guid)
  (commitHash : string)
  (ts : string)
  : System.Guid * byte[] * System.Guid * string * string =
  let op = PT.PackageOp.SetName(loc, PT.Reference.PackageFn(PT.Hash fnHash))
  let opId = Inserts.computeOpHash op
  (opId, BS.PT.PackageOp.serialize opId op, branchId, commitHash, ts)

let tests =
  testSequenced
  <| testList
    "SyncScenarios"
    [ // CONVERGENCE — the core guarantee: concurrent edits to the SAME name settle on the same winner no
      // matter which instance's op arrives first. (origin_ts LWW: newer wins; older is skipped on replay.)
      testTask
        "convergence: concurrent SetName to one name is order-independent (LWW)" {
        let! (hA, hB) = twoFns ()
        let! commitHash = aCommit ()
        let loc : PT.PackageLocation =
          { owner = "Test"; modules = [ "SyncConv" ]; name = "x" }
        let evOld = setNameEvent loc hA commitHash "2099-01-01T00:00:00.100Z"
        let evNew = setNameEvent loc hB commitHash "2099-01-01T00:00:00.200Z"
        let (idA, _, _, _, _) = evOld
        let (idB, _, _, _, _) = evNew
        let ids = [ idA; idB ]

        // order 1: old then new
        do! wipe loc ids
        let! _ = recv evOld
        let! _ = recv evNew
        let! r1 = liveHash loc

        // order 2: new then old (the older op arrives late — must be skipped, not win)
        do! wipe loc ids
        let! _ = recv evNew
        let! _ = recv evOld
        let! r2 = liveHash loc

        Expect.equal r1 [ hB ] "old→new converges to the newer hash"
        Expect.equal
          r2
          [ hB ]
          "new→old ALSO converges to the newer hash (late older op skipped)"
        Expect.equal
          r1
          r2
          "order-independent — the two-instance convergence property"

        do! wipe loc ids
        let! _ = Seed.rebuildProjections ()
        ()
      }

      // IDEMPOTENCY — re-receiving the same op set changes nothing and folds 0 (so "Pulled N" stays honest and
      // a re-pull can't corrupt the binding).
      testTask
        "idempotency: re-receiving the same op folds 0 and leaves the binding unchanged" {
        let! (hA, _) = twoFns ()
        let! commitHash = aCommit ()
        let loc : PT.PackageLocation =
          { owner = "Test"; modules = [ "SyncIdem" ]; name = "y" }
        let ev = setNameEvent loc hA commitHash "2099-01-01T00:00:00.300Z"
        let (idA, _, _, _, _) = ev
        do! wipe loc [ idA ]

        let! first = recv ev
        let! second = recv ev
        let! bound = liveHash loc
        Expect.isTrue (first >= 1L) "first receive folds the new op"
        Expect.equal second 0L "re-receiving the identical op folds nothing"
        Expect.equal bound [ hA ] "binding unchanged after the re-pull"

        do! wipe loc [ idA ]
        let! _ = Seed.rebuildProjections ()
        ()
      }

      // CONFLICT → RESOLUTION → CONVERGE, both directions. A divergence auto-resolves by LWW but is recorded;
      // a human override (keep-mine / keep-theirs) mints a Resolution whose overlay wins and converges.
      testTask
        "conflict → resolution: keep-mine and keep-theirs each override the LWW winner" {
        let! (hLocal, hIncoming) = twoFns ()
        let! commitHash = aCommit ()
        let loc : PT.PackageLocation =
          { owner = "Test"; modules = [ "SyncResolve" ]; name = "z" }
        let evLocal = setNameEvent loc hLocal commitHash "2099-01-01T00:00:00.100Z"
        let evIncoming =
          setNameEvent loc hIncoming commitHash "2099-01-01T00:00:00.200Z"
        let (idL, _, _, _, _) = evLocal
        let (idI, _, _, _, _) = evIncoming

        let applyResolution (choiceHash : string) (at : string) : Task<unit> =
          Resolutions.recordAndApply
            { id = System.Guid.NewGuid() |> string
              branchId = PT.mainBranchId
              location = loc
              choice = PT.Reference.PackageFn(PT.Hash choiceHash)
              resolvedBy = "human"
              at = at }

        // set up the divergence: local hLocal@100, then incoming hIncoming@200 → LWW binds incoming
        do! wipe loc [ idL; idI ]
        let! _ = recv evLocal
        let! _ = recv evIncoming
        let! lww = liveHash loc
        Expect.equal lww [ hIncoming ] "LWW binds the newer incoming hash"

        // keep-MINE: override back to the local hash with a fresh (newer) stamp → converges to local
        do! applyResolution hLocal "2099-01-01T00:00:00.300Z"
        let! kept = liveHash loc
        Expect.equal
          kept
          [ hLocal ]
          "keep-mine: the resolution overrides LWW back to the local hash"

        // keep-THEIRS: a later resolution back to incoming wins (last-resolver by `at`)
        do! applyResolution hIncoming "2099-01-01T00:00:00.400Z"
        let! flipped = liveHash loc
        Expect.equal
          flipped
          [ hIncoming ]
          "keep-theirs: a newer resolution flips it back to incoming"

        do! wipe loc [ idL; idI ]
        let! _ = Seed.rebuildProjections ()
        ()
      }

      // NO PHANTOM CONFLICTS on re-pull: a divergence is recorded once (when the rebind actually folds);
      // re-receiving an already-applied op must NOT record it again (detection only fires on ops about to fold).
      testTask "re-pulling an already-applied op records no phantom conflict" {
        let! (hLocal, hIncoming) = twoFns ()
        let! commitHash = aCommit ()
        let loc : PT.PackageLocation =
          { owner = "Test"; modules = [ "SyncPhantom" ]; name = "p" }
        let evLocal = setNameEvent loc hLocal commitHash "2099-01-01T00:00:00.100Z"
        let evIncoming =
          setNameEvent loc hIncoming commitHash "2099-01-01T00:00:00.200Z"
        let (idL, _, _, _, _) = evLocal
        let (idI, _, _, _, _) = evIncoming

        let conflictCount () : Task<int64> =
          Sql.query "SELECT count(*) AS n FROM sync_conflicts WHERE location = @l"
          |> Sql.parameters [ "l", Sql.string "Test.SyncPhantom.p" ]
          |> Sql.executeRowAsync (fun read -> read.int64 "n")

        do! wipe loc [ idL; idI ]
        let! _ = recv evLocal
        let! _ = recv evIncoming // the rebind folds → one divergence recorded
        let! afterDivergence = conflictCount ()
        Expect.equal afterDivergence 1L "the rebind records exactly one divergence"

        // re-pull the now-superseded local op — already applied, so detection must NOT re-fire
        let! _ = recv evLocal
        let! afterRepull = conflictCount ()
        Expect.equal
          afterRepull
          1L
          "re-pulling the already-applied op records no new (phantom) conflict"

        do! wipe loc [ idL; idI ]
        let! _ = Seed.rebuildProjections ()
        ()
      }

      // PAGINATION / CURSOR RESUME — the serve read (eventsSince) returns one bounded batch and a resume
      // cursor; the next read after that cursor is the next batch, disjoint (so a puller loops to catch up and
      // an interrupted pull resumes where it stopped).
      testTask
        "eventsSince paginates: bounded batches, cursor advances, batches disjoint" {
        let! (_, events1, cur1) = Seed.eventsSince 0L 3L
        Expect.isTrue
          (List.length events1 <= 3)
          "first batch is bounded to the limit"
        Expect.isTrue
          (List.length events1 > 0)
          "first batch non-empty (the seed has committed ops)"

        let! (_, events2, cur2) = Seed.eventsSince cur1 3L
        Expect.isTrue (cur2 >= cur1) "the resume cursor advances monotonically"

        let idsOf evs = evs |> List.map (fun (id, _, _, _, _) -> id) |> Set.ofList
        Expect.isTrue
          (Set.intersect (idsOf events1) (idsOf events2) |> Set.isEmpty)
          "consecutive batches are disjoint — the cursor prevents re-reading"
      }

      // BRANCH SCM SYNC — branch structure (branch_ops) applies before content, so a package op on a
      // freshly-synced branch resolves its branch_id and folds; and a merge marks the branch merged.
      testTask
        "branch sync: structure lands, content on the new branch folds, merge marks it" {
        let! fnHash =
          Sql.query "SELECT hash FROM package_functions LIMIT 1"
          |> Sql.executeRowAsync (fun read -> read.string "hash")
        let! baseCommit = aCommit ()
        let branchId = System.Guid "dead0000-0000-0000-0000-0000000b0b00"
        let loc : PT.PackageLocation =
          { owner = "Test"; modules = [ "BranchSync" ]; name = "onbranch" }

        let createB =
          branchEvent (
            PT.BranchOp.CreateBranch(
              branchId,
              "test-sync-branch",
              Some PT.mainBranchId,
              Some(PT.Hash baseCommit)
            )
          )
        let mergeB = branchEvent (PT.BranchOp.MergeBranch(branchId, PT.mainBranchId))
        let pkgEv =
          setNameEventOn loc fnHash branchId baseCommit "2099-01-01T00:00:00.500Z"

        let cleanup () : Task<unit> =
          task {
            do!
              Sql.query "DELETE FROM locations WHERE branch_id = @b"
              |> Sql.parameters [ "b", Sql.uuid branchId ]
              |> Sql.executeStatementAsync
            do!
              Sql.query "DELETE FROM package_ops WHERE branch_id = @b"
              |> Sql.parameters [ "b", Sql.uuid branchId ]
              |> Sql.executeStatementAsync
            do!
              Sql.query "DELETE FROM branches WHERE id = @b"
              |> Sql.parameters [ "b", Sql.uuid branchId ]
              |> Sql.executeStatementAsync
            do!
              Sql.query "DELETE FROM branch_ops WHERE id = @c OR id = @m"
              |> Sql.parameters
                [ "c", Sql.string (let (i, _, _) = createB in i)
                  "m", Sql.string (let (i, _, _) = mergeB in i) ]
              |> Sql.executeStatementAsync
          }

        do! cleanup ()

        // structure first: the branch lands
        let! _ = Seed.receiveBranchOps [ createB ]
        let! branchExists =
          Sql.query "SELECT count(*) AS n FROM branches WHERE id = @b"
          |> Sql.parameters [ "b", Sql.uuid branchId ]
          |> Sql.executeRowAsync (fun read -> read.int64 "n")
        Expect.equal branchExists 1L "CreateBranch synced — the branch exists"

        // content on the new branch folds — branch_id resolves because structure came first
        let! _ = Seed.receiveOps [] [ pkgEv ]
        let! bound =
          Sql.query
            "SELECT item_hash FROM locations WHERE branch_id = @b AND name = 'onbranch' AND unlisted_at IS NULL"
          |> Sql.parameters [ "b", Sql.uuid branchId ]
          |> Sql.executeAsync (fun read -> read.string "item_hash")
        Expect.equal
          bound
          [ fnHash ]
          "a package op on the synced branch folded into locations under that branch"

        // merge marks it merged
        let! _ = Seed.receiveBranchOps [ mergeB ]
        let! mergedAt =
          Sql.query "SELECT merged_at FROM branches WHERE id = @b"
          |> Sql.parameters [ "b", Sql.uuid branchId ]
          |> Sql.executeRowAsync (fun read -> read.stringOrNone "merged_at")
        Expect.isSome mergedAt "MergeBranch marked the branch merged_at"

        do! cleanup ()
        let! _ = Seed.rebuildProjections ()
        ()
      } ]
