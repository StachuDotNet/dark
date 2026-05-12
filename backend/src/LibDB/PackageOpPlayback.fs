/// Applies PackageOps to the DB projection tables.
/// These tables (package_types, package_values, package_functions, locations) are projections
/// of the source-of-truth package_ops table.
///
/// All writes go through a caller-provided SqliteConnection so the entire op
/// batch can run inside one outer transaction. On cold-start grow (9000+ ops)
/// that turns ~20k tiny WAL commits into one, which is the difference between
/// a 5-second startup and a sub-second one. Inserts.fs (the other caller) opens
/// a one-shot connection per call.
module LibDB.PackageOpPlayback


open System.Threading.Tasks
open FSharp.Control.Tasks
open Microsoft.Data.Sqlite

open Prelude
open LibExecution.ProgramTypes

module PT = LibExecution.ProgramTypes
module RT = LibExecution.RuntimeTypes
module PT2RT = LibExecution.ProgramTypesToRuntimeTypes
module BS = LibSerialization.Binary.Serialization
module DE = LibDB.DependencyExtractor
open LibSerialization.Hashing


// ------------------------------------------------------------------
// Low-level helpers — raw Microsoft.Data.Sqlite on a shared connection.
// ------------------------------------------------------------------

/// Run a non-query SQL statement on the shared connection.
/// `setParams` populates the SqliteCommand's parameters (named with `$name`).
let private exec
  (conn : SqliteConnection)
  (sql : string)
  (setParams : SqliteCommand -> unit)
  : Task<unit> =
  task {
    use cmd = conn.CreateCommand()
    cmd.CommandText <- sql
    setParams cmd
    let! _ = cmd.ExecuteNonQueryAsync()
    return ()
  }

/// Helper for `cmd.Parameters.AddWithValue` that always returns unit.
let inline private p (cmd : SqliteCommand) (name : string) (value : obj) =
  cmd.Parameters.AddWithValue(name, value) |> ignore<SqliteParameter>

/// Bind a `string option` as either the string or DBNull.
let inline private pOpt
  (cmd : SqliteCommand)
  (name : string)
  (value : string option)
  =
  match value with
  | Some s -> p cmd name (box s)
  | None -> p cmd name (box System.DBNull.Value)


// ------------------------------------------------------------------
// Dependency table maintenance.
// ------------------------------------------------------------------

/// Update dependencies for an item atomically.
/// Clears existing dependencies and stores new ones in a single statement
/// (a multi-statement script — SQLite runs them in order on the same command).
let private updateDependencies
  (conn : SqliteConnection)
  (itemHash : string)
  (deps : List<DE.Dependency>)
  : Task<unit> =
  task {
    if List.isEmpty deps then
      do!
        exec
          conn
          "DELETE FROM package_dependencies WHERE item_hash = $item_hash"
          (fun cmd -> p cmd "$item_hash" itemHash)
    else
      // Each dep contributes 6 placeholders to the VALUES list.
      let placeholders =
        deps
        |> List.mapi (fun i _ ->
          $"($item_hash, $hash_{i}, $kind_{i}, $owner_{i}, $modules_{i}, $name_{i})")
        |> String.concat ", "

      let sql =
        "DELETE FROM package_dependencies WHERE item_hash = $item_hash; "
        + "INSERT OR IGNORE INTO package_dependencies "
        + "(item_hash, depends_on_hash, depends_on_item_type, depends_on_owner, depends_on_modules, depends_on_name) "
        + "VALUES "
        + placeholders

      do!
        exec conn sql (fun cmd ->
          p cmd "$item_hash" itemHash
          deps
          |> List.iteri (fun i dep ->
            let (Hash hashStr) = dep.hash
            p cmd $"$hash_{i}" hashStr
            p cmd $"$kind_{i}" (dep.itemKind.toString ())
            match dep.location with
            | Some loc ->
              p cmd $"$owner_{i}" loc.owner
              p cmd $"$modules_{i}" (String.concat "." loc.modules)
              p cmd $"$name_{i}" loc.name
            | None ->
              p cmd $"$owner_{i}" System.DBNull.Value
              p cmd $"$modules_{i}" System.DBNull.Value
              p cmd $"$name_{i}" System.DBNull.Value))
  }


// ------------------------------------------------------------------
// Individual op handlers.
// ------------------------------------------------------------------

/// Apply a single AddType op to the package_types table.
let private applyAddType
  (conn : SqliteConnection)
  (typ : PT.PackageType.PackageType)
  : Task<unit> =
  task {
    // Use the hash already set on the item (computed by LoadPackagesFromDisk
    // or Propagation with SCC awareness). Only recompute if hash is empty.
    let hash =
      match typ.hash with
      | Hash "" -> Hashing.computeTypeHash Hashing.Normal typ
      | h -> h
    let typ = { typ with hash = hash }
    let (Hash hashStr) = hash

    let ptDef = BS.PT.PackageType.serialize hashStr typ
    let rtDef = typ |> PT2RT.PackageType.toRT |> BS.RT.PackageType.serialize hashStr

    do!
      exec
        conn
        """
        INSERT OR REPLACE INTO package_types (hash, pt_def, rt_def)
        VALUES ($hash, $pt_def, $rt_def)
        """
        (fun cmd ->
          p cmd "$hash" hashStr
          p cmd "$pt_def" ptDef
          p cmd "$rt_def" rtDef)

    // Extract and store dependency references atomically. Each
    // Dependency carries its own location (populated by the resolver).
    let refs = DE.extractFromType typ
    do! updateDependencies conn hashStr refs
  }

/// Apply a single AddValue op to the package_values table.
/// Note: rt_dval and value_type are stored as NULL here. They are populated
/// in Phase 3 by Seed.evaluateAllValues after all ops are applied, so cross-
/// package references resolve correctly.
let private applyAddValue
  (conn : SqliteConnection)
  (value : PT.PackageValue.PackageValue)
  : Task<unit> =
  task {
    let hash =
      match value.hash with
      | Hash "" -> Hashing.computeValueHash Hashing.Normal value
      | h -> h
    let value = { value with hash = hash }
    let (Hash hashStr) = hash

    let ptDef = BS.PT.PackageValue.serialize hashStr value

    // ON CONFLICT(hash) since values are content-addressed; we may re-encounter
    // the same hash via re-applied or duplicated ops.
    do!
      exec
        conn
        """
        INSERT INTO package_values (hash, pt_def, rt_dval, value_type)
        VALUES ($hash, $pt_def, NULL, NULL)
        ON CONFLICT(hash) DO UPDATE SET
          pt_def = excluded.pt_def
        """
        (fun cmd ->
          p cmd "$hash" hashStr
          p cmd "$pt_def" ptDef)

    let refs = DE.extractFromValue value
    do! updateDependencies conn hashStr refs
  }

/// Apply a single AddFn op to the package_functions table.
let private applyAddFn
  (conn : SqliteConnection)
  (fn : PT.PackageFn.PackageFn)
  : Task<unit> =
  task {
    let hash =
      match fn.hash with
      | Hash "" -> Hashing.computeFnHash Hashing.Normal fn
      | h -> h
    let fn = { fn with hash = hash }
    let (Hash hashStr) = hash

    let ptDef = BS.PT.PackageFn.serialize hashStr fn
    let rtInstrs = fn |> PT2RT.PackageFn.toRT |> BS.RT.PackageFn.serialize hashStr

    do!
      exec
        conn
        """
        INSERT OR REPLACE INTO package_functions (hash, pt_def, rt_instrs)
        VALUES ($hash, $pt_def, $rt_instrs)
        """
        (fun cmd ->
          p cmd "$hash" hashStr
          p cmd "$pt_def" ptDef
          p cmd "$rt_instrs" rtInstrs)

    let refs = DE.extractFromFn fn
    do! updateDependencies conn hashStr refs
  }

/// Apply a Set*Name op to the locations table.
/// branchId = branch context, commitHash = None means WIP, Some id means committed.
/// isRename = true when this SetName is a standalone rename (not paired with Add*),
///   meaning old locations for the same hash should be deprecated.
let private applySetName
  (conn : SqliteConnection)
  (branchId : PT.BranchId)
  (commitHash : Option<string>)
  (isRename : bool)
  (itemHash : Hash)
  (location : PT.PackageLocation)
  (itemKind : PT.ItemKind)
  : Task<unit> =
  task {
    let modulesStr = String.concat "." location.modules
    let itemTypeStr = itemKind.toString ()
    let locationId = System.Guid.NewGuid()
    let (Hash itemHashStr) = itemHash

    // 1. Deprecate any existing location at the target path (handles updates)
    do!
      exec
        conn
        """
        UPDATE locations
        SET unlisted_at = datetime('now')
        WHERE owner = $owner
          AND modules = $modules
          AND name = $name
          AND item_type = $item_type
          AND unlisted_at IS NULL
          AND branch_id = $branch_id
        """
        (fun cmd ->
          p cmd "$owner" location.owner
          p cmd "$modules" modulesStr
          p cmd "$name" location.name
          p cmd "$item_type" itemTypeStr
          p cmd "$branch_id" branchId)

    // 2. If this is a rename (standalone SetName, not paired with Add*),
    //    also deprecate old locations pointing to the same hash.
    //    We do NOT do this for Add+SetName pairs because multiple items can
    //    legitimately share the same hash (e.g. Int8.ParseError and
    //    Int16.ParseError have identical definitions).
    if isRename then
      do!
        exec
          conn
          """
          UPDATE locations
          SET unlisted_at = datetime('now')
          WHERE item_hash = $item_hash
            AND branch_id = $branch_id
            AND unlisted_at IS NULL
          """
          (fun cmd ->
            p cmd "$item_hash" itemHashStr
            p cmd "$branch_id" branchId)

    // 3. Insert new location entry.
    do!
      exec
        conn
        """
        INSERT INTO locations (location_id, item_hash, owner, modules, name, item_type, branch_id, commit_hash)
        VALUES ($location_id, $item_hash, $owner, $modules, $name, $item_type, $branch_id, $commit_hash)
        """
        (fun cmd ->
          p cmd "$location_id" locationId
          p cmd "$item_hash" itemHashStr
          p cmd "$owner" location.owner
          p cmd "$modules" modulesStr
          p cmd "$name" location.name
          p cmd "$item_type" itemTypeStr
          p cmd "$branch_id" branchId
          pOpt cmd "$commit_hash" commitHash)
  }


/// Serialize a DeprecationKind + message for the annotation_blob column.
/// Keeps the on-disk representation close to the binary op serializer so one
/// reader can surface both op-log history and current projected state.
let private serializeAnnotation
  (kind : PT.DeprecationKind)
  (message : string)
  : byte array =
  use ms = new System.IO.MemoryStream()
  use w = new System.IO.BinaryWriter(ms)
  LibSerialization.Binary.Serializers.PT.PackageOp.DeprecationKind.write w kind
  LibSerialization.Binary.Serializers.Common.String.write w message
  ms.ToArray()


/// Apply a Deprecate op to the deprecations projection table.
/// Supersedes any prior un-superseded row for (branch, item_hash, item_kind).
///
/// CLEANUP: deprecation identity is hash-keyed (`Reference` carries only a
/// Hash). When two unrelated FQNs share a content hash, deprecating one
/// deprecates both. We need to extend `Reference` (and the `deprecations`
/// table) to carry location, then teach the query to filter by
/// `(owner, modules, name)` like dependent lookups do.
let private applyDeprecate
  (conn : SqliteConnection)
  (branchId : PT.BranchId)
  (commitHash : Option<string>)
  (target : PT.Reference)
  (kind : PT.DeprecationKind)
  (message : string)
  : Task<unit> =
  task {
    let (Hash itemHashStr) = target.hash
    let itemKindStr = target.kind.toString ()
    let deprecationId = System.Guid.NewGuid()
    let blob = serializeAnnotation kind message

    do!
      exec
        conn
        """
        UPDATE deprecations
        SET unlisted_at = datetime('now')
        WHERE branch_id = $branch_id
          AND item_hash = $item_hash
          AND item_kind = $item_kind
          AND unlisted_at IS NULL
        """
        (fun cmd ->
          p cmd "$branch_id" branchId
          p cmd "$item_hash" itemHashStr
          p cmd "$item_kind" itemKindStr)

    do!
      exec
        conn
        """
        INSERT INTO deprecations
          (deprecation_id, branch_id, commit_hash, item_hash, item_kind, state, annotation_blob)
        VALUES
          ($deprecation_id, $branch_id, $commit_hash, $item_hash, $item_kind, 'deprecated', $blob)
        """
        (fun cmd ->
          p cmd "$deprecation_id" deprecationId
          p cmd "$branch_id" branchId
          pOpt cmd "$commit_hash" commitHash
          p cmd "$item_hash" itemHashStr
          p cmd "$item_kind" itemKindStr
          p cmd "$blob" blob)
  }


/// Apply an Undeprecate op to the deprecations projection table.
/// Records an `undeprecated`-state row that supersedes any prior row for the
/// same (branch, item_hash, item_kind). This is how child branches override
/// ancestor-branch deprecations.
let private applyUndeprecate
  (conn : SqliteConnection)
  (branchId : PT.BranchId)
  (commitHash : Option<string>)
  (target : PT.Reference)
  : Task<unit> =
  task {
    let (Hash itemHashStr) = target.hash
    let itemKindStr = target.kind.toString ()
    let deprecationId = System.Guid.NewGuid()

    do!
      exec
        conn
        """
        UPDATE deprecations
        SET unlisted_at = datetime('now')
        WHERE branch_id = $branch_id
          AND item_hash = $item_hash
          AND item_kind = $item_kind
          AND unlisted_at IS NULL
        """
        (fun cmd ->
          p cmd "$branch_id" branchId
          p cmd "$item_hash" itemHashStr
          p cmd "$item_kind" itemKindStr)

    do!
      exec
        conn
        """
        INSERT INTO deprecations
          (deprecation_id, branch_id, commit_hash, item_hash, item_kind, state, annotation_blob)
        VALUES
          ($deprecation_id, $branch_id, $commit_hash, $item_hash, $item_kind, 'undeprecated', NULL)
        """
        (fun cmd ->
          p cmd "$deprecation_id" deprecationId
          p cmd "$branch_id" branchId
          pOpt cmd "$commit_hash" commitHash
          p cmd "$item_hash" itemHashStr
          p cmd "$item_kind" itemKindStr)
  }


/// Apply a RevertPropagation op — undoes the source's WIP location and the
/// dependents' repointed locations, restoring the previous state.
let private applyRevertPropagation
  (conn : SqliteConnection)
  (branchId : PT.BranchId)
  (sourceLocation : PT.PackageLocation)
  (restoredSourceRef : PT.Reference)
  (revertedRepoints : List<PT.PropagateRepoint>)
  : Task<unit> =
  task {
    let sourceItemKind = restoredSourceRef.kind

    // For each reverted repoint: unlist toRef, un-unlist fromRef.
    // Skip repoints for the source item — those are handled by the
    // dedicated source-handling block below (avoids redundant
    // double-toggle in mutual recursion).
    let dependentRepoints =
      revertedRepoints
      |> List.filter (fun r ->
        r.location <> sourceLocation || r.toRef.kind <> sourceItemKind)

    for repoint in dependentRepoints do
      let (Hash toHashStr) = repoint.toRef.hash
      let (Hash fromHashStr) = repoint.fromRef.hash

      do!
        exec
          conn
          """
          UPDATE locations
          SET unlisted_at = datetime('now')
          WHERE item_hash = $item_hash
            AND branch_id = $branch_id
            AND unlisted_at IS NULL
          """
          (fun cmd ->
            p cmd "$item_hash" toHashStr
            p cmd "$branch_id" branchId)

      do!
        exec
          conn
          """
          UPDATE locations
          SET unlisted_at = NULL
          WHERE location_id = (
            SELECT location_id FROM locations
            WHERE item_hash = $item_hash
              AND branch_id = $branch_id
              AND unlisted_at IS NOT NULL
            ORDER BY unlisted_at DESC
            LIMIT 1
          )
          """
          (fun cmd ->
            p cmd "$item_hash" fromHashStr
            p cmd "$branch_id" branchId)

    // Undo source: unlist WIP location, un-unlist committed location.
    let modulesStr = String.concat "." sourceLocation.modules
    let itemTypeStr = sourceItemKind.toString ()
    let (Hash restoredSourceHashStr) = restoredSourceRef.hash

    do!
      exec
        conn
        """
        UPDATE locations
        SET unlisted_at = datetime('now')
        WHERE owner = $owner
          AND modules = $modules
          AND name = $name
          AND item_type = $item_type
          AND branch_id = $branch_id
          AND unlisted_at IS NULL
          AND commit_hash IS NULL
        """
        (fun cmd ->
          p cmd "$owner" sourceLocation.owner
          p cmd "$modules" modulesStr
          p cmd "$name" sourceLocation.name
          p cmd "$item_type" itemTypeStr
          p cmd "$branch_id" branchId)

    do!
      exec
        conn
        """
        UPDATE locations
        SET unlisted_at = NULL
        WHERE location_id = (
          SELECT location_id FROM locations
          WHERE item_hash = $item_hash
            AND branch_id = $branch_id
            AND unlisted_at IS NOT NULL
          ORDER BY unlisted_at DESC
          LIMIT 1
        )
        """
        (fun cmd ->
          p cmd "$item_hash" restoredSourceHashStr
          p cmd "$branch_id" branchId)
  }


// ------------------------------------------------------------------
// Op dispatch.
// ------------------------------------------------------------------

/// Apply a single PackageOp to the projection tables.
/// addedHashes = hashes of items added by Add* ops earlier in this batch
///   (used to distinguish "add + name" from "rename").
let private applyOp
  (conn : SqliteConnection)
  (branchId : PT.BranchId)
  (commitHash : Option<string>)
  (addedHashes : Set<Hash>)
  (op : PT.PackageOp)
  : Task<unit> =
  task {
    match op with
    | PT.PackageOp.AddType typ -> do! applyAddType conn typ
    | PT.PackageOp.AddValue value -> do! applyAddValue conn value
    | PT.PackageOp.AddFn fn -> do! applyAddFn conn fn
    | PT.PackageOp.SetName(loc, target) ->
      let isRename = not (Set.contains target.hash addedHashes)
      do! applySetName conn branchId commitHash isRename target.hash loc target.kind
    | PT.PackageOp.Deprecate(target, kind, message) ->
      do! applyDeprecate conn branchId commitHash target kind message
    | PT.PackageOp.Undeprecate target ->
      do! applyUndeprecate conn branchId commitHash target
    | PT.PackageOp.PropagateUpdate _ ->
      // Location changes are already handled by the individual SetName ops that
      // accompany this op in the propagation batch. Applying them here too would
      // create duplicate location entries.
      ()
    | PT.PackageOp.RevertPropagation(_,
                                     _,
                                     sourceLocation,
                                     restoredSourceRef,
                                     revertedRepoints) ->
      do!
        applyRevertPropagation
          conn
          branchId
          sourceLocation
          restoredSourceRef
          revertedRepoints
  }


/// Collect hashes from Add* ops to distinguish "add + name" from "rename".
/// When SetName references a hash that was added in the same batch, it's
/// giving a name to a new item. Otherwise it's a rename (move).
let private collectAddedHashes (ops : List<PT.PackageOp>) : Set<Hash> =
  ops
  |> List.choose (fun op ->
    match op with
    | PT.PackageOp.AddType t -> Some t.hash
    | PT.PackageOp.AddValue v -> Some v.hash
    | PT.PackageOp.AddFn f -> Some f.hash
    | _ -> None)
  |> Set.ofList


// ------------------------------------------------------------------
// Public API.
// ------------------------------------------------------------------

/// Apply a list of PackageOps using a caller-provided open SqliteConnection.
/// The caller controls transaction boundaries — wrap the call in BEGIN/COMMIT
/// for a bulk-replay or use auto-commit for a small commit-time batch.
///
/// Dep-edge location columns are populated directly from each `Dependency`'s
/// `location` field (the resolver stashes it onto `NameResolution<_>` at
/// resolve time). No post-hoc backfill needed.
let applyOpsOnConnection
  (conn : SqliteConnection)
  (branchId : PT.BranchId)
  (commitHash : Option<string>)
  (ops : List<PT.PackageOp>)
  : Task<unit> =
  task {
    let addedHashes = collectAddedHashes ops
    for op in ops do
      do! applyOp conn branchId commitHash addedHashes op
  }


/// Convenience wrapper for callers that don't have a shared connection (e.g.
/// Inserts.fs at commit time, where the op batch is small). Opens a fresh
/// connection per call and wraps the whole batch in a single transaction —
/// faster than auto-commit and makes the apply atomic with respect to other
/// readers.
let applyOps
  (branchId : PT.BranchId)
  (commitHash : Option<string>)
  (ops : List<PT.PackageOp>)
  : Task<unit> =
  task {
    use conn = new SqliteConnection(LibDB.Sqlite.connString)
    do! conn.OpenAsync()
    use tx = conn.BeginTransaction()
    do! applyOpsOnConnection conn branchId commitHash ops
    tx.Commit()
  }
