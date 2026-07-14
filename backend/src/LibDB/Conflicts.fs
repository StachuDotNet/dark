/// Conflicts — the LOCAL review log of divergences. A sync conflict (a name bound to two different contents
/// across instances) auto-resolves at PULL TIME by last-writer-wins; the fact that it happened is recorded
/// here so it can be reviewed and — via a synced `Resolution` — overridden. NOT synced: everyone's `main`
/// shares one id, so competing edits are same-branch; only the pull knows an incoming op superseded a
/// different LOCAL binding, so detection lives in the receive path (`detectDivergences`, called from
/// `Seed.receiveOps`). Stored flat in `sync_conflicts` (schema.sql): the location + both candidate hashes,
/// the chosen hash + the policy that chose it, and a status lifecycle (auto-resolved → acknowledged |
/// overridden).
module LibDB.Conflicts

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Fumble
open LibDB.Sqlite

module PT = LibExecution.ProgramTypes
module BS = LibSerialization.Binary.Serialization

/// One recorded conflict. `chosenHash` + `resolvedBy` are the auto-resolution (which content won, and the
/// policy that picked it); `status` is where it is in review.
type Conflict =
  { id : string
    branchId : System.Guid
    location : string
    itemKind : string
    localHash : string
    incomingHash : string
    chosenHash : string
    resolvedBy : string
    status : string }

/// Canonical "owner.modules.name" for a location (round-trips: owner = first, name = last, modules = middle).
/// Package name components never contain "." (dots separate modules), so this splits back cleanly.
let locationString (loc : PT.PackageLocation) : string =
  String.concat "." (loc.owner :: (loc.modules @ [ loc.name ]))

/// Inverse of `locationString`.
let parseLocation (s : string) : PT.PackageLocation =
  let parts = s.Split('.')

  match parts.Length with
  | 0 -> { owner = ""; modules = []; name = "" }
  | 1 -> { owner = parts[0]; modules = []; name = "" }
  | n ->
    { owner = parts[0]
      modules = parts[1 .. n - 2] |> Array.toList
      name = parts[n - 1] }

/// A content-addressed id for a divergence — same branch+location+candidates always maps to one row, so a
/// re-detected divergence (e.g. re-pull) doesn't duplicate.
let private conflictId
  (branchId : System.Guid)
  (location : string)
  (localHash : string)
  (incomingHash : string)
  : string =
  let raw = $"{branchId}|{location}|{localHash}|{incomingHash}"

  System.Security.Cryptography.SHA256.HashData(
    System.Text.Encoding.UTF8.GetBytes raw
  )
  |> System.Convert.ToHexString
  |> fun s -> s.ToLowerInvariant()

/// Record an auto-resolved divergence (idempotent — INSERT OR IGNORE on the content-addressed id).
let record
  (branchId : System.Guid)
  (location : string)
  (itemKind : string)
  (localHash : string)
  (incomingHash : string)
  (chosenHash : string)
  (resolvedBy : string)
  : Task<unit> =
  Sql.query
    """
    INSERT OR IGNORE INTO sync_conflicts
      (id, branch_id, location, item_kind, local_hash, incoming_hash, chosen_hash, resolved_by, status)
    VALUES
      (@id, @branch_id, @location, @item_kind, @local_hash, @incoming_hash, @chosen_hash, @resolved_by, 'auto-resolved')
    """
  |> Sql.parameters
    [ "id", Sql.string (conflictId branchId location localHash incomingHash)
      "branch_id", Sql.string (string branchId)
      "location", Sql.string location
      "item_kind", Sql.string itemKind
      "local_hash", Sql.string localHash
      "incoming_hash", Sql.string incomingHash
      "chosen_hash", Sql.string chosenHash
      "resolved_by", Sql.string resolvedBy ]
  |> Sql.executeStatementAsync

/// All recorded conflicts, newest first.
let list () : Task<List<Conflict>> =
  Sql.query
    """
    SELECT id, branch_id, location, item_kind, local_hash, incoming_hash, chosen_hash, resolved_by, status
    FROM sync_conflicts
    ORDER BY detected_at DESC
    """
  |> Sql.executeAsync (fun read ->
    { id = read.string "id"
      branchId = System.Guid.Parse(read.string "branch_id")
      location = read.string "location"
      itemKind = read.string "item_kind"
      localHash = read.string "local_hash"
      incomingHash = read.string "incoming_hash"
      chosenHash = read.string "chosen_hash"
      resolvedBy = read.string "resolved_by"
      status = read.string "status" })

/// Acknowledge a conflict — the auto choice stands, just mark it reviewed.
let acknowledge (id : string) : Task<unit> =
  Sql.query
    "UPDATE sync_conflicts SET status = 'acknowledged' WHERE id = @id AND status = 'auto-resolved'"
  |> Sql.parameters [ "id", Sql.string id ]
  |> Sql.executeStatementAsync

/// Mark a conflict overridden — a human picked a candidate (recorded as a synced Resolution).
let markOverridden (id : string) : Task<unit> =
  Sql.query "UPDATE sync_conflicts SET status = 'overridden' WHERE id = @id"
  |> Sql.parameters [ "id", Sql.string id ]
  |> Sql.executeStatementAsync

/// Mark any auto-resolved conflict at this location overridden. Used when a Resolution is applied (local OR
/// synced-in from a peer): the override converges the effective value, so the divergence is settled — a peer
/// that independently recorded the same conflict must stop listing it as unreviewed.
let markOverriddenByLocation
  (branchId : System.Guid)
  (location : string)
  : Task<unit> =
  Sql.query
    "UPDATE sync_conflicts SET status = 'overridden' WHERE branch_id = @branch_id AND location = @location AND status = 'auto-resolved'"
  |> Sql.parameters
    [ "branch_id", Sql.string (string branchId); "location", Sql.string location ]
  |> Sql.executeStatementAsync

/// The current live binding (item_hash, origin_ts) for a location on a branch, if any.
let private currentBinding
  (branchId : System.Guid)
  (loc : PT.PackageLocation)
  (itemKind : PT.ItemKind)
  : Task<Option<string * Option<string>>> =
  Sql.query
    """
    SELECT item_hash, origin_ts FROM locations
    WHERE owner = @owner AND modules = @modules AND name = @name AND item_type = @item_type
      AND branch_id = @branch_id AND unlisted_at IS NULL
    LIMIT 1
    """
  |> Sql.parameters
    [ "owner", Sql.string loc.owner
      "modules", Sql.string (String.concat "." loc.modules)
      "name", Sql.string loc.name
      "item_type", Sql.string (itemKind.toString ())
      "branch_id", Sql.string (string branchId) ]
  |> Sql.executeRowOptionAsync (fun read ->
    (read.string "item_hash", read.stringOrNone "origin_ts"))

/// Detect + record divergences for a batch of RECEIVED ops. Called by `Seed.receiveOps` AFTER the ops are
/// inserted but BEFORE the fold: for each incoming `SetName`, if the name is already bound LOCALLY to a
/// different hash, that's a divergence. The winner is decided by the same timestamp-LWW the fold uses (the
/// incoming op's origin_ts vs the current binding's; exact tie → higher hash), so `chosenHash` matches what
/// the fold will pick — recorded here so LWW is never silent.
let detectDivergences
  (events : List<System.Guid * byte[] * System.Guid * string * string>)
  : Task<unit> =
  task {
    for (opId, opBlob, branchId, _commitHash, originTs) in events do
      let op = BS.PT.PackageOp.deserialize (string opId) opBlob

      // Only an op that is ABOUT TO FOLD (applied = 0 after the receive-insert) can introduce a new
      // divergence. A re-received op that already folded (applied = 1) must be skipped — otherwise re-pulling
      // a long-superseded SetName records a phantom conflict (curBinding is the newer winner, so it looks
      // divergent) that surfaces in `dark conflicts` though nothing actually changed.
      let! applied =
        Sql.query
          "SELECT applied FROM package_ops WHERE id = @id AND branch_id = @branchID"
        |> Sql.parameters [ "id", Sql.uuid opId; "branchID", Sql.uuid branchId ]
        |> Sql.executeRowOptionAsync (fun read -> read.int64 "applied")

      match op, applied with
      | PT.PackageOp.SetName(loc, target), Some 0L ->
        let (PT.Hash incomingHash) = target.hash
        let! cur = currentBinding branchId loc target.kind

        match cur with
        | Some(curHash, curTs) when curHash <> incomingHash ->
          // Same LWW rule as applySetName + the resolution overlay — one shared predicate.
          let incomingStale =
            match curTs with
            | Some ct -> Lww.isStale originTs incomingHash ct curHash
            | None -> false

          let chosenHash = if incomingStale then curHash else incomingHash

          do!
            record
              branchId
              (locationString loc)
              (target.kind.toString ())
              curHash
              incomingHash
              chosenHash
              "auto:last-writer-wins"
        | _ -> ()
      | _ -> ()
  }
