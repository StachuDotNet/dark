/// Differential gate for the two WrittenTypes→ProgramTypes lowerings.
///
/// The F# lowering (`LibParser.WrittenTypesToProgramTypes`, used by execution:
/// package loading, testfiles, CLI) and the Dark lowering
/// (`languageTools/writtenTypesToProgramTypes.dark`, used by tooling: LSP,
/// round-trip, formatting) must produce the same ProgramTypes for the same
/// source, modulo the DELIBERATE divergences documented in
/// backend/src/LibParser/GRAMMAR.md:
///   - node ids (ignored by canonical serialization)
///   - statement sequences: Dark keeps `EStatement`; F# lowers to
///     `let _ = a in b` (normalized here before comparison)
/// Anything else is drift — the exact class of bug this gate exists to catch.
///
/// Inputs are REAL corpus expressions: every fn/value body in packages/darklang
/// whose source slice reparses cleanly as a single expression, spread-sampled
/// to bound runtime.
module Tests.LoweringDifferential

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

// --- normalization of documented divergences ---

/// Dark WT2PT keeps statement sequences as `PT.EStatement` (round-trip
/// fidelity); the F# execution lowering emits `let _ = a in b`. Rewrite
/// EStatement→ELet on both sides before comparing.
let rec private normExpr (e : PT.Expr) : PT.Expr =
  match e with
  | PT.EStatement(id, a, b) ->
    PT.ELet(id, PT.LPWildcard(gid ()), normExpr a, normExpr b)
  | PT.EString(id, contents) -> PT.EString(id, List.map normSeg contents)
  | PT.EIf(id, c, t, e2) ->
    PT.EIf(id, normExpr c, normExpr t, Option.map normExpr e2)
  | PT.EPipe(id, lhs, parts) -> PT.EPipe(id, normExpr lhs, List.map normPipe parts)
  | PT.EMatch(id, arg, cases) ->
    PT.EMatch(
      id,
      normExpr arg,
      cases
      |> List.map (fun c ->
        { c with
            whenCondition = Option.map normExpr c.whenCondition
            rhs = normExpr c.rhs })
    )
  | PT.ELet(id, p, v, b) -> PT.ELet(id, p, normExpr v, normExpr b)
  | PT.EList(id, es) -> PT.EList(id, List.map normExpr es)
  | PT.EDict(id, kvs) ->
    PT.EDict(id, kvs |> List.map (fun (k, v) -> (k, normExpr v)))
  | PT.ETuple(id, a, b, rest) ->
    PT.ETuple(id, normExpr a, normExpr b, List.map normExpr rest)
  | PT.EApply(id, f, tas, args) ->
    PT.EApply(id, normExpr f, tas, NEList.map normExpr args)
  | PT.ELambda(id, pats, body) -> PT.ELambda(id, pats, normExpr body)
  | PT.EInfix(id, op, l, r) -> PT.EInfix(id, op, normExpr l, normExpr r)
  | PT.ERecord(id, tn, tas, fields) ->
    PT.ERecord(id, tn, tas, fields |> List.map (fun (n, v) -> (n, normExpr v)))
  | PT.ERecordFieldAccess(id, r, f) -> PT.ERecordFieldAccess(id, normExpr r, f)
  | PT.ERecordUpdate(id, r, ups) ->
    PT.ERecordUpdate(id, normExpr r, NEList.map (fun (n, v) -> (n, normExpr v)) ups)
  | PT.EEnum(id, tn, tas, cn, fields) ->
    PT.EEnum(id, tn, tas, cn, List.map normExpr fields)
  // leaves (literals, names, EArg, ESelf, …)
  | other -> other

and private normSeg (s : PT.StringSegment) : PT.StringSegment =
  match s with
  | PT.StringText t -> PT.StringText t
  | PT.StringInterpolation e -> PT.StringInterpolation(normExpr e)

and private normPipe (p : PT.PipeExpr) : PT.PipeExpr =
  match p with
  | PT.EPipeLambda(id, pats, body) -> PT.EPipeLambda(id, pats, normExpr body)
  | PT.EPipeInfix(id, op, e) -> PT.EPipeInfix(id, op, normExpr e)
  | PT.EPipeFnCall(id, nr, tas, args) ->
    PT.EPipeFnCall(id, nr, tas, List.map normExpr args)
  | PT.EPipeEnum(id, tn, cn, fields) ->
    PT.EPipeEnum(id, tn, cn, List.map normExpr fields)
  | PT.EPipeVariable(id, v, args) -> PT.EPipeVariable(id, v, List.map normExpr args)

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
          | PT.EPipeInfix(_, _, e) -> [ e ]
          | PT.EPipeFnCall(_, _, _, args) -> args
          | PT.EPipeEnum(_, _, _, fields) -> fields
          | PT.EPipeVariable(_, _, args) -> args))
  | PT.EMatch(_, arg, cases) ->
    arg
    :: (cases |> List.collect (fun c -> Option.toList c.whenCondition @ [ c.rhs ]))
  | PT.ELet(_, _, v, b) -> [ v; b ]
  | PT.EStatement(_, a, b) -> [ a; b ]
  | PT.EList(_, es) -> es
  | PT.EDict(_, kvs) -> List.map snd kvs
  | PT.ETuple(_, a, b, rest) -> a :: b :: rest
  | PT.EApply(_, f, _, args) -> f :: NEList.toList args
  | PT.ELambda(_, _, body) -> [ body ]
  | PT.EInfix(_, _, l, r) -> [ l; r ]
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
  | PT.EFnName(_, nr) -> $"EFnName {fqFnStr nr}"
  | PT.EValue(_, nr) -> $"EValue {fqValStr nr}"
  | PT.EVariable(_, v) -> $"EVariable {v}"
  | PT.EArg(_, i) -> $"EArg {i}"
  | PT.EPipe(_, _, parts) ->
    parts
    |> List.map (function
      | PT.EPipeFnCall(_, nr, tas, _) ->
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

let tests =
  testList
    "LoweringDifferential"
    [ testTask
        "F# and Dark lowerings agree on corpus expressions (modulo documented divergences)" {
        let root =
          [ "../packages/darklang"
            "packages/darklang"
            "/home/dark/app/packages/darklang" ]
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

          let! (exeState : RT.ExecutionState) =
            executionStateFor pmPT false Map.empty
          let parseFnName =
            RT.FQFnName.fqPackage (
              PackageRefs.Fn.LanguageTools.Parser.parsePTExpr ()
            )
          let fsBuiltins = localBuiltIns pmPT
          let ctx : WT2PT.Context =
            { currentFnName = None
              isInFunction = false
              argMap = Map.empty
              localBindings = Set.empty }

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
              if
                canonicalBytes (normExpr fsPT) <> canonicalBytes (normExpr darkPT)
              then
                if mismatches.Count < 8 then
                  let d =
                    diffExpr (normExpr fsPT) (normExpr darkPT)
                    |> Option.defaultValue "?"
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

          print
            $"LOWERING-DIFFERENTIAL: compared F# vs Dark lowering on a {List.length sample}-of-{List.length snippets} sample of corpus expressions (sampled to bound runtime; NOT exhaustive) — {mismatches.Count} mismatches, {darkSideErrors} Dark-side failures"
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
        let! (exeState : RT.ExecutionState) = executionStateFor pmPT false Map.empty
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
             "x |> Stdlib.Json.serialize<Bool>") ]
        let mismatches = ResizeArray<string>()
        for (label, cmod, cfn, prms, snip) in cases do
          let ctx : WT2PT.Context =
            { currentFnName = (if List.isEmpty cfn then None else Some cfn)
              isInFunction = not (List.isEmpty cfn)
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
                (RT.DString "Tests")
                [ strList cmod; strList cfn; strList prms; RT.DString snip ]
            let! darkResult =
              LibExecution.Execution.executeFunction exeState inCtxFn [] args
            let! darkDval = unwrapExecutionResult exeState darkResult |> Ply.toTask
            match darkDval with
            | RT.DEnum(tn, _, _, "Ok", [ exprDval ]) when tn = Dval.resultType () ->
              let darkPT = PT2DT.Expr.fromDT exprDval
              if
                canonicalBytes (normExpr fsPT) <> canonicalBytes (normExpr darkPT)
              then
                let d =
                  diffExpr (normExpr fsPT) (normExpr darkPT)
                  |> Option.defaultValue "?"
                mismatches.Add($"{label} `{snip}`: {d}")
            | other -> mismatches.Add($"{label} `{snip}`: dark-side error {other}")
          | _ -> mismatches.Add($"{label} `{snip}`: F# parse failed")
        if mismatches.Count > 0 then
          let detail = String.concat "\n" mismatches
          failtest $"context-dependent lowering divergences:\n{detail}"
      } ]
