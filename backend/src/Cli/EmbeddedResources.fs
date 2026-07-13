module Cli.EmbeddedResources

open System
open System.IO
open System.Reflection

// Resolve the running executable's directory.
// Assembly.Location returns "" for assemblies embedded in a single-file or AOT
// bundle (and emits IL3000). AppContext.BaseDirectory is the AOT-clean replacement
// for "where is the published binary"; ProcessPath stays as a final fallback.
let private exeDirectory () : string =
  let baseDir = AppContext.BaseDirectory
  if not (String.IsNullOrEmpty(baseDir)) then
    baseDir.TrimEnd('/', '\\')
  else
    let path = System.Environment.ProcessPath
    if String.IsNullOrEmpty(path) then
      Environment.CurrentDirectory
    else
      Path.GetDirectoryName(path)

/// Determines if CLI is running in "installed" mode (in ~/.darklang/bin/) vs portable mode
let private isInstalledMode () : bool =
  let dir = exeDirectory ()
  dir.EndsWith("/.darklang/bin") || dir.EndsWith("\\.darklang\\bin")

/// Gets the appropriate .darklang directory path
let private getDarklangDirectory () : string =
  if isInstalledMode () then
    // Installed mode: use the central ~/.darklang directory
    let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
    Path.Combine(home, ".darklang")
  else
    // Portable mode: use adjacent .darklang directory
    Path.Combine(exeDirectory (), ".darklang")

let private extractResource (resourceName : string) (targetPath : string) : unit =
  let assembly = Assembly.GetExecutingAssembly()

  let targetDir = Path.GetDirectoryName(targetPath)
  if not (Directory.Exists(targetDir)) then
    Directory.CreateDirectory(targetDir) |> ignore

  use stream = assembly.GetManifestResourceStream(resourceName)

  if stream = null then
    // Resource not found - acceptable in debug builds
    ()
  else
    use fileStream = File.Create(targetPath)
    stream.CopyTo(fileStream)

/// Extract a resource that was gzip-compressed at build time.
/// SQLite databases compress ~3-4× with gzip; we ship `data.db.gz`
/// embedded and decompress on first extract. Saves ~7 MB on the binary.
let private extractGzippedResource
  (resourceName : string)
  (targetPath : string)
  : unit =
  let assembly = Assembly.GetExecutingAssembly()

  let targetDir = Path.GetDirectoryName(targetPath)
  if not (Directory.Exists(targetDir)) then
    Directory.CreateDirectory(targetDir) |> ignore

  use stream = assembly.GetManifestResourceStream(resourceName)
  if stream = null then
    ()
  else
    use gzip =
      new System.IO.Compression.GZipStream(
        stream,
        System.IO.Compression.CompressionMode.Decompress
      )
    use fileStream = File.Create(targetPath)
    gzip.CopyTo(fileStream)

let private hasEmbeddedResource (resourceName : string) : bool =
  let assembly = Assembly.GetExecutingAssembly()
  assembly.GetManifestResourceNames() |> Array.contains resourceName


// ── Release reconciliation on an EXISTING store ──────────────────────────────────────────────────────
// The CLI seeds a fresh store from the embedded db, but a store left over from a PREVIOUS CLI release is
// not otherwise migrated: the schema/op-format/hashing can differ, and the shipped CLI doesn't run the
// LocalExec migrator. So on startup we reconcile the store's Release stamp with this binary's
// `LibDB.Releases.currentRelease` before any LibDB connection is opened.

/// The store's Release, as read at startup.
type private StoreRelease =
  | Stamped of int // release_state_v0 has a row — a definitive answer
  | PreTracking // opened fine, but no release_state_v0 — predates Release tracking
  | Unreadable // couldn't open/query the db at all — do NOT destroy it (may be locked, not stale)

/// Read the store's Release stamp via a throwaway, NON-POOLED connection, so LibDB's own (pooled) shared
/// connection is never opened here — a reseed moves the db file aside, and a pooled handle would keep
/// pointing at the moved inode. Distinguishes "opened fine but stale" (safe to reseed) from "couldn't read"
/// (a transient lock / permission issue — must NOT trigger a data-destroying reseed).
let private readStoredRelease (dbPath : string) : StoreRelease =
  try
    use conn =
      new Microsoft.Data.Sqlite.SqliteConnection(
        $"Data Source={dbPath};Mode=ReadOnly;Pooling=false"
      )
    conn.Open()
    use tableCmd = conn.CreateCommand()
    tableCmd.CommandText <-
      "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'release_state_v0'"
    match tableCmd.ExecuteScalar() with
    | null -> PreTracking
    | _ ->
      use cmd = conn.CreateCommand()
      cmd.CommandText <- "SELECT \"release\" FROM release_state_v0 WHERE id = 0"
      match cmd.ExecuteScalar() with
      | null -> PreTracking
      | v -> Stamped(Convert.ToInt32 v)
  with _ ->
    Unreadable

/// Move the stale store (and its WAL/SHM sidecars, so a leftover WAL can't attach to the new db) aside to
/// a timestamped backup, then re-extract the embedded current-Release seed. Returns the backup path.
let private reseedStore (dbPath : string) (storedLabel : string) : string =
  let stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss")
  let backup = $"{dbPath}.{storedLabel}-{stamp}.bak"
  File.Move(dbPath, backup)
  for suffix in [ "-wal"; "-shm" ] do
    let side = dbPath + suffix
    if File.Exists side then File.Move(side, backup + suffix)
  extractGzippedResource "data.db.gz" dbPath
  backup

/// Back up + re-seed the store, then tell the user what happened and how to repopulate it.
let private reseedAndReport
  (dbPath : string)
  (current : int)
  (storedLabel : string)
  : unit =
  let backup = reseedStore dbPath storedLabel
  eprintfn
    $"Upgraded this data directory to Release {current} (was {storedLabel}); previous store backed up to {backup}."
  eprintfn "Re-connect and pull from your peers, or re-author, to repopulate it."

/// Reconcile an existing store's Release with this binary's. Called only when data.db already exists.
let private reconcileExistingStore (dbPath : string) : unit =
  let current = LibDB.Releases.currentRelease
  match readStoredRelease dbPath with
  | Unreadable -> () // couldn't read it — don't touch it; the normal open will proceed or fail loudly
  | storeRel ->
    let stored =
      match storeRel with
      | Stamped r -> Some r
      | _ -> None
    let label =
      match storeRel with
      | Stamped r -> $"release{r}"
      | _ -> "pretracking"
    match LibDB.Releases.planCliUpgrade LibDB.Releases.releases stored current with
    | LibDB.Releases.CliUpgrade.Proceed -> () // up to date
    | LibDB.Releases.CliUpgrade.RefuseNewer r ->
      // A newer store must not be opened by older code (it could corrupt the newer format). Refuse.
      eprintfn
        $"This Darklang data directory is from a newer release (Release {r}); this binary is Release {current}."
      eprintfn
        $"Upgrade the CLI, or move {dbPath} aside to start fresh — refusing to open it with older code."
      exit 1
    | LibDB.Releases.CliUpgrade.MigrateInPlace ->
      // Every pending step is durable — migrate the store forward in place, PRESERVING the data (schema
      // copy-swap + op-format re-serialize + refold). No source needed, so the CLI can run it directly.
      LibDB.Releases.applyPending current
      eprintfn
        $"Upgraded this data directory from {label} to Release {current} (data preserved)."
    | LibDB.Releases.CliUpgrade.Reseed ->
      // A clean-break Release (content hashing changed) or a pre-tracking store of unknown format: the old
      // package data can't be reused in place, so back it up and re-seed from the embedded current-Release
      // store.
      reseedAndReport dbPath current label

let extract () : unit =
  // The embedded resource is `data.db.gz` (the seed db, gzip-compressed at
  // build time to save ~7 MB on binary size). On first run, decompress to
  // `~/.darklang/data.db`; on subsequent runs the file already exists and
  // grow/init proceeds against the local copy.
  if hasEmbeddedResource "data.db.gz" then
    let darklangDir = getDarklangDirectory ()

    Environment.SetEnvironmentVariable("DARK_CONFIG_RUNDIR", darklangDir)

    let dbPath = Path.Combine(darklangDir, "data.db")

    if not (File.Exists(dbPath)) then
      printfn $"Setting up Darklang CLI data directory at {darklangDir}"

      if not (Directory.Exists(darklangDir)) then
        Directory.CreateDirectory(darklangDir) |> ignore

      extractGzippedResource "data.db.gz" dbPath

      let readmePath = Path.Combine(darklangDir, "README.md")
      extractResource "README.md" readmePath

      let logsDir = Path.Combine(darklangDir, "logs")
      Directory.CreateDirectory(logsDir) |> ignore

      printfn "CLI data directory setup complete"
    else
      // An existing store from a previous run/release — reconcile its Release with this binary's before any
      // LibDB connection opens (a mismatch either refuses to open or re-seeds; a match is a no-op).
      reconcileExistingStore dbPath
