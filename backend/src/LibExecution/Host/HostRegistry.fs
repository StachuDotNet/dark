/// The machine's Dark processes: one file per OS process under the instance's rundir, written
/// when the CLI starts and removed when it exits, so `dark ps` from any shell can list what is
/// running on the box (a serve, a daemon, another terminal's TUI), not only itself. A file whose
/// pid is gone is stale and dropped by the next reader; that is what survives a crash or a kill.
///
/// This is the host side; `dark ps` renders it. What is inside another OS process (its own Dark
/// process tree) is that process's, not the registry's: the registry is the outer ring, one row
/// per OS process, and reaching into a row is a signal (`ps cancel` sends TERM, which is the
/// Ctrl-C path that suspends a traced run; `ps kill` sends KILL).
module LibExecution.HostRegistry

open System
open System.IO

type Entry =
  {
    pid : int
    /// What a system monitor shows (`HostProcess.setProcessTitle`).
    title : string
    /// The command line as launched.
    command : string
    /// The branch the process runs on, when it had one at startup.
    branch : string
    started : DateTime
  }

let mutable private directory : string = ""
let mutable private ownFile : string = ""

let private alive (pid : int) : bool =
  if OperatingSystem.IsLinux() then
    Directory.Exists $"/proc/{pid}"
  else
    try
      Diagnostics.Process.GetProcessById(pid) |> ignore<Diagnostics.Process>
      true
    with _ ->
      false

let private serialize (e : Entry) : string =
  let esc (s : string) =
    s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ")
  $"{{\"pid\":{e.pid},\"title\":\"{esc e.title}\",\"command\":\"{esc e.command}\",\"branch\":\"{esc e.branch}\",\"started\":\"{e.started:O}\"}}"

let private field (json : string) (name : string) : string =
  let key = $"\"{name}\":"
  match json.IndexOf key with
  | -1 -> ""
  | i ->
    let rest = json.Substring(i + key.Length)
    if rest.StartsWith "\"" then
      let sb = Text.StringBuilder()
      let mutable j = 1
      let mutable fin = false
      while not fin && j < rest.Length do
        match rest[j] with
        | '\\' when j + 1 < rest.Length ->
          sb.Append(rest[j + 1]) |> ignore<Text.StringBuilder>
          j <- j + 2
        | '"' -> fin <- true
        | c ->
          sb.Append c |> ignore<Text.StringBuilder>
          j <- j + 1
      sb.ToString()
    else
      rest.Substring(0, rest.IndexOfAny [| ','; '}' |])

let private parse (json : string) : Option<Entry> =
  try
    Some
      { pid = int (field json "pid")
        title = field json "title"
        command = field json "command"
        branch = field json "branch"
        started =
          DateTime.Parse(
            field json "started",
            null,
            Globalization.DateTimeStyles.RoundtripKind
          ) }
  with _ ->
    None

/// Where the files live: `<rundir>/run/ps`, beside the daemons' pidfiles. Set once by the CLI.
let setDirectory (rundir : string) : unit =
  directory <- Path.Combine(rundir, "run", "ps")

/// Record this process. Once, at startup, after the title is known. Never raises: a rundir that
/// cannot be written means this process is simply not listed.
let register (title : string) (command : string) (branch : string) : unit =
  if directory <> "" then
    try
      Directory.CreateDirectory directory |> ignore<DirectoryInfo>
      let pid = Environment.ProcessId
      let path = Path.Combine(directory, $"{pid}.json")
      File.WriteAllText(
        path,
        serialize
          { pid = pid
            title = title
            command = command
            branch = branch
            started = DateTime.UtcNow }
      )
      ownFile <- path
      let remove () =
        try
          File.Delete path
        with _ ->
          ()
      AppDomain.CurrentDomain.ProcessExit.Add(fun _ -> remove ())
      // A TERM (what `ps cancel <pid>` sends) does not always reach ProcessExit through a
      // blocking wait; remove the file first, then let the default termination proceed.
      if not (OperatingSystem.IsWindows()) then
        Runtime.InteropServices.PosixSignalRegistration.Create(
          Runtime.InteropServices.PosixSignal.SIGTERM,
          (fun ctx ->
            remove ()
            ctx.Cancel <- false)
        )
        |> ignore<Runtime.InteropServices.PosixSignalRegistration>
    with _ ->
      ()

/// Every Dark process on the machine that is still alive, oldest first. Stale files are removed
/// on the way.
let list () : Entry list =
  if directory = "" || not (Directory.Exists directory) then
    []
  else
    Directory.GetFiles(directory, "*.json")
    |> Array.choose (fun path ->
      match
        parse (
          try
            File.ReadAllText path
          with _ ->
            ""
        )
      with
      | Some e when alive e.pid -> Some e
      | _ ->
        (try
          File.Delete path
         with _ ->
           ())
        None)
    |> Array.sortBy (fun e -> e.started)
    |> Array.toList


/// Whether the file at `path` has been written since `since`. For a resume's stale-read warning
/// (`Interpreter.ReplayPolicy`); a path that is not a file answers false.
let fileChangedSince (path : string) (since : DateTime) : bool =
  try
    File.Exists path && File.GetLastWriteTimeUtc path > since
  with _ ->
    false
