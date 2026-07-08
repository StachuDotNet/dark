/// Package seed: extract and grow.
///
/// A seed is a copy of data.db with projection tables emptied and ops marked
/// unapplied. It has the full schema so it can be used directly as a data.db.
///
/// Export ("extract"): copy data.db, strip derived data, VACUUM.
/// Grow: apply unapplied ops to rebuild projection tables, evaluate values.
///
/// On CLI startup the grow step runs automatically — if everything is already
/// applied it's a single fast SELECT COUNT and returns immediately.
///
/// This module is also the home of **ops ⊥ projections**: the op log
/// (`package_ops`) is canonical; the package tables (functions/types/values,
/// locations, dependencies, deprecations) are regenerable *projections* folded
/// from it. `projectionRegistry` names each projection + the op kinds that
/// dirty it; `applyUnappliedOps` folds pending ops (append and fold are
/// separable — the `applied` flag is the seam); `rebuildProjections` drops the
/// projections, marks every op unapplied, and re-folds → byte-identical tables;
/// `rebuildDirtied` is the incremental counterpart. This is what makes a schema
/// change safe (drop projections, re-fold — the op log is never touched:
/// "durable-canon") and, later, what lets a synced peer's ops fold in like any
/// local edit.
module LibDB.Seed

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open LibExecution.ProgramTypes

open Microsoft.Data.Sqlite
open Fumble
open LibDB.Sqlite

module PT = LibExecution.ProgramTypes
module RT = LibExecution.RuntimeTypes
module PT2RT = LibExecution.ProgramTypesToRuntimeTypes
module Execution = LibExecution.Execution
module Blob = LibExecution.Blob
module BS = LibSerialization.Binary.Serialization


// ---------------------
// Export
// ---------------------

/// Export a seed database to the given output path.
/// Copies the full source DB, then strips derived data and archived branches.
let export (outputPath : string) : Task<unit> =
  task {
    let sourcePath = LibConfig.Config.dbPath

    if System.IO.File.Exists outputPath then System.IO.File.Delete outputPath

    // Checkpoint WAL before copying to ensure all data is in the main file
    let sourceConnStr = $"Data Source={sourcePath};Mode=ReadOnly;Cache=Private"
    use sourceConn = new SqliteConnection(sourceConnStr)
    sourceConn.Open()
    use checkpointCmd = sourceConn.CreateCommand()
    checkpointCmd.CommandText <- "PRAGMA wal_checkpoint(TRUNCATE);"
    checkpointCmd.ExecuteNonQuery() |> ignore<int>
    sourceConn.Close()

    System.IO.File.Copy(sourcePath, outputPath)

    let connStr = $"Data Source={outputPath};Mode=ReadWriteCreate;Cache=Private"

    use conn = new SqliteConnection(connStr)
    conn.Open()

    use pragmaCmd = conn.CreateCommand()
    pragmaCmd.CommandText <-
      "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;"
    pragmaCmd.ExecuteNonQuery() |> ignore<int>

    use cleanCmd = conn.CreateCommand()
    cleanCmd.CommandText <-
      """
      DELETE FROM locations;
      DELETE FROM package_types;
      DELETE FROM package_values;
      DELETE FROM package_functions;
      DELETE FROM package_dependencies;
      DELETE FROM deprecations;

      DELETE FROM package_ops WHERE branch_id IN (
        SELECT id FROM branches WHERE archived_at IS NOT NULL);
      DELETE FROM commits WHERE branch_id IN (
        SELECT id FROM branches WHERE archived_at IS NOT NULL);
      DELETE FROM branches WHERE archived_at IS NOT NULL;

      UPDATE package_ops SET applied = 0;
      UPDATE branch_ops SET applied = 1;
      """
    cleanCmd.ExecuteNonQuery() |> ignore<int>

    use vacuumCmd = conn.CreateCommand()
    vacuumCmd.CommandText <- "VACUUM;"
    vacuumCmd.ExecuteNonQuery() |> ignore<int>

    conn.Close()
  }


// ---------------------
// Grow
// ---------------------

/// Apply all unapplied package_ops in the database.
/// Returns the count of ops applied.
let applyUnappliedOps () : Task<int64> =
  task {
    // Fast check: are there any unapplied ops? Avoids loading blobs when count is 0.
    let! count =
      Sql.query "SELECT COUNT(*) as n FROM package_ops WHERE applied = 0"
      |> Sql.executeRowAsync (fun read -> read.int64 "n")

    if count = 0L then
      return 0L
    else

      let! unappliedOps =
        Sql.query
          """
        SELECT id, op_blob, branch_id, commit_hash
        FROM package_ops
        WHERE applied = 0
        -- rowid (insertion order) is the deterministic tiebreak: created_at is second-resolution, so a
        -- batch's ops share it and ordering by created_at alone leaves same-second ops in an unspecified
        -- order. The fold's final state is order-independent for the cases that matter (SetName resolves by
        -- origin_ts; AddFn is by-hash), but a deterministic replay order keeps re-folds byte-identical.
        ORDER BY created_at ASC, rowid ASC
        """
        |> Sql.executeAsync (fun read ->
          let opId = read.uuid "id"
          let opBlob = read.bytes "op_blob"
          let branchId : PT.BranchId = read.uuid "branch_id"
          let commitHash = read.stringOrNone "commit_hash"
          let op = BS.PT.PackageOp.deserialize opId opBlob
          (opId, op, branchId, commitHash))

      if List.isEmpty unappliedOps then
        return 0L
      else
        let groups =
          unappliedOps
          |> List.groupBy (fun (_, _, branchId, commitHash) ->
            (branchId, commitHash))
          |> Map.toList

        // Bulk cold-start path: open one connection, run all groups + the
        // applied=1 sweep inside a single transaction with synchronous=OFF.
        // For 9000+ ops this turns ~20k individual WAL commits into one and
        // takes the apply phase from ~5s to well under a second. Crash
        // safety isn't a concern here: an aborted run leaves applied=0 on
        // the same ops, and the next boot replays them. Replay isn't strictly
        // idempotent (location_id / deprecation_id come from Guid.NewGuid()
        // so a partial-then-replay produces distinct rows for the same op)
        // but the final-state projection is equivalent — pre-existing rows
        // from the crashed run keep unlisted_at=NULL and get superseded by
        // the replay's fresh inserts the same way a normal re-add would.
        //
        // FK enforcement is disabled for the duration. Microsoft.Data.Sqlite
        // defaults `Foreign Keys=True` on the connection string; with that
        // on, replaying ops in any order other than perfect topological
        // tripped FK violations (locations referencing branches that arrive
        // later in the batch, etc.). Standard bulk-load practice in SQLite
        // is OFF-bulk-load-CHECK; we run `PRAGMA foreign_key_check` after
        // commit and fail loudly if any actual violations were introduced.
        use conn = new SqliteConnection(LibDB.Sqlite.connString)
        do! conn.OpenAsync()
        let runRaw (sql : string) : Task<unit> =
          task {
            use cmd = conn.CreateCommand()
            cmd.CommandText <- sql
            let! _ = cmd.ExecuteNonQueryAsync()
            return ()
          }
        // PRAGMAs that affect transaction semantics must run *outside* a
        // transaction. foreign_keys=OFF in particular only takes effect
        // when not in a tx.
        //
        // synchronous=NORMAL (not OFF): OFF lets the writer skip syncing
        // the change-counter update to disk, which poisons the page cache
        // of any concurrent reader on a different connection — that
        // reader then returns SQLITE_CORRUPT ("database disk image is
        // malformed") even though PRAGMA integrity_check is clean. NORMAL
        // keeps the WAL-mode coherence guarantee at the cost of one fsync
        // per checkpoint; the bulk-grow win still comes from collapsing
        // 9000+ commits into one transaction.
        do!
          runRaw
            "PRAGMA journal_mode=WAL; \
             PRAGMA synchronous=NORMAL; \
             PRAGMA busy_timeout=5000; \
             PRAGMA foreign_keys=OFF;"
        let opCount = List.length unappliedOps
        use _bulk = Telemetry.span "seed.applyOps.bulk" [ "ops", string opCount ]
        use tx = conn.BeginTransaction()

        for ((branchId, commitHash), ops) in groups do
          let opsOnly = ops |> List.map (fun (_, op, _, _) -> op)
          do! PackageOpPlayback.applyOpsOnConnection conn branchId commitHash opsOnly

        // Mark all loaded ops applied in a single statement (inside the same
        // outer transaction).
        do! runRaw "UPDATE package_ops SET applied = 1 WHERE applied = 0"

        tx.Commit()

        // Integrity check. `PRAGMA foreign_key_check` runs regardless of
        // the per-connection `foreign_keys` setting — it scans every FK in
        // every table and returns a row per violation (or nothing when the
        // DB is clean). Anything here is a real data bug: either the seed
        // was inconsistent or our op-replay produced dangling refs.
        // Surface it loudly rather than persisting a silently-broken
        // projection.
        //
        // We don't bother flipping `foreign_keys` back on first — the
        // pragma is per-connection-instance, and this connection is about
        // to be closed. The next connection picks up the connection-string
        // default (`Foreign Keys=True`) on its own.
        let violations = ResizeArray<string * string * string * string>()
        use checkCmd = conn.CreateCommand()
        checkCmd.CommandText <- "PRAGMA foreign_key_check"
        use! reader = checkCmd.ExecuteReaderAsync()
        // fantomas can't format `while! reader.ReadAsync() do ...` inside a
        // task CE, so we drive the loop with a mutable flag.
        let mutable keepReading = true
        while keepReading do
          let! hasNext = reader.ReadAsync()
          if hasNext then
            // columns: table, rowid, parent, fkid
            let row =
              reader.GetString(0),
              reader.GetValue(1).ToString(),
              reader.GetString(2),
              reader.GetValue(3).ToString()
            violations.Add(row)
          else
            keepReading <- false
        if violations.Count > 0 then
          let summary =
            violations
            |> Seq.truncate 5
            |> Seq.map (fun (t, r, p, f) -> $"  {t} rowid={r} → {p} (fk_id={f})")
            |> String.concat "\n"
          Exception.raiseInternal
            $"foreign_key_check reported {violations.Count} \
              violation(s) after grow:\n{summary}"
            [ "first_violations", summary ]

        return int64 opCount
  }


/// A regenerable projection in the per-branch cache: `table` is the projection, and `dirtiedBy` is
/// the set of `package_ops` kinds whose arrival invalidates it (so an incremental update re-folds
/// only the projections an incoming op touches). Op-kind names match the `PackageOp` DU cases.
type Projection = { table : string; dirtiedBy : Set<string> }

/// The committed events after `cursor` from this instance's own log (≤ `limit`), the commits they
/// reference, and the new cursor (max rowid in the batch, or `cursor` if nothing is new). Exactly what a
/// peer serves — the READ half of the event-log seam. Native (F#) so serving a large batch doesn't pay
/// per-row interpreter overhead (a Dark `List.map` + `Dict.get` over thousands of rows is seconds; this
/// is milliseconds). Each event is (id, opBlobHex, branchId, commitHash, originTs); each commit is
/// (hash, message, branchId, accountId, createdAt).
let eventsSince
  (cursor : int64)
  (limit : int64)
  : Task<
      List<string * string * string * string * string> *
      List<string * string * string * string * string> *
      int64> =
  task {
    let! opRows =
      Sql.query
        $"SELECT rowid AS rid, id, hex(op_blob) AS blob, branch_id, commit_hash, origin_ts
          FROM package_ops
          WHERE rowid > {cursor} AND commit_hash IS NOT NULL
          ORDER BY rowid
          LIMIT {limit}"
      |> Sql.executeAsync (fun read ->
        (read.int64 "rid",
         read.string "id",
         read.string "blob",
         read.string "branch_id",
         read.string "commit_hash",
         read.string "origin_ts"))

    let events =
      opRows |> List.map (fun (_, id, blob, br, ch, ts) -> (id, blob, br, ch, ts))

    let newCursor =
      match opRows with
      | [] -> cursor
      | rows -> rows |> List.map (fun (rid, _, _, _, _, _) -> rid) |> List.max

    // Only the commits the batch's ops reference (rowid in (cursor, newCursor]) — a bounded event batch
    // carries a bounded set of commits, never the whole commit history.
    let! commits =
      Sql.query
        $"SELECT DISTINCT c.hash AS hash, c.message AS message, c.branch_id AS branch_id,
            c.account_id AS account_id, c.created_at AS created_at
          FROM commits c
          JOIN package_ops o ON o.commit_hash = c.hash
          WHERE o.rowid > {cursor} AND o.rowid <= {newCursor}"
      |> Sql.executeAsync (fun read ->
        (read.string "hash",
         read.string "message",
         read.string "branch_id",
         read.string "account_id",
         read.string "created_at"))

    return (commits, events, newCursor)
  }

/// The branch ops after <param cursor> from this instance's `branch_ops` log, as (id, opBlob-as-hex) pairs,
/// plus the new cursor (max rowid, or <param cursor> if nothing new). Branch ops carry their own structure
/// (branchId, commit, merge, …) inside the blob, so — unlike package events — they need no side metadata.
/// Ordered by rowid so a receiver applies them in the same order (CreateBranch before dependent ops).
let branchOpsSince (cursor : int64) (limit : int64) : Task<List<string * string> * int64> =
  task {
    let! rows =
      Sql.query
        $"SELECT rowid AS rid, id, hex(op_blob) AS blob
          FROM branch_ops
          WHERE rowid > {cursor}
          ORDER BY rowid
          LIMIT {limit}"
      |> Sql.executeAsync (fun read ->
        (read.int64 "rid", read.string "id", read.string "blob"))

    let events = rows |> List.map (fun (_, id, blob) -> (id, blob))

    let newCursor =
      match rows with
      | [] -> cursor
      | _ -> rows |> List.map (fun (rid, _, _) -> rid) |> List.max

    return (events, newCursor)
  }

/// Apply branch ops RECEIVED from a peer: deserialize each blob → BranchOp → insertAndApply (idempotent,
/// content-addressed by hash). Applied in order, so CreateBranch lands before the commits/merges that depend
/// on it. Returns the count processed (branch ops are low-volume; the puller advances a per-peer cursor, so a
/// re-pull doesn't re-count in practice).
let receiveBranchOps (events : List<string * byte[]>) : Task<int64> =
  task {
    let mutable applied = 0L

    for (id, opBlob) in events do
      let op = BS.PT.BranchOp.deserialize id opBlob
      do! BranchOpPlayback.insertAndApply op
      applied <- applied + 1L

    return applied
  }

/// Append events RECEIVED from a peer (over HTTP) into the local op log, then fold them into the
/// projections — the general event-log append (`Builtin.appendEvents`). Unlike `insertAndApplyOps`
/// (the LOCAL-authoring path, which stamps a fresh `nextOriginTs`), this PRESERVES each op's original
/// `origin_ts` — essential for the timestamp-LWW to converge the same on every instance regardless of
/// arrival order. Idempotent: `INSERT OR IGNORE` on the content-addressed id, and only unapplied ops
/// fold. Returns the number of ops NEWLY applied (INSERT OR IGNORE skips already-present ones), so a puller
/// reports the real change count. Folding stays here (F#) — invisible to Dark.
let receiveOps
  (commits : List<string * string * System.Guid * System.Guid * string>)
  (events : List<System.Guid * byte[] * System.Guid * string * string>)
  : Task<int64> =
  task {
    if List.isEmpty events then
      return 0L
    else
      // Insert the referenced commits FIRST (same transaction, in order) so the ops' commit_hash FK is
      // satisfied — a synced op belongs to a commit that must exist on the receiver. INSERT OR IGNORE dedups.
      let commitInserts =
        commits
        |> List.map (fun (hash, message, branchId, accountId, createdAt) ->
          let sql =
            """
            INSERT OR IGNORE INTO commits (hash, message, branch_id, account_id, created_at)
            VALUES (@hash, @message, @branch_id, @account_id, @created_at)
            """
          let ps =
            [ "hash", Sql.string hash
              "message", Sql.string message
              "branch_id", Sql.uuid branchId
              "account_id", Sql.uuid accountId
              "created_at", Sql.string createdAt ]
          (sql, [ ps ]))

      let opInserts =
        events
        |> List.map (fun (opId, opBlob, branchId, commitHash, originTs) ->
          // Convergence fix (canonical origin_ts): the op id is content-only, so two instances that
          // independently author the SAME op stamp it with different local `origin_ts`. If we kept
          // first-writer's stamp (INSERT OR IGNORE), a later competing edit could resolve differently on each
          // instance → permanent divergence. Instead reconcile to the MIN stamp (deterministic on every
          // instance), and if that LOWERS an already-applied op's stamp, mark it unapplied so the fold re-runs
          // and the binding's `locations.origin_ts` is refreshed to the reconciled value.
          let sql =
            """
            INSERT INTO package_ops
              (id, op_blob, branch_id, applied, commit_hash, propagation_id, origin_ts)
            VALUES (@id, @op_blob, @branch_id, @applied, @commit_hash, @propagation_id, @origin_ts)
            ON CONFLICT(id, branch_id) DO UPDATE SET
              origin_ts = MIN(package_ops.origin_ts, excluded.origin_ts),
              applied =
                CASE WHEN excluded.origin_ts < package_ops.origin_ts THEN 0
                     ELSE package_ops.applied END
            """
          let ps =
            [ "id", Sql.uuid opId
              "op_blob", Sql.bytes opBlob
              "branch_id", Sql.uuid branchId
              "applied", Sql.bool false
              "commit_hash", Sql.string commitHash
              "propagation_id", Sql.dbnull
              "origin_ts", Sql.string originTs ]
          (sql, [ ps ]))

      let _ = (commitInserts @ opInserts) |> Sql.executeTransactionSync
      // Record any divergences BEFORE the fold: an incoming SetName that rebinds a name already bound
      // locally to a different hash is a sync conflict (auto-resolved by LWW). Recorded so it's reviewable.
      do! Conflicts.detectDivergences events
      // Honest change count = ops that actually FOLD — newly inserted, plus any the MIN-reconcile above
      // lowered (marked unapplied) so they re-fold. A pure re-pull folds nothing → 0. (Insert rows-affected
      // can't be used now that the op insert is an upsert: a DO UPDATE counts even a no-op re-pull.)
      let! foldedCount = applyUnappliedOps ()
      return foldedCount
  }


/// The regenerable projections — every table the op-fold writes. `deprecations` is one: it's folded
/// from `Deprecate`/`Undeprecate` ops (its `annotation_blob` reconstructs from the op), so it's
/// regenerable and `export` strips it like the others. NOT `package_blobs` (canonical content —
/// op-playback never writes it), nor the op log / branch / commit / account state.
let projectionRegistry : List<Projection> =
  [ { table = "package_functions"; dirtiedBy = Set.ofList [ "AddFn" ] }
    { table = "package_types"; dirtiedBy = Set.ofList [ "AddType" ] }
    { table = "package_values"; dirtiedBy = Set.ofList [ "AddValue" ] }
    { table = "locations"
      dirtiedBy = Set.ofList [ "SetName"; "RevertPropagation" ] }
    { table = "package_dependencies"
      dirtiedBy = Set.ofList [ "AddFn"; "AddType"; "AddValue" ] }
    { table = "deprecations"; dirtiedBy = Set.ofList [ "Deprecate"; "Undeprecate" ] } ]

/// The tables a full rebuild clears + refolds — derived from the registry (single source of truth).
let projectionTables : List<string> =
  projectionRegistry |> List.map (fun p -> p.table)

/// Which projection tables an incoming op kind invalidates (incremental-refold targets).
let projectionsDirtiedBy (opKind : string) : List<string> =
  projectionRegistry
  |> List.filter (fun p -> Set.contains opKind p.dirtiedBy)
  |> List.map (fun p -> p.table)

/// The projection tables a whole op BATCH dirties — the union over its op kinds. This is the
/// incremental-refold *decision*: a rebuild after appending a batch need only clear+refold
/// these tables, leaving every other projection (and its rows) untouched.
let projectionsDirtiedByBatch (opKinds : Set<string>) : Set<string> =
  opKinds |> Set.toList |> List.collect projectionsDirtiedBy |> Set.ofList

/// An op's kind name (matches the registry's keys).
let opKindName (op : PT.PackageOp) : string =
  match op with
  | PT.PackageOp.AddType _ -> "AddType"
  | PT.PackageOp.AddValue _ -> "AddValue"
  | PT.PackageOp.AddFn _ -> "AddFn"
  | PT.PackageOp.SetName _ -> "SetName"
  | PT.PackageOp.Deprecate _ -> "Deprecate"
  | PT.PackageOp.Undeprecate _ -> "Undeprecate"
  | PT.PackageOp.PropagateUpdate _ -> "PropagateUpdate"
  | PT.PackageOp.RevertPropagation _ -> "RevertPropagation"

/// Incremental refold (the selective counterpart to `rebuildProjections`): clear ONLY the
/// projections this op-kind batch dirties, then re-fold ONLY the ops of those kinds back into
/// them — leaving every other projection untouched. Returns the count re-folded.
///
/// Faithful for content-addressed `Add*` kinds (their fold is batch-independent). Note: a
/// `SetName`-only refold would mis-detect renames (rename detection needs the batch's added
/// hashes), so include the accompanying `Add*` kinds when refolding `SetName`.
let rebuildDirtied (opKinds : Set<string>) : Task<int64> =
  task {
    let dirtied = projectionsDirtiedByBatch opKinds
    for t in Set.toList dirtied do
      do! Sql.query $"DELETE FROM {t}" |> Sql.executeStatementAsync

    let! ops =
      Sql.query
        "SELECT id, op_blob, branch_id, commit_hash FROM package_ops ORDER BY created_at ASC, rowid ASC"
      |> Sql.executeAsync (fun read ->
        let opId = read.uuid "id"
        let op = BS.PT.PackageOp.deserialize opId (read.bytes "op_blob")
        let branchId : PT.BranchId = read.uuid "branch_id"
        let commitHash = read.stringOrNone "commit_hash"
        (op, branchId, commitHash))

    let relevant =
      ops |> List.filter (fun (op, _, _) -> Set.contains (opKindName op) opKinds)
    let groups = relevant |> List.groupBy (fun (_, b, c) -> (b, c)) |> Map.toList
    for ((branchId, commitHash), g) in groups do
      do!
        PackageOpPlayback.applyOps
          branchId
          commitHash
          (g |> List.map (fun (op, _, _) -> op))
    return int64 (List.length relevant)
  }

/// ops⊥projections prototype: drop every projection table and re-fold the entire
/// package_ops log to rebuild them. Proves projections are *regenerable from the ops*
/// (the ops⊥projections split) — losing a projection costs only the CPU to re-fold; the
/// op log (package_ops) is the canonical durable state and is never touched here.
/// Returns the count of ops re-applied.
let rebuildProjections () : Task<int64> =
  task {
    // 1. clear the regenerable projection tables — from the projection registry (single
    // source of truth), so the rebuild set can never drift from the fold/dirty descriptors.
    for t in projectionTables do
      do! Sql.query $"DELETE FROM {t}" |> Sql.executeStatementAsync
    // 2. mark all ops unapplied so the fold reprocesses the whole log
    do! Sql.query "UPDATE package_ops SET applied = 0" |> Sql.executeStatementAsync
    // 3. re-fold ops -> projections via the existing playback path
    return! applyUnappliedOps ()
  }


/// Projection-currency counters for `dark status`: `(opsCount, foldedThrough)` — total ops in the
/// canonical `package_ops` log vs how many are folded into the projections (the `applied` flag).
/// Equal => the projection cache is current; a gap => ops have been appended/pulled but not yet
/// folded (run `branch rebuild`, or restart to `growIfNeeded`).
let projectionStatus () : Task<int64 * int64> =
  task {
    let! total =
      Sql.query "SELECT COUNT(*) as cnt FROM package_ops"
      |> Sql.executeRowAsync (fun read -> read.int64 "cnt")
    let! folded =
      Sql.query "SELECT COUNT(*) as cnt FROM package_ops WHERE applied = 1"
      |> Sql.executeRowAsync (fun read -> read.int64 "cnt")
    return (total, folded)
  }


/// Evaluate all package values that have NULL rt_dval.
/// Multi-pass: values may depend on other values, so we retry until convergence.
let evaluateAllValues
  (builtins : RT.Builtins)
  (pm : RT.PackageManager)
  : Task<Result<unit, string list>> =
  task {
    let program : RT.Program = { dbs = Map.empty }

    let notify _ _ _ _ = uply { return () }
    let sendException _ _ _ _ = uply { return () }

    let exeState =
      Execution.createState
        builtins
        pm
        Execution.noTracing
        sendException
        notify
        PT.mainBranchId
        program

    let maxPasses = 10
    let mutable pass = 0
    let mutable keepGoing = true
    let mutable lastErrors : string list = []

    while keepGoing do
      pass <- pass + 1

      let! unevaluatedValues =
        Sql.query
          """
          SELECT pv.hash, pv.pt_def, l.owner, l.modules, l.name
          FROM package_values pv
          LEFT JOIN locations l ON l.item_hash = pv.hash AND l.unlisted_at IS NULL
          WHERE pv.rt_dval IS NULL
          """
        |> Sql.executeAsync (fun read ->
          let hash = Hash(read.string "hash")
          let ptDef = read.bytes "pt_def"
          let owner = read.stringOrNone "owner" |> Option.defaultValue "?"
          let modules = read.stringOrNone "modules" |> Option.defaultValue ""
          let name = read.stringOrNone "name" |> Option.defaultValue "?"
          let fullName =
            if modules = "" then $"{owner}.{name}" else $"{owner}.{modules}.{name}"
          (hash, ptDef, fullName))

      if List.isEmpty unevaluatedValues then
        keepGoing <- false
        lastErrors <- []
      else if pass > maxPasses then
        keepGoing <- false
        lastErrors <-
          [ $"Gave up after {maxPasses} passes with {List.length unevaluatedValues} values remaining" ]
      else
        let errors = ResizeArray<string>()
        let mutable successCount = 0

        for (valueHash, ptDefBytes, fullName) in unevaluatedValues do
          try
            let ptValue = BS.PT.PackageValue.deserialize valueHash ptDefBytes
            let instrs = PT2RT.Expr.toRT Map.empty 0 None ptValue.body
            let! result = Execution.executeExpr exeState instrs

            match result with
            | Error(rte, _callStack) ->
              let! errorResult = Execution.runtimeErrorToString exeState rte
              let errorMsg =
                match errorResult with
                | Ok(RT.DString s) -> s
                | Ok other -> $"{other}"
                | Error(rte2, _) -> $"(could not stringify error: {rte2})"
              errors.Add(
                $"Value {valueHash} ({fullName}): evaluation failed - {errorMsg}"
              )
            | Ok dval ->
              // Promote any ephemeral blobs inside the value to
              // persistent so we can serialize. Streams remain
              // non-persistable and trip the [isPersistable] guard
              // below with a clear error.
              let! dval = LibExecution.Blob.promote pm.persistBlob dval

              if not (LibExecution.Dval.isPersistable dval) then
                let reason =
                  LibExecution.Dval.nonPersistableReason dval
                  |> Option.defaultValue "value is not persistable"
                errors.Add(
                  $"Value {valueHash} ({fullName}): cannot store in val — {reason}"
                )
              else
                let rtHash = PT2RT.Hash.toRT valueHash
                let rtValue : RT.PackageValue.PackageValue =
                  { hash = rtHash; body = dval }
                let (Hash defHash) = valueHash
                let rtDvalBytes = BS.RT.PackageValue.serialize rtHash rtValue
                let valueType = RT.Dval.toValueType dval
                let valueTypeBytes = BS.RT.ValueType.serialize valueType

                do!
                  Sql.query
                    """
                    UPDATE package_values
                    SET rt_dval = @rt_dval, value_type = @value_type
                    WHERE hash = @hash
                    """
                  |> Sql.parameters
                    [ "hash", Sql.string defHash
                      "rt_dval", Sql.bytes rtDvalBytes
                      "value_type", Sql.bytes valueTypeBytes ]
                  |> Sql.executeStatementAsync

                successCount <- successCount + 1
          with ex ->
            errors.Add($"Value {valueHash} ({fullName}): exception - {ex.Message}")

        if successCount = 0 then
          keepGoing <- false
          lastErrors <- errors |> List.ofSeq

    if List.isEmpty lastErrors then return Ok() else return Error lastErrors
  }


/// The grow step for CLI/test startup.
/// Applies any unapplied ops, generates package ref hashes, then evaluates values.
/// On a warm DB this is a single fast SELECT COUNT and returns immediately.
///
/// builtins is a function (not a value) because it must be constructed AFTER
/// hashes are generated — builtin construction triggers PackageRefs hash lookups.
let growIfNeeded
  (getBuiltins : unit -> RT.Builtins)
  (pm : RT.PackageManager)
  (log : string -> unit)
  : Task<bool> =
  task {
    use _span = Telemetry.span "seed.growIfNeeded" []
    let! appliedCount =
      Telemetry.timeTask "seed.applyOps" [] (fun () -> applyUnappliedOps ())
    if appliedCount > 0L then
      log $"Growing package DB from ops ({appliedCount} ops to apply)..."
      Telemetry.event "seed.applyOps.count" [ ("count", string appliedCount) ]
      do!
        Telemetry.timeTask "seed.generateRefs" [] (fun () ->
          task {
            do! PackageRefsGenerator.generate ()
            LibExecution.PackageRefs.reloadHashes ()
          })
      let! _evalResult =
        Telemetry.timeTask "seed.evaluateValues" [] (fun () ->
          evaluateAllValues (getBuiltins ()) pm)
      do!
        Telemetry.timeTask "seed.walCheckpoint" [] (fun () ->
          Sql.query "PRAGMA wal_checkpoint(TRUNCATE);" |> Sql.executeStatementAsync)
      log "Package DB ready"
      return true
    else
      return false
  }
