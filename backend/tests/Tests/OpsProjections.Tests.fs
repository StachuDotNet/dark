/// Tests for the ops⊥projections prototype (LibDB.Seed.rebuildProjections).
/// Proves the central claim of the storage split: the projection tables are
/// *regenerable from the op log* — drop them, re-fold package_ops, and they
/// come back identical. The op log (package_ops) is canonical and untouched.
module Tests.OpsProjections

open Expecto

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude

open Fumble
open LibDB.Sqlite

module Seed = LibDB.Seed
module Releases = LibDB.Releases
module PT = LibExecution.ProgramTypes
module Inserts = LibDB.Inserts
module BS = LibSerialization.Binary.Serialization

let private countRows (table : string) : Task<int64> =
  Sql.query $"SELECT COUNT(*) as n FROM {table}"
  |> Sql.executeRowAsync (fun read -> read.int64 "n")

/// A content fingerprint of the projections: the sorted set of every projected item's hash. Two folds that
/// produce the same fingerprint produced the same projections (down to identity), regardless of row order
/// or nondeterministic columns like `location_id`.
let private itemHashes () : Task<string> =
  task {
    let q (table : string) =
      Sql.query $"SELECT hash FROM {table} ORDER BY hash"
      |> Sql.executeAsync (fun read -> read.string "hash")
    let! fns = q "package_functions"
    let! typs = q "package_types"
    let! vals = q "package_values"
    return String.concat "\n" (fns @ typs @ vals)
  }

// `testSequenced` because the drop+rebuild case DELETEs + refolds the *shared* projection
// tables and marks all ops unapplied — it must not run concurrently with other DB tests (it
// would race their reads/writes mid-rebuild). The registry cases are pure but ride along.
let tests =
  testSequenced
  <| testList
    "OpsProjections"
    [ testTask
        "rebuildProjections deterministically regenerates projections from the op log" {
        // package_blobs is canonical content — a rebuild must never touch it
        let! blobsBefore = countRows "package_blobs"

        // drop the regenerable projections + re-fold the entire op log
        let! reapplied = Seed.rebuildProjections ()
        Expect.isTrue (reapplied > 0L) "ops were re-folded"
        let! fns1 = countRows "package_functions"
        let! locs1 = countRows "locations"
        Expect.isTrue
          (fns1 > 0L)
          "projections regenerated (non-empty) from the op log"

        // a SECOND rebuild reproduces the EXACT same projections — the rebuild is a deterministic
        // function of the op log. (Robust to other tests mutating the shared DB: we compare two
        // rebuilds of the *current* log, not a pristine-seed count they'd perturb.)
        let! _ = Seed.rebuildProjections ()
        let! fns2 = countRows "package_functions"
        let! locs2 = countRows "locations"
        Expect.equal
          fns2
          fns1
          "package_functions: a re-rebuild reproduces the same projection"
        Expect.equal
          locs2
          locs1
          "locations: a re-rebuild reproduces the same projection"

        // canonical content (package_blobs) is NOT a projection — untouched across rebuilds
        let! blobsAfter = countRows "package_blobs"
        Expect.equal
          blobsAfter
          blobsBefore
          "package_blobs (canonical content) preserved, not dropped"
      }

      // Stronger than the count check above: the exact CONTENT of the projections (the set of projected
      // item hashes) is reproduced byte-for-byte across a re-fold — the fold is a deterministic function
      // of the op log, not merely cardinality-preserving. This is what makes "drop the projections and
      // re-fold" safe, and (later) what lets two instances fold the same log to the same tables.
      testTask "re-fold reproduces identical projection CONTENT (deterministic fold, not just counts)" {
        let itemHashes () : Task<string> =
          task {
            let q (table : string) =
              Sql.query $"SELECT hash FROM {table} ORDER BY hash"
              |> Sql.executeAsync (fun read -> read.string "hash")
            let! fns = q "package_functions"
            let! typs = q "package_types"
            let! vals = q "package_values"
            return String.concat "\n" (fns @ typs @ vals)
          }

        let! _ = Seed.rebuildProjections ()
        let! fp1 = itemHashes ()
        Expect.isTrue (String.length fp1 > 0) "non-vacuous: the projections have content"
        let! _ = Seed.rebuildProjections ()
        let! fp2 = itemHashes ()
        Expect.equal
          fp2
          fp1
          "the exact set of projected item hashes is identical across a re-fold (deterministic fold)"
      }

      // The authoring stamp behind the timestamp-LWW: locally-inserted ops must get a STRICTLY-increasing
      // origin_ts, even within one wall-clock millisecond — otherwise two sequential SetNames to the same
      // name would tie and be reordered by content hash (a later edit could lose its own location).
      test "origin_ts stamps are strictly increasing (sequential local edits never tie the LWW)" {
        let stamps = List.init 100 (fun _ -> LibDB.Inserts.nextOriginTs ())
        Expect.equal stamps (List.sort stamps) "stamps are monotonic (non-decreasing)"
        Expect.equal
          (List.length (List.distinct stamps))
          100
          "stamps are strictly distinct — no ties even across a burst within one millisecond"
      }

      // ── THE POINT, end to end: migrate a store from one Release of Dark to the next WITHOUT losing
      // authored work. A *durable* Release step carries the op log forward (a schema copy-swap + an optional
      // op-format re-serialize); the projections are then dropped and RE-FOLDED from that same log in the
      // new Release's format. This is exactly why the two design pieces exist:
      //   • ops ⊥ projections — you migrate the LOG (the authored work), never the derived tables; the
      //     projections are disposable and regenerated. Losing a projection costs only CPU.
      //   • meaning-stable hashing — a format re-serialize keeps each op's IDENTITY (the id hashes the op's
      //     MEANING, not its bytes), so names/dependencies don't churn across the migration.
      // (Contrast the shipped Release 3, a clean-BREAK that throws the package data away and rebuilds — this
      // test proves the DURABLE path that carries it forward, the one a real future release will use.) ──
      testTask
        "release migration (durable): authored op log carried forward + projections re-folded, nothing lost" {
        let! opsBefore = countRows "package_ops"
        let! blobsBefore = countRows "package_blobs"
        Expect.isTrue (opsBefore > 0L) "there is authored work to migrate (not a vacuous test)"
        let! fpBefore = itemHashes ()

        // A durable forward Release step (n = code+1): a real schema change + the op-format re-serialize
        // path. NOT clearForRebuild — the authored op log is preserved and carried forward. (The remap is
        // identity here; a real format bump swaps this one fn. The op ids are unaffected either way, because
        // they hash the op's meaning, not its bytes — that's the property meaning-stable hashing buys.)
        Releases.applyRelease
          { n = Releases.currentRelease + 1
            sql =
              "CREATE INDEX IF NOT EXISTS idx_release_migration_demo ON package_ops(origin_ts)"
            reserialize = Some(fun blob -> blob)
            clearForRebuild = false }
        // the step marked the log unapplied; startup re-folds the projections from the (carried-forward) log
        let! _ = Seed.rebuildProjections ()

        let! opsAfter = countRows "package_ops"
        let! blobsAfter = countRows "package_blobs"
        let! fpAfter = itemHashes ()
        Expect.equal
          opsAfter
          opsBefore
          "the authored op log is PRESERVED across the migration — nothing lost"
        Expect.equal blobsAfter blobsBefore "canonical content (package_blobs) preserved"
        Expect.equal
          fpAfter
          fpBefore
          "projections re-fold IDENTICALLY in the new Release — same items, meaning-stable"

        let! idxExists =
          Sql.query
            "SELECT COUNT(*) as n FROM sqlite_master WHERE type='index' AND name='idx_release_migration_demo'"
          |> Sql.executeRowAsync (fun read -> read.int64 "n")
        Expect.equal idxExists 1L "the Release's schema change actually landed"

        // leave the shared store as we found it
        do!
          Sql.query "DROP INDEX IF EXISTS idx_release_migration_demo"
          |> Sql.executeStatementAsync
      }

      // the projection registry — the fold/dirty descriptors
      test "the projection registry covers exactly the 6 regenerable projections" {
        Expect.equal
          (List.sort Seed.projectionTables)
          (List.sort
            [ "package_functions"
              "package_types"
              "package_values"
              "locations"
              "package_dependencies"
              "deprecations" ])
          "the registry's tables are exactly Seed.export's stripped projections (incl. deprecations)"
      }

      test "projectionsDirtiedBy maps an op kind to the projections it invalidates" {
        Expect.equal
          (List.sort (Seed.projectionsDirtiedBy "AddFn"))
          (List.sort [ "package_functions"; "package_dependencies" ])
          "AddFn dirties the fn projection + the dependency edges"
        Expect.equal
          (List.sort (Seed.projectionsDirtiedBy "AddType"))
          (List.sort [ "package_types"; "package_dependencies" ])
          "AddType dirties the type projection + deps"
        Expect.equal
          (Seed.projectionsDirtiedBy "SetName")
          [ "locations" ]
          "SetName dirties only locations"
        Expect.equal
          (Seed.projectionsDirtiedBy "RevertPropagation")
          [ "locations" ]
          "RevertPropagation dirties only locations"
        Expect.equal
          (Seed.projectionsDirtiedBy "Deprecate")
          [ "deprecations" ]
          "Deprecate dirties the deprecations projection (it IS regenerable from ops)"
        Expect.isEmpty
          (Seed.projectionsDirtiedBy "PropagateUpdate")
          "PropagateUpdate is a no-op (its accompanying SetNames do the work) — dirties nothing"
      }

      // the incremental-refold DECISION: which projections a whole op batch dirties (the union)
      test
        "projectionsDirtiedByBatch unions a batch's dirtied projections; AddFn-only skips locations" {
        let addFnOnly = Seed.projectionsDirtiedByBatch (Set.ofList [ "AddFn" ])
        Expect.equal
          addFnOnly
          (Set.ofList [ "package_functions"; "package_dependencies" ])
          "an AddFn-only batch dirties functions + deps"
        Expect.isFalse
          (Set.contains "locations" addFnOnly)
          "locations is NOT dirtied — an incremental refold leaves it (and its rows) untouched"

        let mixed =
          Seed.projectionsDirtiedByBatch (Set.ofList [ "AddFn"; "SetName" ])
        Expect.equal
          mixed
          (Set.ofList [ "package_functions"; "package_dependencies"; "locations" ])
          "an AddFn+SetName batch dirties functions + deps + locations (the union)"

        Expect.isEmpty
          (Seed.projectionsDirtiedByBatch Set.empty)
          "an empty batch dirties no projections"
      }

      // the selective FOLD: rebuildDirtied refolds ONLY the dirtied tables, leaving others alone
      testTask
        "rebuildDirtied {AddFn} refolds functions but leaves locations untouched (selective)" {
        let! locsBefore = countRows "locations"
        let! refolded = Seed.rebuildDirtied (Set.ofList [ "AddFn" ])
        Expect.isTrue (refolded > 0L) "AddFn ops were re-folded"
        let! fnsAfter = countRows "package_functions"
        Expect.isTrue
          (fnsAfter > 0L)
          "package_functions regenerated from the AddFn ops"
        // SELECTIVITY: locations isn't in {AddFn}'s dirtied set, so it's never cleared/refolded
        let! locsAfter = countRows "locations"
        Expect.equal
          locsAfter
          locsBefore
          "locations untouched — rebuildDirtied {AddFn} refolds only the dirtied projections"
      }

      // A schema change keeps your work. The bootstrap drops ONLY `projectionTables` and re-folds the op
      // log (LocalExec.Migrations.dropProjectionTables) — so the authored, canonical data must NEVER appear
      // in that drop-set. If it did, a schema bump would delete your work. This guards that line.
      test
        "a schema change never drops the op log: no canonical table is in the projection drop-set" {
        let canonical =
          [ "package_ops" // the authored op log — the truth
            "package_blobs" // canonical content (op-playback never writes it)
            "branches"
            "commits"
            "branch_ops"
            "accounts_v0"
            "user_data_v0"
            "toplevels_v0"
            "scripts_v0"
            "sync_remotes"
            "sync_cursors"
            "sync_conflicts" ]
        canonical
        |> List.iter (fun t ->
          Expect.isFalse
            (List.contains t Seed.projectionTables)
            $"{t} is canonical and must NOT be in the projection drop-set (it would be lost on a schema change)")
      }

      // A schema change keeps your work, end to end. A schema change now runs `rebuildProjections` (drop
      // projections + re-fold), exactly what this exercises. The first test pins projection-regen + blobs;
      // this pins the thing that actually matters — your authored op LOG (and branch/commit state) come
      // through a full re-fold IDENTICAL. If this regresses, a schema bump is eating real work.
      testTask "a schema change keeps your work: a full re-fold preserves the op log" {
        let! opsBefore = countRows "package_ops"
        let! branchesBefore = countRows "branches"
        let! commitsBefore = countRows "commits"
        Expect.isTrue
          (opsBefore > 0L)
          "there are ops to preserve (not a vacuous test)"

        let! _ = Seed.rebuildProjections ()

        let! opsAfter = countRows "package_ops"
        let! branchesAfter = countRows "branches"
        let! commitsAfter = countRows "commits"
        Expect.equal
          opsAfter
          opsBefore
          "package_ops (the authored op log) is untouched by a re-fold"
        Expect.equal
          branchesAfter
          branchesBefore
          "branches preserved across a re-fold"
        Expect.equal commitsAfter commitsBefore "commits preserved across a re-fold"
      }

      // Projection-currency counters — the `dark status` glance (`projectionStatus` → opsCount vs
      // folded-through). Equal when the cache is current; a gap when ops are appended/pulled but not yet
      // folded. Guards the surface that tells you a `branch rebuild` is owed.
      testTask
        "projectionStatus: folded == total when current; a gap appears when an op is unapplied" {
        let! _ = Seed.rebuildProjections () // re-fold → every op applied → current
        let! (total1, folded1) = Seed.projectionStatus ()
        Expect.isTrue (total1 > 0L) "there are ops to count"
        Expect.equal
          folded1
          total1
          "after a rebuild, folded-through == total (cache current)"
        // mark one op unapplied → a one-op gap
        do!
          Sql.query
            "UPDATE package_ops SET applied = 0 WHERE rowid = (SELECT MIN(rowid) FROM package_ops)"
          |> Sql.executeStatementAsync
        let! (total2, folded2) = Seed.projectionStatus ()
        Expect.equal
          total2
          total1
          "total ops unchanged (the canonical log is untouched)"
        Expect.equal
          folded2
          (folded1 - 1L)
          "one unapplied op → folded-through drops by one (a visible gap)"
        let! _ = Seed.rebuildProjections () // restore: re-fold so the shared DB stays consistent
        ()
      }

      // The append seam sync sits on (`Seed.receiveOps` ← `Builtin.eventLogAppendNative`). Re-receiving the log's own
      // committed ops is a no-op — INSERT OR IGNORE on the content-addressed ids — so the cursor comes back as
      // the current max rowid and the projections are unchanged. This is what makes an incremental pull safe to
      // retry; the same append path a real peer's events take (commits shipped alongside them).
      testTask "receiveOps is idempotent and reports zero newly-applied for known ops" {
        let! events =
          Sql.query
            "SELECT id, op_blob, branch_id, commit_hash, origin_ts FROM package_ops WHERE commit_hash IS NOT NULL ORDER BY rowid DESC LIMIT 8"
          |> Sql.executeAsync (fun read ->
            (System.Guid.Parse(read.string "id"),
             read.bytes "op_blob",
             System.Guid.Parse(read.string "branch_id"),
             read.string "commit_hash",
             read.string "origin_ts"))

        Expect.isTrue (not (List.isEmpty events)) "there are committed ops to re-receive"

        let! fnsBefore = countRows "package_functions"
        let! applied = Seed.receiveOps [] events
        let! fnsAfter = countRows "package_functions"

        Expect.equal
          applied
          0L
          "re-receiving existing ops applies 0 (INSERT OR IGNORE) — the honest change count"
        Expect.equal
          fnsAfter
          fnsBefore
          "re-receiving existing ops is a no-op (idempotent append)"
      }

      // The convergence property, at the seam: a genuinely NEW op arriving from a "peer" (here: a fresh
      // SetName binding an unused name to an existing fn hash) must be appended PRESERVING its origin_ts — not
      // re-stamped with a fresh local one, which is exactly what would make two instances' LWW diverge — and
      // then folded into `locations` by the invisible playback. This is what carries across the HTTP wire.
      testTask "receiveOps appends a new op preserving origin_ts and folds it" {
        let! fnHash =
          Sql.query "SELECT hash FROM package_functions LIMIT 1"
          |> Sql.executeRowAsync (fun read -> read.string "hash")
        let! commitHash =
          Sql.query "SELECT hash FROM commits LIMIT 1"
          |> Sql.executeRowAsync (fun read -> read.string "hash")

        let loc : PT.PackageLocation =
          { owner = "Test"; modules = [ "SyncConv" ]; name = "thing" }
        let op = PT.PackageOp.SetName(loc, PT.Reference.PackageFn(PT.Hash fnHash))
        let opId = Inserts.computeOpHash op
        let opBlob = BS.PT.PackageOp.serialize opId op
        // A stamp no local edit would ever produce, so preservation is unambiguous.
        let distinctiveTs = "2099-01-01 00:00:00.000042"

        let deleteOp () : Task<unit> =
          Sql.query "DELETE FROM package_ops WHERE id = @id"
          |> Sql.parameters [ "id", Sql.uuid opId ]
          |> Sql.executeStatementAsync
        do! deleteOp () // clean any prior run

        let! _ =
          Seed.receiveOps [] [ (opId, opBlob, PT.mainBranchId, commitHash, distinctiveTs) ]

        let! storedTs =
          Sql.query "SELECT origin_ts FROM package_ops WHERE id = @id"
          |> Sql.parameters [ "id", Sql.uuid opId ]
          |> Sql.executeRowAsync (fun read -> read.string "origin_ts")
        Expect.equal
          storedTs
          distinctiveTs
          "the received op kept its original origin_ts (not a fresh local stamp)"

        let! bound =
          Sql.query
            "SELECT item_hash FROM locations WHERE owner = 'Test' AND modules = 'SyncConv' AND name = 'thing' AND unlisted_at IS NULL"
          |> Sql.executeAsync (fun read -> read.string "item_hash")
        Expect.equal
          bound
          [ fnHash ]
          "the received op folded into `locations` (invisible playback)"

        // restore the shared DB: drop the test op + its projection row, re-fold
        do! deleteOp ()
        do!
          Sql.query
            "DELETE FROM locations WHERE owner = 'Test' AND modules = 'SyncConv' AND name = 'thing'"
          |> Sql.executeStatementAsync
        let! _ = Seed.rebuildProjections ()
        ()
      } ]
