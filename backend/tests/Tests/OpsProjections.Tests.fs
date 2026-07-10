/// Tests for ops⊥projections (LibDB.Seed.rebuildProjections).
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

        // A durable forward Release step (n = code+1): a real schema change (the index below) that runs the
        // reserialize branch of applyRelease (NOT clearForRebuild) — so the authored op log is preserved and
        // carried forward, then re-folded. What this asserts: nothing is LOST across a durable release (op-log
        // count, blobs, and the re-folded projections all survive identically).
        // CLEANUP(reserialize-test): the remap is identity because `reserialize : byte[] -> byte[]` doesn't get
        // the op id, so a genuine deserialize→re-encode can't be driven here. A real byte-changing-but-meaning-
        // stable test needs the id threaded into reserialize; until then this exercises the path, not the transform.
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


      // The append seam sync sits on (`Seed.receiveOps` ← `Builtin.packageOpsAppendNative`). Re-receiving the log's own
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

      // These tests (and SyncScenarios.Tests.fs) exercise the receive/fold/detect/resolve seam on ONE store,
      // applying a "peer's" ops through the real receive path — which is where convergence, LWW, and the
      // origin_ts reconcile actually live. The genuinely-two-instance case (N isolated stores syncing the wire,
      // torn down per test) lives in MultiInstance.Tests.fs, and the real-process/HTTP case in SyncE2E.Tests.fs.

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
      }

      testTask "conflict → resolution → converge: divergence detected, LWW applied, human override wins" {
        // Two distinct fn contents to bind one name to, in turn.
        let! twoFns =
          Sql.query "SELECT hash FROM package_functions LIMIT 2"
          |> Sql.executeAsync (fun read -> read.string "hash")

        let hashA, hashB =
          match twoFns with
          | a :: b :: _ -> a, b
          | _ -> Exception.raiseInternal "seed needs >=2 functions" []

        let! commitHash =
          Sql.query "SELECT hash FROM commits LIMIT 1"
          |> Sql.executeRowAsync (fun read -> read.string "hash")

        let loc : PT.PackageLocation =
          { owner = "Test"; modules = [ "Diverge" ]; name = "dv" }

        let mkEvent (h : string) (ts : string) =
          let op = PT.PackageOp.SetName(loc, PT.Reference.PackageFn(PT.Hash h))
          let opId = Inserts.computeOpHash op
          (opId, BS.PT.PackageOp.serialize opId op, PT.mainBranchId, commitHash, ts)

        let (idA, blobA, _, _, _) = mkEvent hashA "2099-01-01T00:00:00.001Z"
        let (idB, blobB, _, _, _) = mkEvent hashB "2099-01-01T00:00:00.002Z"

        let cleanup () : Task<unit> =
          task {
            do!
              Sql.query "DELETE FROM package_ops WHERE id = @a OR id = @b"
              |> Sql.parameters [ "a", Sql.uuid idA; "b", Sql.uuid idB ]
              |> Sql.executeStatementAsync
            do!
              Sql.query
                "DELETE FROM locations WHERE owner = 'Test' AND modules = 'Diverge' AND name = 'dv'"
              |> Sql.executeStatementAsync
            do!
              Sql.query "DELETE FROM sync_conflicts WHERE location = 'Test.Diverge.dv'"
              |> Sql.executeStatementAsync
            do!
              Sql.query "DELETE FROM resolutions WHERE location = 'Test.Diverge.dv'"
              |> Sql.executeStatementAsync
          }

        do! cleanup ()

        // #1: bind the name to A (no prior binding — no divergence).
        let! _ =
          Seed.receiveOps [] [ (idA, blobA, PT.mainBranchId, commitHash, "2099-01-01T00:00:00.001Z") ]
        // #2: a peer's op rebinds it to B — that's a divergence (curHash=A ≠ incoming=B), auto-resolved by LWW.
        let! _ =
          Seed.receiveOps [] [ (idB, blobB, PT.mainBranchId, commitHash, "2099-01-01T00:00:00.002Z") ]

        let! conflicts = LibDB.Conflicts.list ()

        let recorded =
          conflicts
          |> List.exists (fun (c: LibDB.Conflicts.Conflict) ->
            c.location = "Test.Diverge.dv"
            && c.localHash = hashA
            && c.incomingHash = hashB
            && c.chosenHash = hashB) // B is newer-by-stamp, so LWW picks it — and that's recorded

        Expect.isTrue
          recorded
          "detectDivergences recorded the name→two-hashes divergence with the LWW winner"

        // The live binding is B — LWW picked the newer op.
        let liveHash () : Task<List<string>> =
          Sql.query
            "SELECT item_hash FROM locations WHERE owner = 'Test' AND modules = 'Diverge' AND name = 'dv' AND unlisted_at IS NULL"
          |> Sql.executeAsync (fun read -> read.string "item_hash")

        let! boundLww = liveHash ()
        Expect.equal boundLww [ hashB ] "LWW bound the name to the newer content (B)"

        // A human keeps THEIR version (A): mint + apply a Resolution with a newer `at` so it wins the overlay.
        let res : LibDB.Resolutions.Resolution =
          { id = "test-resolution-diverge"
            branchId = PT.mainBranchId
            location = loc
            choice = PT.Reference.PackageFn(PT.Hash hashA)
            resolvedBy = "human"
            at = "2099-01-01T00:00:00.003Z" }

        do! LibDB.Resolutions.recordAndApply res

        // The binding converged on the human's choice (A), overriding the LWW outcome — the "keep mine" that
        // then propagates as a synced Resolution so peers converge too.
        let! boundResolved = liveHash ()

        Expect.equal
          boundResolved
          [ hashA ]
          "the resolution overrode the LWW binding → converged on the human's pick"

        do! cleanup ()
        let! _ = Seed.rebuildProjections ()
        ()
      }

      // The op id is content-only, so two instances that independently author the SAME op
      // stamp it with different local origin_ts. receiveOps must reconcile to the MIN (deterministic on every
      // instance) in BOTH package_ops AND the folded locations binding — otherwise a later competing edit
      // resolves differently on each instance and they diverge permanently. This is the two-instance
      // convergence property, reproduced on one store via re-receives at different stamps.
      testTask "a re-received op reconciles origin_ts to the MIN in package_ops + locations (convergence)" {
        let! fnHash =
          Sql.query "SELECT hash FROM package_functions LIMIT 1"
          |> Sql.executeRowAsync (fun read -> read.string "hash")
        let! commitHash =
          Sql.query "SELECT hash FROM commits LIMIT 1"
          |> Sql.executeRowAsync (fun read -> read.string "hash")

        let loc : PT.PackageLocation =
          { owner = "Test"; modules = [ "M1Reconcile" ]; name = "dv" }
        let op = PT.PackageOp.SetName(loc, PT.Reference.PackageFn(PT.Hash fnHash))
        let opId = Inserts.computeOpHash op
        let opBlob = BS.PT.PackageOp.serialize opId op

        let recv (ts : string) : Task<unit> =
          task {
            let! _ = Seed.receiveOps [] [ (opId, opBlob, PT.mainBranchId, commitHash, ts) ]
            ()
          }

        let opTs () : Task<string> =
          Sql.query "SELECT origin_ts FROM package_ops WHERE id = @id"
          |> Sql.parameters [ "id", Sql.uuid opId ]
          |> Sql.executeRowAsync (fun read -> read.string "origin_ts")

        let locTs () : Task<List<string>> =
          Sql.query
            "SELECT origin_ts FROM locations WHERE owner = 'Test' AND modules = 'M1Reconcile' AND name = 'dv' AND unlisted_at IS NULL"
          |> Sql.executeAsync (fun read -> read.string "origin_ts")

        let cleanup () : Task<unit> =
          task {
            do!
              Sql.query "DELETE FROM package_ops WHERE id = @id"
              |> Sql.parameters [ "id", Sql.uuid opId ]
              |> Sql.executeStatementAsync
            do!
              Sql.query
                "DELETE FROM locations WHERE owner = 'Test' AND modules = 'M1Reconcile' AND name = 'dv'"
              |> Sql.executeStatementAsync
          }

        do! cleanup ()

        // #1 receive at ts=100 → binding folded with 100
        do! recv "2099-01-01T00:00:00.100Z"
        let! ts1 = opTs ()
        let! loc1 = locTs ()
        Expect.equal ts1 "2099-01-01T00:00:00.100Z" "op stamped at 100"
        Expect.equal loc1 [ "2099-01-01T00:00:00.100Z" ] "binding folded at 100"

        // #2 re-receive the SAME op at an EARLIER 050 → reconcile DOWN to MIN, and the binding re-folds to 050
        do! recv "2099-01-01T00:00:00.050Z"
        let! ts2 = opTs ()
        let! loc2 = locTs ()
        Expect.equal ts2 "2099-01-01T00:00:00.050Z" "op reconciled to the MIN (050)"
        Expect.equal loc2 [ "2099-01-01T00:00:00.050Z" ] "binding origin_ts refreshed to the MIN (050)"

        // #3 re-receive at a LATER 200 → MIN stays 050, no spurious change
        do! recv "2099-01-01T00:00:00.200Z"
        let! ts3 = opTs ()
        Expect.equal ts3 "2099-01-01T00:00:00.050Z" "a later stamp never raises the reconciled MIN"

        do! cleanup ()
        let! _ = Seed.rebuildProjections ()
        () } ]
