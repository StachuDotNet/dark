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

/// The .darklang directory to use when nothing says otherwise.
let private getDefaultDarklangDirectory () : string =
  if isInstalledMode () then
    // Installed mode: use the central ~/.darklang directory
    let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
    Path.Combine(home, ".darklang")
  else
    // Portable mode: use adjacent .darklang directory
    Path.Combine(exeDirectory (), ".darklang")

/// Where this instance keeps its store, logs and local config.
///
/// An explicit `DARK_CONFIG_RUNDIR` wins over the default. Overwriting it instead would
/// mean every process on a machine shares one store, so a second instance -- a throwaway
/// store to try something in, or two binaries measured against the same data -- could not
/// be asked for at all.
let private getDarklangDirectory () : string =
  match Environment.GetEnvironmentVariable "DARK_CONFIG_RUNDIR" with
  | null
  | "" -> getDefaultDarklangDirectory ()
  | explicit -> explicit

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

/// The embedded schema, or None in a debug build that did not embed it.
let embeddedSchema () : Option<string> =
  LibDB.SeedTopUp.schemaFrom (Assembly.GetExecutingAssembly())


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



/// Copy the store aside before an upgrade touches it. Kept next to the store, one per calendar day:
/// three upgrades in an afternoon leave one copy, of the state before the day's first change.
///
/// Best-effort. A failed backup does not stop the upgrade.
let private backupBeforeUpgrade (dbPath : string) : unit =
  try
    if File.Exists dbPath then
      let dir = Path.Combine(Path.GetDirectoryName(dbPath), "backups")
      Directory.CreateDirectory(dir) |> ignore<DirectoryInfo>

      let stamp = System.DateTime.UtcNow.ToString("yyyy-MM-dd")
      let target = Path.Combine(dir, $"data.db.before-upgrade-{stamp}")

      if not (File.Exists target) then
        File.Copy(dbPath, target)
        eprintfn $"Backed up your store to {target} before upgrading it."
  with e ->
    System.Console.Error.WriteLine($"could not back up the store: {e.Message}")


/// Bindings this store holds that the embedded seed did not write, captured before an upgrade folds.
///
/// `locations.op_id` is the op the fold credited with each binding, so an op the seed does not carry is one
/// authored here or pulled from a peer. Upgrading must not silently take those back: the seed's version of a
/// name you edited is newer by stamp and would win LWW.
///
/// (owner, modules, name, item_type, item_hash, source). `source` separates a name you edited from one
/// that merely followed it through propagation, which matters only for what gets reported: one edit to
/// a core function repoints hundreds of callers.
let mutable locallyAuthored
  : List<string * string * string * string * string * string> =
  []


/// Top up an existing store with this binary's embedded package ops (`LibDB.SeedTopUp.topUp`).
///
/// Failure is not fatal on purpose: a store that could not be topped up is no worse off than before.
let private reseedFromEmbedded (dbPath : string) : unit =
  let temp =
    Path.Combine(Path.GetTempPath(), $"dark-seed-{System.Guid.NewGuid()}.db")

  try
    try
      extractGzippedResource "data.db.gz" temp

      if File.Exists temp then
        locallyAuthored <-
          LibDB.SeedTopUp.topUp dbPath temp (fun () -> backupBeforeUpgrade dbPath)
    with e ->
      System.Console.Error.WriteLine(
        $"could not top up the package store: {e.Message}"
      )
  finally
    try
      if File.Exists temp then File.Delete temp
    with _ ->
      ()


/// Sub-timings for `extract`, in Stopwatch ticks. Collected rather than logged because `extract` runs
/// before telemetry has an output path: it's what sets DARK_CONFIG_RUNDIR, where the log lives.
/// `Cli.Main` drains this once telemetry is up.
let timings : ResizeArray<string * int64> = ResizeArray()

let inline private timed (label : string) (f : unit -> 'a) : 'a =
  let t0 = System.Diagnostics.Stopwatch.GetTimestamp()
  let r = f ()
  timings.Add(label, System.Diagnostics.Stopwatch.GetTimestamp() - t0)
  r

let private storeStamp = LibDB.SeedTopUp.storeStamp
let private recordStoreStamp = LibDB.SeedTopUp.recordStoreStamp

let extract () : unit =
  // On first run, decompress the embedded seed db to `~/.darklang/data.db`; afterwards the
  // file exists and grow/init proceeds against the local copy.
  if timed "extract.hasResource" (fun () -> hasEmbeddedResource "data.db.gz") then
    let darklangDir = getDarklangDirectory ()

    Environment.SetEnvironmentVariable("DARK_CONFIG_RUNDIR", darklangDir)

    let dbPath = Path.Combine(darklangDir, "data.db")

    // Asked ONCE, and it decides everything below.
    //
    // The stamp says which build last reconciled this store with its own embedded seed. The
    // schema, the release steps and the seed are all fixed per binary, so a store this same
    // build has already reconciled cannot need any of them again -- and nothing outside can
    // create that need, because an older binary running here records ITS hash and we come back
    // and do the work.
    //
    // It used to guard only the seed top-up, and the schema pass ran on every single command:
    // `CREATE TABLE IF NOT EXISTS` for every table, then the release list, then every index.
    // Measured on the published binary, that was 31 ms of a 203 ms startup, paid by `dark ps`
    // and `dark eval 1L` alike.
    let build = LibConfig.Config.buildHash
    // A build with no hash of its own cannot claim anything, so it does the work every time,
    // which is what every build did before.
    // `File.Exists` first: reading the stamp opens the db, which CREATES it, and an empty file
    // here reads as a store that needs no seed.
    let reconciled =
      build <> "dev" && File.Exists(dbPath) && storeStamp dbPath = Some build

    // An EXISTING store keeps whatever shape the seed it was born from had: the schema never runs
    // against it, so a table or column added since is simply absent, and the top-up below is the first
    // thing to trip over it -- as a raw SQLite error ("table locations has no column named previous"),
    // on a store that is otherwise fine. Bring the shape forward first, in the order the statements
    // require.
    if File.Exists(dbPath) && not reconciled then
      try
        match embeddedSchema () with
        | Some sql ->
          timed "extract.schema" (fun () -> LibDB.Releases.applySchemaTables sql)
          timed "extract.releases" (fun () -> LibDB.Releases.runPending ())
          timed "extract.indexes" (fun () -> LibDB.Releases.applySchemaIndexes sql)
        | None -> ()
      with e ->
        System.Console.Error.WriteLine(
          $"could not bring the store's schema up to date: {e.Message}"
        )

    if not (File.Exists(dbPath)) then
      eprintfn $"Setting up Darklang CLI data directory at {darklangDir}"

      if not (Directory.Exists(darklangDir)) then
        Directory.CreateDirectory(darklangDir) |> ignore

      extractGzippedResource "data.db.gz" dbPath

      // Everything in a store this fresh came from the seed, so the ledger can be filled with
      // certainty exactly once. Without it the first upgrade after an install has no provenance.
      try
        use conn =
          new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}")
        conn.Open()
        use cmd = conn.CreateCommand()
        cmd.CommandText <-
          "CREATE TABLE IF NOT EXISTS seed_ops (op_id TEXT PRIMARY KEY);
           INSERT OR IGNORE INTO seed_ops (op_id) SELECT id FROM package_ops;"
        cmd.ExecuteNonQuery() |> ignore<int>
      with e ->
        System.Console.Error.WriteLine(
          $"could not record which ops came from this build: {e.Message}"
        )

      let readmePath = Path.Combine(darklangDir, "README.md")
      extractResource "README.md" readmePath

      let logsDir = Path.Combine(darklangDir, "logs")
      Directory.CreateDirectory(logsDir) |> ignore

      // A store just written from this binary's own seed is by definition reconciled with it.
      recordStoreStamp dbPath LibConfig.Config.buildHash

      eprintfn "CLI data directory setup complete"
    // Top up an existing store with this binary's own package code (see
    // `reseedFromEmbedded`: additive, content-addressed), then `growIfNeeded` folds
    // it; without this, upgrading the binary would mean wiping the store.
    else if not reconciled then
      // The backup happens inside the top-up, once it knows there is something to top up.
      timed "extract.topUpStore" (fun () -> reseedFromEmbedded dbPath)
      if build <> "dev" then recordStoreStamp dbPath build
