/// Tests for LibDB.Seed.rebuildProjections: the projection tables are *regenerable from the op log* — drop
/// them, re-fold package_ops, and they come back identical. The op log (package_ops) is canonical, untouched.
module Tests.OpsProjections

open Expecto

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude

open Fumble
open LibDB.Sqlite

module Seed = LibDB.Seed
module Releases = LibDB.Releases

let private countRows (table : string) : Task<int64> =
  Sql.query $"SELECT COUNT(*) as n FROM {table}"
  |> Sql.executeRowAsync (fun read -> read.int64 "n")

/// A content fingerprint of the projections: the sorted set of every projected item's hash. Two folds that
/// produce the same fingerprint produced the same projections (down to identity), regardless of row order or
/// nondeterministic columns like `location_id`.
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


let rebuildIsDeterministic =
  testTask
    "rebuildProjections deterministically regenerates projections from the op log" {
    // package_blobs is canonical content — a rebuild must never touch it
    let! blobsBefore = countRows "package_blobs"

    // drop the regenerable projections + re-fold the entire op log
    let! reapplied = Seed.rebuildProjections ()
    Expect.isTrue (reapplied > 0L) "ops were re-folded"
    let! fns1 = countRows "package_functions"
    let! locs1 = countRows "locations"
    Expect.isTrue (fns1 > 0L) "projections regenerated (non-empty) from the op log"

    // a SECOND rebuild reproduces the EXACT same projections — the rebuild is a deterministic function of the
    // op log. (Robust to other tests mutating the shared DB: we compare two rebuilds of the *current* log.)
    let! _ = Seed.rebuildProjections ()
    let! fns2 = countRows "package_functions"
    let! locs2 = countRows "locations"
    Expect.equal
      fns2
      fns1
      "package_functions: a re-rebuild reproduces the same projection"
    Expect.equal locs2 locs1 "locations: a re-rebuild reproduces the same projection"

    // canonical content (package_blobs) is NOT a projection — untouched across rebuilds
    let! blobsAfter = countRows "package_blobs"
    Expect.equal
      blobsAfter
      blobsBefore
      "package_blobs (canonical content) preserved, not dropped"
  }

let refoldReproducesContent =
  // Stronger than the count check above: the exact CONTENT of the projections (the set of projected item
  // hashes) is reproduced across a re-fold — the fold is a deterministic function of the op log, not merely
  // cardinality-preserving. This is what makes "drop the projections and re-fold" safe.
  testTask
    "re-fold reproduces identical projection CONTENT (deterministic fold, not just counts)" {
    let! _ = Seed.rebuildProjections ()
    let! fp1 = itemHashes ()
    Expect.isTrue (String.length fp1 > 0) "non-vacuous: the projections have content"
    let! _ = Seed.rebuildProjections ()
    let! fp2 = itemHashes ()
    Expect.equal
      fp2
      fp1
      "the exact set of projected item hashes is identical across a re-fold"
  }

let originTsStrictlyIncreasing =
  // The authoring stamp behind the timestamp-LWW: locally-inserted ops must get a STRICTLY-increasing
  // origin_ts, even within one wall-clock millisecond — otherwise two sequential SetNames to the same name
  // would tie and be reordered by content hash (a later edit could lose its own location).
  test
    "origin_ts stamps are strictly increasing (sequential local edits never tie the LWW)" {
    let stamps = List.init 100 (fun _ -> LibDB.Inserts.nextOriginTs ())
    Expect.equal stamps (List.sort stamps) "stamps are monotonic (non-decreasing)"
    Expect.equal
      (List.length (List.distinct stamps))
      100
      "stamps are strictly distinct — no ties even across a burst within one millisecond"
  }

let durableReleaseCarriesForward =
  // THE POINT, end to end: migrate a store from one Release to the next WITHOUT losing authored work. A
  // durable Release step carries the op log forward (a schema copy-swap + an optional op-format re-serialize);
  // the projections are then dropped and RE-FOLDED from that same log in the new format. The op log is
  // canonical — you migrate the LOG, never the derived tables; and meaning-stable hashing keeps each op's
  // identity across a re-serialize. (Contrast the shipped Release 3, a clean-BREAK; this proves the DURABLE
  // path a real future release will use.)
  testTask
    "release migration (durable): authored op log carried forward + projections re-folded, nothing lost" {
    let! opsBefore = countRows "package_ops"
    let! blobsBefore = countRows "package_blobs"
    Expect.isTrue
      (opsBefore > 0L)
      "there is authored work to migrate (not a vacuous test)"
    let! fpBefore = itemHashes ()

    // A durable forward Release step (n = code+1): a real schema change that runs the reserialize branch of
    // applyRelease (NOT clearForRebuild) — so the authored op log is preserved, carried forward, re-folded.
    // CLEANUP(reserialize-test): the remap is identity because `reserialize : byte[] -> byte[]` doesn't get the
    // op id, so a genuine deserialize→re-encode can't be driven here — this exercises the path, not the transform.
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
      "projections re-fold IDENTICALLY in the new Release — same items"

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

let registryCoversProjections =
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

let noCanonicalInDropSet =
  // A schema change keeps your work: the bootstrap drops ONLY `projectionTables` and re-folds the op log, so
  // the authored, canonical data must NEVER appear in that drop-set. If it did, a schema bump would delete it.
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

let schemaChangeKeepsWork =
  // A schema change now runs `rebuildProjections` (drop projections + re-fold). This pins the thing that
  // matters — your authored op LOG (and branch/commit state) come through a full re-fold IDENTICAL.
  testTask "a schema change keeps your work: a full re-fold preserves the op log" {
    let! opsBefore = countRows "package_ops"
    let! branchesBefore = countRows "branches"
    let! commitsBefore = countRows "commits"
    Expect.isTrue (opsBefore > 0L) "there are ops to preserve (not a vacuous test)"

    let! _ = Seed.rebuildProjections ()

    let! opsAfter = countRows "package_ops"
    let! branchesAfter = countRows "branches"
    let! commitsAfter = countRows "commits"
    Expect.equal
      opsAfter
      opsBefore
      "package_ops (the authored op log) is untouched by a re-fold"
    Expect.equal branchesAfter branchesBefore "branches preserved across a re-fold"
    Expect.equal commitsAfter commitsBefore "commits preserved across a re-fold"
  }


// testSequenced because the rebuild cases DELETE + refold the *shared* projection tables and mark all ops
// unapplied — they must not race other DB tests' reads/writes mid-rebuild. The pure cases ride along.
let tests =
  testSequenced
  <| testList
    "OpsProjections"
    [ rebuildIsDeterministic
      refoldReproducesContent
      originTsStrictlyIncreasing
      durableReleaseCarriesForward
      registryCoversProjections
      noCanonicalInDropSet
      schemaChangeKeepsWork ]
