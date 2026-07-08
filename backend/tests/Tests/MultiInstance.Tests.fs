/// A tight, general-purpose harness for spinning up FRESH, ISOLATED Darklang instances in-process — each
/// instance is its own store. Two flavours:
///   freshInstance  — an empty store (schema only; `main` from schema.sql). For structure-level tests.
///   seededInstance — a COPY of a ready-to-go baseline store (the full package seed), so a test that needs
///                    real functions/types/values (most CLI experiences) is cheap: the baseline is snapshotted
///                    once, then each instance is a fast file-copy of it.
/// Switch the active store with `activate`, sync the wire between instances via the real receive path, and
/// assert they converge. Spinning up an instance is a couple of lines:
///   let! a = seededInstance "a"
///   let! b = seededInstance "b"
///
/// `testSequenced` + a `finally` that always restores the default store, so swapping the process-global
/// connection can't disturb the parallel store tests (they've completed by the sequenced phase; and we hand
/// the default store back no matter what). The swap lives behind `Sql.useStoreForTesting` (test-only).
module Tests.MultiInstance

open Expecto

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Fumble
open LibDB.Sqlite

module Seed = LibDB.Seed
module Inserts = LibDB.Inserts
module Resolutions = LibDB.Resolutions
module Account = LibCloud.Account
module PT = LibExecution.ProgramTypes
module BS = LibSerialization.Binary.Serialization
module Hashing = LibSerialization.Hashing.Hashing
module File = LibCloud.File
module Config = LibCloud.Config

// ── the fresh-instance harness ──────────────────────────────────────────────────────────────────────

type Instance = { name : string; path : string }

let private schemaSql = lazy (File.readfile Config.Migrations "schema.sql")

let private deleteStore (path : string) : unit =
  for suffix in [ ""; "-wal"; "-shm" ] do
    try
      System.IO.File.Delete(path + suffix)
    with _ ->
      ()

/// Copy a SQLite store + its WAL/SHM sidecars (so the copy is a consistent snapshot on open).
let private copyStore (src : string) (dst : string) : unit =
  deleteStore dst
  for suffix in [ ""; "-wal"; "-shm" ] do
    if System.IO.File.Exists(src + suffix) then
      System.IO.File.Copy(src + suffix, dst + suffix, overwrite = true)

let private tmpPath (name : string) : string =
  $"/tmp/dark-test-instance-{name}-{System.Guid.NewGuid()}.db"

/// Snapshotted ONCE: a baseline of the seeded default store, so `seededInstance` is a cheap local copy.
/// Captured lazily during the sequenced phase, when no parallel test is writing the default store.
let private baselineSeed : Lazy<string> =
  lazy
    (let template = "/tmp/dark-test-seed-baseline.db"
     copyStore LibConfig.Config.dbPath template
     template)

/// A fresh, EMPTY, isolated instance (schema only; `main` from schema.sql), made the active store.
let freshInstance (name : string) : Task<Instance> =
  task {
    let path = tmpPath name
    deleteStore path
    Sql.useStoreForTesting path
    Sql.query (schemaSql.Force()) |> Sql.executeStatementSync
    return { name = name; path = path }
  }

/// A fresh isolated instance that is a COPY of the seeded baseline (full package seed), made the active store.
let seededInstance (name : string) : Task<Instance> =
  task {
    let path = tmpPath name
    copyStore (baselineSeed.Force()) path
    Sql.useStoreForTesting path
    return { name = name; path = path }
  }

let activate (inst : Instance) : unit = Sql.useStoreForTesting inst.path

let teardown (insts : List<Instance>) : unit =
  Sql.resetStoreForTesting ()
  insts |> List.iter (fun i -> deleteStore i.path)

// ── helpers (build wire events, inspect projections, convert the JSON-ish wire) ───────────────────────

let private branchEventAt
  (op : PT.BranchOp)
  (originTs : string)
  : string * byte[] * string =
  let (PT.Hash h) = Hashing.computeBranchOpHash op
  (h, BS.PT.BranchOp.serialize h op, originTs)

let private branchEvent (op : PT.BranchOp) : string * byte[] * string =
  branchEventAt op "2026-07-08T00:00:00.000Z"

/// A SetName package-op event, exactly as it crosses the wire / arrives at `receiveOps`.
let private setNameEvent
  (loc : PT.PackageLocation)
  (fnHash : string)
  (commitHash : string)
  (ts : string)
  : System.Guid * byte[] * System.Guid * string * string =
  let op = PT.PackageOp.SetName(loc, PT.Reference.PackageFn(PT.Hash fnHash))
  let opId = Inserts.computeOpHash op
  (opId, BS.PT.PackageOp.serialize opId op, PT.mainBranchId, commitHash, ts)

/// An existing commit hash from the seed. Sync ops reference commits that already exist (commits travel in the
/// wire alongside their ops), so a test references a real seed commit rather than minting accounts + commits.
let private existingCommit () : Task<string> =
  Sql.query "SELECT hash FROM commits LIMIT 1"
  |> Sql.executeRowAsync (fun read -> read.string "hash")

/// The active instance's branch-op-log high-water mark (so a `branchOpsSince` returns only NEW branch ops).
let private currentBranchCursor () : Task<int64> =
  Sql.query "SELECT COALESCE(MAX(rowid), 0) AS c FROM branch_ops"
  |> Sql.executeRowAsync (fun read -> read.int64 "c")

let private branchNames () : Task<List<string>> =
  Sql.query "SELECT name FROM branches ORDER BY name"
  |> Sql.executeAsync (fun read -> read.string "name")

let private liveHash (loc : PT.PackageLocation) : Task<List<string>> =
  Sql.query
    "SELECT item_hash FROM locations WHERE owner = @o AND modules = @m AND name = @n AND unlisted_at IS NULL"
  |> Sql.parameters
    [ "o", Sql.string loc.owner
      "m", Sql.string (String.concat "." loc.modules)
      "n", Sql.string loc.name ]
  |> Sql.executeAsync (fun read -> read.string "item_hash")

/// Two distinct real function hashes from the seed (both instances share the seed, so a hash on A exists on B).
let private twoFunctionHashes () : Task<string * string> =
  task {
    let! hs =
      Sql.query "SELECT hash FROM package_functions LIMIT 2"
      |> Sql.executeAsync (fun read -> read.string "hash")
    match hs with
    | a :: b :: _ -> return (a, b)
    | _ -> return Exception.raiseInternal "seed needs >= 2 functions" []
  }

/// The active instance's current op-log high-water mark, so a later `eventsSince` returns only NEW ops
/// (not the whole seed).
let private currentCursor () : Task<int64> =
  Sql.query "SELECT COALESCE(MAX(rowid), 0) AS c FROM package_ops"
  |> Sql.executeRowAsync (fun read -> read.int64 "c")

/// The count of unreviewed conflicts recorded on the active instance.
let private conflictCount () : Task<int64> =
  Sql.query "SELECT COUNT(*) AS n FROM sync_conflicts"
  |> Sql.executeRowAsync (fun read -> read.int64 "n")

let private wireEvent
  ((id, hex, br, ch, ts) : string * string * string * string * string)
  : System.Guid * byte[] * System.Guid * string * string =
  (System.Guid.Parse id, System.Convert.FromHexString hex, System.Guid.Parse br, ch, ts)

let private wireCommit
  ((h, msg, br, acct, at) : string * string * string * string * string)
  : string * string * System.Guid * System.Guid * string =
  (h, msg, System.Guid.Parse br, System.Guid.Parse acct, at)

/// Read the active instance's NEW package ops since `cursor` as a wire batch, ready to feed another
/// instance's `receiveOps` (this is the real serialize→wire→deserialize round-trip sync does).
let private wireSince (cursor : int64) : Task<List<string * string * System.Guid * System.Guid * string> * List<System.Guid * byte[] * System.Guid * string * string>> =
  task {
    let! (commitsW, eventsW, _) = Seed.eventsSince cursor 100000L
    return (List.map wireCommit commitsW, List.map wireEvent eventsW)
  }

// ── tests ─────────────────────────────────────────────────────────────────────────────────────────────

let tests =
  testSequenced
  <| testList
    "MultiInstance"
    [ testTask "fresh instances are isolated stores, and branch structure syncs across the wire" {
        let mutable insts : List<Instance> = []

        try
          let! a = freshInstance "a"
          insts <- [ a ]
          let! b = freshInstance "b"
          insts <- [ a; b ]

          activate a
          let create =
            PT.BranchOp.CreateBranch(
              System.Guid.NewGuid(),
              "feature",
              Some PT.mainBranchId,
              None
            )
          let! _ = Seed.receiveBranchOps [ branchEvent create ]
          let! aBranches = branchNames ()

          activate b
          let! bBefore = branchNames ()

          activate a
          let! (wire, _cursor) = Seed.branchOpsSince 0L 1000L

          activate b
          let! _ =
            Seed.receiveBranchOps (
              wire
              |> List.map (fun ((id, hex, ts) : string * string * string) ->
                (id, System.Convert.FromHexString hex, ts))
            )
          let! bAfter = branchNames ()

          Expect.equal aBranches [ "feature"; "main" ] "A has both branches"
          Expect.equal bBefore [ "main" ] "B is isolated: only main before sync"
          Expect.equal bAfter [ "feature"; "main" ] "B converged: feature arrived over the wire"
        finally
          teardown insts
      }

      testTask "a fresh schema-only store runs the sync path (the composite-PK upsert folds a package op)" {
        let mutable insts : List<Instance> = []

        try
          let! a = freshInstance "a"
          insts <- [ a ]

          // A fresh store built from schema.sql alone must run receiveOps — whose ON CONFLICT(id, branch_id)
          // upsert needs the composite PK to be IN schema.sql (not only in an incremental migration). This
          // guards against schema.sql drifting from the migrated production schema.
          let loc : PT.PackageLocation = { owner = "Test"; modules = [ "Fresh" ]; name = "x" }
          let fnHash =
            "00000000000000000000000000000000000000000000000000000000deadbeef"
          let commitHash = "fresh-" + string (System.Guid.NewGuid())
          let ts = "2026-07-08T00:00:00.000Z"
          let commit =
            (commitHash, "author x", PT.mainBranchId, Account.IDs.darklang, ts)

          let! _ = Seed.receiveOps [ commit ] [ setNameEvent loc fnHash commitHash ts ]
          let! bound = liveHash loc

          Expect.equal bound [ fnHash ] "the package op folded on the fresh schema-built store"
        finally
          teardown insts
      }

      testTask "package convergence: a rename authored on A folds identically on B via the wire" {
        let mutable insts : List<Instance> = []

        try
          let! a = seededInstance "a"
          insts <- [ a ]
          let! b = seededInstance "b"
          insts <- [ a; b ]

          let loc : PT.PackageLocation =
            { owner = "Test"; modules = [ "Conv" ]; name = "greeting" }
          let ts = "2026-07-08T12:00:00.000Z"

          // Author on A: bind a fresh name to a real seeded function, on a new commit.
          activate a
          let! (fnHash, _) = twoFunctionHashes ()
          let! commitHash = existingCommit ()
          let! cursorBefore = currentCursor ()
          let! _ = Seed.receiveOps [] [ setNameEvent loc fnHash commitHash ts ]
          let! aHash = liveHash loc
          let! (commits, events) = wireSince cursorBefore

          // Apply A's wire on B.
          activate b
          let! bBefore = liveHash loc
          let! _ = Seed.receiveOps commits events
          let! bHash = liveHash loc

          Expect.equal aHash [ fnHash ] "A bound the name"
          Expect.equal bBefore [] "B didn't have the binding before sync (isolated seed)"
          Expect.equal bHash [ fnHash ] "B converged to A's binding via the wire"
        finally
          teardown insts
      }

      testTask "cross-store conflict: concurrent binds to one name converge (LWW) + record a conflict" {
        let mutable insts : List<Instance> = []

        try
          let! a = seededInstance "a"
          insts <- [ a ]
          let! b = seededInstance "b"
          insts <- [ a; b ]

          let loc : PT.PackageLocation =
            { owner = "Test"; modules = [ "Conflict" ]; name = "dup" }

          // A binds loc → fnA at ts …100; B binds loc → fnB at ts …200 (later ⇒ the LWW winner).
          activate a
          let! (fnA, fnB) = twoFunctionHashes ()
          let! commit = existingCommit ()
          let! curA = currentCursor ()
          let! _ =
            Seed.receiveOps [] [ setNameEvent loc fnA commit "2026-07-08T00:00:00.100Z" ]
          let! (commitsA, eventsA) = wireSince curA

          activate b
          let! curB = currentCursor ()
          let! _ =
            Seed.receiveOps [] [ setNameEvent loc fnB commit "2026-07-08T00:00:00.200Z" ]
          let! (commitsB, eventsB) = wireSince curB

          // Cross-sync: B receives A's op (divergence!), A receives B's op.
          activate b
          let! _ = Seed.receiveOps commitsA eventsA
          let! bConflicts = conflictCount ()
          let! bFinal = liveHash loc

          activate a
          let! _ = Seed.receiveOps commitsB eventsB
          let! aFinal = liveHash loc

          Expect.equal bFinal [ fnB ] "B keeps the LWW winner (its own later bind) after receiving A's older op"
          Expect.equal aFinal [ fnB ] "A converges to the same LWW winner after receiving B's op"
          Expect.isGreaterThan bConflicts 0L "B recorded the divergence as a conflict (never silent)"
        finally
          teardown insts
      }

      testTask "bidirectional pull: A's and B's independent edits both land on the other" {
        let mutable insts : List<Instance> = []

        try
          let! a = seededInstance "a"
          insts <- [ a ]
          let! b = seededInstance "b"
          insts <- [ a; b ]

          let locX : PT.PackageLocation = { owner = "Test"; modules = [ "Bi" ]; name = "x" }
          let locY : PT.PackageLocation = { owner = "Test"; modules = [ "Bi" ]; name = "y" }
          let ts = "2026-07-08T12:00:00.000Z"

          activate a
          let! (fnA, fnB) = twoFunctionHashes ()
          let! commit = existingCommit ()
          let! curA = currentCursor ()
          let! _ = Seed.receiveOps [] [ setNameEvent locX fnA commit ts ]
          let! (cA, eA) = wireSince curA

          activate b
          let! curB = currentCursor ()
          let! _ = Seed.receiveOps [] [ setNameEvent locY fnB commit ts ]
          let! (cB, eB) = wireSince curB

          // Sync both directions.
          activate b
          let! _ = Seed.receiveOps cA eA
          let! bx = liveHash locX
          let! by = liveHash locY

          activate a
          let! _ = Seed.receiveOps cB eB
          let! ax = liveHash locX
          let! ay = liveHash locY

          Expect.equal ax [ fnA ] "A keeps its own X"
          Expect.equal ay [ fnB ] "A gains B's Y"
          Expect.equal bx [ fnA ] "B gains A's X"
          Expect.equal by [ fnB ] "B keeps its own Y"
        finally
          teardown insts
      }

      testTask "pagination: eventsSince returns bounded, disjoint batches with an advancing cursor" {
        let mutable insts : List<Instance> = []

        try
          let! a = seededInstance "a"
          insts <- [ a ]

          activate a
          let! (fnA, _) = twoFunctionHashes ()
          let! commit = existingCommit ()
          let! cur0 = currentCursor ()

          for i in [ 1; 2; 3 ] do
            let loc : PT.PackageLocation =
              { owner = "Test"; modules = [ "Page" ]; name = $"n{i}" }
            let! _ =
              Seed.receiveOps [] [ setNameEvent loc fnA commit $"2026-07-08T00:00:0{i}.000Z" ]
            ()

          let! (_, batch1, c1) = Seed.eventsSince cur0 2L
          let! (_, batch2, c2) = Seed.eventsSince c1 2L

          Expect.equal (List.length batch1) 2 "first batch is bounded to the limit"
          Expect.equal (List.length batch2) 1 "second batch has exactly the remaining op"
          Expect.isGreaterThan c1 cur0 "cursor advanced past the first batch"
          Expect.isGreaterThan c2 c1 "cursor advanced again"
        finally
          teardown insts
      }

      testTask "resolution converges: a keep-mine override propagates and the other instance adopts it" {
        let mutable insts : List<Instance> = []

        try
          let! a = seededInstance "a"
          insts <- [ a ]
          let! b = seededInstance "b"
          insts <- [ a; b ]

          let loc : PT.PackageLocation = { owner = "Test"; modules = [ "Res" ]; name = "pick" }

          // A and B concurrently bind loc; the later stamp (fnB) is the LWW winner on both after cross-sync.
          activate a
          let! (fnA, fnB) = twoFunctionHashes ()
          let! commit = existingCommit ()
          let! curA = currentCursor ()
          let! _ = Seed.receiveOps [] [ setNameEvent loc fnA commit "2026-07-08T00:00:00.100Z" ]
          let! (cA, eA) = wireSince curA

          activate b
          let! curB = currentCursor ()
          let! _ = Seed.receiveOps [] [ setNameEvent loc fnB commit "2026-07-08T00:00:00.200Z" ]
          let! (cB, eB) = wireSince curB

          activate b
          let! _ = Seed.receiveOps cA eA
          activate a
          let! _ = Seed.receiveOps cB eB

          // A's human overrides back to fnA (a later `at` than either op) and it converges to B via the log.
          activate a
          let res =
            Resolutions.mk
              loc
              (PT.Reference.PackageFn(PT.Hash fnA))
              "human"
              PT.mainBranchId
              "2026-07-08T00:00:00.300Z"
          do! Resolutions.recordAndApply res
          let! aResolved = liveHash loc
          let! (resWire, _) = Resolutions.resolutionsSince 0L 1000L

          activate b
          let! _ = Resolutions.receiveResolutions resWire
          let! bResolved = liveHash loc

          Expect.equal aResolved [ fnA ] "A's resolution overrode the LWW winner back to fnA"
          Expect.equal bResolved [ fnA ] "B adopted A's resolution via the synced resolutions log"
        finally
          teardown insts
      }

      testTask "branch merge syncs across stores: a branch's create + merge land on B (merged_at set)" {
        let mutable insts : List<Instance> = []

        try
          let! a = seededInstance "a"
          insts <- [ a ]
          let! b = seededInstance "b"
          insts <- [ a; b ]

          let branchId = System.Guid.NewGuid()

          activate a
          let! baseCommit = existingCommit ()
          let! bCur0 = currentBranchCursor ()

          let createB =
            branchEvent (
              PT.BranchOp.CreateBranch(
                branchId,
                "feature",
                Some PT.mainBranchId,
                Some(PT.Hash baseCommit)
              )
            )
          let mergeB = branchEvent (PT.BranchOp.MergeBranch(branchId, PT.mainBranchId))

          // Author on A: create the branch, then merge it. (Content-on-branch folding is covered by
          // SyncScenarios before the merge; merge re-homes it, so this focuses on the lifecycle sync.)
          let! _ = Seed.receiveBranchOps [ createB ]
          let! _ = Seed.receiveBranchOps [ mergeB ]

          let! (branchWire, _) = Seed.branchOpsSince bCur0 1000L

          activate b
          let! _ =
            Seed.receiveBranchOps (
              branchWire
              |> List.map (fun ((id, hex, ts) : string * string * string) ->
                (id, System.Convert.FromHexString hex, ts))
            )

          let! branchExists =
            Sql.query "SELECT count(*) AS n FROM branches WHERE id = @b"
            |> Sql.parameters [ "b", Sql.uuid branchId ]
            |> Sql.executeRowAsync (fun read -> read.int64 "n")
          let! mergedAt =
            Sql.query "SELECT merged_at FROM branches WHERE id = @b"
            |> Sql.parameters [ "b", Sql.uuid branchId ]
            |> Sql.executeRowAsync (fun read -> read.stringOrNone "merged_at")

          Expect.equal branchExists 1L "the branch's create + merge synced to B"
          Expect.isSome mergedAt "the merge marked the branch merged on B"
        finally
          teardown insts
      }

      testTask "structural convergence: concurrent rebases of one branch converge to the LWW winner, order-independent" {
        let mutable insts : List<Instance> = []

        try
          let! a = seededInstance "a"
          insts <- [ a ]
          let! b = seededInstance "b"
          insts <- [ a; b ]

          let branchId = System.Guid.NewGuid()
          let baseX =
            "1111111111111111111111111111111111111111111111111111111111111111"
          let baseY =
            "2222222222222222222222222222222222222222222222222222222222222222"

          // Both instances know the branch (shared CreateBranch, older than either rebase). Then two
          // instances rebase it CONCURRENTLY to different bases: X at ts .200 (newer) beats Y at ts .100.
          let createB =
            branchEventAt
              (PT.BranchOp.CreateBranch(branchId, "feature", Some PT.mainBranchId, None))
              "2026-01-01T00:00:00.000Z"
          let rebaseX =
            branchEventAt
              (PT.BranchOp.RebaseBranch(branchId, PT.Hash baseX))
              "2026-07-08T00:00:00.200Z"
          let rebaseY =
            branchEventAt
              (PT.BranchOp.RebaseBranch(branchId, PT.Hash baseY))
              "2026-07-08T00:00:00.100Z"

          let baseOf () : Task<Option<string>> =
            Sql.query "SELECT base_commit_hash FROM branches WHERE id = @id"
            |> Sql.parameters [ "id", Sql.uuid branchId ]
            |> Sql.executeRowAsync (fun read -> read.stringOrNone "base_commit_hash")

          // A authors X (newer), THEN receives Y (older) — Y must lose the LWW.
          activate a
          let! _ = Seed.receiveBranchOps [ createB ]
          let! _ = Seed.receiveBranchOps [ rebaseX ]
          let! _ = Seed.receiveBranchOps [ rebaseY ]
          let! aBase = baseOf ()

          // B authors Y (older) FIRST, THEN receives X (newer) — X arrives last but must still win.
          activate b
          let! _ = Seed.receiveBranchOps [ createB ]
          let! _ = Seed.receiveBranchOps [ rebaseY ]
          let! _ = Seed.receiveBranchOps [ rebaseX ]
          let! bBase = baseOf ()

          Expect.equal aBase (Some baseX) "A: the newer rebase (ts .200 → baseX) wins; the later-arriving older one is skipped"
          Expect.equal bBase (Some baseX) "B: converges to the SAME base though the winner arrived last — order-independent"
        finally
          teardown insts
      } ]
