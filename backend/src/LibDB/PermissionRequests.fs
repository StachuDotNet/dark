/// Permission changes asked for where the policy is read-only, inside `dark agent run`.
///
/// A request is written to `permission-requests` in the store directory, which the sandbox
/// leaves writable: the agent must still be able to ask. The answers are kept in the policy
/// directory, which it cannot write, so an agent can add requests but cannot mark one answered.
/// A request's id is a hash of what it asks for, so a request changed after the person read it
/// is a different request, and approving the old id applies nothing.
module LibDB.PermissionRequests

open System
open System.IO
open System.Text.Json

type Request =
  {
    id : string
    at : string
    /// The command the agent was started as, and where.
    by : string
    why : string
    /// The `dark permissions` arguments, as given.
    args : List<string>
  }

let private requestsPath () : string =
  Path.Combine(LibConfig.Config.runDir, "permission-requests")

/// In the policy directory: `id<TAB>approved|denied<TAB>when`.
let private answersFileName = "requests-answered"

/// The newest request the person has been told about, so the notice is said once.
let private noticedFileName = "requests-noticed"

let private idOf (line : string) : string =
  let digest =
    Security.Cryptography.SHA256.HashData(Text.Encoding.UTF8.GetBytes line)
  Convert.ToHexString(digest, 0, 4).ToLowerInvariant()

let private parse (line : string) : Option<Request> =
  try
    use doc = JsonDocument.Parse line
    let root = doc.RootElement
    let text (name : string) =
      match root.TryGetProperty name with
      | true, v when v.ValueKind = JsonValueKind.String -> v.GetString()
      | _ -> ""
    let args =
      match root.TryGetProperty "args" with
      | true, v when v.ValueKind = JsonValueKind.Array ->
        [ for a in v.EnumerateArray() do
            if a.ValueKind = JsonValueKind.String then yield a.GetString() ]
      | _ -> []
    if List.isEmpty args then
      None
    else
      Some
        { id = idOf line
          at = text "at"
          by = text "by"
          why = text "why"
          args = args }
  with _ ->
    None

let private all () : List<Request> =
  try
    File.ReadAllLines(requestsPath ())
    |> List.ofArray
    |> List.filter (fun l -> l.Trim() <> "")
    |> List.choose parse
  with
  | :? FileNotFoundException
  | :? DirectoryNotFoundException -> []

let private answered () : Set<string> =
  match LocalFile.read answersFileName with
  | LocalFile.Read bytes ->
    (Text.Encoding.UTF8.GetString bytes).Split('\n')
    |> Array.choose (fun line ->
      match line.Split('\t') with
      | [| id; _; _ |] -> Some id
      | _ -> None)
    |> Set.ofArray
  | _ -> Set.empty

/// The requests nobody has answered, oldest first. One asked twice is listed once.
let pending () : List<Request> =
  if not (File.Exists(requestsPath ())) then
    []
  else
    let done' = answered ()
    all ()
    |> List.filter (fun r -> not (Set.contains r.id done'))
    |> List.distinctBy (fun r -> r.args)

/// Ask for `dark permissions <args>`. Returns the request's id.
let record (args : List<string>) (why : string) : string =
  let by =
    let agent =
      match Environment.GetEnvironmentVariable "DARK_AGENT" with
      | null
      | "" -> "a command outside `dark agent run`"
      | agent -> agent
    $"{agent} in {Environment.CurrentDirectory}"
  let line =
    use stream = new MemoryStream()
    (use writer = new Utf8JsonWriter(stream)
     writer.WriteStartObject()
     writer.WriteString("at", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))
     writer.WriteString("by", by)
     writer.WriteString("why", why)
     writer.WriteStartArray("args")
     for a in args do
       writer.WriteStringValue a
     writer.WriteEndArray()
     writer.WriteEndObject())
    Text.Encoding.UTF8.GetString(stream.ToArray())
  File.AppendAllText(requestsPath (), line + "\n")
  idOf line

/// Record the person's answer, for this request and any other asking for the same thing.
/// False when no pending request has that id.
let answer (id : string) (approved : bool) : bool =
  let waiting = all ()
  match waiting |> List.tryFind (fun r -> r.id = id) with
  | Some request when not (Set.contains id (answered ())) ->
    let outcome = if approved then "approved" else "denied"
    let at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
    for same in waiting |> List.filter (fun r -> r.args = request.args) do
      LocalFile.appendLine answersFileName $"{same.id}\t{outcome}\t{at}"
    true
  | _ -> false

/// How many requests are waiting, the first time a command runs after a new one arrived;
/// None after that, and None where the policy is read-only (inside the sandbox, nobody there
/// can answer them).
let newlyWaiting () : Option<int> =
  if not (File.Exists(requestsPath ())) then
    None
  else
    match pending () with
    | [] -> None
    | waiting ->
      let newest = (List.rev waiting |> List.head |> Option.get).id
      let noticed =
        match LocalFile.read noticedFileName with
        | LocalFile.Read bytes -> Text.Encoding.UTF8.GetString bytes
        | _ -> ""
      if noticed = newest then
        None
      else
        try
          LocalFile.writeAtomic noticedFileName (Text.Encoding.UTF8.GetBytes newest)
          Some(List.length waiting)
        with _ ->
          None
