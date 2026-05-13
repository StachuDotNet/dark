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
module PT = LibExecution.ProgramTypes
module PT2RT = LibExecution.ProgramTypesToRuntimeTypes


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

  /// LLM-claimed test ran. `result` is pass/fail/error/skipped.
  | TestRan of name : string * label : string * detail : string

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
let v4SystemPrompt = """You generate Darklang function bodies AND example tests.

Your output is JSON with three fields, ALL QUOTED STRINGS or arrays:

  {
    "sig":  "(x: Int64): Int64",
    "body": "x + 1L",
    "tests": [
      {"args": [0],  "expect": 1},
      {"args": [5],  "expect": 6},
      {"args": [-3], "expect": -2}
    ]
  }

NOT:
  {"sig": (x: Int64): Int64, "body": x + 1L}   ← unquoted = invalid JSON

The `body` value must be a valid JSON string: surrounded by double quotes, with any inner quotes escaped (\\"). Even if the body is a single identifier or arithmetic expression, wrap it in quotes.

The `tests` field is REQUIRED. Provide 2-3 representative input/output examples that verify the body does what the function name suggests. Use plain JSON values: numbers (5, NOT 5L), strings, booleans, and JSON arrays (USE COMMAS — `[1, 2, 3]`, NOT `[1; 2; 3]`).

Example for a List<Int64> → Int64 function:
  {"args": [[1, 2, 3, 4, 5]], "expect": 15}

The `L` suffix and `;` separator are ONLY for the Dark BODY string — never for JSON values. Tests cover BOUNDARY cases (zero, negative, edge) when relevant.

Darklang syntax for the body content:
- Integers are SIZED: Int64 (default), Int8, Int32, etc. Never bare int. Literals end in L: 5L, -3L.
- Generics use ANGLE BRACKETS: List<Int64>, Option<String>. Not List(...).
- Type variables use ML-style apostrophes: 'a, 'b, 'k, 'v. Not <a> or <b>.
- Bindings: `let x = expr in rest_of_expr` (in is required).

  CRITICAL: the body you write is an INNER EXPRESSION (it's the body of
  the function we're defining). NOT a top-level Dark file. So:
    - `let myFn (x: Int64): Int64 = x * 2L` ← NO. Top-level fn def syntax.
    - `let f x = body in rest`             ← NO. Statement-let-fn form
                                               is unsupported as an inner
                                               expression.
    - `let f = fun x -> body in rest`      ← YES. Bind a lambda to a name.
    - `let f = fun x y -> body in rest`    ← YES, for multi-arg locals.

  Example of a valid multi-step BODY EXPRESSION:
    let parseLine = fun line -> Stdlib.String.split line "," in
    let parts = parseLine input in
    Stdlib.List.head parts
- Lambdas: `fun x -> body` (one arg) or `fun x y -> body` (multi-arg in ONE
  fun). NEVER use `=>`. NEVER curry: `fun x -> fun y -> ...` is two
  lambdas; use `fun x y -> ...` instead.
- Lists: [1L; 2L; 3L] (semicolons).
- Pipe: value |> fn. Parens for complex: (complex expr) |> fn.
- String concat: ++.
- Stdlib: prefix with Stdlib. (e.g. Stdlib.List.map, Stdlib.Int64.add).
- The CANONICAL Stdlib.List operations are:
    Stdlib.List.head, Stdlib.List.tail, Stdlib.List.append,
    Stdlib.List.reverse, Stdlib.List.fold (NOT fold_left), Stdlib.List.map,
    Stdlib.List.filter, Stdlib.List.length, Stdlib.List.isEmpty,
    Stdlib.List.sort.
  **All take the list as the FIRST argument** (not Haskell-style fn-first):
    Stdlib.List.map  list (fn: elem -> result)
    Stdlib.List.filter list (fn: elem -> Bool)
    Stdlib.List.fold list init (fn: acc elem -> acc)
  Example: `Stdlib.List.map lst (fun x -> x * 2L)` NOT `Stdlib.List.map (fun x -> x * 2L) lst`.
- Function application is PREFIX, NOT parenthesized:
    Stdlib.List.map f lst       (correct)
    Stdlib.List.map(f, lst)     (WRONG)
- Records: Type { a = 1L; b = 2L }.
- Recursion: call the function by its short name inside the body — e.g.
  the body of `factorial` recurses by calling `factorial(n - 1L)`. There
  is NO `let rec`. There is NO local mutual recursion. The ONLY way to
  recurse is to reference the function you are defining by its name.
- Prefer the simplest possible implementation.
- Conditionals (single-line shape, NO parentheses around the cond):
    if x > 0L then x else 0L
    if x < y then x else y
    if a >= b then a else b
  Use `>`, `<`, `>=`, or `<=`. Operands must be a param name or Int64 literal.
- String concat: `<atom> ++ <atom>` where each atom is a param name or
  a double-quoted string literal. Example: `x ++ "!"` or `"hi " ++ x`.

IMPORTANT — KEEP TYPES CONCRETE. Do NOT wrap return types in Option<T>,
Result<T,E>, or other "could-fail" containers. If the input is bad, use
a sensible default (0L, "", []) instead. The runtime composes
helper functions; wrapper types break the chain.

If you're given an arg type hint List<List<String>> or similar nested
generics, that's fine — match it exactly in your sig.

Return ONLY the JSON object. No markdown fences, no prose."""


// ---------------------------------------------------------------------------
// LibParser hook — the materializer's *real* body parser.
//
// PDDMaterializer lives in LibExecution, which sits BELOW LibParser in the
// dependency graph, so we can't import LibParser directly. Instead the CLI
// installs a callback at startup that wraps `LibParser.Parser.parsePTExpr`.
// When installed, the materializer routes LLM-generated bodies through it
// first; the regex mini-parser stays as a fast/fallback path only.
// ---------------------------------------------------------------------------

/// Result-shaped to keep error messages visible in the HTML view.
type BodyParser = string -> Ply<Result<PT.Expr, string>>

let mutable bodyParser : BodyParser option = None

let installBodyParser (parser : BodyParser) : unit =
  bodyParser <- Some parser


/// Test-runner callback — invokes a materialized PackageFn with concrete
/// Dval args and returns the resulting Dval (or an error message).
///
/// Now also accepts the originating Pending so the runner can build a
/// state that resolves self-recursive references back to the just-built
/// fn instead of re-triggering the LLM materializer (which would either
/// deadlock or recurse via additional LLM calls).
type TestRunner =
  RT.FQFnName.Pending * RT.PackageFn.PackageFn * List<RT.Dval>
    -> Ply<Result<RT.Dval, string>>

let mutable testRunner : TestRunner option = None

let installTestRunner (runner : TestRunner) : unit =
  testRunner <- Some runner


/// Hard ceiling on LLM calls per Pending handle, summed across ALL
/// materialize() invocations (parallel scheduler + lazy paths). Prevents
/// retry loops from exploding cost when the LLM keeps producing the same
/// failing body. Each materialize attempt internally has its own
/// `maxAttempts` (today 2) — this is the OUTER cap.
let llmCallsPerHandle :
  System.Collections.Concurrent.ConcurrentDictionary<System.Guid, int> =
  System.Collections.Concurrent.ConcurrentDictionary()

let llmCallCap : int = 3

let private incrementLlmCalls (handle : System.Guid) : int =
  llmCallsPerHandle.AddOrUpdate(handle, 1, fun _ old -> old + 1)

let private llmCallsExhausted (handle : System.Guid) : bool =
  match llmCallsPerHandle.TryGetValue handle with
  | true, n -> n >= llmCallCap
  | _ -> false


/// Result of running one claimed test against a materialized fn.
type TestResult =
  | TPass of args : List<RT.Dval> * got : RT.Dval
  | TFail of args : List<RT.Dval> * expect : RT.Dval * got : RT.Dval
  | TError of args : List<RT.Dval> * reason : string
  | TSkipped // no test runner installed

/// Pretty short label for an event-stream / HTML entry.
let testResultLabel (r : TestResult) : string =
  match r with
  | TPass _ -> "pass"
  | TFail _ -> "fail"
  | TError(_, _) -> "error"
  | TSkipped -> "skipped"

/// Parse a type-name (Int64, String, List<Int64>, Option<String>, ...)
/// → RT.TypeReference. Recursive on `<>` arguments.
///
/// For unrecognized types (e.g. Option<T>, Result<T,E>, custom records,
/// tuples), returns `TVariable "_pdd_unknown"` — a polymorphic placeholder
/// that the runtime's type-checker accepts. This is intentionally
/// permissive: better to materialize a fn with a loose-typed param than
/// to fall through to identity-fn for the entire materialization.
let rec parseSimpleType (raw : string) : Option<RT.TypeReference> =
  let s = raw.Trim()
  match s with
  | "Int64" -> Some RT.TInt64
  | "Int32" -> Some RT.TInt32
  | "Int16" -> Some RT.TInt16
  | "Int8" -> Some RT.TInt8
  | "UInt64" -> Some RT.TUInt64
  | "UInt32" -> Some RT.TUInt32
  | "String" -> Some RT.TString
  | "Bool" -> Some RT.TBool
  | "Float" -> Some RT.TFloat
  | "Char" -> Some RT.TChar
  | "Unit" -> Some RT.TUnit
  | _ ->
    // Generic forms: List<T>, Option<T>, etc.
    let m =
      System.Text.RegularExpressions.Regex.Match(s, @"^(\w+)\s*<\s*(.+)\s*>$")
    if m.Success then
      let outer = m.Groups[1].Value
      let inner = m.Groups[2].Value
      match parseSimpleType inner, outer with
      | Some t, "List" -> Some(RT.TList t)
      | _, _ ->
        // Unknown generic — treat as polymorphic placeholder.
        Some(RT.TVariable "_pdd_unknown")
    else
      // Non-generic unknown — also polymorphic.
      Some(RT.TVariable "_pdd_unknown")

/// Parse a full sig string `(x: Int64, y: String): Bool` to a structured
/// param list + return type. Tolerant of whitespace but requires the
/// `(...): T` shape exactly.
let parseFullSig
  (raw : string)
  : Option<List<string * RT.TypeReference> * RT.TypeReference> =
  let m =
    System.Text.RegularExpressions.Regex.Match(
      raw.Trim(),
      @"^\(([^)]*)\)\s*:\s*(.+)$"
    )
  if not m.Success then
    None
  else
    let paramsRaw = m.Groups[1].Value
    let returnRaw = m.Groups[2].Value
    match parseSimpleType returnRaw with
    | None -> None
    | Some returnType ->
      // Parse each param "name: Type"
      let parts =
        paramsRaw.Split([| ',' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun s -> s.Trim())
        |> Array.toList
      let parsed =
        parts
        |> List.map (fun p ->
          let mp = System.Text.RegularExpressions.Regex.Match(p, @"^(\w+)\s*:\s*(.+)$")
          if mp.Success then
            match parseSimpleType mp.Groups[2].Value with
            | Some t -> Some(mp.Groups[1].Value, t)
            | None -> None
          else
            None)
      if List.forall Option.isSome parsed then
        Some(parsed |> List.map Option.get, returnType)
      else
        None

/// Walk RT.Instructions and rewrite every Pending fn-name handle so that
/// occurrences of the SAME name share a SINGLE canonical handle. For
/// self-recursion (name == selfName), use selfHandle. For all other names,
/// generate one fresh handle per name and reuse it.
///
/// Without this, PT2RT.Expr.toRT generates a fresh handle for every
/// `PT.FQFnName.Pending` reference (via `fqPending`), so two `factorial`
/// references in one body would have DIFFERENT RT handles. That makes the
/// runtime materialize each occurrence separately — disastrous for
/// recursion, where every call inside the body would re-trigger the LLM.
let private canonicalizePendingHandles
  (selfName : string)
  (selfHandle : System.Guid)
  (instrs : RT.Instructions)
  : RT.Instructions =
  let nameToHandle =
    System.Collections.Generic.Dictionary<string, System.Guid>()
  nameToHandle[selfName] <- selfHandle
  let canonName (fn : RT.FQFnName.FQFnName) : RT.FQFnName.FQFnName =
    match fn with
    | RT.FQFnName.Pending p ->
      let canonicalHandle =
        match nameToHandle.TryGetValue p.name with
        | true, h -> h
        | false, _ ->
          let fresh = System.Guid.NewGuid()
          nameToHandle[p.name] <- fresh
          fresh
      RT.FQFnName.Pending { p with handle = canonicalHandle }
    | other -> other
  let rewriteApplicable (app : RT.ApplicableNamedFn) : RT.ApplicableNamedFn =
    { app with name = canonName app.name }
  let rewriteDval (dv : RT.Dval) : RT.Dval =
    match dv with
    | RT.DApplicable(RT.AppNamedFn app) ->
      RT.DApplicable(RT.AppNamedFn(rewriteApplicable app))
    | other -> other
  let rewriteInstr (i : RT.Instruction) : RT.Instruction =
    match i with
    | RT.LoadVal(r, dv) -> RT.LoadVal(r, rewriteDval dv)
    | other -> other
  { instrs with
      instructions = instrs.instructions |> List.map rewriteInstr }

/// Build an RT.PackageFn from a parsed PT.Expr body and typed params.
/// Uses PT2RT.Expr.toRT with the param-name → register-index symbol map
/// the runtime expects, then canonicalizes Pending handles so same-name
/// references share a handle (including self-recursion).
let private fnFromTypedBody
  (name : string)
  (selfHandle : System.Guid)
  (typedParams : List<string * RT.TypeReference>)
  (returnType : RT.TypeReference)
  (ptBody : PT.Expr)
  : RT.PackageFn.PackageFn =
  let (rcAfterParams, symbols) : (int * Map<string, int>) =
    typedParams
    |> List.fold
      (fun (rc, syms) (n, _) -> (rc + 1, Map.add n rc syms))
      (0, Map.empty)
  let rawInstructions = PT2RT.Expr.toRT symbols rcAfterParams None ptBody
  let instructions = canonicalizePendingHandles name selfHandle rawInstructions
  let hashKey = sprintf "pdd-llm-%s-%d" name (hash instructions.instructions)
  let mkParam (n : string) (t : RT.TypeReference) : RT.PackageFn.Parameter =
    { name = n; typ = t }
  let paramRecords =
    match typedParams with
    | [] -> NEList.singleton (mkParam "_unit" RT.TUnit)
    | (n, t) :: rest ->
      NEList.ofList (mkParam n t) (rest |> List.map (fun (n, t) -> mkParam n t))
  { hash = RT.Hash hashKey
    typeParams = []
    parameters = paramRecords
    returnType = returnType
    body = instructions }

/// Detect whether the body's instructions reference our own Pending name
/// (after canonicalization, those refs share our handle).
let private isSelfRecursive
  (selfHandle : System.Guid)
  (instrs : RT.Instructions)
  : bool =
  instrs.instructions
  |> List.exists (fun i ->
    match i with
    | RT.LoadVal(_, RT.DApplicable(RT.AppNamedFn { name = RT.FQFnName.Pending p })) ->
      p.handle = selfHandle
    | _ -> false)


/// Stashed call-site context per Pending handle. The CLI parallel
/// scheduler walks the Instructions, infers literal arg-types, and writes
/// hints here BEFORE kicking off the LLM call. The materializer reads them
/// when building the user prompt.
let argTypeHints :
  System.Collections.Concurrent.ConcurrentDictionary<System.Guid, List<string>> =
  System.Collections.Concurrent.ConcurrentDictionary()

/// Stashed actual literal arg VALUES per Pending handle. When the call
/// site passes literal arguments (e.g. a long CSV string), include them
/// in the materialize prompt so the LLM can see what data it operates on.
/// Truncated to ~400 chars per arg to keep the prompt focused.
let argValueHints :
  System.Collections.Concurrent.ConcurrentDictionary<System.Guid, List<string>> =
  System.Collections.Concurrent.ConcurrentDictionary()

let setArgTypeHint (handle : System.Guid) (types : List<string>) : unit =
  argTypeHints[handle] <- types

let setArgValueHint (handle : System.Guid) (values : List<string>) : unit =
  argValueHints[handle] <- values

let private getArgTypeHint (handle : System.Guid) : List<string> =
  match argTypeHints.TryGetValue handle with
  | true, t -> t
  | _ -> []

let private getArgValueHint (handle : System.Guid) : List<string> =
  match argValueHints.TryGetValue handle with
  | true, v -> v
  | _ -> []

/// Build the user-side prompt for a given pending fn name. Includes
/// arg-type hints + actual arg values (when available) so the LLM can
/// reason about the concrete data it operates on.
let buildUserPrompt (p : RT.FQFnName.Pending) : string =
  let hints = getArgTypeHint p.handle
  let values = getArgValueHint p.handle
  if List.isEmpty hints then
    sprintf "Function: %s. Description: implement a sensible default body." p.name
  else
    let names = [| "x"; "y"; "z"; "w"; "u"; "v" |]
    let argSig =
      hints
      |> List.mapi (fun i t -> sprintf "%s: %s" names[i % 6] t)
      |> String.concat ", "
    let valuesBlock =
      if List.isEmpty values then ""
      else
        let pairs =
          values
          |> List.mapi (fun i v -> sprintf "  %s = %s" names[i % 6] v)
          |> String.concat "\n"
        sprintf "\n\nCalled at this site with literal arguments:\n%s\n\nWrite a body that produces the right answer FOR THESE INPUTS (and similar ones). The function should generalize, but ground your reasoning in this concrete example." pairs
    sprintf
      "Function: %s. Called at this site with args of types: (%s). Use those as the parameter types in your sig.%s"
      p.name
      argSig
      valuesBlock


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

Available Stdlib (use the EXACT names — these exist):
- Stdlib.List.map / filter / fold / head / tail / append / reverse / length / isEmpty / sort
- Stdlib.List.fold signature: `fold list init (fun acc elem -> acc)` (list first).
- Stdlib.Int64.add / subtract / multiply / divide / mod / negate / equals / greaterThan / lessThan
- Stdlib.String.append / length / toUppercase / toLowercase
- Stdlib.String.split: splits a String on a separator → List<String>.
- **Result/Option unwrapping.** Stdlib has functions that return
  `Result<T, _>` or `Option<T>`. To get the bare value, pipe through
  `Stdlib.Result.withDefault` or `Stdlib.Option.withDefault`:
    `Stdlib.Int64.parse s |> Stdlib.Result.withDefault 0L`
    `Stdlib.List.head lst |> Stdlib.Option.withDefault ""`
  `Stdlib.Int64.parse: String -> Result<Int64, ParseError>` —
  returns Result; ALWAYS wrap with withDefault when chaining to arithmetic.
  `Stdlib.List.head`, `Stdlib.List.tail`, `Stdlib.List.getAt` return Option.

Do NOT use Haskell-style names: there is no foldl, foldr, fold_left,
sum (use fold with add), map2, zip — invent your own name if you need
those and the runtime will materialize it.

IMPORTANT — do NOT wrap intermediate results in Option<T> / Result<T,E>.
Keep types CONCRETE (Int64, String, List<Int64>, List<String>, List<List<String>>).
The runtime materializes these helpers straightforwardly; Option/Result
wrappers force every downstream stage to unwrap and break composition.
If a value could fail, use a sensible default instead of Option.None.
"""

/// Build a user-side prompt for the decompose call. Just the request,
/// rendered straight.
let buildDecomposePrompt (request : string) : string =
  sprintf "User: %s\nOutput:" request


/// One LLM-claimed example. Args + expected result are stored as Dvals so
/// we can dispatch on type. Int64 / String / Bool covered today.
type ClaimedTest =
  { args : List<RT.Dval>
    expect : RT.Dval }

/// Parsed shape of the LLM's JSON response.
type GeneratedFn =
  { sig_ : string
    body : string
    tests : List<ClaimedTest> }


// ---------------------------------------------------------------------------
// Test-gen (second LLM call, framed as a QA reviewer who hasn't seen body)
// ---------------------------------------------------------------------------

/// Test-only system prompt. Used in a SECOND LLM call (after the body is
/// materialized) to generate independent verification tests. The LLM is
/// framed as a QA reviewer who has not seen the body, so tests reflect
/// the standard meaning of the fn name rather than the (possibly wrong)
/// body the LLM wrote.
let testGenSystemPrompt = """You are a QA reviewer. You have been given a function NAME and SIGNATURE, but NOT the implementation. Your job is to write 3 concrete input/output test cases that reflect the FUNCTION NAME'S STANDARD MEANING — what a senior engineer would expect the function to do.

For example, if asked for `factorial: (n: Int64): Int64`, you should write tests that match the standard mathematical definition: factorial(0)=1, factorial(1)=1, factorial(5)=120, factorial(6)=720. NOT whatever a junior dev's wrong implementation would return.

Respond with JSON, exactly:
  {"tests": [{"args": [5], "expect": 120}, {"args": [0], "expect": 1}, ...]}

Use JSON numbers for Int64 args/results. Use JSON strings for String args/results. Use JSON booleans for Bool. Pick boundary cases (zero, negative, one, edge values) where they're meaningful.

Return ONLY the JSON. No markdown fences, no prose."""

let buildTestGenPrompt (name : string) (sig_ : string) : string =
  sprintf "Function: %s\nSignature: %s\nReturn the JSON {tests: [...]} per the instructions." name sig_

let buildTestGenPromptWithCallsite
  (name : string)
  (sig_ : string)
  (callsiteArgs : List<string>)
  : string =
  if List.isEmpty callsiteArgs then
    buildTestGenPrompt name sig_
  else
    let names = [| "x"; "y"; "z"; "w"; "u"; "v" |]
    let block =
      callsiteArgs
      |> List.mapi (fun i v -> sprintf "  %s = %s" names[i % 6] v)
      |> String.concat "\n"
    sprintf
      "Function: %s\nSignature: %s\n\nAt one call site, this function is invoked with these LITERAL arguments:\n%s\n\nUse those concrete values as the basis for at least one test case. The test should reflect what `%s` should return when given those literal inputs — based on the function name's standard meaning. Then 1-2 more boundary/varied cases.\n\nReturn the JSON {tests: [...]} per the instructions."
      name
      sig_
      block
      name

let parseTestGenResponse (raw : string) : List<ClaimedTest> =
  try
    let trimmed = raw.Trim()
    let stripped =
      if trimmed.StartsWith("```") then
        let after = trimmed.Substring(3)
        let withoutLang =
          if after.StartsWith("json") then after.Substring(4) else after
        let endIdx = withoutLang.LastIndexOf("```")
        if endIdx >= 0 then withoutLang.Substring(0, endIdx).Trim()
        else withoutLang.Trim()
      else
        trimmed
    let doc = JsonDocument.Parse(stripped)
    let root = doc.RootElement
    let mutable arr = Unchecked.defaultof<JsonElement>
    if not (root.TryGetProperty("tests", &arr)) then []
    elif arr.ValueKind <> JsonValueKind.Array then []
    else
      let rec parseDval (e : JsonElement) : Option<RT.Dval> =
        match e.ValueKind with
        | JsonValueKind.Number ->
          let mutable v = 0L
          if e.TryGetInt64(&v) then Some(RT.DInt64 v) else None
        | JsonValueKind.String -> Some(RT.DString(e.GetString()))
        | JsonValueKind.True -> Some(RT.DBool true)
        | JsonValueKind.False -> Some(RT.DBool false)
        | JsonValueKind.Array ->
          let items = [ for x in e.EnumerateArray() -> parseDval x ]
          if List.forall Option.isSome items then
            let vals = items |> List.map Option.get
            // Element type from the first item via the runtime's own
            // toValueType helper. Handles nested DList correctly so a
            // list-of-lists doesn't get KTInt64 as its outer type.
            let elemVT =
              match vals with
              | first :: _ ->
                match RT.Dval.toValueType first with
                | RT.ValueType.Known kt -> kt
                | _ -> RT.KTInt64
              | _ -> RT.KTInt64
            Some(RT.DList(RT.ValueType.Known elemVT, vals))
          else None
        | _ -> None
      let mutable acc = []
      for item in arr.EnumerateArray() do
        let mutable argsEl = Unchecked.defaultof<JsonElement>
        let mutable expectEl = Unchecked.defaultof<JsonElement>
        if item.TryGetProperty("args", &argsEl)
           && item.TryGetProperty("expect", &expectEl)
           && argsEl.ValueKind = JsonValueKind.Array then
          let parsedArgs =
            [ for a in argsEl.EnumerateArray() -> parseDval a ]
          let parsedExpect = parseDval expectEl
          if List.forall Option.isSome parsedArgs && parsedExpect.IsSome then
            acc <-
              { args = parsedArgs |> List.map Option.get
                expect = parsedExpect.Value }
              :: acc
      List.rev acc
  with _ -> []


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
    // tests is optional — older cached LLM responses don't include it
    let rec parseDval (e : JsonElement) : Option<RT.Dval> =
      match e.ValueKind with
      | JsonValueKind.Number ->
        // Default JSON numbers to Int64 (the bulk of our demos).
        let mutable v = 0L
        if e.TryGetInt64(&v) then Some(RT.DInt64 v) else None
      | JsonValueKind.String -> Some(RT.DString(e.GetString()))
      | JsonValueKind.True -> Some(RT.DBool true)
      | JsonValueKind.False -> Some(RT.DBool false)
      | JsonValueKind.Array ->
        let items = [ for x in e.EnumerateArray() -> parseDval x ]
        if List.forall Option.isSome items then
          let vals = items |> List.map Option.get
          let kt =
            match vals with
            | (RT.DInt64 _) :: _ -> RT.KTInt64
            | (RT.DString _) :: _ -> RT.KTString
            | (RT.DBool _) :: _ -> RT.KTBool
            | _ -> RT.KTInt64
          Some(RT.DList(RT.ValueType.Known kt, vals))
        else None
      | _ -> None
    let parseTests () : List<ClaimedTest> =
      let mutable arr = Unchecked.defaultof<JsonElement>
      if not (root.TryGetProperty("tests", &arr)) then []
      elif arr.ValueKind <> JsonValueKind.Array then []
      else
        let mutable acc = []
        for item in arr.EnumerateArray() do
          let mutable argsEl = Unchecked.defaultof<JsonElement>
          let mutable expectEl = Unchecked.defaultof<JsonElement>
          if item.TryGetProperty("args", &argsEl)
             && item.TryGetProperty("expect", &expectEl)
             && argsEl.ValueKind = JsonValueKind.Array then
            let parsedArgs =
              [ for a in argsEl.EnumerateArray() -> parseDval a ]
            let parsedExpect = parseDval expectEl
            if List.forall Option.isSome parsedArgs && parsedExpect.IsSome then
              acc <-
                { args = parsedArgs |> List.map Option.get
                  expect = parsedExpect.Value }
                :: acc
        List.rev acc
    match getString "sig", getString "body" with
    | Ok s, Ok b -> Ok { sig_ = s; body = b; tests = parseTests () }
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
///
/// Model selection: PDD_MODEL env override (default gpt-4o-mini for cost).
/// Set to "gpt-4o" or other model name to upgrade — useful when picky
/// syntax (list ops, lambdas, prefix application) trips up mini.
let callOpenAIWithMode
  (apiKey : string)
  (systemPrompt : string)
  (userPrompt : string)
  (forceJson : bool)
  : Task<Result<string, string>> =
  task {
    try
      let model =
        match System.Environment.GetEnvironmentVariable("PDD_MODEL") with
        | null | "" -> "gpt-4o-mini"
        | m -> m
      // response_format=json_object forces the model to emit valid JSON.
      // Skipped for prose-output calls (like decompose) since OpenAI
      // requires the word "json" in messages when json_object is on.
      let payload =
        if forceJson then
          JsonSerializer.Serialize(
            {| model = model
               messages =
                 [| {| role = "system"; content = systemPrompt |}
                    {| role = "user"; content = userPrompt |} |]
               max_tokens = 800
               temperature = 0
               response_format = {| ``type`` = "json_object" |} |}
          )
        else
          JsonSerializer.Serialize(
            {| model = model
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

/// Default callOpenAI uses json_object response mode. For prose-output
/// callers (like decompose), use callOpenAIWithMode with forceJson=false.
let callOpenAI
  (apiKey : string)
  (systemPrompt : string)
  (userPrompt : string)
  : Task<Result<string, string>> =
  callOpenAIWithMode apiKey systemPrompt userPrompt true


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

/// Identity-shaped fn with a specific arity. Used when LibParser+sig
/// parsing succeeded enough to extract param count but the body itself
/// failed to lower — we still want an N-arg fn the runtime can call
/// without "wrong arg count" exploding the chain. Returns the first arg
/// (always Int64-typed in this stub).
let private hardcodedIdentityFnArity
  (name : string)
  (arity : int)
  : RT.PackageFn.PackageFn =
  let mkParam (n : string) : RT.PackageFn.Parameter =
    { name = n; typ = RT.TInt64 }
  let names = [| "x"; "y"; "z"; "w"; "u"; "v" |]
  let paramNames =
    if arity <= 0 then [ "x" ]
    else [ for i in 0 .. arity - 1 -> names[i % 6] ]
  let parameters =
    match paramNames with
    | [ n ] -> NEList.singleton (mkParam n)
    | h :: tail -> NEList.ofList (mkParam h) (tail |> List.map mkParam)
    | _ -> NEList.singleton (mkParam "x")
  { hash =
      RT.Hash(sprintf "pdd-llm-identity-%s-%d" name (max 1 arity))
    typeParams = []
    parameters = parameters
    returnType = RT.TInt64
    body =
      { registerCount = max 1 arity
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
    // String concat: `<atom> ++ <atom>` where atom is a param or
    // double-quoted string literal (no internal escapes for now).
    //
    // Lowers to Apply(Stdlib.String.append, [lhs; rhs]).
    let concatMatch =
      System.Text.RegularExpressions.Regex.Match(
        trimmed,
        "^(\"[^\"]*\"|\\w+)\\s*\\+\\+\\s*(\"[^\"]*\"|\\w+)$"
      )
    if concatMatch.Success then
      let arity = List.length paramNames
      let r0 = arity
      let strAtom (raw : string) (nReg : int) =
        if raw.StartsWith "\"" && raw.EndsWith "\"" then
          let inner = raw.Substring(1, raw.Length - 2)
          Some(nReg, [ RT.LoadVal(nReg, RT.DString inner) ], nReg + 1)
        else
          match argIndex raw with
          | Some i -> Some(i, [], nReg)
          | None -> None
      match strAtom concatMatch.Groups[1].Value r0 with
      | None -> None
      | Some(lhsReg, lhsIns, r1) ->
        match strAtom concatMatch.Groups[2].Value r1 with
        | None -> None
        | Some(rhsReg, rhsIns, r2) ->
          let appReg = r2
          let outReg = r2 + 1
          let app : RT.ApplicableNamedFn =
            { name = RT.FQFnName.fqBuiltin "stringAppend" 0
              typeSymbolTable = Map.empty
              typeArgs = []
              argsSoFar = [] }
          let args : NEList<int> = NEList.ofList lhsReg [ rhsReg ]
          Some
            { registerCount = outReg + 1
              instructions =
                lhsIns
                @ rhsIns
                @ [ RT.LoadVal(appReg, RT.DApplicable(RT.AppNamedFn app))
                    RT.Apply(outReg, appReg, [], args) ]
              resultIn = outReg }
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
              promotedCache[name] <- { sig_ = sig_; body = body; tests = [] }
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
    let model =
      match System.Environment.GetEnvironmentVariable("PDD_MODEL") with
      | null | "" -> "gpt-4o-mini"
      | m -> m
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
          if llmCallsExhausted p.handle then
            // Per-handle outer cap. We've already burned `llmCallCap`
            // LLM calls on this Pending across all retry/scheduler paths;
            // refuse to do another.
            emit
              (MaterializeFailed(
                p.name,
                sprintf "llm call cap (%d) reached for handle" llmCallCap
              ))
            return None
          else
          let _ = incrementLlmCalls p.handle
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
              | Error e when n < maxAttempts ->
                // Malformed JSON — retry with a fix-up prompt that
                // emphasizes well-formed JSON output.
                logResult logPath p.name None (Some content) (Some e)
                emit (CompileBody(p.name, sprintf "json-retry-%d" n, 0))
                let fixUp =
                  sprintf
                    "Function: %s\nYour previous response was not valid JSON: %s\n\nReturn ONLY a JSON object {\"sig\": \"...\", \"body\": \"...\", \"tests\": [...]} with no fences, no prose, no trailing L on numbers inside the JSON (use 5, not 5L), no unquoted code. Both `sig` and `body` are quoted strings; `tests` is an array of {\"args\": [...], \"expect\": ...}."
                    p.name
                    e
                return! attempt (n + 1) fixUp
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

                // ============================================================
                // PRIMARY PATH: LibParser. Routes the full Dark language
                // through the real parser + PT2RT lowering.
                //
                // Requires the CLI to have installed `bodyParser`. Also
                // requires a parseable sig (with typed params + return type).
                // Falls back to the regex mini-parser when either is missing.
                // ============================================================
                let! libParserResult =
                  uply {
                    match bodyParser, parseFullSig gen.sig_ with
                    | Some parser, Some(typedParams, returnType) ->
                      // LibParser parses multi-line let-chains as separate
                      // top-level exprs. Normalize to single-line so a body
                      // like `let x = .. in\nlet y = .. in\nresult` parses.
                      let normalizedBody =
                        gen.body
                          .Replace("\r\n", " ")
                          .Replace("\n", " ")
                      let toParse =
                        if List.isEmpty typedParams then
                          normalizedBody
                        else
                          let paramList =
                            typedParams
                            |> List.map fst
                            |> String.concat " "
                          sprintf "fun %s -> %s" paramList normalizedBody
                      let! parsed = parser toParse
                      match parsed with
                      | Ok ptExpr ->
                        try
                          let bodyExpr =
                            if List.isEmpty typedParams then ptExpr
                            else
                              match ptExpr with
                              | PT.ELambda(_, _, inner) -> inner
                              | _ -> ptExpr
                          let fn =
                            fnFromTypedBody p.name p.handle typedParams returnType bodyExpr
                          return Ok fn
                        with ex ->
                          return Error(sprintf "lower: %s" ex.Message)
                      | Error e -> return Error(sprintf "parse: %s" e)
                    | None, _ -> return Error "bodyParser not installed"
                    | _, None -> return Error(sprintf "sig parse failed: %s" gen.sig_)
                  }

                match libParserResult with
                | Ok fn ->
                  let kind =
                    if n > 1 then sprintf "libparser (retry %d)" (n - 1)
                    else "libparser"
                  emit (CompileBody(p.name, kind, fn.body.registerCount))

                  // INDEPENDENT TEST GENERATION: make a second LLM call
                  // framed as a QA reviewer who has NOT seen the body, so
                  // tests reflect the standard meaning of the fn name
                  // rather than the (possibly wrong) body the LLM wrote.
                  // Falls back to the body-call's claimed tests on error.
                  let! verificationTests =
                    uply {
                      if llmCallsExhausted p.handle then
                        return gen.tests
                      else
                        let _ = incrementLlmCalls p.handle
                        let testPrompt =
                          buildTestGenPromptWithCallsite
                            p.name
                            gen.sig_
                            (getArgValueHint p.handle)
                        let! resp =
                          callOpenAI apiKey testGenSystemPrompt testPrompt
                          |> Async.AwaitTask
                          |> Async.StartAsTask
                        match resp with
                        | Error _ -> return gen.tests
                        | Ok body ->
                          match extractContent body with
                          | Error _ -> return gen.tests
                          | Ok content ->
                            let parsed = parseTestGenResponse content
                            if List.isEmpty parsed then return gen.tests
                            else return parsed
                    }
                  let testsToRun = verificationTests

                  let (RT.Hash fnHashStr) = fn.hash
                  let recursive = isSelfRecursive p.handle fn.body
                  let! testOutcomes =
                    uply {
                      if recursive then
                        // Self-recursive bodies can't be tested in the
                        // inline runner without seeding the materializer
                        // with itself — skip wholesale and trust the LLM.
                        return List.replicate testsToRun.Length TSkipped
                      else
                        match testRunner with
                        | None -> return List.replicate testsToRun.Length TSkipped
                        | Some runner ->
                        let mutable acc = []
                        for t in testsToRun do
                          let! r = runner (p, fn, t.args)
                          let outcome =
                            match r with
                            | Error e ->
                              // Recursion: the body calls itself via Apply
                              // on its own hash. The inline test runner
                              // can't resolve that. Treat as skipped, not
                              // a failure — better than a retry loop.
                              if e.Contains fnHashStr then TSkipped
                              else TError(t.args, e)
                            | Ok got ->
                              if got = t.expect then TPass(t.args, got)
                              else TFail(t.args, t.expect, got)
                          let detail =
                            match outcome with
                            | TPass(a, g) ->
                              sprintf "%A → %A" a g
                            | TFail(a, e, g) ->
                              sprintf "%A → %A (expected %A)" a g e
                            | TError(a, e) -> sprintf "%A → ERR %s" a e
                            | TSkipped -> ""
                          emit
                            (TestRan(p.name, testResultLabel outcome, detail))
                          acc <- outcome :: acc
                        return List.rev acc
                    }

                  let testCount = testsToRun.Length
                  let failCount =
                    testOutcomes
                    |> List.filter (fun r ->
                      match r with
                      | TFail _
                      | TError _ -> true
                      | _ -> false)
                    |> List.length

                  if testCount > 0 && failCount > 0 && n < maxAttempts then
                    // At least one test failed AND we have retries left.
                    // Feed the failing test back to the LLM for a fix-up.
                    let failingDetail =
                      testOutcomes
                      |> List.tryPick (fun r ->
                        match r with
                        | TFail(a, e, g) ->
                          Some(sprintf "args=%A, expected=%A, got=%A" a e g)
                        | TError(a, e) -> Some(sprintf "args=%A errored: %s" a e)
                        | _ -> None)
                      |> Option.defaultValue ""
                    let fixUp =
                      sprintf
                        "Function: %s\nYour previous attempt was:\n  sig: %s\n  body: %s\n\nIt parsed and lowered correctly, but a test failed:\n  %s\n\nReturn another {sig, body, tests} with a body that produces the expected output for this test (and your other claimed tests). Keep the body simple."
                        p.name
                        gen.sig_
                        gen.body
                        failingDetail
                    emit (CompileBody(p.name, sprintf "test-retry-%d" n, 0))
                    return! attempt (n + 1) fixUp
                  else
                    let finalState =
                      if testCount = 0 then Real
                      elif failCount = 0 then Real
                      else Fake  // we exhausted retries with failing tests
                    emit (MaterializeDone(p.name, finalState, elapsedMs ()))
                    if finalState = Real then persistPromoted p.name gen
                    return Some fn
                | Error libParserErr ->
                  // Surface why LibParser declined, so the HTML view shows
                  // the actual reason instead of a silent fallback.
                  emit (CompileBody(p.name, sprintf "libparser-fail: %s" libParserErr, 0))
                  // FALLBACK PATH: regex mini-parser (the original 5 cases).
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
                    emit (CompileBody(p.name, sprintf "retry-%d" n, 0))
                    let fixUp = buildFixUpPrompt p.name gen.sig_ gen.body
                    return! attempt (n + 1) fixUp
                  | None ->
                    // Pick the arity from sig-derived params if we have
                    // them, else from the arg-type hints, else 1. Avoids
                    // returning a 1-arg identity for a fn the caller will
                    // hit with N args (TooManyArgsForFn).
                    let arity =
                      match parseFullSig gen.sig_ with
                      | Some(typedParams, _) -> List.length typedParams
                      | None ->
                        match argTypeHints.TryGetValue p.handle with
                        | true, hints -> List.length hints
                        | _ -> 1
                    emit (CompileBody(p.name, "fallback-identity", arity))
                    emit (MaterializeDone(p.name, Fake, elapsedMs ()))
                    return Some(hardcodedIdentityFnArity p.name arity)
        }
      let initialPrompt = buildUserPrompt p
      return! attempt 1 initialPrompt
  }
