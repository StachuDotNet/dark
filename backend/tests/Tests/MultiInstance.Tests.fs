/// Multi-instance sync tests: spin up isolated in-process stores, sync the wire between them via the real
/// receive path, and assert they converge. The harness (instances, wire builders, projection inspectors, and
/// the in-process CLI driver) lives in `MultiInstanceHarness.fs`.
module Tests.MultiInstance

open Expecto

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Fumble
open LibDB.Sqlite

module Seed = LibDB.Seed
module Resolutions = LibDB.Resolutions
module Account = LibCloud.Account
module PT = LibExecution.ProgramTypes

open Tests.MultiInstanceHarness


let freshInstancesAreIsolated =
  testTask
    "fresh instances are isolated stores, and branch structure syncs across the wire" {
    let! a = freshInstance "a"
    let! b = freshInstance "b"

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
    let! (wire, _) = Seed.branchOpsSince 0L 1000L

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
    Expect.equal
      bAfter
      [ "feature"; "main" ]
      "B converged: feature arrived over the wire"
    teardown [ a; b ]
  }


let branchIdentityIsGlobal =
  testTask
    "branch identity is GLOBAL: a synced branch keeps the SAME id + name (never re-minted)" {
    // A branch `whatever` is the same `whatever` on every instance you sync — same id and name, never
    // labeled by which instance it came from. Wholesale branch-op sync carries identity verbatim.
    let! a = freshInstance "a"
    let! b = freshInstance "b"
    let branchId = System.Guid.NewGuid()

    activate a
    let create =
      PT.BranchOp.CreateBranch(branchId, "shared", Some PT.mainBranchId, None)
    let! _ = Seed.receiveBranchOps [ branchEvent create ]
    let! (wire, _) = Seed.branchOpsSince 0L 1000L

    activate b
    let! _ =
      Seed.receiveBranchOps (
        wire
        |> List.map (fun ((id, hex, ts) : string * string * string) ->
          (id, System.Convert.FromHexString hex, ts))
      )
    let! bId =
      Sql.query "SELECT id FROM branches WHERE name = @n"
      |> Sql.parameters [ "n", Sql.string "shared" ]
      |> Sql.executeRowOptionAsync (fun read -> read.string "id")

    Expect.equal
      bId
      (Some(string branchId))
      "B's `shared` branch has the SAME id A minted"
    teardown [ a; b ]
  }


let freshSchemaStoreRunsSyncPath =
  testTask
    "a fresh schema-only store runs the sync path (the composite-PK upsert folds a package op)" {
    // A fresh store built from schema.sql alone must run receiveOps — whose ON CONFLICT(id, branch_id) upsert
    // needs the composite PK to be IN schema.sql, not only in an incremental migration. Guards schema.sql
    // drifting from the migrated production schema.
    let! a = freshInstance "a"

    let loc : PT.PackageLocation =
      { owner = "Test"; modules = [ "Fresh" ]; name = "x" }
    let fnHash = "00000000000000000000000000000000000000000000000000000000deadbeef"
    let commitHash = "fresh-" + string (System.Guid.NewGuid())
    let ts = "2026-07-08T00:00:00.000Z"
    let commit = (commitHash, "author x", PT.mainBranchId, Account.IDs.darklang, ts)

    let! _ = Seed.receiveOps [ commit ] [ setNameEvent loc fnHash commitHash ts ]
    let! bound = liveHash loc

    Expect.equal
      bound
      [ fnHash ]
      "the package op folded on the fresh schema-built store"
    teardown [ a ]
  }


let packageRenameConverges =
  testTask
    "package convergence: a rename authored on A folds identically on B via the wire" {
    let! a = seededInstance "a"
    let! b = seededInstance "b"

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
    teardown [ a; b ]
  }


let deprecationConverges =
  testTask
    "deprecation converges: a Deprecate authored on A folds into `deprecations` on B via the wire" {
    // Coverage for a NON-SetName op kind end to end — proves the fold/wire path isn't SetName-specific.
    let! a = seededInstance "a"
    let! b = seededInstance "b"

    activate a
    let! fnHash = undeprecatedFunctionHash ()
    let! commit = existingCommit ()
    let! cursorBefore = currentCursor ()

    let deprecatedCount () : Task<int64> =
      Sql.query
        "SELECT COUNT(*) AS n FROM deprecations WHERE item_hash = @h AND state = 'deprecated' AND unlisted_at IS NULL"
      |> Sql.parameters [ "h", Sql.string fnHash ]
      |> Sql.executeRowAsync (fun read -> read.int64 "n")

    let! _ =
      Seed.receiveOps [] [ deprecateEvent fnHash commit "2026-07-08T12:00:00.000Z" ]
    let! aDep = deprecatedCount ()
    let! (commits, events) = wireSince cursorBefore

    activate b
    let! bBefore = deprecatedCount ()
    let! _ = Seed.receiveOps commits events
    let! bAfter = deprecatedCount ()

    Expect.isGreaterThan
      aDep
      0L
      "A folded the Deprecate op into the deprecations projection"
    Expect.equal
      bBefore
      0L
      "B didn't have the deprecation before sync (isolated seed)"
    Expect.isGreaterThan
      bAfter
      0L
      "B converged: the deprecation arrived + folded via the wire"
    teardown [ a; b ]
  }


let crossStoreConflictConverges =
  testTask
    "cross-store conflict: concurrent binds to one name converge (LWW) + record a conflict" {
    let! a = seededInstance "a"
    let! b = seededInstance "b"

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
    let! bConflicts = conflictCountAt "Test.Conflict.dup"
    let! bFinal = liveHash loc

    activate a
    let! _ = Seed.receiveOps commitsB eventsB
    let! aFinal = liveHash loc

    Expect.equal
      bFinal
      [ fnB ]
      "B keeps the LWW winner (its own later bind) after receiving A's older op"
    Expect.equal
      aFinal
      [ fnB ]
      "A converges to the same LWW winner after receiving B's op"
    Expect.equal
      bConflicts
      1L
      "B recorded exactly this divergence as a conflict (never silent)"
    teardown [ a; b ]
  }


let bidirectionalPull =
  testTask "bidirectional pull: A's and B's independent edits both land on the other" {
    let! a = seededInstance "a"
    let! b = seededInstance "b"

    let locX : PT.PackageLocation =
      { owner = "Test"; modules = [ "Bi" ]; name = "x" }
    let locY : PT.PackageLocation =
      { owner = "Test"; modules = [ "Bi" ]; name = "y" }
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
    teardown [ a; b ]
  }


let paginationBoundedBatches =
  testTask
    "pagination: eventsSince returns bounded, disjoint batches with an advancing cursor" {
    let! a = seededInstance "a"

    activate a
    let! (fnA, _) = twoFunctionHashes ()
    let! commit = existingCommit ()
    let! cur0 = currentCursor ()

    for i in [ 1; 2; 3 ] do
      let loc : PT.PackageLocation =
        { owner = "Test"; modules = [ "Page" ]; name = $"n{i}" }
      let! _ =
        Seed.receiveOps
          []
          [ setNameEvent loc fnA commit $"2026-07-08T00:00:0{i}.000Z" ]
      ()

    let! (_, batch1, c1) = Seed.eventsSince cur0 2L
    let! (_, batch2, c2) = Seed.eventsSince c1 2L

    Expect.equal (List.length batch1) 2 "first batch is bounded to the limit"
    Expect.equal (List.length batch2) 1 "second batch has exactly the remaining op"
    Expect.isGreaterThan c1 cur0 "cursor advanced past the first batch"
    Expect.isGreaterThan c2 c1 "cursor advanced again"
    teardown [ a ]
  }


let resolutionConverges =
  testTask
    "resolution converges: a keep-mine override propagates and the other instance adopts it" {
    let! a = seededInstance "a"
    let! b = seededInstance "b"

    let loc : PT.PackageLocation =
      { owner = "Test"; modules = [ "Res" ]; name = "pick" }

    // A and B concurrently bind loc; the later stamp (fnB) is the LWW winner on both after cross-sync.
    activate a
    let! (fnA, fnB) = twoFunctionHashes ()
    let! commit = existingCommit ()
    let! curA = currentCursor ()
    let! _ =
      Seed.receiveOps [] [ setNameEvent loc fnA commit "2026-07-08T00:00:00.100Z" ]
    let! (cA, eA) = wireSince curA

    activate b
    let! curB = currentCursor ()
    let! _ =
      Seed.receiveOps [] [ setNameEvent loc fnB commit "2026-07-08T00:00:00.200Z" ]
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

    Expect.equal
      aResolved
      [ fnA ]
      "A's resolution overrode the LWW winner back to fnA"
    Expect.equal
      bResolved
      [ fnA ]
      "B adopted A's resolution via the synced resolutions log"
    teardown [ a; b ]
  }


let resolutionSurvivesRebuild =
  testTask
    "resolution survives a projection rebuild — rebuildProjections re-applies the overlay" {
    // A rebuild re-folds the op log (which would pick the LWW winner), so it MUST also re-apply the
    // resolutions overlay or a human override is silently lost. Effective binding = fold → then resolutions.
    let! a = seededInstance "a"

    let loc : PT.PackageLocation =
      { owner = "Test"; modules = [ "ResRebuild" ]; name = "pick" }
    activate a
    let! (fnA, fnB) = twoFunctionHashes ()
    let! commit = existingCommit ()
    // fnB is the LWW winner (ts .200 > .100); a human overrides back to fnA at a later `at`.
    let! _ =
      Seed.receiveOps [] [ setNameEvent loc fnA commit "2026-07-08T00:00:00.100Z" ]
    let! _ =
      Seed.receiveOps [] [ setNameEvent loc fnB commit "2026-07-08T00:00:00.200Z" ]
    let res =
      Resolutions.mk
        loc
        (PT.Reference.PackageFn(PT.Hash fnA))
        "human"
        PT.mainBranchId
        "2026-07-08T00:00:00.300Z"
    do! Resolutions.recordAndApply res
    let! beforeRebuild = liveHash loc
    Expect.equal
      beforeRebuild
      [ fnA ]
      "the override took (fnA, not the LWW winner fnB)"

    let! _ = Seed.rebuildProjections ()
    let! afterRebuild = liveHash loc
    Expect.equal
      afterRebuild
      [ fnA ]
      "the override SURVIVED the rebuild (else it reverts to fnB)"
    teardown [ a ]
  }


let resolutionSurvivesIncrementalFold =
  testTask
    "resolution survives the INCREMENTAL fold on the pull path (receiveOps re-applies the overlay)" {
    // The incremental fold on the PULL path (receiveOps) must re-apply the resolution overlay, just as
    // rebuildProjections does; otherwise a synced-in resolution is reverted the moment a later op folds, and
    // two peers that had agreed diverge. Hard shape: the resolution's apply is an idempotent no-op (the
    // binding already holds the chosen content), so the binding keeps the OLD op's origin_ts; a newer op then
    // folds and out-ranks it, and only a post-fold reapply restores the human's pick.
    let! a = seededInstance "a"

    let loc : PT.PackageLocation =
      { owner = "Test"; modules = [ "ResIncFold" ]; name = "pick" }
    activate a
    let! (fnA, fnB) = twoFunctionHashes ()
    let! commit = existingCommit ()
    // Binding is fnA at .100. The resolution ALSO picks fnA (at .300) — so applyToLocations is a no-op and
    // the binding's origin_ts stays .100, NOT .300.
    let! _ =
      Seed.receiveOps [] [ setNameEvent loc fnA commit "2026-07-08T00:00:00.100Z" ]
    let res =
      Resolutions.mk
        loc
        (PT.Reference.PackageFn(PT.Hash fnA))
        "human"
        PT.mainBranchId
        "2026-07-08T00:00:00.300Z"
    do! Resolutions.recordAndApply res
    let! beforeFold = liveHash loc
    Expect.equal beforeFold [ fnA ] "the resolution's pick (fnA) is bound"

    // A fresh fnB op (.200) folds via the INCREMENTAL pull path. .200 out-ranks the binding's stale .100, so
    // the fold flips it to fnB — but the resolution (.300) is newer, so the post-fold reapply must win again.
    let! folded =
      Seed.receiveOps [] [ setNameEvent loc fnB commit "2026-07-08T00:00:00.200Z" ]
    Expect.isGreaterThan
      folded
      0L
      "the fnB op actually folded (so the reapply path runs)"
    let! afterFold = liveHash loc
    Expect.equal
      afterFold
      [ fnA ]
      "the override survives the incremental fold (else receiveOps folds to fnB)"
    teardown [ a ]
  }


let durableReleaseMigratesInPlace =
  testTask
    "a durable Release migrates a seeded store IN PLACE, preserving its data (not a clean-break)" {
    // Guards the CLI data-preserving upgrade path (planCliUpgrade MigrateInPlace → applyPending →
    // applyRelease's durable branch). A durable step (here an additive schema column) must land on an EXISTING
    // store and keep every op + its projected content — unlike a clean-break, which clears.
    let! a = seededInstance "durablemig"

    activate a
    let countOps () =
      Sql.query "SELECT COUNT(*) AS n FROM package_ops"
      |> Sql.executeRowAsync (fun read -> read.int64 "n")
    let! opsBefore = countOps ()
    let! probeFn = undeprecatedFunctionHash ()
    Expect.isGreaterThan opsBefore 0L "the seeded store has ops to preserve"

    LibDB.Releases.applyRelease (
      { n = 999
        sql = "ALTER TABLE package_ops ADD COLUMN migration_probe TEXT"
        reserialize = None
        clearForRebuild = false }
      : LibDB.Releases.Release
    )

    let! hasCol =
      Sql.query
        "SELECT COUNT(*) AS n FROM pragma_table_info('package_ops') WHERE name = 'migration_probe'"
      |> Sql.executeRowAsync (fun read -> read.int64 "n")
    let! opsAfter = countOps ()
    let! fnStillThere =
      Sql.query "SELECT COUNT(*) AS n FROM package_functions WHERE hash = @h"
      |> Sql.parameters [ "h", Sql.string probeFn ]
      |> Sql.executeRowAsync (fun read -> read.int64 "n")

    Expect.equal hasCol 1L "the durable schema step added the column in place"
    Expect.equal
      opsAfter
      opsBefore
      "every op preserved (durable is not a clean-break)"
    Expect.equal
      fnStillThere
      1L
      "the package content survived the in-place migration"
    teardown [ a ]
  }


let branchMergeSyncs =
  testTask
    "branch merge syncs across stores: a branch's create + merge land on B (merged_at set)" {
    let! a = seededInstance "a"
    let! b = seededInstance "b"

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

    // Author on A: create the branch, then merge it. (Content-on-branch folding is covered by SyncScenarios;
    // merge re-homes it, so this focuses on the lifecycle sync.)
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
    teardown [ a; b ]
  }


let concurrentRebasesConverge =
  testTask
    "concurrent rebases of one branch converge to the LWW winner, order-independent" {
    let! a = seededInstance "a"
    let! b = seededInstance "b"

    let branchId = System.Guid.NewGuid()
    let baseX = "1111111111111111111111111111111111111111111111111111111111111111"
    let baseY = "2222222222222222222222222222222222222222222222222222222222222222"

    // Both instances know the branch (shared CreateBranch, older than either rebase). Then two instances
    // rebase it CONCURRENTLY to different bases: X at ts .200 (newer) beats Y at ts .100.
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

    Expect.equal aBase (Some baseX) "A: the newer rebase (ts .200 → baseX) wins"
    Expect.equal
      bBase
      (Some baseX)
      "B: converges to the SAME base though the winner arrived last"
    teardown [ a; b ]
  }


let darkConflictsRendersConflict =
  testTask
    "dark conflicts renders a real recorded conflict (regression: short-hash slice was Int32)" {
    // `conflicts.dark`'s `short` did `String.slice h 0 8` with bare Int32 literals; `String.slice` wants
    // `Int`, so the whole `dark conflicts` display threw a type-mismatch on ANY real conflict. No unit test
    // drove the display, so it was invisible. Build a genuine cross-store conflict, then assert the CLI
    // renders it (location + truncated hashes) instead of crashing.
    let! a = seededInstance "a"
    let! b = seededInstance "b"

    let loc : PT.PackageLocation =
      { owner = "Test"; modules = [ "ConflictCli" ]; name = "dup" }
    activate a
    let! (fnA, fnB) = twoFunctionHashes ()
    let! commit = existingCommit ()
    let! curA = currentCursor ()
    let! _ =
      Seed.receiveOps [] [ setNameEvent loc fnA commit "2026-07-08T00:00:00.100Z" ]
    let! (cA, eA) = wireSince curA

    activate b
    let! _ =
      Seed.receiveOps [] [ setNameEvent loc fnB commit "2026-07-08T00:00:00.200Z" ]
    // B (holding the later LWW winner) receives A's older op → records a conflict.
    let! _ = Seed.receiveOps cA eA
    let! n = conflictCountAt "Test.ConflictCli.dup"
    Expect.equal n 1L "the divergence was recorded as a conflict on B"

    let! state = buildCliState ()
    let! out = runCli state [ "conflicts" ]
    // If `short` regressed, `String.slice` throws, `executeFunction` returns Error, and `runCli` fails the
    // test. Reaching here with the location rendered is the guard.
    Expect.stringContains
      out
      "Test.ConflictCli.dup"
      "the CLI lists the diverged location instead of crashing"
    teardown [ a; b ]
  }


let darkConflictsCleanPath =
  testTask
    "dark conflicts on a converged store reports no conflicts (the clean path)" {
    let! a = seededInstance "a"

    activate a
    let! n = conflictCount ()
    Expect.equal n 0L "a freshly-seeded store has no conflicts"

    let! state = buildCliState ()
    let! out = runCli state [ "conflicts" ]
    Expect.stringContains
      out
      "converged"
      "the clean path reports everything is converged"
    teardown [ a ]
  }


let cliDriverReadsActiveStore =
  testTask
    "the in-process CLI driver reads the active store (dispatch + rendering work end to end)" {
    // Sanity that runCli drives the real dispatch against the swapped store, so the CLI tests above are
    // meaningful. `version` is a pure, always-available command with stable output.
    let! a = seededInstance "a"

    activate a
    let! state = buildCliState ()
    let! out = runCli state [ "version" ]
    Expect.isFalse (out = "") "the CLI produced output for `version`"
    teardown [ a ]
  }


let viewsRealSeededItem =
  testTask
    "an instance can view a real seeded package item — `dark view Stdlib.Option.Option`" {
    // Grounds the synthetic Test.* convergence scenarios in something real: the seeded store holds REAL
    // packages, and the CLI resolves + renders a well-known one.
    let! a = seededInstance "a"

    activate a
    let! state = buildCliState ()
    let! out = runCli state [ "view"; "Darklang.Stdlib.Option.Option" ]
    Expect.stringContains out "Option" "the real Option type resolves + renders"
    let low = (out : string).ToLower()
    Expect.isFalse
      (low.Contains "not found" || low.Contains "no such" || low.Contains "error")
      "the view isn't an error banner"
    teardown [ a ]
  }


let tests =
  testSequenced
  <| testList
    "MultiInstance"
    [ freshInstancesAreIsolated
      branchIdentityIsGlobal
      freshSchemaStoreRunsSyncPath
      packageRenameConverges
      deprecationConverges
      crossStoreConflictConverges
      bidirectionalPull
      paginationBoundedBatches
      resolutionConverges
      resolutionSurvivesRebuild
      resolutionSurvivesIncrementalFold
      durableReleaseMigratesInPlace
      branchMergeSyncs
      concurrentRebasesConverge
      darkConflictsRendersConflict
      darkConflictsCleanPath
      cliDriverReadsActiveStore
      viewsRealSeededItem ]
