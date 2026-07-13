/// Resolutions — SYNCED decisions that OVERRIDE the op-fold for a contested name. A divergence auto-resolves
/// by policy (last-writer-wins); a human — or a keep-local policy — can decide differently. That decision is
/// NOT a new op: the op log is authored content; a resolution is a thin overlay that picks among EXISTING
/// candidates. Effective binding = fold(package_ops)[LWW] → then apply resolutions per location
/// [last-resolver-wins by `at`]. A resolution carries its own fresh `at` stamp, so it competes in the SAME
/// timestamp-LWW that orders bindings — which lets a "keep mine" decision propagate where re-emitting the
/// original SetName (same content hash → same op id → no new rowid) could not. Table: `resolutions`
/// (schema.sql); `id` is a uuid carried over the wire so peers apply idempotently (INSERT OR IGNORE). This
/// syncs as the third named EventLog (`resolutions`).
module LibDB.Resolutions

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Fumble
open LibDB.Sqlite

module PT = LibExecution.ProgramTypes

// CLEANUP(resolution-shape): this may be over-specified. (branchId, location, choice) is the flattened
// (branch, PackageLocation, Reference) binding-slot identity `applySetName` uses — correct + tested +
// converges. Revisit once more of the sync UX is exercised: does a resolution want a leaner shape, or a
// genuinely generic "override projection cell X → Y" one that isn't hardwired to package bindings?
/// One resolution: bind `location` to `choice` (a Reference — hash + kind together, NOT two loose strings),
/// decided by `resolvedBy` ("human" or a policy name) at `at`. `id` is the wire-carried idempotency key.
/// `branchId` is load-bearing, not premature generality: bindings are per-branch (a name can even hold a fn
/// AND a type live at once), so `applySetName` keys a binding by (owner, modules, name, item_type, branch_id)
/// — a resolution has to name the same slot, and a synced one must tell a peer WHICH branch to override.
type Resolution =
  { id : string
    branchId : System.Guid
    location : PT.PackageLocation
    choice : PT.Reference
    resolvedBy : string
    at : string }

/// Mint a resolution for a "keep this candidate" decision. `at` is the resolver time (the same stamp format
/// op origin_ts / locations use), which becomes the LWW stamp the binding competes on.
let mk
  (location : PT.PackageLocation)
  (choice : PT.Reference)
  (resolvedBy : string)
  (branchId : System.Guid)
  (at : string)
  : Resolution =
  { id = System.Guid.NewGuid() |> string
    branchId = branchId
    location = location
    choice = choice
    resolvedBy = resolvedBy
    at = at }

/// Persist a resolution (idempotent on `id` — a re-pulled resolution from a peer doesn't duplicate). The
/// Reference is flattened to the (chosen_hash, item_kind) columns the wire + `applySetName` speak in.
let record (r : Resolution) : Task<unit> =
  let (PT.Hash h) = r.choice.hash

  Sql.query
    """
    INSERT OR IGNORE INTO resolutions (id, branch_id, location, item_kind, chosen_hash, resolved_by, at)
    VALUES (@id, @b, @location, @item_kind, @hash, @by, @at)
    """
  |> Sql.parameters
    [ "id", Sql.string r.id
      "b", Sql.string (string r.branchId)
      "location", Sql.string (Conflicts.locationString r.location)
      "item_kind", Sql.string (r.choice.kind.toString ())
      "hash", Sql.string h
      "by", Sql.string r.resolvedBy
      "at", Sql.string r.at ]
  |> Sql.executeStatementAsync

/// Apply a resolution to the `locations` projection — the OVERLAY step. Re-binds the location to the choice
/// content, gated by the SAME timestamp-LWW `applySetName` uses: a resolution whose `at` is older than the
/// live binding's `origin_ts` is stale and skipped (exact tie breaks by the higher content hash, portably).
/// So every instance converges on the same winner regardless of arrival order.
let applyToLocations (r : Resolution) : Task<unit> =
  task {
    let loc = r.location
    let modulesStr = String.concat "." loc.modules
    let itemTypeStr = r.choice.kind.toString ()
    let (PT.Hash chosenHash) = r.choice.hash

    let! cur =
      Sql.query
        """
        SELECT item_hash, origin_ts FROM locations
        WHERE owner = @o AND modules = @m AND name = @n AND item_type = @t
          AND branch_id = @b AND unlisted_at IS NULL
        LIMIT 1
        """
      |> Sql.parameters
        [ "o", Sql.string loc.owner
          "m", Sql.string modulesStr
          "n", Sql.string loc.name
          "t", Sql.string itemTypeStr
          "b", Sql.string (string r.branchId) ]
      |> Sql.executeRowOptionAsync (fun read ->
        (read.string "item_hash", read.stringOrNone "origin_ts"))

    let skip =
      match cur with
      // already bound to the choice content — idempotent no-op (so a re-pulled resolution doesn't churn)
      | Some(curHash, _) when curHash = chosenHash -> true
      // stale: older-by-stamp than the live binding (the one shared LWW rule)
      | Some(curHash, Some curTs) when curHash <> chosenHash ->
        Lww.isStale r.at chosenHash curTs curHash
      | _ -> false

    if skip then
      return ()
    else
      do!
        Sql.query
          """
          UPDATE locations SET unlisted_at = datetime('now')
          WHERE owner = @o AND modules = @m AND name = @n AND item_type = @t
            AND branch_id = @b AND unlisted_at IS NULL
          """
        |> Sql.parameters
          [ "o", Sql.string loc.owner
            "m", Sql.string modulesStr
            "n", Sql.string loc.name
            "t", Sql.string itemTypeStr
            "b", Sql.string (string r.branchId) ]
        |> Sql.executeStatementAsync

      do!
        Sql.query
          """
          INSERT INTO locations
            (location_id, item_hash, owner, modules, name, item_type, branch_id, commit_hash, origin_ts)
          VALUES (@lid, @hash, @o, @m, @n, @t, @b, NULL, @at)
          """
        |> Sql.parameters
          [ "lid", Sql.string (System.Guid.NewGuid() |> string)
            "hash", Sql.string chosenHash
            "o", Sql.string loc.owner
            "m", Sql.string modulesStr
            "n", Sql.string loc.name
            "t", Sql.string itemTypeStr
            "b", Sql.string (string r.branchId)
            "at", Sql.string r.at ]
        |> Sql.executeStatementAsync
  }

/// Record + immediately apply (the local-authoring path: a human / keep-local decision takes effect now).
let recordAndApply (r : Resolution) : Task<unit> =
  task {
    do! record r
    do! applyToLocations r
    // The override settled this location — clear any conflict recorded here (on THIS instance, and on a peer
    // that received the resolution), so it stops showing as an unreviewed conflict after it's been resolved.
    do! Conflicts.markOverriddenByLocation r.branchId (Conflicts.locationString r.location)
  }

/// Replay EVERY stored resolution (ordered by `at`) onto the `locations` projection — the overlay half of
/// "effective binding = fold(package_ops) → then apply resolutions". A projection rebuild re-folds only the op
/// log, so it must call this afterward or a human override is silently lost (the binding reverts to the LWW
/// loser). Idempotent + LWW-gated per resolution, so replaying an already-applied overlay is a no-op.
let reapplyAll () : Task<unit> =
  task {
    let! rows =
      Sql.query
        "SELECT id, branch_id, location, item_kind, chosen_hash, resolved_by, at FROM resolutions ORDER BY at ASC"
      |> Sql.executeAsync (fun read ->
        { id = read.string "id"
          branchId = System.Guid.Parse(read.string "branch_id")
          location = Conflicts.parseLocation (read.string "location")
          choice =
            PT.Reference.fromHashAndKind (
              PT.Hash(read.string "chosen_hash"),
              PT.ItemKind.fromString (read.string "item_kind")
            )
          resolvedBy = read.string "resolved_by"
          at = read.string "at" })
    for r in rows do
      do! applyToLocations r
      // Keep the conflict log consistent with the overlay: a location that carries a resolution is settled, so
      // its recorded divergence is 'overridden', not a still-open 'auto-resolved'. (recordAndApply does this on
      // the live path; a re-fold that goes through reapplyAll must do it too, or the resolved conflict pops back
      // up as unreviewed in `dark conflicts` after a grow.)
      do! Conflicts.markOverriddenByLocation r.branchId (Conflicts.locationString r.location)
  }


// ── the resolutions log as a synced EventLog (the third named log) ──

/// Resolutions authored after <param cursor> (rowid), as their field tuple + the new cursor. The sync read
/// for the `resolutions` EventLog — a peer serves these so an override converges everywhere.
let resolutionsSince
  (cursor : int64)
  (limit : int64)
  : Task<List<string * string * string * string * string * string * string> * int64> =
  task {
    let! rows =
      Sql.query
        $"SELECT rowid AS rid, id, branch_id, location, item_kind, chosen_hash, resolved_by, at
          FROM resolutions
          WHERE rowid > {cursor}
          ORDER BY rowid
          LIMIT {limit}"
      |> Sql.executeAsync (fun read ->
        (read.int64 "rid",
         read.string "id",
         read.string "branch_id",
         read.string "location",
         read.string "item_kind",
         read.string "chosen_hash",
         read.string "resolved_by",
         read.string "at"))

    let events =
      rows |> List.map (fun (_, id, b, l, k, h, by, at) -> (id, b, l, k, h, by, at))

    let newCursor =
      match rows with
      | [] -> cursor
      | _ -> rows |> List.map (fun (rid, _, _, _, _, _, _, _) -> rid) |> List.max

    return (events, newCursor)
  }

/// Apply resolutions RECEIVED from a peer: record (idempotent by id) + apply to locations (the LWW-gated
/// overlay). Order-independent — each is gated by its own `at` stamp. Returns the count processed.
let receiveResolutions
  (events : List<string * string * string * string * string * string * string>)
  : Task<int64> =
  task {
    let mutable n = 0L

    for (id, branchId, location, itemKind, chosenHash, resolvedBy, at) in events do
      // Count only NEWLY-recorded resolutions (recordAndApply is idempotent on the id), so the puller's
      // "Pulled N" is honest on a re-pull — matching the package-op and branch-op counts.
      let existed =
        Sql.query "SELECT 1 FROM resolutions WHERE id = @id"
        |> Sql.parameters [ "id", Sql.string id ]
        |> Sql.executeExistsSync
      let r : Resolution =
        { id = id
          branchId = System.Guid.Parse branchId
          location = Conflicts.parseLocation location
          choice =
            PT.Reference.fromHashAndKind (
              PT.Hash chosenHash,
              PT.ItemKind.fromString itemKind
            )
          resolvedBy = resolvedBy
          at = at }

      do! recordAndApply r
      if not existed then n <- n + 1L

    return n
  }
