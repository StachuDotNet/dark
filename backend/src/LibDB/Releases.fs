/// The Release migrator — moves a store forward one Release at a time, and REFUSES to open a store from a
/// NEWER Release with older code.
///
/// A **Release** is the single version coordinate spanning {language/`ProgramTypes`, op-serialization
/// format, SQL schema, content-hashing} — the same integer (`currentRelease`) that gates
/// cross-instance sync also gates whether this binary may open this store. One coordinate, one upgrade.
///
/// A `Release N` step bundles:
///   1. a forward, **copy-and-swap** canonical `.sql` — never `DROP`; the op log is preserved,
///   2. an optional **op-format remap** — re-serialize the whole log once, in one transaction,
///   3. a **projection refold** — mark ops unapplied; startup regenerates the projections.
/// Projections are dropped+refolded, never migrated. Forward-only; the undo is "restore from a peer"
/// (every peer holds the whole log, so the tailnet is the backup).
///
/// The current baseline is **Release 3** — a fresh store is born here; see `releases` for why 3 is a
/// clean break and how the first real format change appends the next entry.
module LibDB.Releases

open Prelude

open Fumble
open LibDB.Sqlite


/// THE version coordinate (see the module doc). A store is stamped with this Release; older code refuses
/// to open a store stamped NEWER, and cross-instance sync uses this same integer as its wire-format version.
let currentRelease : int = 4


/// One forward step that ARRIVES at Release `n` (apply it to move a store from `n-1` to `n`).
type Release =
  {
    /// the Release this step lands on
    n : int
    /// canonical-table forward migration — copy-and-swap, NEVER drop. "" = no canonical-shape change.
    sql : string
    /// op-format remap (old `op_blob` bytes → new), only when the serialization format changed.
    /// `None` = the op format is unchanged at this step (the common case).
    reserialize : (byte[] -> byte[]) option
    /// CLEAN-BREAK boundary: when `true`, pre-this-Release data is disposable — the package dataset is
    /// CLEARED and rebuilds from source (dev) or re-pulls from a same-Release peer. Use only when the
    /// change can't be cheaply migrated (e.g. a content-hash redefinition). The default (`false`) is the
    /// durable path: the canonical copy-swap `.sql` + op-format reserialize + re-fold, keeping the log.
    clearForRebuild : bool
  }


/// The ordered registry of forward steps. Add an entry when you bump `currentRelease`; the
/// migrator does the rest. Each `n` must be exactly one greater than the previous (see
/// `registryIsWellFormed`).
///
/// **Release 3 — meaning-stable hashing.** Hashes are now over the alpha-normalized canonical form, so older
/// `op_blob`s embed stale hashes and can't be cheaply migrated — hence the CLEAN-BREAK marker. A fresh store
/// is born at the current Release and never replays this step; it's the worked example of the clean-break path.
///
/// **Release 4 — sync commit-order stamp.** Adds `package_ops.committed_seq` (the coordinate sync's
/// `eventsSince` pages by) and backfills existing committed ops by rowid — a DURABLE step (the op log is
/// preserved). Fresh stores get the column from schema.sql; this step is only for a real Release-3 → 4 store.
let releases : Release list =
  [ { n = 3; sql = ""; reserialize = None; clearForRebuild = true }
    { n = 4
      sql =
        "ALTER TABLE package_ops ADD COLUMN committed_seq INTEGER;"
        + " UPDATE package_ops SET committed_seq = rowid"
        + " WHERE commit_hash IS NOT NULL AND committed_seq IS NULL"
      reserialize = None
      clearForRebuild = false } ]


// ── The pure planning half (unit-tested; takes the registry explicitly so tests inject their own) ──


/// The steps to move a store from `storeN` up to `codeN`: the entries with `storeN < n <= codeN`, in
/// ascending order. Pure.
let pendingReleases
  (registry : Release list)
  (storeN : int)
  (codeN : int)
  : Release list =
  registry
  |> List.filter (fun r -> r.n > storeN && r.n <= codeN)
  |> List.sortBy (fun r -> r.n)


/// Is the registry well-formed: strictly ascending, **contiguous** (no gaps), no duplicates, and none
/// above `codeRelease`? A gap would silently skip a migration; a dup would double-apply; an entry above
/// the code's Release is unreachable. Pure guard — unit-tested and asserted at boot.
let registryIsWellFormed (registry : Release list) (codeRelease : int) : bool =
  let ns = registry |> List.map (fun r -> r.n)
  let contiguous = ns |> List.pairwise |> List.forall (fun (a, b) -> b = a + 1)
  let distinct = (List.distinct ns) = ns
  let noneAboveCode = ns |> List.forall (fun n -> n <= codeRelease)
  contiguous && distinct && noneAboveCode


// ── The store's Release stamp (a tiny local table, separate from the schema-hash stamp) ──


let private releaseTable = "release_state_v0"


/// The Release this store was last stamped at, or `None` if it predates Release tracking (or is fresh).
let storedRelease () : int option =
  let exists =
    Sql.query "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @name"
    |> Sql.parameters [ "name", Sql.string releaseTable ]
    |> Sql.executeExistsSync
  if not exists then
    None
  else
    match
      // `release` is a SQLite keyword (SAVEPOINT RELEASE) — quote the column so it can never mis-parse.
      Sql.query $"SELECT \"release\" FROM {releaseTable} WHERE id = 0"
      |> Sql.execute (fun read -> read.int64 "release")
    with
    | Ok [ r ] -> Some(int r)
    | _ -> None


/// Stamp the store at Release `n`.
let writeRelease (n : int) : unit =
  Sql.query
    $"CREATE TABLE IF NOT EXISTS {releaseTable} (id INTEGER PRIMARY KEY, \"release\" INTEGER NOT NULL)"
  |> Sql.executeStatementSync
  Sql.query
    $"INSERT OR REPLACE INTO {releaseTable} (id, \"release\") VALUES (0, @release)"
  |> Sql.parameters [ "release", Sql.int64 (int64 n) ]
  |> Sql.executeStatementSync


// ── Applying a step ──


/// Re-serialize the WHOLE op log once through `remap` (old `op_blob` → new), in a single transaction.
/// The op id is content-addressed over the op's MEANING (a normalized canonical form), not its raw
/// bytes — so a pure *format* change keeps the same id, and we update `op_blob` in place. (A remap that
/// changes an op's meaning/hash is a different, louder operation — the hash-remap path — not this.)
let reserializeLog (remap : byte[] -> byte[]) : unit =
  let rows =
    Sql.query "SELECT id, branch_id, op_blob FROM package_ops"
    |> Sql.execute (fun read ->
      (read.string "id", read.string "branch_id", read.bytes "op_blob"))
    |> Result.unwrap
  let updates =
    rows
    |> List.map (fun (id, branchId, blob) ->
      ("UPDATE package_ops SET op_blob = @blob WHERE id = @id AND branch_id = @branch_id",
       [ [ "blob", Sql.bytes (remap blob)
           "id", Sql.string id
           "branch_id", Sql.string branchId ] ]))
  Sql.executeTransactionSync updates |> ignore<List<int>>


/// The package dataset cleared by a `clearForRebuild` boundary: the PT op log + blobs, the branch
/// structure, the regenerable projections, and the RT-derived caches (traces). Reload-from-source (dev)
/// repopulates it. We KEEP accounts and user data — only the package world is reset. (`rt_dval` lives in
/// `package_values`, so it's cleared with the projections — RT recomputed from PT.)
let private rebuildClearTables : List<string> =
  [ "package_ops"; "package_blobs"; "branches"; "commits"; "branch_ops" ]
  @ LibDB.Seed.projectionTables
  @ [ "traces"; "trace_fn_calls" ]


/// Clear the package dataset for a clean-break Release (FK off; the rows go, the tables stay so the
/// next reload/sync refills them).
let clearForRebuildData () : unit =
  Sql.query "PRAGMA foreign_keys = OFF" |> Sql.executeStatementSync
  for t in rebuildClearTables do
    Sql.query (sprintf "DELETE FROM \"%s\"" t) |> Sql.executeStatementSync


/// Apply one Release step. A CLEAN-BREAK (`clearForRebuild`) clears the package dataset so it rebuilds
/// from source/peer (disposable pre-Release data). Otherwise it's the durable path: the canonical
/// copy-swap `.sql` (if any), then the op-format remap (if any, which marks the log unapplied so startup
/// refolds projections from the new bytes).
let applyRelease (r : Release) : unit =
  if r.clearForRebuild then
    print
      $"Release {r.n}: clean-break boundary — clearing the package dataset; it rebuilds from source / re-pulls from a same-Release peer."
    clearForRebuildData ()
  else
    if r.sql <> "" then Sql.query r.sql |> Sql.executeStatementSync
    match r.reserialize with
    | Some remap ->
      reserializeLog remap
      Sql.query "UPDATE package_ops SET applied = 0" |> Sql.executeStatementSync
    | None -> ()


// ── The boot guard + forward migrator ──


/// What `applyPending` decides to do, factored out of the DB-mutating path so the guard is a **pure,
/// unit-testable** function of (storedRelease, codeRelease).
type ReleaseAction =
  /// no stored Release → stamp `code` (a fresh store, or a pre-tracking store already at the current format)
  | StampFresh
  /// store == code → nothing to do (the steady state)
  | UpToDate
  /// store > code → REFUSE: a newer store; older code would misread the op format
  | RefuseNewer of storeN : int
  /// store < code → apply these steps in order, then stamp `code`
  | Migrate of Release list


/// Pure: reconcile the store's Release with this binary's. See `ReleaseAction`. Total over the four cases;
/// no DB access — `applyPending` reads/writes the store around it.
let planRelease
  (registry : Release list)
  (stored : int option)
  (codeRelease : int)
  : ReleaseAction =
  match stored with
  | None -> StampFresh
  | Some s when s = codeRelease -> UpToDate
  | Some s when s > codeRelease -> RefuseNewer s
  | Some s -> Migrate(pendingReleases registry s codeRelease)


/// What the CLI should do with an EXISTING store, given the same (registry, stored, code) inputs as
/// `planRelease`. The CLI has no package source to rebuild from, so its clean-break path RE-SEEDS from the
/// embedded current-Release store (discarding + backing up the old data); a DURABLE migration instead runs
/// the steps in place and PRESERVES the store.
[<RequireQualifiedAccess>]
type CliUpgrade =
  | Proceed // already at the code Release
  | RefuseNewer of storeN : int // a newer store — older code must not open it
  | MigrateInPlace // every pending step is durable — migrate forward, keeping the data
  | Reseed // a pending clean-break step, or a pre-tracking store of unknown format — discard + re-seed


/// Pure: the CLI's upgrade decision. Reuses `planRelease`, then splits a `Migrate` on whether ANY pending
/// step is a clean-break (`clearForRebuild`): a clean break invalidates the on-disk content (e.g. a hashing
/// change), so it can't migrate in place — the CLI re-seeds; an all-durable run migrates forward in place.
/// `StampFresh` here means a pre-tracking store (no Release stamp) — the CLI can't trust its on-disk format,
/// so it re-seeds rather than assume it's already current.
let planCliUpgrade
  (registry : Release list)
  (stored : int option)
  (codeRelease : int)
  : CliUpgrade =
  match planRelease registry stored codeRelease with
  | UpToDate -> CliUpgrade.Proceed
  | RefuseNewer s -> CliUpgrade.RefuseNewer s
  | StampFresh -> CliUpgrade.Reseed
  | Migrate steps ->
    if steps |> List.exists (fun r -> r.clearForRebuild) then
      CliUpgrade.Reseed
    else
      CliUpgrade.MigrateInPlace


/// Reconcile the store's Release with this binary's (`codeRelease = currentRelease`) and execute the
/// decision. The *decision* is `planRelease` (pure, tested); this wraps it with the DB read/write.
let applyPending (codeRelease : int) : unit =
  if not (registryIsWellFormed releases codeRelease) then
    Exception.raiseInternal
      "Release registry is not well-formed (a gap, duplicate, or an entry above the code Release)"
      [ "codeRelease", codeRelease ]
  match planRelease releases (storedRelease ()) codeRelease with
  | UpToDate -> ()
  | StampFresh -> writeRelease codeRelease
  | RefuseNewer s ->
    Exception.raiseInternal
      $"This store is on Release {s}; this Dark speaks Release {codeRelease}. Upgrade Dark to open it — never open a newer store with older code."
      []
  | Migrate steps ->
    for r in steps do
      let extra = if r.reserialize.IsSome then " + op re-serialize" else ""
      print $"Applying Release {r.n} (schema{extra})…"
      applyRelease r
    writeRelease codeRelease
