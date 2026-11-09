/// `dark agent run`: the sandbox an agent runs in, and the requests it makes there.
module Tests.AgentSandbox

open Expecto
open Prelude

module AgentSandbox = LibDB.AgentSandbox

let private facts : AgentSandbox.Facts =
  { cwd = "/work/project"
    runDir = "/home/me/.darklang"
    policyDir = "/home/me/.darklang/policy"
    binDir = "/work/project/build/out/bin"
    writable = [ "/home/me/.claude" ]
    uid = "1000"
    stdinIsTerminal = true
    command = [ "claude"; "--resume" ] }

let private present =
  set
    [ "/work/project"
      "/work/project/.git"
      "/work/project/.git/config"
      "/home/me/.darklang"
      "/home/me/.darklang/policy"
      "/work/project/build/out/bin"
      "/home/me/.claude" ]

/// Where `[flag; path; path]` first appears in `args`, if it does.
let private indexOf
  (flag : string)
  (path : string)
  (args : List<string>)
  : Option<int> =
  args |> List.windowed 3 |> List.tryFindIndex (fun w -> w = [ flag; path; path ])

let protectedPathsAreMountedReadOnlyLast =
  test "the policy and the binary are mounted read-only after everything writable" {
    let args = AgentSandbox.bwrapArgs (fun p -> Set.contains p present) facts
    let at flag path =
      match indexOf flag path args with
      | Some i -> i
      | None -> failtest $"no {flag} {path} in {args}"
    let lastWritable =
      [ "/work/project"; "/home/me/.darklang"; "/home/me/.claude" ]
      |> List.map (at "--bind")
      |> List.max
    for p in
      [ "/home/me/.darklang/policy"
        "/work/project/build/out/bin"
        "/work/project/.git/config" ] do
      Expect.isGreaterThan
        (at "--ro-bind" p)
        lastWritable
        $"{p} is read-only over its writable parent"
    Expect.isLessThan
      (at "--ro-bind" "/")
      lastWritable
      "the rest of the filesystem is read-only"
    Expect.isNone
      (indexOf "--ro-bind" "/work/project/.git/hooks" args)
      "a path that is not there is not named"
  }

let directoriesAboveProtectedPathsCannotBeRenamed =
  test
    "every directory between a writable root and a protected path is a mount point" {
    let args = AgentSandbox.bwrapArgs (fun p -> Set.contains p present) facts
    for anchor in [ "/work/project/build"; "/work/project/build/out" ] do
      Expect.isSome (indexOf "--bind" anchor args) $"{anchor} is bound onto itself"
    Expect.isNone (indexOf "--bind" "/work" args) "nothing above a writable root"
  }

let theCommandRunsLastAndUnchanged =
  test "the command is passed through as given, after the sandbox's own arguments" {
    let args = AgentSandbox.bwrapArgs (fun p -> Set.contains p present) facts
    Expect.equal
      (args |> List.rev |> List.take 3)
      [ "--resume"; "claude"; "--" ]
      "after --"
    Expect.contains args "--disable-userns" "no nested user namespaces"
    Expect.isFalse
      (List.contains "--new-session" args)
      "a terminal keeps its session"
    let piped =
      AgentSandbox.bwrapArgs
        (fun p -> Set.contains p present)
        { facts with stdinIsTerminal = false }
    Expect.contains piped "--new-session" "without a terminal, a new session"
  }

/// The boundary itself, checked by `scripts/testing/check-agent-sandbox` against a throwaway
/// install. bwrap cannot start inside the dev container (Docker's AppArmor profile refuses its
/// mounts), so there this says why it skipped; on a host it runs every check.
let theSandboxHoldsWhereBwrapRuns =
  testTask
    "inside the sandbox the policy and binary are read-only and allow makes a request" {
    let repo =
      System.IO.Path.GetFullPath(
        System.IO.Path.Combine(__SOURCE_DIRECTORY__, "../../..")
      )
    let cli =
      match System.Environment.GetEnvironmentVariable "DARK_TEST_CLI" with
      | null
      | "" -> System.IO.Path.Combine(repo, "backend/Build/out/Cli/Debug/net10.0/Cli")
      | path -> path
    let psi =
      System.Diagnostics.ProcessStartInfo(
        System.IO.Path.Combine(repo, "scripts/testing/check-agent-sandbox")
      )
    psi.ArgumentList.Add(System.IO.Path.GetFullPath cli)
    psi.Environment["DARK_CHECK_STORE"] <- LibDB.Sqlite.currentDbPath
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    let p : System.Diagnostics.Process = System.Diagnostics.Process.Start psi
    let! (out : string) = p.StandardOutput.ReadToEndAsync()
    let! (err : string) = p.StandardError.ReadToEndAsync()
    p.WaitForExit()
    let code = p.ExitCode
    p.Dispose()
    match code with
    | 0 -> ()
    | 77 -> skiptest $"bwrap cannot start here: {out.Trim()}"
    | code -> failtest $"check-agent-sandbox exited {code}:\n{out}\n{err}"
  }

let tests =
  testList
    "agentSandbox"
    [ protectedPathsAreMountedReadOnlyLast
      directoriesAboveProtectedPathsCannotBeRenamed
      theCommandRunsLastAndUnchanged
      theSandboxHoldsWhereBwrapRuns ]
