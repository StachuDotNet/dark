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


// ---------------------------------------------------------------------------
// Event sink — structured lifecycle events the CLI / HTML view consume.
// ---------------------------------------------------------------------------

/// State badges for the interactive HTML view (per H3 in 21-heavy-hitters).
type FnState =
  | InProgress   // currently materializing (LLM in flight)
  | Real         // materialized OK, body translated, executed (or executable)
  | Fake         // returned identity / EmptyBody fallback — body wasn't parsable
  | Cached       // hit pendingFnInstrCache; skipped LLM
  | Failed       // materialization errored or aborted


/// Discrete events emitted during a PDD run. Consumers translate these
/// into stderr lines, HTML updates, JSONL log lines, etc.
type PDDEvent =
  /// Materialization started for this Pending. Includes the model name
  /// so the UI can show "addOne ▸ gpt-4o-mini …".
  | MaterializeStart of name : string * model : string

  /// LLM call returned its body. `body` is the raw text from
  /// `choices[0].message.content` (may include code-fences etc.).
  | LLMResponse of name : string * elapsedMs : int * body : string

  /// `parseLLMResponse` succeeded — we have a clean (sig, body) pair.
  | ParseOk of name : string * sig_ : string * body : string

  /// Mini-parser ran on the body. `kind` is "constant" | "identity" |
  /// "arith-add" | "arith-sub" | "arith-mul" | "fallback-identity".
  | CompileBody of name : string * kind : string * registerCount : int

  /// Materialization finished. `state` summarizes the outcome.
  | MaterializeDone of name : string * state : FnState * elapsedMs : int

  /// Materialization failed for some specific reason.
  | MaterializeFailed of name : string * reason : string

/// Where events go. Default is no-op; the CLI installs a sink that
/// writes to stderr (colored) + appends to an HTML view file (H3).
type EventSink = PDDEvent -> unit

let nullSink : EventSink = fun _ -> ()

/// The currently-installed sink. Set by callers (CLI, tests) before
/// invoking `materialize`. Implemented as a mutable ref to avoid
/// plumbing through every signature; PDD is single-threaded today.
let mutable currentSink : EventSink = nullSink

let private emit (ev : PDDEvent) : unit =
  try
    currentSink ev
  with _ -> ()  // sink failures never propagate


/// The v5 system prompt — tightens v4's output format. The big change
/// over v4: explicit instruction that BOTH JSON values must be quoted
/// strings (v4 callers sometimes returned `"body": x * 2L` unquoted).
/// Keeps the v4 Darklang syntax rules.
let v4SystemPrompt = """You generate Darklang function bodies.

Your output is JSON. Both fields are QUOTED STRINGS.

  {"sig": "(x: Int64): Int64", "body": "x + 1L"}

NOT:
  {"sig": (x: Int64): Int64, "body": x + 1L}   ← unquoted = invalid JSON

The `body` value must be a valid JSON string: surrounded by double quotes, with any inner quotes escaped (\\"). Even if the body is a single identifier or arithmetic expression, wrap it in quotes.

Darklang syntax for the body content:
- Integers are SIZED: Int64 (default), Int8, Int32, etc. Never bare int. Literals end in L: 5L, -3L.
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
- Conditionals (single-line shape, NO parentheses around the cond):
    if x > 0L then x else 0L
    if x < y then x else y
    if a >= b then a else b
  Use `>`, `<`, `>=`, or `<=`. Operands must be a param name or Int64 literal.

Return ONLY the JSON object. No markdown fences, no prose."""


/// Build the user-side prompt for a given pending fn name. Day-1: only uses
/// the name. Day-N adds sig hints, surrounding-context, etc.
let buildUserPrompt (name : string) : string =
  sprintf "Function: %s. Description: implement a sensible default body." name


/// Fix-up prompt used when the mini-parser rejects the LLM's first body.
/// Restates the grammar explicitly and asks for a simpler equivalent body.
let buildFixUpPrompt (name : string) (sig_ : string) (failedBody : string) : string =
  sprintf
    "Function: %s
Your previous attempt was:
  sig: %s
  body: %s

Our mini-parser couldn't lower that body. The mini-parser ONLY accepts these body shapes:
- atom:        a parameter name (e.g. x, y)  OR  an Int64 literal (e.g. 42L, -7L)
- arith:       <atom> <op> <atom>  where op is +, -, *
- unary minus: -<param-name>  (e.g. -x)
- if-else:     if <expr> <cmp> <expr> then <expr> else <expr>
               where cmp is one of >, <, >=, <=
               and each <expr> is itself an atom, arith, or unary-minus.

Return JSON {\"sig\": \"%s\", \"body\": \"...\"} with a body that matches the grammar above. Keep the function's intended semantics if possible; otherwise, return the closest simple approximation."
    name
    sig_
    failedBody
    sig_


/// System prompt for the high-level "decompose a user request into a
/// Darklang expression" path. The output expression may reference fn
/// names the LLM invents; those names become Pending refs, materialized
/// by `materialize` at call time. This is the PDD blog-post pseudocode
/// shape (https://stachu.net/psuedocode-driven-development).
let decomposeSystemPrompt =
  """You decompose a user's request into a single Darklang expression that fulfills it.

The expression should read like the user's intent in pipeline form. Use Stdlib functions you know exist (Stdlib.List.map, Stdlib.Int64.add, etc.). For everything else — fetching URLs, parsing CSVs, computing variance, summarizing text, ANYTHING — just invent a name that describes what it does and use it. The runtime materializes those at call time via LLM.

Output ONLY the Darklang expression, no prose, no markdown fences, no comments.

Examples:

User: compute 2+3
Output: Stdlib.Int64.add 2L 3L

User: from a CSV, find the date with biggest variance between open and close
Output: csv |> parseCsv |> skipHeader |> Stdlib.List.map calculateVariance |> sortByVarianceDescending |> takeHead |> getDateField

User: fetch the top HN headline and sentiment-score it
Output: fetchUrl "https://news.ycombinator.com" |> extractTopHeadline |> sentimentScore

User: port haskell's `nub` to Dark for a List<Int64>
Output: fun lst -> nub lst

Darklang syntax:
- Int64 literals end in L: 5L, -3L
- Lists: [1L; 2L; 3L]
- Strings: double-quoted
- Pipe: value |> fn
- Function application is PREFIX, NOT parenthesized: f x y, not f(x, y)
- String concat: ++
"""

/// Build a user-side prompt for the decompose call. Just the request,
/// rendered straight.
let buildDecomposePrompt (request : string) : string =
  sprintf "User: %s\nOutput:" request


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
let parseMinimalBodyN (paramNames : List<string>) (body : string) : Option<RT.Instructions> =
  let trimmed = body.Trim()
  let opBuiltin (sym : string) : Option<string> =
    match sym with
    | "+" -> Some "int64Add"
    | "-" -> Some "int64Subtract"
    | "*" -> Some "int64Multiply"
    | _ -> None
  let argIndex (name : string) : Option<int> =
    paramNames |> List.tryFindIndex (fun p -> p = name)

  // Case 1a: <param> <op> <int>L — e.g. "x + 1L"
  let opLiteralMatch =
    System.Text.RegularExpressions.Regex.Match(
      trimmed,
      @"^(\w+)\s*([+\-*])\s*(-?\d+)L$"
    )

  // Case 1b: <param> <op> <param> — e.g. "x * y"
  let opTwoParamsMatch =
    System.Text.RegularExpressions.Regex.Match(
      trimmed,
      @"^(\w+)\s*([+\-*])\s*(\w+)$"
    )

  if opLiteralMatch.Success && (argIndex opLiteralMatch.Groups[1].Value).IsSome then
    match opBuiltin (opLiteralMatch.Groups[2].Value) with
    | Some builtinName ->
      let n = System.Int64.Parse(opLiteralMatch.Groups[3].Value)
      let argReg = (argIndex opLiteralMatch.Groups[1].Value).Value
      let arity = List.length paramNames
      // Registers:
      //   0..arity-1: args
      //   arity:     DApplicable wrapping the int64 builtin
      //   arity+1:   DInt64 n (the constant)
      //   arity+2:   result of Apply
      let appReg = arity
      let constReg = arity + 1
      let resultReg = arity + 2
      let builtinApp : RT.ApplicableNamedFn =
        { name = RT.FQFnName.fqBuiltin builtinName 0
          typeSymbolTable = Map.empty
          typeArgs = []
          argsSoFar = [] }
      let nargs : NEList<int> = NEList.ofList argReg [ constReg ]
      Some
        { registerCount = resultReg + 1
          instructions =
            [ RT.LoadVal(appReg, RT.DApplicable(RT.AppNamedFn builtinApp))
              RT.LoadVal(constReg, RT.DInt64 n)
              RT.Apply(resultReg, appReg, [], nargs) ]
          resultIn = resultReg }
    | None -> None

  elif opTwoParamsMatch.Success then
    let lhsIdx = argIndex opTwoParamsMatch.Groups[1].Value
    let rhsIdx = argIndex opTwoParamsMatch.Groups[3].Value
    match lhsIdx, rhsIdx, opBuiltin opTwoParamsMatch.Groups[2].Value with
    | Some lhs, Some rhs, Some builtinName ->
      let arity = List.length paramNames
      // Registers:
      //   0..arity-1: args
      //   arity:      DApplicable wrapping the int64 builtin
      //   arity+1:    result of Apply
      let appReg = arity
      let resultReg = arity + 1
      let builtinApp : RT.ApplicableNamedFn =
        { name = RT.FQFnName.fqBuiltin builtinName 0
          typeSymbolTable = Map.empty
          typeArgs = []
          argsSoFar = [] }
      let nargs : NEList<int> = NEList.ofList lhs [ rhs ]
      Some
        { registerCount = resultReg + 1
          instructions =
            [ RT.LoadVal(appReg, RT.DApplicable(RT.AppNamedFn builtinApp))
              RT.Apply(resultReg, appReg, [], nargs) ]
          resultIn = resultReg }
    | _ -> None

  // Case 2: Int64 literal — return constant regardless of args
  elif (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^-?\d+L$")) then
    let n = System.Int64.Parse(trimmed.TrimEnd('L'))
    let arity = List.length paramNames
    let constReg = arity
    Some
      { registerCount = constReg + 1
        instructions = [ RT.LoadVal(constReg, RT.DInt64 n) ]
        resultIn = constReg }

  // Case 3: identity — variable matches a param name
  elif (argIndex trimmed).IsSome then
    let idx = (argIndex trimmed).Value
    let arity = List.length paramNames
    Some
      { registerCount = max 1 arity
        instructions = []
        resultIn = idx }

  else
    // Compile a sub-expression (`atom` | `atom <op> atom`) returning
    // (resultReg, instructions, nextFreeReg).
    //   atom = param-name | -?\d+L
    //   op   = + | - | *
    let compileExpr (raw : string) (nextReg : int) =
      let raw = raw.Trim()
      let compileAtom (s : string) (nReg : int) =
        match argIndex s with
        | Some i -> Some(i, [], nReg)
        | None ->
          if System.Text.RegularExpressions.Regex.IsMatch(s, @"^-?\d+L$") then
            let n = System.Int64.Parse(s.TrimEnd('L'))
            Some(nReg, [ RT.LoadVal(nReg, RT.DInt64 n) ], nReg + 1)
          // Unary minus on a param: `-x` → Apply(int64Negate, [x])
          elif s.StartsWith "-" && (argIndex (s.Substring 1)).IsSome then
            let inner = (argIndex (s.Substring 1)).Value
            let appReg = nReg
            let outReg = nReg + 1
            let app : RT.ApplicableNamedFn =
              { name = RT.FQFnName.fqBuiltin "int64Negate" 0
                typeSymbolTable = Map.empty
                typeArgs = []
                argsSoFar = [] }
            let args : NEList<int> = NEList.ofList inner []
            Some(
              outReg,
              [ RT.LoadVal(appReg, RT.DApplicable(RT.AppNamedFn app))
                RT.Apply(outReg, appReg, [], args) ],
              outReg + 1
            )
          else
            None
      // Try atom-only first.
      match compileAtom raw nextReg with
      | Some r -> Some r
      | None ->
        let arithMatch =
          System.Text.RegularExpressions.Regex.Match(
            raw,
            @"^(\w+|-?\d+L)\s*([+\-*])\s*(\w+|-?\d+L)$"
          )
        if not arithMatch.Success then
          None
        else
          let lhsRaw = arithMatch.Groups[1].Value
          let opRaw = arithMatch.Groups[2].Value
          let rhsRaw = arithMatch.Groups[3].Value
          match opBuiltin opRaw with
          | None -> None
          | Some bname ->
            match compileAtom lhsRaw nextReg with
            | None -> None
            | Some(lReg, lIns, r1) ->
              match compileAtom rhsRaw r1 with
              | None -> None
              | Some(rReg, rIns, r2) ->
                let appReg = r2
                let outReg = r2 + 1
                let app : RT.ApplicableNamedFn =
                  { name = RT.FQFnName.fqBuiltin bname 0
                    typeSymbolTable = Map.empty
                    typeArgs = []
                    argsSoFar = [] }
                let args : NEList<int> = NEList.ofList lReg [ rReg ]
                Some(
                  outReg,
                  lIns
                  @ rIns
                  @ [ RT.LoadVal(appReg, RT.DApplicable(RT.AppNamedFn app))
                      RT.Apply(outReg, appReg, [], args) ],
                  outReg + 1
                )

    // Case 4: if <cond-lhs> <cmp> <cond-rhs> then <then-branch> else <else-branch>
    // where each sub-expr is `atom` or `atom <op> atom`.
    // Use non-greedy captures so multi-word arith subexprs fit.
    let ifMatch =
      System.Text.RegularExpressions.Regex.Match(
        trimmed,
        @"^if\s+(.+?)\s+(>=|<=|>|<)\s+(.+?)\s+then\s+(.+?)\s+else\s+(.+)$"
      )
    if not ifMatch.Success then
      None
    else
      let cmpBuiltin =
        match ifMatch.Groups[2].Value with
        | ">" -> Some "int64GreaterThan"
        | "<" -> Some "int64LessThan"
        | ">=" -> Some "int64GreaterThanOrEqualTo"
        | "<=" -> Some "int64LessThanOrEqualTo"
        | _ -> None
      match cmpBuiltin with
      | None -> None
      | Some bname ->
        let arity = List.length paramNames
        let resultReg = arity
        let r0 = arity + 1
        match compileExpr ifMatch.Groups[1].Value r0 with
        | None -> None
        | Some(lhsReg, lhsInstrs, r1) ->
          match compileExpr ifMatch.Groups[3].Value r1 with
          | None -> None
          | Some(rhsReg, rhsInstrs, r2) ->
            let cmpAppReg = r2
            let condReg = r2 + 1
            match compileExpr ifMatch.Groups[4].Value (r2 + 2) with
            | None -> None
            | Some(thenReg, thenInstrs, r3) ->
              match compileExpr ifMatch.Groups[5].Value r3 with
              | None -> None
              | Some(elseReg, elseInstrs, r4) ->
                let cmpApp : RT.ApplicableNamedFn =
                  { name = RT.FQFnName.fqBuiltin bname 0
                    typeSymbolTable = Map.empty
                    typeArgs = []
                    argsSoFar = [] }
                let cmpArgs : NEList<int> = NEList.ofList lhsReg [ rhsReg ]
                let copyThen = [ RT.CopyVal(resultReg, thenReg) ]
                let copyElse = [ RT.CopyVal(resultReg, elseReg) ]
                let thenBlock = thenInstrs @ copyThen
                let elseBlock = elseInstrs @ copyElse
                let instructions =
                  lhsInstrs
                  @ rhsInstrs
                  @ [ RT.LoadVal(cmpAppReg, RT.DApplicable(RT.AppNamedFn cmpApp))
                      RT.Apply(condReg, cmpAppReg, [], cmpArgs)
                      RT.JumpByIfFalse(List.length thenBlock + 1, condReg) ]
                  @ thenBlock
                  @ [ RT.JumpBy(List.length elseBlock) ]
                  @ elseBlock
                Some
                  { registerCount = r4
                    instructions = instructions
                    resultIn = resultReg }


/// One-param convenience wrapper.
let parseMinimalBody (paramName : string) (body : string) : Option<RT.Instructions> =
  parseMinimalBodyN [ paramName ] body


/// Extract parameter names from a sig string like
///   "(x: Int64): Int64"          → ["x"]
///   "(x: Int64, y: Int64): Int64" → ["x"; "y"]
/// Returns None if it can't make sense of the format.
let parseParamNames (sig_ : string) : Option<List<string>> =
  let m = System.Text.RegularExpressions.Regex.Match(sig_, @"^\(([^)]*)\)")
  if not m.Success then None
  else
    let inside = m.Groups[1].Value.Trim()
    if inside = "" then Some []
    else
      let parts = inside.Split(',') |> Array.map (fun s -> s.Trim())
      let names =
        parts
        |> Array.map (fun p ->
          // Split on `:` to get "name: type" → just "name"
          let nameAndType = p.Split(':') |> Array.map (fun s -> s.Trim())
          if nameAndType.Length >= 1 then nameAndType[0] else "")
        |> Array.toList
      if names |> List.forall (fun n -> n <> "") then Some names else None


/// Build a PackageFn around a parsed body. All params are Int64; return
/// type Int64. Hash is deterministic from name+body so retries hit cache.
let private fnFromBodyN
  (name : string)
  (paramNames : List<string>)
  (instructions : RT.Instructions)
  : RT.PackageFn.PackageFn =
  let hashKey = sprintf "pdd-llm-%s-%d" name (hash instructions.instructions)
  let mkParam (n : string) (t : RT.TypeReference) : RT.PackageFn.Parameter =
    { name = n; typ = t }
  let parameters =
    match paramNames with
    | [] ->
      // Zero-arg fns aren't supported by NEList; pretend it's one Unit
      // arg. Real zero-arg fns wouldn't hit the mini-parser anyway.
      NEList.singleton (mkParam "_unit" RT.TUnit)
    | head :: tail ->
      NEList.ofList
        (mkParam head RT.TInt64)
        (tail |> List.map (fun n -> mkParam n RT.TInt64))
  { hash = RT.Hash hashKey
    typeParams = []
    parameters = parameters
    returnType = RT.TInt64
    body = instructions }


/// One-param convenience wrapper.
let private fnFromBody
  (name : string)
  (paramName : string)
  (instructions : RT.Instructions)
  : RT.PackageFn.PackageFn =
  fnFromBodyN name [ paramName ] instructions


// ---------------------------------------------------------------------------
// Promotion cache — H4
//
// On successful materialization we persist {name, sig, body} to a JSONL
// file. On lookup we check this cache before calling the LLM. Re-runs
// of the same prompt are then ~free (no API call, no waiting).
//
// Format (one line per cached fn):
//   {"name": "addOne", "sig": "(x: Int64): Int64", "body": "x + 1L"}
//
// File: rundir/pdd-cache/promoted.jsonl
//
// This is the spike's lightweight promotion. A real production version
// inserts into the package store with a content-addressed hash + branch
// awareness; that's deferred.
// ---------------------------------------------------------------------------

let private cachePath = "rundir/pdd-cache/promoted.jsonl"
let private decomposeCachePath = "rundir/pdd-cache/decomposed.jsonl"

/// In-memory mirror of the cache file. Loaded lazily on first lookup;
/// updated on every successful materialization.
let private promotedCache : System.Collections.Concurrent.ConcurrentDictionary<string, GeneratedFn> =
  System.Collections.Concurrent.ConcurrentDictionary()

let private cacheLoaded : bool ref = ref false

let private loadCacheOnce () : unit =
  if not cacheLoaded.Value then
    cacheLoaded.Value <- true
    try
      if System.IO.File.Exists cachePath then
        let lines = System.IO.File.ReadAllLines cachePath
        for line in lines do
          if not (System.String.IsNullOrWhiteSpace line) then
            try
              let doc = JsonDocument.Parse(line)
              let r = doc.RootElement
              let name = r.GetProperty("name").GetString()
              // `sig` is a reserved word in F#, so the serializer field
              // is `sig_`. Read it back under the same name.
              let sig_ = r.GetProperty("sig_").GetString()
              let body = r.GetProperty("body").GetString()
              promotedCache[name] <- { sig_ = sig_; body = body }
            with _ -> ()
    with _ -> ()

let tryLookupPromoted (name : string) : GeneratedFn option =
  loadCacheOnce ()
  match promotedCache.TryGetValue name with
  | true, gen -> Some gen
  | false, _ -> None

let private persistPromoted (name : string) (gen : GeneratedFn) : unit =
  try
    let dir = System.IO.Path.GetDirectoryName cachePath
    if not (System.IO.Directory.Exists dir) then
      System.IO.Directory.CreateDirectory dir |> ignore<System.IO.DirectoryInfo>
    let payload =
      JsonSerializer.Serialize(
        {| name = name; sig_ = gen.sig_; body = gen.body |}
      )
    System.IO.File.AppendAllText(cachePath, payload + "\n")
    promotedCache[name] <- gen
  with _ -> ()


// Decompose cache — maps a free-text prompt to the Dark expression the
// LLM produced last time. Hit means we skip the decompose LLM call entirely.

let private decomposeCache : System.Collections.Concurrent.ConcurrentDictionary<string, string> =
  System.Collections.Concurrent.ConcurrentDictionary()

let private decomposeCacheLoaded : bool ref = ref false

let private loadDecomposeCacheOnce () : unit =
  if not decomposeCacheLoaded.Value then
    decomposeCacheLoaded.Value <- true
    try
      if System.IO.File.Exists decomposeCachePath then
        let lines = System.IO.File.ReadAllLines decomposeCachePath
        for line in lines do
          if not (System.String.IsNullOrWhiteSpace line) then
            try
              let doc = JsonDocument.Parse(line)
              let r = doc.RootElement
              let prompt = r.GetProperty("prompt").GetString()
              let expr = r.GetProperty("expr").GetString()
              decomposeCache[prompt] <- expr
            with _ -> ()
    with _ -> ()

let tryLookupDecomposed (prompt : string) : string option =
  loadDecomposeCacheOnce ()
  match decomposeCache.TryGetValue(prompt.Trim()) with
  | true, expr -> Some expr
  | false, _ -> None

let persistDecomposed (prompt : string) (expr : string) : unit =
  try
    let dir = System.IO.Path.GetDirectoryName decomposeCachePath
    if not (System.IO.Directory.Exists dir) then
      System.IO.Directory.CreateDirectory dir |> ignore<System.IO.DirectoryInfo>
    let payload =
      JsonSerializer.Serialize({| prompt = prompt.Trim(); expr = expr |})
    System.IO.File.AppendAllText(decomposeCachePath, payload + "\n")
    decomposeCache[prompt.Trim()] <- expr
  with _ -> ()


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
    let startedAt = DateTime.UtcNow
    let model = "gpt-4o-mini"
    emit (MaterializeStart(p.name, model))
    let elapsedMs () = int (DateTime.UtcNow - startedAt).TotalMilliseconds

    // H4: check promoted cache first. If we've materialized this name
    // before AND the body still parses with the mini-parser, return the
    // cached PackageFn without calling the LLM.
    let cachedHit =
      match tryLookupPromoted p.name with
      | Some gen ->
        match parseMinimalBody "x" gen.body with
        | Some instrs ->
          emit (CompileBody(p.name, "cached", instrs.registerCount))
          emit (MaterializeDone(p.name, Cached, elapsedMs ()))
          Some(fnFromBody p.name "x" instrs)
        | None -> None
      | None -> None
    if cachedHit.IsSome then
      return cachedHit
    else

    let apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    if String.IsNullOrEmpty(apiKey) then
      logResult logPath p.name None None (Some "OPENAI_API_KEY not set")
      emit (MaterializeFailed(p.name, "OPENAI_API_KEY not set"))
      return None
    else
      // Round-trip with the LLM. On parse failure we retry once with a
      // grammar-explicit fix-up prompt before falling back to identity.
      // Cap: 2 LLM calls total per materialization.
      let maxAttempts = 2
      let rec attempt (n : int) (prompt : string) =
        uply {
          let! resp =
            callOpenAI apiKey v4SystemPrompt prompt
            |> Async.AwaitTask
            |> Async.StartAsTask
          match resp with
          | Error e ->
            logResult logPath p.name None None (Some e)
            emit (MaterializeFailed(p.name, e))
            return None
          | Ok body ->
            emit (LLMResponse(p.name, elapsedMs (), body))
            match extractContent body with
            | Error e ->
              logResult logPath p.name None None (Some e)
              emit (MaterializeFailed(p.name, e))
              return None
            | Ok content ->
              match parseLLMResponse content with
              | Error e ->
                logResult logPath p.name None (Some content) (Some e)
                emit (MaterializeFailed(p.name, e))
                return None
              | Ok gen ->
                logResult logPath p.name (Some gen.sig_) (Some gen.body) None
                emit (ParseOk(p.name, gen.sig_, gen.body))
                let paramNames =
                  parseParamNames gen.sig_
                  |> Option.defaultValue [ "x" ]
                match parseMinimalBodyN paramNames gen.body with
                | Some instrs ->
                  let kind =
                    if instrs.instructions.Length = 0 then "identity"
                    elif instrs.instructions.Length = 1 then "constant"
                    else "arith"
                  let labelledKind =
                    if n > 1 then sprintf "%s (retry %d)" kind (n - 1) else kind
                  emit (CompileBody(p.name, labelledKind, instrs.registerCount))
                  emit (MaterializeDone(p.name, Real, elapsedMs ()))
                  persistPromoted p.name gen
                  return Some(fnFromBodyN p.name paramNames instrs)
                | None when n < maxAttempts ->
                  // Mini-parser refused: feed the failed body back to the
                  // LLM with an explicit grammar and ask for a simpler body.
                  emit (CompileBody(p.name, sprintf "retry-%d" n, 0))
                  let fixUp = buildFixUpPrompt p.name gen.sig_ gen.body
                  return! attempt (n + 1) fixUp
                | None ->
                  emit (CompileBody(p.name, "fallback-identity", 1))
                  emit (MaterializeDone(p.name, Fake, elapsedMs ()))
                  return Some(hardcodedIdentityFn p.name)
        }
      let initialPrompt = buildUserPrompt p.name
      return! attempt 1 initialPrompt
  }
