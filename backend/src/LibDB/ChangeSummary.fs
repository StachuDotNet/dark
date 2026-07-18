/// What a batch of incoming ops actually MEANS to a human, so sync can say "1 new value" instead of
/// "3 changes". A raw op count is a lie by omission: authoring one value emits an `AddValue` (content) AND a
/// `SetName` (the binding), so a single edit reads as 2-3 "changes". Only the name-binding ops are
/// user-visible; the Add* ops are the content those bindings point at.
///
/// Classified against the CURRENT bindings, so this must run BEFORE the fold. That also makes it
/// self-filtering for a re-pull: an op whose target hash already equals the live binding is not a change.
module LibDB.ChangeSummary

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Fumble
open LibDB.Sqlite

module PT = LibExecution.ProgramTypes
module BS = LibSerialization.Binary.Serialization


/// One human-meaningful change. `action` is "new" | "updated" | "deleted"; `itemKind` is "fn" | "type" |
/// "value"; `location` is the dotted name.
type Change = { action : string; itemKind : string; location : string }


/// The live binding's hash at a location on a branch, if any. Keyed by name alone — one name holds one
/// item, so "what is bound here" has a single answer regardless of kind.
let private boundHash
  (branchId : System.Guid)
  (loc : PT.PackageLocation)
  : Task<Option<string>> =
  Sql.query
    """
    SELECT item_hash FROM locations
    WHERE owner = @owner AND modules = @modules AND name = @name
      AND branch_id = @branch_id AND unlisted_at IS NULL
    LIMIT 1
    """
  |> Sql.parameters
    [ "owner", Sql.string loc.owner
      "modules", Sql.string (String.concat "." loc.modules)
      "name", Sql.string loc.name
      "branch_id", Sql.string (string branchId) ]
  |> Sql.executeRowOptionAsync (fun read -> read.string "item_hash")


/// The location a hash is currently bound to, if any — a `Deprecate` names a hash, not a name, so this is how
/// "deleted X" gets a name to show.
let private locationOfHash
  (branchId : System.Guid)
  (itemHash : string)
  : Task<Option<string * string>> =
  Sql.query
    """
    SELECT owner, modules, name, item_type FROM locations
    WHERE item_hash = @item_hash AND branch_id = @branch_id AND unlisted_at IS NULL
    LIMIT 1
    """
  |> Sql.parameters
    [ "item_hash", Sql.string itemHash; "branch_id", Sql.string (string branchId) ]
  |> Sql.executeRowOptionAsync (fun read ->
    let owner = read.string "owner"
    let modules = read.string "modules"
    let name = read.string "name"
    let dotted =
      if modules = "" then $"{owner}.{name}" else $"{owner}.{modules}.{name}"
    (dotted, read.string "item_type"))


/// Classify a batch of incoming ops into the changes a human would recognize. Ops that change nothing (a
/// re-pull binding the same hash) are omitted, so the result is empty exactly when the pull was a no-op.
let ofIncoming
  (events : List<System.Guid * byte[] * System.Guid * string * string>)
  : Task<List<Change>> =
  task {
    let mutable changes = []

    for (opId, opBlob, branchId, _commitHash, _originTs) in events do
      let op =
        try
          Some(BS.PT.PackageOp.deserialize (string opId) opBlob)
        with _ ->
          // A peer's unparseable op is already surfaced + skipped by the receive path; don't double-report.
          None

      match op with
      | Some(PT.PackageOp.SetName(loc, target)) ->
        let (PT.Hash incomingHash) = target.hash
        let itemKind = target.kind.toString ()
        let! current = boundHash branchId loc

        match current with
        | Some h when h = incomingHash -> () // already bound to this — not a change
        | Some _ ->
          changes <-
            { action = "updated"
              itemKind = itemKind
              location = Conflicts.locationString loc }
            :: changes
        | None ->
          changes <-
            { action = "new"
              itemKind = itemKind
              location = Conflicts.locationString loc }
            :: changes

      | Some(PT.PackageOp.Deprecate(target, _kind, _msg)) ->
        let (PT.Hash targetHash) = target.hash
        let! loc = locationOfHash branchId targetHash

        match loc with
        | Some(dotted, itemKind) ->
          changes <-
            { action = "deleted"; itemKind = itemKind; location = dotted } :: changes
        | None -> () // deprecating something we can't name locally — nothing useful to report

      // Add* carry content for a SetName that's in the same batch; Propagate/Revert/Undeprecate aren't
      // separately meaningful to a human reading "what did I just pull".
      | _ -> ()

    return List.rev changes
  }
