module LibDB.BranchOpPlayback

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open LibExecution.ProgramTypes

open Fumble
open LibDB.Sqlite

module PT = LibExecution.ProgramTypes
module BS = LibSerialization.Binary.Serialization
open LibSerialization.Hashing


/// Apply a BranchOp to the branches/commits tables.
/// This is the single source of truth for what each op does —
/// mutation sites call insertAndApply, and replay calls applyOp directly.
let applyOp (op : PT.BranchOp) (originTs : string) : Task<unit> =
  task {
    let (Hash opHash) = Hashing.computeBranchOpHash op
    match op with
    | PT.BranchOp.CreateBranch(branchId, name, parentBranchId, baseCommitHash) ->
      let baseCommitHashParam =
        match baseCommitHash with
        | Some(Hash h) -> Sql.string h
        | None -> Sql.dbnull

      let parentIdParam =
        match parentBranchId with
        | Some pid -> Sql.uuid pid
        | None -> Sql.dbnull

      do!
        Sql.query
          """
          INSERT OR IGNORE INTO branches
            (id, name, parent_branch_id, base_commit_hash, base_ts, base_op, created_at)
          VALUES (@id, @name, @parent_id, @base_commit_hash, @base_ts, @base_op, datetime('now'))
          """
        |> Sql.parameters
          [ "id", Sql.uuid branchId
            "name", Sql.string name
            "parent_id", parentIdParam
            "base_commit_hash", baseCommitHashParam
            // stamp the initial base so a later RebaseBranch with an OLDER origin_ts loses the LWW
            "base_ts", Sql.string originTs
            "base_op", Sql.string opHash ]
        |> Sql.executeStatementAsync

    | PT.BranchOp.CreateCommit(commitHash, message, accountId, branchId, _opHashes) ->
      let (Hash commitHashStr) = commitHash
      do!
        Sql.query
          """
          INSERT OR IGNORE INTO commits (hash, message, branch_id, account_id, created_at)
          VALUES (@hash, @message, @branch_id, @account_id, datetime('now'))
          """
        |> Sql.parameters
          [ "hash", Sql.string commitHashStr
            "message", Sql.string message
            "branch_id", Sql.uuid branchId
            "account_id", Sql.uuid accountId ]
        |> Sql.executeStatementAsync

    | PT.BranchOp.RebaseBranch(branchId, newBaseCommitHash) ->
      let (Hash h) = newBaseCommitHash
      // LWW-gate: base_commit_hash is a COMPETING value, so a rebase that's older (by origin_ts) than the
      // branch's current base — an old rebase arriving late via sync — must lose. `Lww.isStale` is computed
      // identically on every instance (origin_ts, op-id tiebreak), so concurrent rebases of the same branch
      // converge to the same base regardless of arrival order. (merge/archive are monotonic → no gate.)
      let! cur =
        Sql.query "SELECT base_ts, base_op FROM branches WHERE id = @id"
        |> Sql.parameters [ "id", Sql.uuid branchId ]
        |> Sql.executeRowOptionAsync (fun read ->
          (read.string "base_ts", read.string "base_op"))
      let stale =
        match cur with
        | Some(curTs, curOp) -> Lww.isStale originTs opHash curTs curOp
        | None -> false // branch row absent (shouldn't happen); the UPDATE no-ops anyway
      if not stale then
        do!
          Sql.query
            """
            UPDATE branches
            SET base_commit_hash = @base_commit_hash, base_ts = @base_ts, base_op = @base_op
            WHERE id = @id
            """
          |> Sql.parameters
            [ "id", Sql.uuid branchId
              "base_commit_hash", Sql.string h
              "base_ts", Sql.string originTs
              "base_op", Sql.string opHash ]
          |> Sql.executeStatementAsync

    | PT.BranchOp.MergeBranch(branchId, intoBranchId) ->
      let mergeStatements =
        let parentParams =
          [ "parent_id", Sql.uuid intoBranchId; "branch_id", Sql.uuid branchId ]
        [ // Keyed by name, not (name, kind): one name holds one item, so a child binding supersedes the
          // parent's binding at that name whatever kind either holds.
          ("""
           UPDATE locations
           SET unlisted_at = datetime('now')
           WHERE branch_id = @parent_id
             AND unlisted_at IS NULL
             AND (owner, modules, name) IN (
               SELECT owner, modules, name
               FROM locations
               WHERE branch_id = @branch_id AND unlisted_at IS NULL
             )
           """,
           [ parentParams ])

          ("UPDATE commits SET branch_id = @parent_id WHERE branch_id = @branch_id",
           [ parentParams ])

          // Drop duplicate child ops before moving the remaining rows to the
          // parent; otherwise the (id, branch_id) PK rejects the UPDATE below.
          // This preserves program state because the duplicate op content is
          // identical.
          //
          // History caveat: deleting the child row can make getCommitOps miss
          // that child's commit attribution. The proper fix is to store
          // commit-to-op attribution separately from package_ops.
          ("""
           DELETE FROM package_ops
           WHERE branch_id = @branch_id
             AND id IN (
               SELECT id FROM package_ops WHERE branch_id = @parent_id
             )
           """,
           [ parentParams ])

          ("UPDATE package_ops SET branch_id = @parent_id WHERE branch_id = @branch_id",
           [ parentParams ])

          ("""
           UPDATE locations SET branch_id = @parent_id
           WHERE branch_id = @branch_id AND unlisted_at IS NULL
           """,
           [ parentParams ])

          ("UPDATE branches SET merged_at = datetime('now') WHERE id = @id",
           [ [ "id", Sql.uuid branchId ] ]) ]

      let _ = Sql.executeTransactionSync mergeStatements
      ()

    | PT.BranchOp.ArchiveBranch branchId ->
      do!
        Sql.query "UPDATE branches SET archived_at = datetime('now') WHERE id = @id"
        |> Sql.parameters [ "id", Sql.uuid branchId ]
        |> Sql.executeStatementAsync
  }


/// Insert a BranchOp with a SPECIFIC origin_ts (the authoring stamp) into branch_ops and apply it.
/// The RECEIVE path passes the peer's origin_ts (preserved, so the structural LWW converges the same on
/// every instance). INSERT OR IGNORE keeps it idempotent (content-addressed by hash).
let insertAndApplyWithTs (op : PT.BranchOp) (originTs : string) : Task<unit> =
  task {
    let opHash = Hashing.computeBranchOpHash op
    let (Hash hashStr) = opHash
    let opBlob = BS.PT.BranchOp.serialize hashStr op

    // Phase 1: Insert with applied=false, preserving origin_ts
    let! rowsAffected =
      Sql.query
        """
        INSERT OR IGNORE INTO branch_ops (id, op_blob, applied, origin_ts, created_at)
        VALUES (@id, @op_blob, 0, @origin_ts, datetime('now'))
        """
      |> Sql.parameters
        [ "id", Sql.string hashStr
          "op_blob", Sql.bytes opBlob
          "origin_ts", Sql.string originTs ]
      |> Sql.executeNonQueryAsync

    // Phase 2: Apply if newly inserted (skip if duplicate)
    if rowsAffected > 0 then
      do! applyOp op originTs

      // Phase 3: Mark as applied
      try
        do!
          Sql.query "UPDATE branch_ops SET applied = 1 WHERE id = @id"
          |> Sql.parameters [ "id", Sql.string hashStr ]
          |> Sql.executeStatementAsync
      with ex ->
        System.Console.Error.WriteLine(
          $"Warning: Failed to mark BranchOp {hashStr} as applied: {ex.Message}"
        )
  }

/// Insert a LOCALLY-authored BranchOp (self-stamps a fresh origin_ts) + apply it. Branch ops are rare —
/// a deliberate create/commit/merge/rebase — so a plain UTC-ms stamp is monotonic enough for the LWW, and
/// this keeps the many local-authoring call sites unchanged.
let insertAndApply (op : PT.BranchOp) : Task<unit> =
  let originTs =
    System.DateTime.UtcNow.ToString(
      "yyyy-MM-ddTHH:mm:ss.fffZ",
      System.Globalization.CultureInfo.InvariantCulture
    )
  insertAndApplyWithTs op originTs
