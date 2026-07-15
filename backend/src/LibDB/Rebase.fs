module LibDB.Rebase

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open LibExecution.ProgramTypes

open Fumble
open LibDB.Sqlite

module PT = LibExecution.ProgramTypes


type RebaseConflict =
  { owner : string; modules : string; name : string; itemType : string }


/// Get location paths modified on a branch since a given commit
let private getLocationPathsModifiedSince
  (branchId : PT.BranchId)
  (sinceCommitHash : Option<Hash>)
  : Task<List<RebaseConflict>> =
  task {
    match sinceCommitHash with
    | None ->
      // No base commit - all committed locations on this branch are "modified"
      return!
        Sql.query
          """
          SELECT DISTINCT owner, modules, name, item_type
          FROM locations
          WHERE branch_id = @branch_id
            AND commit_hash IS NOT NULL
            AND unlisted_at IS NULL
          """
        |> Sql.parameters [ "branch_id", Sql.uuid branchId ]
        |> Sql.executeAsync (fun read ->
          { owner = read.string "owner"
            modules = read.string "modules"
            name = read.string "name"
            itemType = read.string "item_type" })
    | Some(Hash commitHashStr) ->
      // Locations committed after the base commit
      return!
        Sql.query
          """
          SELECT DISTINCT l.owner, l.modules, l.name, l.item_type
          FROM locations l
          JOIN commits c ON l.commit_hash = c.hash
          WHERE l.branch_id = @branch_id
            AND l.commit_hash IS NOT NULL
            AND l.unlisted_at IS NULL
            AND c.created_at > (SELECT created_at FROM commits WHERE hash = @since_commit_hash)
          """
        |> Sql.parameters
          [ "branch_id", Sql.uuid branchId
            "since_commit_hash", Sql.string commitHashStr ]
        |> Sql.executeAsync (fun read ->
          { owner = read.string "owner"
            modules = read.string "modules"
            name = read.string "name"
            itemType = read.string "item_type" })
  }


/// Check for rebase conflicts without performing rebase
let getConflicts (branchId : PT.BranchId) : Task<List<RebaseConflict>> =
  task {
    let! branchOpt = Branches.get branchId
    match branchOpt with
    | None -> return []
    | Some branch ->
      match branch.parentBranchId with
      | None -> return [] // main branch, nothing to rebase
      | Some parentId ->
        let branchLocations =
          getLocationPathsModifiedSince branchId branch.baseCommitHash
        let parentLocations =
          getLocationPathsModifiedSince parentId branch.baseCommitHash

        let! branchLocs = branchLocations
        let! parentLocs = parentLocations

        // Conflict = same (owner, modules, name, itemType) modified on both sides
        let branchSet =
          branchLocs
          |> List.map (fun l -> (l.owner, l.modules, l.name, l.itemType))
          |> Set.ofList

        let conflicts =
          parentLocs
          |> List.filter (fun l ->
            Set.contains (l.owner, l.modules, l.name, l.itemType) branchSet)

        return conflicts
  }


/// The parent branch's latest commit hash (None if the parent has no commits yet).
let private parentLatestCommit (parentId : PT.BranchId) : Task<Option<Hash>> =
  Sql.query
    """
    SELECT hash FROM commits
    WHERE branch_id = @parent_id
    ORDER BY created_at DESC
    LIMIT 1
    """
  |> Sql.parameters [ "parent_id", Sql.uuid parentId ]
  |> Sql.executeRowOptionAsync (fun read -> Hash(read.string "hash"))


/// Is the branch behind its parent — would a rebase actually move its base? Read-only.
///
/// Distinct from `getConflicts`, which is empty BOTH when the branch is up to date AND when it's
/// behind-but-clean (parent advanced, no overlapping edits). So `rebase --status` needs this to tell
/// "nothing to do" apart from "a clean rebase is waiting."
let needsRebase (branchId : PT.BranchId) : Task<bool> =
  task {
    let! branchOpt = Branches.get branchId
    match branchOpt with
    | None -> return false
    | Some branch ->
      match branch.parentBranchId with
      | None -> return false // main branch: nothing to rebase onto
      | Some parentId ->
        let! parentLatest = parentLatestCommit parentId
        return branch.baseCommitHash <> parentLatest
  }


/// Perform rebase: verify no conflicts, update base_commit_hash to parent's latest.
///
/// TODO (multi-tenant): same TOCTOU shape as Merge.merge — reads
/// parent's latest commit + conflict set, then applies, no version
/// check or transaction across the read+apply pair.
let rebase (branchId : PT.BranchId) : Task<Result<string, List<RebaseConflict>>> =
  task {
    let! branchOpt = Branches.get branchId
    match branchOpt with
    | None -> return Error []
    | Some branch ->
      match branch.parentBranchId with
      | None -> return Ok "Main branch, nothing to rebase"
      | Some parentId ->
        // Get parent's latest commit
        let! parentLatest = parentLatestCommit parentId

        if branch.baseCommitHash = parentLatest then
          return Ok "Already up to date"
        else
          // Check for conflicts
          let! conflicts = getConflicts branchId

          if not (List.isEmpty conflicts) then
            return Error conflicts
          else
            // Record and apply the rebase
            match parentLatest with
            | Some newBase ->
              do!
                BranchOpPlayback.insertAndApply (
                  PT.BranchOp.RebaseBranch(branchId, newBase)
                )
            | None -> ()

            return Ok "Rebased successfully"
  }
