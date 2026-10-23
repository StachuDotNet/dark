/// Guest code cannot change the host's policy, from any place the host runs it.
///
/// Every host-only policy builtin (`Tests.PermissionsGate.hostOnlyPolicyBuiltins`, found rather
/// than listed) is called from each kind of guest code the CLI runs: `eval`, `run`, a package
/// test, and a `val` body. Each must refuse with the admin message, and the stored policy must be
/// the same afterwards. The instance is granted `native` first: every policy builtin needs it, so
/// without it the gate refuses them all before the admin check is reached, and a test that only
/// asked for "refused" would pass over a missing check. A read every guest may make runs alongside
/// as the control that `native` really was granted.
module Tests.HostOnlyBoundary

open System.Threading.Tasks
open FSharp.Control.Tasks

open Expecto
open Prelude

open Tests.CliTestHarness
open Tests.CliDsl

/// Arguments that type-check for each host-only builtin, as Dark source. A host-only builtin with
/// no entry here fails the test rather than going untested.
let private guestArgs : Map<string, string> =
  Map
    [ "pmPolicySetInstance", "(Builtin.pmPolicyGetInstance ())"
      "pmPolicyEditInstance", "[] [] []"
      "pmPolicyListPackages", "Stdlib.Option.Option.None"
      "pmPolicyRevokePackage", "Stdlib.Option.Option.None \"0000\""
      "pmPolicyApprovalsStale", "()"
      "pmPolicyApproveVersion",
      "Stdlib.Option.Option.None \"Tests.HostOnly.f\" \"0000\" Stdlib.Option.Option.None false false"
      "pmPolicyUnapproveVersion", "Stdlib.Option.Option.None \"Tests.HostOnly.f\"" ]

let private control = "Builtin.pmPolicyApprovedVersions Stdlib.Option.Option.None"

/// A Dark expression whose value is one line per call, `name: ALLOWED` or `name: <the failure>`.
/// Each call goes through `Stdlib.Live.apply`, so one refusal does not end the program before
/// the rest.
let private reportSource (hostOnly : List<string>) : string =
  let calls =
    ("control", control)
    :: (hostOnly
        |> List.map (fun name ->
          match Map.tryFind name guestArgs with
          | Some args -> (name, $"Builtin.{name} {args}")
          | None ->
            failtest $"{name} is host-only and has no arguments in guestArgs"))
  let binding (i : int) (name : string, expr : string) =
    $"let line{i} =\n"
    + $"  match Stdlib.Live.apply (fun _ -> {expr}) () with\n"
    + $"  | Ok _ -> \"{name}: ALLOWED\"\n"
    + $"  | Error e -> \"{name}: \" + e\n"
  let lines = calls |> List.mapi (fun i _ -> $"line{i}") |> String.concat ", "
  (calls |> List.mapi binding |> String.concat "")
  + $"Stdlib.String.join [ {lines} ] \"\\n\""

/// `source`, indented under a binding that holds it.
let private indented (source : string) : string =
  source.Split('\n') |> Array.map (fun l -> "  " + l) |> String.concat "\n"

/// The line for `name` in `output`.
let private lineFor (output : string) (name : string) : string =
  output.Split('\n')
  |> Array.tryFind (fun line -> line.Trim().StartsWith $"{name}: ")
  |> Option.defaultWith (fun () -> failtest $"no line for {name} in:\n{output}")

let private expectRefusedEverywhere
  (context : string)
  (hostOnly : List<string>)
  (output : string)
  : unit =
  Expect.stringContains
    (lineFor output "control")
    "ALLOWED"
    $"{context}: native was granted, so a read gets through ({output})"
  for name in hostOnly do
    let line = lineFor output name
    Expect.stringContains
      line
      "trusted `dark permissions` command"
      $"{context}: {name} is refused as policy administration"
    Expect.isFalse (line.Contains "ALLOWED") $"{context}: {name} did not run"

/// What the store says about policy: the instance rules and every approval.
let private policyState (state : Target) : Task<string> =
  task {
    let! rules = runCliPlain state [ "permissions"; "list" ]
    let! approved = runCliPlain state [ "permissions"; "approved" ]
    return rules + "\n---\n" + approved
  }

let private guestContextsCannotManagePolicies =
  instanceTest "no guest context can call a host-only policy builtin" (fun state ->
    task {
      do! start state
      do! run state [ "permissions"; "allow"; "native" ]
      let! hostOnly = Tests.PermissionsGate.hostOnlyPolicyBuiltins ()
      Expect.isGreaterThanOrEqual
        (List.length hostOnly)
        7
        $"the host-only builtins were found: {hostOnly}"
      let source = reportSource hostOnly
      let! before = policyState state

      // `eval`
      let! evaluated = runCliPlain state [ "eval"; source ]
      expectRefusedEverywhere "eval" hostOnly evaluated

      // `run`
      let path =
        System.IO.Path.Combine(
          System.IO.Path.GetTempPath(),
          $"dark-host-only-{System.Guid.NewGuid()}.dark"
        )
      try
        System.IO.File.WriteAllText(path, source + " |> Stdlib.printLine\n0")
        let! ran = runCliPlain state [ "run"; path ]
        expectRefusedEverywhere "run" hostOnly ran
      finally
        System.IO.File.Delete path

      // A package test: it fails with the report as its message.
      do!
        run
          state
          [ "test"
            "add"
            "Tests.HostOnly.boundary"
            "Stdlib.Test.fail (\n" + indented source + ")" ]
      let! tested = runCliPlain state [ "test"; "run"; "--force"; "Tests.HostOnly" ]
      expectRefusedEverywhere "test" hostOnly tested

      // A `val` body, evaluated when it is authored.
      do! value state "Tests.HostOnly.report" source
      let! valued = runCliPlain state [ "eval"; "Tests.HostOnly.report" ]
      expectRefusedEverywhere "val" hostOnly valued

      let! after = policyState state
      Expect.equal after before "the stored policy is what it was"
    })

let tests = testList "HostOnlyBoundary" [ guestContextsCannotManagePolicies ]
