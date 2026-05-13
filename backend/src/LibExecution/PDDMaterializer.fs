/// PDD materializer — turns a Pending fn reference into a concrete
/// PackageFn by calling an LLM (gpt-4o-mini for the spike) and (eventually)
/// translating the body into Dark PT/RT.
///
/// Day-2b scope:
/// - Real HTTP call to OpenAI's chat completions endpoint.
/// - Parse the JSON `{sig, body}` response.
/// - Log the raw response so a human can inspect what the LLM said.
/// - **Return a hardcoded identity-shaped PackageFn** regardless of the body
///   string. Translating the body string into real RT instructions is
///   deferred until LibParser support or structured-output JSON is wired.
///
/// The key takeaway: this proves end-to-end *the wire works*. The runtime
/// can call code it didn't have, materialize it via the LLM, and execute
/// something useful. The "something useful" is currently identity, but the
/// LLM's actual answer is captured.
module LibExecution.PDDMaterializer

open System
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
module RT = LibExecution.RuntimeTypes


/// The v4 system prompt — verified empirically during the design loop.
/// See pdd-thinking/16-prompt-shapes.md and pdd-thinking/17-day-1-quick-reference.md.
let v4SystemPrompt = """You generate Darklang function bodies. Reply with ONLY a JSON object {"sig": "(<params>): <ReturnType>", "body": "<Darklang expression>"}.

Darklang syntax notes:
- Integers are SIZED: Int64 (default), Int8, Int32, etc. Never bare int.
- Generics use ANGLE BRACKETS: List<Int64>, Option<String>. Not List(...).
- Type variables use ML-style apostrophes: 'a, 'b, 'k, 'v. Not <a> or <b>.
- Bindings: let x = expr in rest_of_expr (in is required).
- Lambdas: fun x -> body. NEVER use =>.
- Lists: [1L; 2L; 3L] (semicolons).
- Pipe: value |> fn. Parens for complex: (complex expr) |> fn.
- String concat: ++.
- Stdlib: prefix with Stdlib. (e.g. Stdlib.List.map, Stdlib.Int64.add).
- Function application is PREFIX, NOT parenthesized:
    Stdlib.List.map f lst       (correct)
    Stdlib.List.map(f, lst)     (WRONG)
- Records: Type { a = 1L; b = 2L }.
- Recursion: call the function by its short name inside the body.
- Prefer the simplest possible implementation.

Return ONLY the JSON object. No markdown fences, no prose."""


/// Build the user-side prompt for a given pending fn name. Day-1: only uses
/// the name. Day-N adds sig hints, surrounding-context, etc.
let buildUserPrompt (name : string) : string =
  sprintf "Function: %s. Description: implement a sensible default body." name


/// Parsed shape of the LLM's JSON response.
type GeneratedFn = { sig_ : string; body : string }


/// Parse the model's response (the `content` string from OpenAI's chat
/// completion). Returns Error with a reason on malformed input.
let parseLLMResponse (raw : string) : Result<GeneratedFn, string> =
  try
    let trimmed = raw.Trim()
    // Some models wrap their output in ```json ... ``` despite instructions
    // saying not to. Be tolerant.
    let stripped =
      if trimmed.StartsWith("```") then
        let after = trimmed.Substring(3)
        // Drop optional `json` language tag
        let withoutLang =
          if after.StartsWith("json") then after.Substring(4) else after
        let endIdx = withoutLang.LastIndexOf("```")
        if endIdx >= 0 then withoutLang.Substring(0, endIdx).Trim()
        else withoutLang.Trim()
      else
        trimmed
    let doc = JsonDocument.Parse(stripped)
    let root = doc.RootElement
    let getString (name : string) : Result<string, string> =
      let mutable v = Unchecked.defaultof<JsonElement>
      if root.TryGetProperty(name, &v) then
        if v.ValueKind = JsonValueKind.String then Ok(v.GetString())
        else Error(sprintf "field '%s' is not a string" name)
      else
        Error(sprintf "missing field '%s'" name)
    match getString "sig", getString "body" with
    | Ok s, Ok b -> Ok { sig_ = s; body = b }
    | Error e, _
    | _, Error e -> Error e
  with ex -> Error(sprintf "JSON parse failed: %s" ex.Message)


/// Shared HttpClient. A single instance is recommended over creating one
/// per call to avoid socket exhaustion.
let private httpClient : Lazy<HttpClient> =
  lazy
    let h = new HttpClient()
    h.Timeout <- TimeSpan.FromSeconds(15.0)
    h


/// The raw LLM call. Returns the JSON body string (or Error). API key is
/// read from OPENAI_API_KEY env each call so a test can rotate it.
let callOpenAI
  (apiKey : string)
  (systemPrompt : string)
  (userPrompt : string)
  : Task<Result<string, string>> =
  task {
    try
      let payload =
        JsonSerializer.Serialize(
          {| model = "gpt-4o-mini"
             messages =
               [| {| role = "system"; content = systemPrompt |}
                  {| role = "user"; content = userPrompt |} |]
             max_tokens = 800
             temperature = 0 |}
        )
      let req =
        new HttpRequestMessage(
          HttpMethod.Post,
          "https://api.openai.com/v1/chat/completions"
        )
      req.Headers.Authorization <-
        new Headers.AuthenticationHeaderValue("Bearer", apiKey)
      req.Content <-
        new StringContent(payload, Encoding.UTF8, "application/json")
      let! resp = httpClient.Value.SendAsync(req)
      let! body = resp.Content.ReadAsStringAsync()
      if resp.IsSuccessStatusCode then
        return Ok body
      else
        return
          Error(sprintf "HTTP %d: %s" (int resp.StatusCode) body)
    with ex -> return Error(sprintf "request failed: %s" ex.Message)
  }


/// Pull the `content` string out of a successful chat-completion response.
let extractContent (responseBody : string) : Result<string, string> =
  try
    let doc = JsonDocument.Parse(responseBody)
    let choices = doc.RootElement.GetProperty("choices")
    if choices.GetArrayLength() = 0 then
      Error "no choices in response"
    else
      let first = choices[0]
      let message = first.GetProperty("message")
      let content = message.GetProperty("content").GetString()
      Ok content
  with ex -> Error(sprintf "extract content failed: %s" ex.Message)


/// Append a JSONL log entry for a materialization attempt. Each line:
/// {"t": iso-time, "name": "...", "result": "ok"|"err", "sig": "...",
///  "body": "...", "error": "..."}
let private logResult
  (logPath : string)
  (name : string)
  (sig_ : string option)
  (body : string option)
  (error : string option)
  : unit =
  try
    let payload =
      JsonSerializer.Serialize(
        {| t = DateTime.UtcNow.ToString("O")
           name = name
           result = if error.IsSome then "err" else "ok"
           sig_ = Option.defaultValue "" sig_
           body = Option.defaultValue "" body
           error = Option.defaultValue "" error |}
      )
    let dir = System.IO.Path.GetDirectoryName(logPath)
    if not (String.IsNullOrEmpty(dir)) && not (System.IO.Directory.Exists(dir)) then
      System.IO.Directory.CreateDirectory(dir) |> ignore<System.IO.DirectoryInfo>
    System.IO.File.AppendAllText(logPath, payload + "\n")
  with _ -> () // logging failures don't propagate


/// Day-2b identity-shaped PackageFn. Body is empty; resultIn=0 (the arg
/// register) → returns its single Int64 arg unchanged.
let private hardcodedIdentityFn (name : string) : RT.PackageFn.PackageFn =
  { hash = RT.Hash(sprintf "pdd-llm-identity-%s" name)
    typeParams = []
    parameters =
      NEList.singleton { RT.PackageFn.name = "x"; typ = RT.TInt64 }
    returnType = RT.TInt64
    body =
      { registerCount = 1
        instructions = []
        resultIn = 0 } }


/// A tiny parser for LLM body strings — handles the simplest two cases so
/// we can stand up a real "LLM controls the runtime value" demo. Both
/// kinds of body produce a one-instruction PackageFn that takes a single
/// Int64 arg.
///
/// Cases handled:
///   "42L"     → constant: returns DInt64 42 regardless of arg
///   "-7L"     → constant: returns DInt64 (-7)
///   "x"       → identity: returns the arg unchanged (assumes param name "x")
///   <other>   → returns None (caller falls back to hardcodedIdentityFn)
let parseMinimalBody (paramName : string) (body : string) : Option<RT.Instructions> =
  let trimmed = body.Trim()
  // Int64 literal: optional minus, digits, trailing 'L'
  let m = System.Text.RegularExpressions.Regex.Match(trimmed, "^(-?\d+)L$")
  if m.Success then
    let n = System.Int64.Parse(m.Groups[1].Value)
    Some
      { registerCount = 2
        // arg goes into register 0 (interpreter places args first); load
        // the constant into register 1 and return that.
        instructions = [ RT.LoadVal(1, RT.DInt64 n) ]
        resultIn = 1 }
  elif trimmed = paramName then
    // Identity: return arg unchanged. Body has no instructions; resultIn
    // points at the arg register.
    Some
      { registerCount = 1
        instructions = []
        resultIn = 0 }
  else
    None


/// Build a PackageFn around a parsed body. Single Int64 parameter, Int64
/// return type. Hash is deterministic from name+body so retries hit cache.
let private fnFromBody
  (name : string)
  (paramName : string)
  (instructions : RT.Instructions)
  : RT.PackageFn.PackageFn =
  let hashKey = sprintf "pdd-llm-%s-%d" name (hash instructions.instructions)
  { hash = RT.Hash hashKey
    typeParams = []
    parameters =
      NEList.singleton { RT.PackageFn.name = paramName; typ = RT.TInt64 }
    returnType = RT.TInt64
    body = instructions }


/// The materializer entry point — plug into PackageManager.materializeFn or
/// ExecutionState.fns.materialize. Reads OPENAI_API_KEY from env; if not
/// set, returns None immediately.
///
/// Behavior:
///  - Builds the v4 prompt + asks for gpt-4o-mini's body for the pending name.
///  - Logs the raw + parsed response to rundir/logs/pdd-materialize.jsonl.
///  - Returns Some <hardcoded identity PackageFn> on success (the LLM's
///    actual body is logged but not yet translated to RT). On failure
///    returns None.
let materialize (p : RT.FQFnName.Pending) : Ply<Option<RT.PackageFn.PackageFn>> =
  uply {
    let logPath = "rundir/logs/pdd-materialize.jsonl"
    let apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    if String.IsNullOrEmpty(apiKey) then
      logResult logPath p.name None None (Some "OPENAI_API_KEY not set")
      return None
    else
      let userPrompt = buildUserPrompt p.name
      let! resp =
        callOpenAI apiKey v4SystemPrompt userPrompt
        |> Async.AwaitTask
        |> Async.StartAsTask
      match resp with
      | Error e ->
        logResult logPath p.name None None (Some e)
        return None
      | Ok body ->
        match extractContent body with
        | Error e ->
          logResult logPath p.name None None (Some e)
          return None
        | Ok content ->
          match parseLLMResponse content with
          | Error e ->
            logResult logPath p.name None (Some content) (Some e)
            return None
          | Ok gen ->
            logResult logPath p.name (Some gen.sig_) (Some gen.body) None
            // Try the minimal-body parser first; if it recognizes the shape
            // we build a PackageFn that actually executes the LLM's intent.
            // Otherwise fall back to identity-shape (the LLM body is still
            // logged for inspection).
            match parseMinimalBody "x" gen.body with
            | Some instrs -> return Some(fnFromBody p.name "x" instrs)
            | None -> return Some(hardcodedIdentityFn p.name)
  }
