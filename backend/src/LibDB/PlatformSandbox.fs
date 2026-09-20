/// Confining a platform to what it said it would do.
///
/// A manifest's effects are already a claim the gate checks before every call. They can be more
/// than a claim: they can CONFINE the process, so a platform that never mentioned the network
/// cannot reach it even if its executable decides to try. That is the one thing running somebody
/// else's binary in a separate process buys that nothing else can, and it is worth collecting.
///
/// What is here is deliberately narrow: the network, and nothing else. It is the restriction
/// available on Linux with no privileges, no extra binary to install, and no pre-exec hook, which
/// .NET's `Process.Start` does not offer. The filesystem needs mount namespaces and a root to pivot
/// into; syscalls need seccomp, which has to be installed between fork and exec. Both are real and
/// both are bigger than this.
///
/// FAIL VISIBLE, never fail closed and never fail silent. A platform that cannot be confined on
/// this machine still runs, and `dark platforms <Name>` says so in a sentence. Refusing to run it
/// would make the whole feature unusable on macOS; running it and saying nothing would let someone
/// believe in a sandbox they do not have.
module LibDB.PlatformSandbox

open Prelude

module Effects = LibExecution.Effects

/// How to actually start a platform, and what that buys.
type Plan =
  {
    executable : string
    arguments : List<string>

    /// One sentence, for a person reading `dark platforms <Name>`. Says what is confined, or why
    /// nothing is.
    confinement : string
  }

/// Does this platform reach the network by its own account?
let private wantsNetwork (effects : Set<Effects.Effect>) : bool =
  effects
  |> Set.exists (fun e ->
    match e with
    | Effects.Effect.Http
    | Effects.Effect.HttpServer -> true
    // `Native` means "no rule could honestly confine this", and that is as true of a namespace as
    // of a policy rule. A platform declaring it gets no sandbox and is told so, rather than one
    // that would be a comforting lie.
    | Effects.Effect.Native -> true
    | _ -> false)

/// `unshare`, from util-linux, which is how a network namespace is entered without privileges.
///
/// Looked up once. Absent on macOS and on a minimal container, and its absence is a sentence rather
/// than an error.
let private unsharePath : Lazy<Option<string>> =
  lazy ([ "/usr/bin/unshare"; "/bin/unshare" ] |> List.tryFind System.IO.File.Exists)

/// Whether this machine lets an unprivileged process enter a user and network namespace at all.
///
/// The binary being present is not the same question. Docker's default seccomp profile refuses the
/// syscall (the devcontainer sets `seccomp=unconfined` precisely so it does not), Ubuntu 24.04's
/// AppArmor restricts unprivileged user namespaces, and `kernel.unprivileged_userns_clone=0` turns
/// them off outright. On any of those `unshare` exits with EPERM before it ever execs the platform,
/// and the platform reads as one that "exited during startup, without saying what it speaks",
/// which blames the wrong thing. Probed once, with `true` as the payload, so the answer is a
/// sentence in the listing rather than a mystery at spawn.
let private unshareWorks : Lazy<Option<string>> =
  lazy
    (match unsharePath.Force() with
     | None -> None
     | Some unshare ->
       try
         let psi = System.Diagnostics.ProcessStartInfo(unshare)
         for a in [ "--map-current-user"; "--net"; "--"; "/bin/true" ] do
           psi.ArgumentList.Add a
         psi.RedirectStandardError <- true
         psi.RedirectStandardOutput <- true
         psi.UseShellExecute <- false
         use p = System.Diagnostics.Process.Start psi
         if p.WaitForExit 5000 && p.ExitCode = 0 then Some unshare else None
       with _ ->
         None)

let private onLinux () =
  System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform
    System.Runtime.InteropServices.OSPlatform.Linux

/// How to start this platform's executable, given what it says it does.
let plan (effects : Set<Effects.Effect>) (executable : string) : Plan =
  let bare =
    { executable = executable; arguments = []; confinement = "not confined" }

  if wantsNetwork effects then
    { bare with
        confinement = "not confined: it asked for the network, so it is given one" }
  elif not (onLinux ()) then
    { bare with
        confinement =
          "not confined: this machine has no way to do it without privileges" }
  else
    match unsharePath.Force(), unshareWorks.Force() with
    | None, _ ->
      { bare with confinement = "not confined: `unshare` is not on this machine" }
    | Some _, None ->
      { bare with
          confinement =
            "not confined: this machine does not let an unprivileged process enter a network namespace" }
    | Some _, Some unshare ->
      // `--map-current-user` rather than `--map-root-user`: the process needs a user namespace to
      // be allowed a network namespace at all, and it does not need to be root inside it. Mapping
      // to root would hand it capabilities over its own namespaces for no reason.
      { executable = unshare
        arguments = [ "--map-current-user"; "--net"; "--"; executable ]
        confinement = "no network: it never asked for one" }
