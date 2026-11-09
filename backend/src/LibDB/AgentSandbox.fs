/// `dark agent run <cmd...>`: run an agent where it cannot change what it is allowed to do.
///
/// On Linux the command runs under bubblewrap (`bwrap`), as this user, in its own user, PID and
/// IPC namespaces. The filesystem is read-only except for the working directory, the store
/// directory and a private /tmp; inside those, the policy directory and the `dark` binary are
/// mounted read-only again. Nothing in the sandbox can write the policy, so `dark permissions
/// allow` there writes a request instead (`PermissionRequests`).
module LibDB.AgentSandbox

open System
open System.IO

/// What the bwrap arguments are built from. Paths are absolute.
type Facts =
  {
    cwd : string
    runDir : string
    policyDir : string
    /// The directory holding the running `dark`: the binary and, for a framework build, its dlls.
    binDir : string
    /// Paths the person asked to be writable too (`--write`).
    writable : List<string>
    uid : string
    /// Without a terminal the command also gets a new session, so it cannot reach this one.
    stdinIsTerminal : bool
    command : List<string>
  }

let private trim (path : string) : string =
  let full = Path.GetFullPath path
  if full.Length > 1 then full.TrimEnd('/') else full

/// True when `path` is `root` or below it.
let private within (root : string) (path : string) : bool =
  path = root || path.StartsWith(root + "/", StringComparison.Ordinal)

/// The directories from just below `root` down to `path`'s parent.
let private between (root : string) (path : string) : List<string> =
  let rec up (p : string) (acc : List<string>) =
    match Path.GetDirectoryName p with
    | null -> acc
    | parent when parent = root || not (within root parent) -> acc
    | parent -> up parent (parent :: acc)
  up path []

/// The `.git/config` and `.git/hooks` of the repository holding `cwd`, which git runs commands
/// from: an agent writing them would get its commands run later, outside the sandbox.
let private gitProtected (exists : string -> bool) (cwd : string) : List<string> =
  let rec find (dir : string) =
    let git = Path.Combine(dir, ".git")
    if exists git then
      [ Path.Combine(git, "config"); Path.Combine(git, "hooks") ]
      |> List.filter exists
    else
      match Path.GetDirectoryName dir with
      | null -> []
      | parent -> find parent
  find cwd

/// The bwrap arguments for `facts`. `exists` says whether a path exists, so the arguments never
/// name one that does not (bwrap refuses those).
let bwrapArgs (exists : string -> bool) (facts : Facts) : List<string> =
  let writable =
    facts.cwd :: facts.runDir :: facts.writable
    |> List.map trim
    |> List.filter exists
    |> List.distinct
    // Parents before children, so a later bind never covers an earlier one.
    |> List.sortBy (fun p -> p.Length)
  let protectedPaths =
    [ trim facts.policyDir; trim facts.binDir ]
    @ gitProtected exists (trim facts.cwd)
    |> List.filter exists
    |> List.distinct
  // A read-only mount inside a writable directory still lets the directory ABOVE it be renamed,
  // taking the mount along and leaving room for a writable stand-in. Every directory between a
  // writable root and a protected path is bound onto itself, which makes it a mount point, and a
  // mount point cannot be renamed.
  let anchors =
    protectedPaths
    |> List.collect (fun p ->
      writable
      |> List.filter (fun w -> within w p && w <> p)
      |> List.collect (fun w -> between w p))
    |> List.distinct
    |> List.sortBy (fun p -> p.Length)
  let bind flag (path : string) = [ flag; path; path ]
  let sessionBus = $"/run/user/{facts.uid}"
  [ yield! [ "--die-with-parent"; "--unshare-user"; "--disable-userns" ]
    yield! [ "--unshare-pid"; "--unshare-ipc" ]
    yield! bind "--ro-bind" "/"
    yield! [ "--dev"; "/dev"; "--proc"; "/proc"; "--tmpfs"; "/tmp" ]
    // The user's session bus and systemd: `systemd-run --user` would start a command outside.
    if exists sessionBus then yield! [ "--tmpfs"; sessionBus ]
    // A Docker socket is root on this machine.
    // `/var/run` is usually a link to `/run`, and bwrap cannot mount through a link.
    if exists "/run/docker.sock" then
      yield! [ "--ro-bind"; "/dev/null"; "/run/docker.sock" ]
    elif exists "/var/run/docker.sock" then
      yield! [ "--ro-bind"; "/dev/null"; "/var/run/docker.sock" ]
    for w in writable do
      yield! bind "--bind" w
    for a in anchors do
      yield! bind "--bind" a
    for p in protectedPaths do
      yield! bind "--ro-bind" p
    yield! [ "--setenv"; "DARK_AGENT"; String.concat " " facts.command ]
    if not facts.stdinIsTerminal then yield "--new-session"
    yield! [ "--chdir"; trim facts.cwd; "--" ]
    yield! facts.command ]

/// `bwrap` on the PATH, if it is there.
let private findBwrap () : Option<string> =
  match Environment.GetEnvironmentVariable "PATH" with
  | null -> None
  | path ->
    path.Split(':')
    |> Array.map (fun dir -> Path.Combine(dir, "bwrap"))
    |> Array.tryFind File.Exists

/// Why `dark agent run` cannot run here, if it cannot.
let unavailable () : Option<string> =
  if not (OperatingSystem.IsLinux()) then
    Some
      "`dark agent run` is Linux only for now: it uses bubblewrap, which macOS does not have."
  elif Option.isNone (findBwrap ()) then
    Some
      "`dark agent run` needs bubblewrap (`bwrap`) on the PATH. Install it with your package manager, e.g. `apt install bubblewrap`."
  else
    None

/// Replace this process with `command` running in the sandbox. Returns only on failure, with
/// the exit code to use; bwrap reports its own failures (no user namespaces, say) itself.
let run (writable : List<string>) (command : List<string>) : int =
  let fail (why : string) =
    Console.Error.WriteLine why
    1
  match
    unavailable (), findBwrap (), LibExecution.HostSecurity.policyDirectory ()
  with
  | Some why, _, _ -> fail why
  | None, None, _ -> fail "bwrap is not on the PATH"
  | None, _, Error why -> fail why
  | None, Some bwrap, Ok policyDir ->
    Directory.CreateDirectory policyDir |> ignore<DirectoryInfo>
    let facts =
      { cwd = Environment.CurrentDirectory
        runDir = LibConfig.Config.runDir
        policyDir = policyDir
        binDir = Path.GetDirectoryName Environment.ProcessPath
        writable = writable |> List.map Path.GetFullPath
        uid = string (LibExecution.HostPosix.getuid ())
        stdinIsTerminal = not Console.IsInputRedirected
        command = command }
    let exists (p : string) = File.Exists p || Directory.Exists p
    match LibExecution.HostPosix.exec bwrap (bwrapArgs exists facts) with
    | Ok() -> 0
    | Error(_, message) -> fail $"could not start bwrap: {message}"
