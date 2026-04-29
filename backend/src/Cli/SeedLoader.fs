/// Locates the seed.db at startup. Replaces the prior embed-and-extract
/// flow (where seed.db was gzipped inside the binary) with a search through
/// candidate locations + optional download. Decoupling seed from binary lets
/// us update package data without rebuilding the executable.
module Cli.SeedLoader

open System
open System.IO

// ---------------------------------------------------------------------------
// Version tracking
// ---------------------------------------------------------------------------
//
// The seed must match the binary's expectations on two axes:
//
// 1. ProgramTypes shape — the seed's `package_*.pt_def`, `package_*.rt_*`,
//    and `package_ops.op_blob` blobs are binary-serialized PT/RT values. If
//    the F# union/record shape of ProgramTypes.fs changes incompatibly,
//    those blobs can't be deserialized. Tracked via SQLite `PRAGMA
//    user_version` set on the seed at export time, compared against this
//    constant at load time.
//
// 2. SQL schema — the migrations applied to the seed must be a subset of
//    what the binary knows. Tracked via the `system_migrations_v0` table.
//    A binary with migrations beyond what the seed has is OK (the grow path
//    applies them); a binary missing migrations the seed has is NOT OK
//    (the seed has rows we can't read).
//
// CURRENT_SEED_VERSION should be bumped whenever Language/ProgramTypes.fs
// changes in a way that breaks binary serialization (new union case, removed
// field, reordered case constructors, etc.). Eventually this should be
// derived mechanically — e.g. an MSBuild target that hashes a stable
// "shape descriptor" file or runs an FSharp.Compiler.Service-based AST hash.
// For v1, manual + a CI check that catches forgotten bumps.

let CURRENT_SEED_VERSION : int = 1


// ---------------------------------------------------------------------------
// Options
// ---------------------------------------------------------------------------

type LoadOptions =
  {
    /// Explicit path from --seed-db. Takes precedence over all other locations.
    seedDbArg : Option<string>

    /// Don't prompt the user (CI / scripts). Defaults to true if stdin isn't
    /// a TTY. Can be forced via --non-interactive.
    nonInteractive : bool

    /// Don't attempt to download. If all local candidates exhausted, fail.
    noDownload : bool

    /// URL to fetch seed.db from when all local options exhausted.
    downloadUrl : string
  }

let defaultDownloadUrl = "https://darklang.com/download/seed.db"

let defaultOptions =
  { seedDbArg = None
    nonInteractive = false
    noDownload = false
    downloadUrl = defaultDownloadUrl }


/// Strip out the F#-side flags from argv. Returns (parsed options, remaining args).
/// The remaining args are passed through to the Dark-side handler.
let parseArgs (args : List<string>) : LoadOptions * List<string> =
  let mutable opts = defaultOptions
  let mutable rest = []
  let mutable i = 0
  let arr = List.toArray args
  while i < arr.Length do
    let a = arr[i]
    match a with
    | "--seed-db" when i + 1 < arr.Length ->
      opts <- { opts with seedDbArg = Some arr[i + 1] }
      i <- i + 2
    | s when s.StartsWith("--seed-db=") ->
      opts <- { opts with seedDbArg = Some(s.Substring("--seed-db=".Length)) }
      i <- i + 1
    | "--non-interactive" ->
      opts <- { opts with nonInteractive = true }
      i <- i + 1
    | "--no-download" ->
      opts <- { opts with noDownload = true }
      i <- i + 1
    | "--download-from" when i + 1 < arr.Length ->
      opts <- { opts with downloadUrl = arr[i + 1] }
      i <- i + 2
    | other ->
      rest <- other :: rest
      i <- i + 1
  opts, List.rev rest


// ---------------------------------------------------------------------------
// Candidate locations
// ---------------------------------------------------------------------------

type Candidate =
  | ArgPath of path : string
  | EnvVar of varName : string * path : string
  | Adjacent of path : string
  | Home of path : string

let private candidatePath (c : Candidate) : string =
  match c with
  | ArgPath p -> p
  | EnvVar(_, p) -> p
  | Adjacent p -> p
  | Home p -> p

let private candidateLabel (c : Candidate) : string =
  match c with
  | ArgPath p -> $"--seed-db {p}"
  | EnvVar(name, p) -> $"${name} → {p}"
  | Adjacent p -> $"adjacent to binary: {p}"
  | Home p -> $"home: {p}"

let private exeDirectory () : string =
  let baseDir = AppContext.BaseDirectory
  if String.IsNullOrEmpty(baseDir) then
    let path = Environment.ProcessPath
    if String.IsNullOrEmpty(path) then
      Environment.CurrentDirectory
    else
      Path.GetDirectoryName(path)
  else
    baseDir.TrimEnd('/', '\\')

let homePath () : string =
  let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
  Path.Combine(home, ".darklang", "seed.db")

let private candidates (opts : LoadOptions) : List<Candidate> =
  [ match opts.seedDbArg with
    | Some p -> ArgPath p
    | None -> ()

    let envVal = Environment.GetEnvironmentVariable("DARKLANG_SEED_DB")
    if not (String.IsNullOrEmpty envVal) then EnvVar("DARKLANG_SEED_DB", envVal)

    Adjacent(Path.Combine(exeDirectory (), "seed.db"))

    Home(homePath ()) ]


// ---------------------------------------------------------------------------
// Validation
// ---------------------------------------------------------------------------

type ValidationError =
  /// File doesn't exist on disk.
  | NotFound
  /// File exists but couldn't be opened as a SQLite db.
  | NotSqlite of reason : string
  /// File is a SQLite db but version doesn't match.
  | VersionMismatch of seedVersion : int * binaryVersion : int

/// Read PRAGMA user_version from a SQLite db. Returns 0 if unset.
let private readSeedVersion (path : string) : Result<int, string> =
  try
    let connStr = $"Data Source={path};Mode=ReadOnly;Cache=Private"
    use conn = new Microsoft.Data.Sqlite.SqliteConnection(connStr)
    conn.Open()
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "PRAGMA user_version;"
    let result = cmd.ExecuteScalar()
    let v =
      match result with
      | :? int64 as i -> int i
      | :? int as i -> i
      | _ -> 0
    Ok v
  with ex ->
    Error ex.Message

let private validate (path : string) : Result<unit, ValidationError> =
  if not (File.Exists path) then
    Error NotFound
  else
    match readSeedVersion path with
    | Error msg -> Error(NotSqlite msg)
    | Ok seedVer when seedVer = CURRENT_SEED_VERSION -> Ok()
    | Ok seedVer -> Error(VersionMismatch(seedVer, CURRENT_SEED_VERSION))


// ---------------------------------------------------------------------------
// Interactive prompts
// ---------------------------------------------------------------------------

let private isStdinTty () : bool = not Console.IsInputRedirected

let private effectivelyInteractive (opts : LoadOptions) : bool =
  not opts.nonInteractive && isStdinTty ()

let private prompt (question : string) : bool =
  Console.Error.Write(question + " [Y/n] ")
  Console.Error.Flush()
  match Console.ReadLine() with
  | null -> false
  | s ->
    let s = s.Trim().ToLowerInvariant()
    s = "" || s = "y" || s = "yes"


// ---------------------------------------------------------------------------
// Download
// ---------------------------------------------------------------------------

let private download (url : string) (target : string) : Result<unit, string> =
  try
    Console.Error.WriteLine($"Downloading seed.db from {url} ...")
    use http = new System.Net.Http.HttpClient()
    http.Timeout <- TimeSpan.FromMinutes(5.0)
    let response = http.GetAsync(url).Result
    if not response.IsSuccessStatusCode then
      Error
        $"download failed: HTTP {int response.StatusCode} {response.ReasonPhrase}"
    else
      let dir = Path.GetDirectoryName(target)
      if not (Directory.Exists dir) then
        Directory.CreateDirectory(dir) |> ignore<DirectoryInfo>
      let bytes = response.Content.ReadAsByteArrayAsync().Result
      File.WriteAllBytes(target, bytes)
      Console.Error.WriteLine(
        $"Downloaded {bytes.Length / 1024 / 1024} MB to {target}"
      )
      Ok()
  with ex ->
    Error $"download failed: {ex.Message}"


// ---------------------------------------------------------------------------
// Error formatting
// ---------------------------------------------------------------------------

let private describeError (e : ValidationError) : string =
  match e with
  | NotFound -> "not found"
  | NotSqlite reason -> $"not a SQLite db: {reason}"
  | VersionMismatch(seed, bin) ->
    $"version mismatch (seed v{seed}, binary expects v{bin})"

let private formatNotFoundError
  (cands : List<Candidate>)
  (errors : List<Candidate * ValidationError>)
  : string =
  let lines = ResizeArray<string>()
  lines.Add("Could not find a usable seed.db. Tried:")
  for c in cands do
    let err = errors |> List.tryFind (fun (c', _) -> c' = c) |> Option.map snd
    match err with
    | Some e -> lines.Add($"  ✗ {candidateLabel c} — {describeError e}")
    | None -> lines.Add($"  ✗ {candidateLabel c}")
  lines.Add("")
  lines.Add("To provide a seed.db:")
  lines.Add("  - Pass --seed-db PATH on the command line")
  lines.Add("  - Set DARKLANG_SEED_DB environment variable")
  lines.Add("  - Place one adjacent to the binary")
  lines.Add($"  - Place one at {homePath ()}")
  lines.Add(
    $"  - Run interactively to download from {defaultDownloadUrl}, or pass --download-from URL"
  )
  String.concat "\n" lines


// ---------------------------------------------------------------------------
// Main entry point
// ---------------------------------------------------------------------------

/// Locates a seed.db, validates it, and returns the path. On success the
/// caller should copy it to the runtime data.db location (or open it
/// directly if read-only is fine).
let load (opts : LoadOptions) : Result<string, string> =
  let cands = candidates opts
  let mutable found = None
  let mutable errors = []
  for c in cands do
    if found.IsNone then
      let path = candidatePath c
      match validate path with
      | Ok() -> found <- Some(path, c)
      | Error e -> errors <- (c, e) :: errors

  match found with
  | Some(path, c) ->
    Console.Error.WriteLine($"Using seed.db ({candidateLabel c})")
    Ok path
  | None ->
    if opts.noDownload then
      Error(formatNotFoundError cands errors)
    elif not (effectivelyInteractive opts) then
      Error(
        formatNotFoundError cands errors
        + "\n\n(running non-interactively; would prompt to download otherwise)"
      )
    else
      // Show what we tried, then prompt
      Console.Error.WriteLine("No usable seed.db found locally. Tried:")
      for c in cands do
        let err = errors |> List.tryFind (fun (c', _) -> c' = c) |> Option.map snd
        match err with
        | Some e ->
          Console.Error.WriteLine($"  ✗ {candidateLabel c} — {describeError e}")
        | None -> Console.Error.WriteLine($"  ✗ {candidateLabel c}")
      Console.Error.WriteLine("")
      let q = $"Download from {opts.downloadUrl}?"
      if not (prompt q) then
        Error(formatNotFoundError cands errors)
      else
        let target = homePath ()
        match download opts.downloadUrl target with
        | Error msg -> Error msg
        | Ok() ->
          match validate target with
          | Ok() ->
            Console.Error.WriteLine(
              $"Using seed.db ({candidateLabel (Home target)})"
            )
            Ok target
          | Error e -> Error $"downloaded seed is not usable: {describeError e}"


// ---------------------------------------------------------------------------
// Install into runtime data dir
// ---------------------------------------------------------------------------

/// Sets up DARK_CONFIG_RUNDIR, copies the seed to the runtime data.db location
/// if it isn't already there. After this returns, LibConfig.Config.dbPath
/// refers to a usable database.
///
/// Idempotent: if data.db already exists at the runtime location, the seed
/// is NOT re-copied (so subsequent runs use the user's grown DB, not the
/// fresh seed). To force a reset, delete ~/.darklang/data.db before running.
let installSeed (seedPath : string) : unit =
  let darklangDir =
    let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
    let exeDir = exeDirectory ()
    if exeDir.EndsWith("/.darklang/bin") || exeDir.EndsWith("\\.darklang\\bin") then
      Path.Combine(home, ".darklang")
    else
      Path.Combine(exeDir, ".darklang")

  Environment.SetEnvironmentVariable("DARK_CONFIG_RUNDIR", darklangDir)

  if not (Directory.Exists darklangDir) then
    Directory.CreateDirectory(darklangDir) |> ignore<DirectoryInfo>
    Directory.CreateDirectory(Path.Combine(darklangDir, "logs"))
    |> ignore<DirectoryInfo>

  let dbPath = Path.Combine(darklangDir, "data.db")
  if not (File.Exists dbPath) then
    Console.Error.WriteLine($"Setting up Darklang data directory at {darklangDir}")
    if seedPath <> dbPath then File.Copy(seedPath, dbPath)
    Console.Error.WriteLine("Data directory ready")
