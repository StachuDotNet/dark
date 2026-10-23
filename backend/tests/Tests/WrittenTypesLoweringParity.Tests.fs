/// Differential gate for the two WrittenTypes→ProgramTypes lowerings.
///
/// The F# lowering (`LibParser.WrittenTypesToProgramTypes`, used by execution:
/// package loading, testfiles, CLI) and the Dark lowering
/// (`languageTools/writtenTypesToProgramTypes.dark`, used by tooling: LSP,
/// round-trip, formatting) must produce the same ProgramTypes for the same
/// source — identical except for node ids (which canonical serialization
/// ignores). Any other difference is drift, the class of bug this gate catches.
///
/// Three tests: a broad sweep over REAL corpus expressions (fn/value bodies in
/// packages/darklang that reparse cleanly as one expression, spread-sampled to
/// bound runtime, lowered with an empty context), and a few pinned cases lowered
/// under a real (module, fn, params) context — catching divergences that only
/// surface once names resolve (self-calls, a match pattern shadowing a param).
/// The third covers whole SCRIPTS, the CLI path: declarations, modules, impls and
/// script values, through `Cli.parseCliScript` against `CliScript.parseForCli`.
module Tests.WrittenTypesLoweringParity

open System.Threading.Tasks
open FSharp.Control.Tasks

open Expecto

open Prelude
open TestUtils.TestUtils

module PT = LibExecution.ProgramTypes
module RT = LibExecution.RuntimeTypes
module Dval = LibExecution.Dval
module PT2DT = LibExecution.ProgramTypesToDarkTypes
module PackageRefs = LibExecution.PackageRefs
module P = LibParser.Parser
module WT = LibParser.WrittenTypes
module WT2PT = LibParser.WrittenTypesToProgramTypes
module NR = LibParser.NameResolver
module Canonical = LibSerialization.Hashing.Canonical
module Cli = Builtins.CliHost.Libs.Cli

// --- id-independent canonical comparison ---

/// id-independent canonical bytes (the content-hash serialization)
let private canonicalBytes (e : PT.Expr) : byte[] =
  use ms = new System.IO.MemoryStream()
  use w = new System.IO.BinaryWriter(ms)
  Canonical.writeExpr Canonical.Normal w e
  w.Flush()
  ms.ToArray()

// --- lockstep differ: report the first divergent node, human-readably ---
let private ctorName (x : obj) : string =
  let s = sprintf "%A" x
  s.Split([| ' '; '('; '\n' |])[0]

let rec private kidsE (e : PT.Expr) : List<PT.Expr> =
  match e with
  | PT.EString(_, contents) ->
    contents
    |> List.choose (function
      | PT.StringInterpolation e -> Some e
      | _ -> None)
  | PT.EIf(_, c, t, e2) -> [ c; t ] @ Option.toList e2
  | PT.EPipe(_, lhs, parts) ->
    lhs
    :: (parts
        |> List.collect (function
          | PT.EPipeLambda(_, _, b) -> [ b ]
          | PT.EPipeInfix(_, _, e, _) -> [ e ]
          | PT.EPipeFnCall(_, _, _, args, _) -> args
          | PT.EPipeEnum(_, _, _, fields) -> fields
          | PT.EPipeVariable(_, _, args) -> args))
  | PT.EMatch(_, arg, cases) ->
    arg
    :: (cases |> List.collect (fun c -> Option.toList c.whenCondition @ [ c.rhs ]))
  | PT.ELet(_, _, v, b) -> [ v; b ]
  | PT.EUnwrap(_, operand) -> [ operand ]
  | PT.EStatement(_, a, b) -> [ a; b ]
  | PT.EList(_, es) -> es
  | PT.EDict(_, kvs) -> List.map snd kvs
  | PT.ETuple(_, a, b, rest) -> a :: b :: rest
  | PT.EApply(_, f, _, args) -> f :: NEList.toList args
  | PT.ELambda(_, _, body) -> [ body ]
  | PT.EInfix(_, _, l, r, _) -> [ l; r ]
  | PT.ERecord(_, _, _, fields) -> List.map snd fields
  | PT.ERecordFieldAccess(_, r, _) -> [ r ]
  | PT.ERecordUpdate(_, r, ups) -> r :: (NEList.toList ups |> List.map snd)
  | PT.EEnum(_, _, _, _, fields) -> fields
  | _ -> []

let private fqFnStr (nr : PT.NameResolution<PT.FQFnName.FQFnName>) : string =
  let orig = String.concat "." nr.originalName
  match nr.resolved with
  | Ok r ->
    let n =
      match r.name with
      | PT.FQFnName.Builtin b -> $"Builtin({b.name},v{b.version})"
      | PT.FQFnName.Package(PT.Hash h) -> $"Pkg({h.Substring(0, 12)}…)"
      | PT.FQFnName.TraitMethod { trait_ = PT.Hash t; method_ = m; implFn = _ } ->
        $"Trait({t.Substring(0, 12)}…).{m}"
    $"{orig}=Ok {n} loc=%A{r.location}"
  | Error e -> $"{orig}=Error %A{e}"

let private fqValStr (nr : PT.NameResolution<PT.FQValueName.FQValueName>) : string =
  let orig = String.concat "." nr.originalName
  match nr.resolved with
  | Ok r ->
    let n =
      match r.name with
      | PT.FQValueName.Builtin b -> $"Builtin({b.name},v{b.version})"
      | PT.FQValueName.Package(PT.Hash h) -> $"Pkg({h.Substring(0, 12)}…)"
    $"{orig}=Ok {n}"
  | Error e -> $"{orig}=Error %A{e}"

/// details of the name-ish payload for nodes whose difference is not in child exprs
let private nodeDetail (e : PT.Expr) : string =
  match e with
  | PT.EFnName(_, nr, _) -> $"EFnName {fqFnStr nr}"
  | PT.EValue(_, nr) -> $"EValue {fqValStr nr}"
  | PT.EVariable(_, v) -> $"EVariable {v}"
  | PT.EArg(_, i) -> $"EArg {i}"
  | PT.EPipe(_, _, parts) ->
    parts
    |> List.map (function
      | PT.EPipeFnCall(_, nr, tas, _, _) ->
        $"EPipeFnCall {fqFnStr nr} tas={List.length tas}"
      | PT.EPipeVariable(_, v, _) -> $"EPipeVariable {v}"
      | PT.EPipeLambda _ -> "EPipeLambda"
      | PT.EPipeInfix _ -> "EPipeInfix"
      | PT.EPipeEnum(_, nr, cn, _) -> sprintf "EPipeEnum %A.%s" nr cn)
    |> String.concat "; "
    |> sprintf "EPipe parts: %s"
  | PT.ERecord(_, nr, tas, _) -> sprintf "ERecord %A tas=%d" nr (List.length tas)
  | PT.EEnum(_, nr, tas, cn, _) ->
    sprintf "EEnum %A tas=%d case=%s" nr (List.length tas) cn
  | PT.EApply(_, _, tas, _) -> sprintf "EApply tas=%A" tas
  | PT.ELambda(_, pats, _) -> sprintf "ELambda pats=%A" (NEList.toList pats)
  | PT.ELet(_, pat, _, _) -> sprintf "ELet pat=%A" pat
  | PT.EMatch(_, _, cases) ->
    sprintf "EMatch pats=%A" (cases |> List.map (fun c -> c.pat))
  | other -> sprintf "%A" other

let rec private diffExpr (a : PT.Expr) (b : PT.Expr) : Option<string> =
  if canonicalBytes a = canonicalBytes b then
    None
  else
    let ka, kb = kidsE a, kidsE b
    if ctorName a = ctorName b && List.length ka = List.length kb then
      match List.zip ka kb |> List.tryPick (fun (x, y) -> diffExpr x y) with
      | Some d -> Some d
      | None -> Some $"[{ctorName a}] fs: {nodeDetail a}\n     dark: {nodeDetail b}"
    else
      Some $"fs {ctorName a} ({nodeDetail a}) vs dark {ctorName b} ({nodeDetail b})"


// --- corpus snippet harvesting ---

let private sliceRange (src : string) (r : LibParser.Tokenizer.TokenRange) : string =
  let lines = src.Split '\n'
  let safeLine i = if i >= 0 && i < lines.Length then lines[i] else ""
  let clamp (s : string) i = max 0 (min i s.Length)
  if r.start.row = r.end_.row then
    let line = safeLine r.start.row
    line.Substring(
      clamp line r.start.column,
      clamp line r.end_.column - clamp line r.start.column
    )
  else
    let first =
      (safeLine r.start.row).Substring(clamp (safeLine r.start.row) r.start.column)
    let middle = [ for i in r.start.row + 1 .. r.end_.row - 1 -> safeLine i ]
    let lastLine = safeLine r.end_.row
    let last = lastLine.Substring(0, clamp lastLine r.end_.column)
    String.concat "\n" (first :: middle @ [ last ])

let rec private harvest (src : string) (d : WT.Declaration) : List<string> =
  match d with
  | WT.DFunction f -> [ sliceRange src (WT.exprRange f.body) ]
  | WT.DValue v -> [ sliceRange src (WT.exprRange v.body) ]
  | WT.DModule m -> m.declarations |> List.collect (harvest src)
  | _ -> []

/// a snippet participates only if it reparses cleanly, standalone, as exactly
/// one expression (multi-statement bodies split at top level and are skipped)
let private parseSingleExpr (snippet : string) : Option<WT.Expr> =
  let r = P.parse snippet
  match r.parsed, r.diagnostics with
  | Some(WT.SourceFile { declarations = []; exprsToEval = [ e ] }), [] -> Some e
  | _ -> None

// --- the script path ---

/// What one lowering made of a script: its declarations' hashes by kind (sorted, with
/// submodules flattened in, since F# keeps them inline and Dark nests them) and its
/// top-level expressions' canonical bytes. Hashes are content hashes, so two equal
/// shapes mean the same program types.
type private ScriptShape =
  { types : List<string>
    values : List<string>
    fns : List<string>
    traits : List<string>
    impls : List<string>
    exprs : List<string> }

let private sortedHashes (hashes : List<PT.Hash>) : List<string> =
  hashes |> List.map (fun (PT.Hash h) -> h) |> List.sort

let private exprsKey (exprs : List<PT.Expr>) : List<string> =
  exprs |> List.map (canonicalBytes >> System.Convert.ToHexString)

let private fsShape (m : Builtins.CliHost.Utils.CliScript.PTCliScriptModule) =
  { types = (m.types @ m.submodules.types) |> List.map _.hash |> sortedHashes
    values = (m.values @ m.submodules.values) |> List.map _.hash |> sortedHashes
    fns = (m.fns @ m.submodules.fns) |> List.map _.hash |> sortedHashes
    traits = m.traits |> List.map _.hash |> sortedHashes
    impls = m.impls |> List.map _.hash |> sortedHashes
    exprs = exprsKey m.exprs }

let private field (name : string) (d : RT.Dval) : RT.Dval =
  match d with
  | RT.DRecord(_, _, _, fields) ->
    match Map.tryFind name fields with
    | Some v -> v
    | None -> failtest $"no field {name} in %A{d}"
  | other -> failtest $"expected a record, got %A{other}"

let private items (d : RT.Dval) : List<RT.Dval> =
  match d with
  | RT.DList(_, items) -> items
  | other -> failtest $"expected a list, got %A{other}"

let private darkShape (m : RT.Dval) =
  let subs = field "submodules" m |> items
  let hashesOf kind (d : RT.Dval) =
    field kind d |> items |> List.map (field "hash" >> PT2DT.Hash.fromDT)
  let withSubs kind = hashesOf kind m @ (subs |> List.collect (hashesOf kind))
  { types = withSubs "types" |> sortedHashes
    values = withSubs "values" |> sortedHashes
    fns = withSubs "fns" |> sortedHashes
    traits = hashesOf "traits" m |> sortedHashes
    impls = hashesOf "impls" m |> sortedHashes
    exprs = field "exprs" m |> items |> List.map PT2DT.Expr.fromDT |> exprsKey }

/// Scripts that pin a shape the corpus might not happen to contain.
let private pinnedScripts : List<string * string> =
  [ "expressions only", "let x = 1L\nlet y = x + 2L\ny * 3L"
    "statements", "()\n\"done\""
    "types and fns",
    "type P = { x: Int64, y: Int64 }\nlet norm (p: P) : Int64 = p.x * p.x + p.y * p.y\nnorm (P { x = 3L, y = 4L })"
    // Each `a` calls its own module's `h`. Hashed over the NAME `h` rather than what it
    // resolved to, the two `a`s share a hash and one body runs for both.
    "same body, different siblings",
    "module M =\n  let h () : Int64 = 1L\n  let a () : Int64 = h ()\nmodule N =\n  let h () : Int64 = 2L\n  let a () : Int64 = h ()\nM.a () + N.a ()"
    "mutual recursion",
    "let isEven (n: Int) : Bool = if n == 0 then true else isOdd (n - 1)\nlet isOdd (n: Int) : Bool = if n == 0 then false else isEven (n - 1)\n0"
    "values",
    "val first = later ()\nlet later () : Int64 = second\nval second = 2L\nfirst + second"
    "impl of a stdlib trait",
    "type Vec = { dx: Int64, dy: Int64 }\nlet vecAdd (a: Vec) (b: Vec) : Vec = Vec { dx = a.dx + b.dx, dy = a.dy + b.dy }\nimpl Stdlib.Add for Vec =\n  let add (a: Vec) (b: Vec) : Vec = vecAdd a b\n(Vec { dx = 1L, dy = 2L } + Vec { dx = 3L, dy = 4L }).dy"
    "own trait",
    "trait Describe<'a> =\n  let describe (v: 'a) : String\ntype Cat = { name: String }\nlet catDescribe (c: Cat) : String = c.name\nimpl Describe for Cat =\n  let describe (c: Cat) : String = catDescribe c\n0"
    "impl of a trait that doesn't exist",
    "type Q = { a: Int64 }\nlet qa (x: Q) (y: Q) : Q = x\nimpl Nope.NotATrait for Q =\n  let add (x: Q) (y: Q) : Q = qa x y\n0"
    "a named test", "test t = 1L |> Stdlib.Test.equal 1L\n0"
    "a syntax error", "let x = \n" ]

let private scriptPathParity =
  testTask "F# and Dark lower whole scripts alike (accept, reject, hashes)" {
    let root =
      [ "/home/dark/app"; ".."; "." ]
      |> List.tryFind (fun r ->
        System.IO.Directory.Exists(
          System.IO.Path.Combine(r, "scripts/perf/workloads")
        ))
    match root with
    | None -> () // not a full checkout -- nothing to gate
    | Some root ->
      let fromDisk =
        [ "scripts/perf/workloads"; "backend/testfiles/execution/stdlib"; "docs" ]
        |> List.collect (fun dir ->
          System.IO.Directory.GetFiles(
            System.IO.Path.Combine(root, dir),
            "*.dark",
            System.IO.SearchOption.AllDirectories
          )
          |> Array.sort
          |> Array.toList)
        |> List.map (fun path ->
          System.IO.Path.GetRelativePath(root, path),
          System.IO.File.ReadAllText path)
      let scripts = pinnedScripts @ fromDisk
      let! (state : RT.ExecutionState) = executionStateFor pmPT false
      let parseFn =
        RT.FQFnName.fqPackage (
          PackageRefs.Fn.LanguageTools.Parser.CliScript.parseForCli ()
        )
      let disagreements = ResizeArray<string>()
      let mutable examined = 0
      for (name, code) in scripts do
        let! fs = Cli.parseCliScript state "Tests" "" code |> Ply.toTask
        let! darkResult =
          LibExecution.Execution.executeFunction
            state
            parseFn
            []
            (NEList.ofList
              (RT.DUuid PT.BranchId.Main.Guid)
              [ RT.DString "Tests"; RT.DString ""; RT.DString name; RT.DString code ])
        let dark =
          match darkResult with
          | Ok(RT.DEnum(_, _, _, "Ok", [ RT.DTuple(m, _, []) ])) -> Ok(darkShape m)
          | Ok(RT.DEnum(_, _, _, "Error", [ e ])) -> Error $"%A{e}"
          | Ok other -> failtest $"{name}: parseForCli returned %A{other}"
          | Error(rte, _) -> failtest $"{name}: parseForCli raised {rte}"
        examined <- examined + 1
        match fs, dark with
        | Error _, Error _ -> ()
        | Ok _, Error e ->
          disagreements.Add $"{name}: only F# accepts it; Dark says {e}"
        | Error diags, Ok _ ->
          let first =
            diags
            |> List.tryHead
            |> Option.map (fun (d : P.Diagnostic) -> d.message)
            |> Option.defaultValue ""
          disagreements.Add $"{name}: only Dark accepts it; F# says {first}"
        | Ok m, Ok darkM ->
          let fsM = fsShape m
          let differs =
            [ if fsM.types <> darkM.types then "types"
              if fsM.values <> darkM.values then "values"
              if fsM.fns <> darkM.fns then "fns"
              if fsM.traits <> darkM.traits then "traits"
              if fsM.impls <> darkM.impls then "impls"
              if fsM.exprs <> darkM.exprs then "exprs" ]
          if not differs.IsEmpty then
            disagreements.Add
              $"""{name}: the lowerings differ in {String.concat ", " differs}"""
      // A corpus that silently shrank to nothing would agree with everything.
      Expect.isGreaterThan examined 40 "scripts examined"
      if disagreements.Count > 0 then
        failtest (
          $"{disagreements.Count} of {examined} scripts lower differently:\n"
          + String.concat "\n" disagreements
        )
  }

let tests =
  testList
    "WrittenTypesLoweringParity"
    [ testTask "F# and Dark lowerings agree on corpus expressions (modulo node ids)" {
        let root =
          [ // Escape hatch for bisecting this test: the sample is `corpus/400` spread across the whole
            // tree, so ANY edit to the corpus moves which snippets get checked, and
            // a pre-existing divergence can appear or vanish for reasons unrelated
            // to the change under test. Point this at a pristine checkout to compare
            // the same snippets across two versions of the lowering.
            System.Environment.GetEnvironmentVariable "DARK_PARITY_CORPUS"
            "../packages/darklang"
            "packages/darklang"
            "/home/dark/app/packages/darklang" ]
          |> List.filter (fun p -> not (isNull p))
          |> List.tryFind System.IO.Directory.Exists
        match root with
        | None -> () // not a full checkout — nothing to gate
        | Some root ->
          let snippets =
            System.IO.Directory.GetFiles(
              root,
              "*.dark",
              System.IO.SearchOption.AllDirectories
            )
            |> Array.sort
            |> Array.toList
            |> List.collect (fun f ->
              let src = System.IO.File.ReadAllText f
              match (P.parse src).parsed with
              | Some(WT.SourceFile sf) ->
                sf.declarations
                |> List.collect (harvest src)
                |> List.map (fun s -> (f, s))
              | None -> [])
            |> List.choose (fun (f, snip) ->
              parseSingleExpr snip |> Option.map (fun e -> (f, snip, e)))
          // spread-sample across the whole corpus to bound runtime
          let target = 400
          let step = max 1 (List.length snippets / target)
          let sample =
            snippets
            |> List.mapi (fun i s -> (i, s))
            |> List.filter (fun (i, _) -> i % step = 0)
            |> List.map snd

          let unwrapSnippets =
            [ "fun x -> Ok x?"
              "fun x -> Some x?.field?"
              "fun x -> Ok ((Stdlib.Int.parse x)?)"
              "fun x -> Ok x??"
              "fun x -> Some ((x |> Stdlib.List.head)?)" ]
            |> List.map (fun source ->
              match parseSingleExpr source with
              | Some expr -> ("unwrap", source, expr)
              | None -> failtest $"Unwrap fixture did not parse: {source}")
          let sample = unwrapSnippets @ sample

          let! (exeState : RT.ExecutionState) = executionStateFor pmPT false
          let parseFnName =
            RT.FQFnName.fqPackage (
              PackageRefs.Fn.LanguageTools.Parser.parsePTExpr ()
            )
          let fsBuiltins = localBuiltIns pmPT
          let ctx : WT2PT.Context =
            { currentFnName = None; argMap = Map.empty; localBindings = Set.empty }

          let mismatches = ResizeArray<string * string>()
          let mutable darkSideErrors = 0
          for (f, snip, wtExpr) in sample do
            let! (fsPT : PT.Expr) =
              WT2PT.Expr.toPT fsBuiltins pmPT NR.OnMissing.Allow [] ctx wtExpr
              |> Ply.toTask
            let! darkResult =
              LibExecution.Execution.executeFunction
                exeState
                parseFnName
                []
                (NEList.singleton (RT.DString snip))
            let! darkDval = unwrapExecutionResult exeState darkResult |> Ply.toTask
            match darkDval with
            | RT.DEnum(tn, _, _, "Ok", [ exprDval ]) when tn = Dval.resultType () ->
              let darkPT = PT2DT.Expr.fromDT exprDval
              if canonicalBytes fsPT <> canonicalBytes darkPT then
                if mismatches.Count < 8 then
                  let d = diffExpr fsPT darkPT |> Option.defaultValue "?"
                  print $"DRILL {f}:\n  {d}"
                mismatches.Add(f, snip)
            | other ->
              // the Dark path failed where the F# path parsed — also a finding,
              // but tracked separately from lowering mismatches
              if darkSideErrors < 3 then
                let shown =
                  if snip.Length > 120 then snip.Substring(0, 120) else snip
                print $"DARK-ERR {f}: %A{other}\n  snippet: {shown}"
              darkSideErrors <- darkSideErrors + 1

          // A Dark lowering that errors where the F# one parsed is the same drift this test exists
          // for. Counted but never asserted, it can reach every snippet in the corpus and stay green.
          if darkSideErrors > 0 then
            failtest
              $"the Dark lowering errored on {darkSideErrors} corpus snippets (first 3 printed above)"

          if mismatches.Count > 0 then
            let detail =
              mismatches
              |> Seq.truncate 5
              |> Seq.map (fun (f, s) ->
                let shown = if s.Length > 200 then s.Substring(0, 200) + "…" else s
                $"{f}:\n{shown}")
              |> String.concat "\n---\n"
            failtest
              $"{mismatches.Count} lowering divergences between F# and Dark WT2PT:\n{detail}"
      }

      // The differential above lowers with an EMPTY module/fn context, so it is blind
      // to divergences that only appear once names resolve in a real fn — the pipe
      // fn-first resolution, qualified self-calls, and a match pattern shadowing a
      // parameter. This pins those by lowering hand-picked snippets under a real
      // (module, fn-name, params) context through BOTH lowerings and asserting they
      // agree — the coverage that would have caught the earlier Dark-lowering breaks.
      testTask
        "F# and Dark lowerings agree under a real fn context (self-call, match-shadow)" {
        let! (exeState : RT.ExecutionState) = executionStateFor pmPT false
        let fsBuiltins = localBuiltIns pmPT
        let inCtxFn =
          RT.FQFnName.fqPackage (
            PackageRefs.Fn.LanguageTools.Parser.parsePTExprInContext ()
          )
        // (label, currentModule, currentFnName, params, snippet)
        let cases
          : List<string * List<string> * List<string> * List<string> * string> =
          [ // a match pattern that shadows a parameter must bind the PATTERN, not the
            // parameter (EArg) — the Dark bug produced EArg here
            ("match-shadows-param",
             [ "M" ],
             [ "Tests"; "M"; "f" ],
             [ "x" ],
             "match 99L with | x -> x")
            // a QUALIFIED self-call is a normal reference, not ESelf (matches F#)
            ("qualified-self-call",
             [ "M" ],
             [ "Tests"; "M"; "f" ],
             [ "n" ],
             "Tests.M.f n")
            // a pipe fn-call with explicit type args (`|> f<T>`) — the type args must
            // lower onto the EPipeFnCall node identically on both sides (guards the move
            // of pipe type args off the fn identifier in the Dark WrittenTypes)
            ("pipe-fn-call-type-args",
             [ "M" ],
             [],
             [ "x" ],
             "x |> Stdlib.Json.serialize<Bool>")
            ("bare-some", [ "M" ], [], [], "Some 5")
            ("bare-none", [ "M" ], [], [], "None")
            ("bare-ok", [ "M" ], [], [], "Ok 5")
            ("bare-error", [ "M" ], [], [], "Error \"no\"")
            ("private-qualified-function",
             [ "M" ],
             [],
             [],
             "Stdlib.Cli.Posix.__abiOpenAppend ()")
            ("private-error-mapper",
             [ "M" ],
             [],
             [],
             "Error 1 |> Stdlib.Result.mapError Stdlib.Cli.Posix.__fromErrno")
            ("internal-package-prefix",
             [ "M" ],
             [],
             [],
             "PACKAGE.Darklang.Stdlib.List.map") ]
        let mismatches = ResizeArray<string>()
        for (label, cmod, cfn, prms, snip) in cases do
          let ctx : WT2PT.Context =
            { currentFnName = (if List.isEmpty cfn then None else Some cfn)
              argMap = prms |> List.mapi (fun i p -> (p, i)) |> Map.ofList
              localBindings = Set.empty }
          match (P.parse snip).parsed with
          | Some(WT.SourceFile { exprsToEval = [ e ] }) ->
            let! (fsPT : PT.Expr) =
              WT2PT.Expr.toPT fsBuiltins pmPT NR.OnMissing.Allow cmod ctx e
              |> Ply.toTask
            let strList xs = xs |> List.map RT.DString |> Dval.list RT.KTString
            let args =
              NEList.ofList
                (RT.DUuid PT.BranchId.Main.Guid) // main, explicitly, so a concurrently-switched process can't move it
                [ RT.DString "Tests"
                  strList cmod
                  strList cfn
                  strList prms
                  RT.DString snip ]
            let! darkResult =
              LibExecution.Execution.executeFunction exeState inCtxFn [] args
            let! darkDval = unwrapExecutionResult exeState darkResult |> Ply.toTask
            match darkDval with
            | RT.DEnum(tn, _, _, "Ok", [ exprDval ]) when tn = Dval.resultType () ->
              let darkPT = PT2DT.Expr.fromDT exprDval
              if canonicalBytes fsPT <> canonicalBytes darkPT then
                let d = diffExpr fsPT darkPT |> Option.defaultValue "?"
                mismatches.Add($"{label} `{snip}`: {d}")
            | other -> mismatches.Add($"{label} `{snip}`: dark-side error {other}")
          | _ -> mismatches.Add($"{label} `{snip}`: F# parse failed")
        if mismatches.Count > 0 then
          let detail = String.concat "\n" mismatches
          failtest $"context-dependent lowering divergences:\n{detail}"
      }
      scriptPathParity ]
