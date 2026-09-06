/// The origins this instance may reach with the SSRF guards OFF.
///
/// `httpGetUnsafeBytes` disables the guards so servers on loopback/RFC-1918/a tailnet
/// are reachable; it is in the general builtin set, so any Dark the CLI runs
/// (including fetched code) can call it. So the guards come off only towards origins
/// this machine was pointed at: the relay stored in local config (written by
/// `dark sync setup`) and URLs on this process's command line. Fetched code can
/// influence neither. The stored side is a hook, not a snapshot, so an origin added
/// during this process counts immediately.
///
/// `httpClientRequest` is separate and stays fully guarded.
module LibExecution.UnguardedOrigins

/// scheme://host:port, lowercased, with the default port made explicit so `http://h`
/// and `http://h:80` compare equal. None when the string is not a URL we can reason
/// about, which is a refusal, not a pass.
let originOf (url : string) : string option =
  match System.Uri.TryCreate(url, System.UriKind.Absolute) with
  | true, uri ->
    let scheme = uri.Scheme.ToLowerInvariant()
    let host = uri.Host.ToLowerInvariant()
    if host = "" then None else Some $"{scheme}://{host}:{uri.Port}"
  | _ -> None

/// URLs named on the command line of this process. Set once at startup.
let mutable private fromArgv : Set<string> = Set.empty

/// Where the stored origins come from. A hook because this module sits below `LibDB`
/// and must not depend on it, and because the answer can change within one process.
let mutable private stored : unit -> string list = fun () -> []

let setFromArgv (urls : string seq) : unit =
  fromArgv <- urls |> Seq.choose originOf |> Set.ofSeq

let setStoredLookup (lookup : unit -> string list) : unit = stored <- lookup

/// May the guards be skipped for this URL?
let isAllowed (url : string) : bool =
  match originOf url with
  | None -> false
  | Some origin ->
    Set.contains origin fromArgv
    || (stored () |> List.choose originOf |> List.contains origin)

/// Names the origin (the caller may not have built the URL) and both ways to allow
/// it. No "refused:" prefix: callers wrap errors in their own words.
let refusalMessage (url : string) : string =
  let where = originOf url |> Option.defaultValue url
  $"{where} is not the relay you sync with, so it cannot be fetched with the "
  + "network protections off. Point this instance at it with `dark sync setup`, or "
  + "pass its URL on the command line."


/// The write secret for the relay, by origin. A hook for the same reason the stored
/// lookup is one: this module sits below `LibDB`.
///
/// The transport looks the credential up itself rather than being handed one, because
/// the secret must not reach Dark: `configGet` has no capability, so any Dark the CLI
/// runs could read it, including a package pulled from a peer. Dark still decides WHEN
/// to push.
let mutable private secretLookup : string -> string option = fun _ -> None

let setSecretLookup (lookup : string -> string option) : unit =
  secretLookup <- lookup

/// The `Authorization` header for <param url>, when a secret is stored for this
/// instance's relay and the url is actually one of its origins. Empty otherwise, so an
/// ordinary request carries nothing.
///
/// A header rather than a query parameter: a query string ends up in access logs and
/// proxy traces, a poor place for the one string that grants write access.
let authHeadersFor (url : string) : List<string * string> =
  match originOf url with
  | None -> []
  | Some origin ->
    match secretLookup origin with
    | Some secret when secret <> "" -> [ "Authorization", $"Bearer {secret}" ]
    | _ -> []
