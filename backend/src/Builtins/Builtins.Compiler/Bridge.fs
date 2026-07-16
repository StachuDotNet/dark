module Builtins.Compiler.Bridge

// The ProgramTypes -> compiler-AST bridge (plan §6), the durable path that
// avoids re-parsing text. It is a TOTAL function: every construct it
// understands it lowers; everything else returns a structured, categorized
// hard-fail (never a guess, never a silent drop). Each `Error` string is a
// blocker record: "unsupported-<what>: <detail>" — greppable and rank-able.
//
// Scope: Int/Bool/String/Char arithmetic + if/let/vars/params, cross-fn calls
// (whole call-graph), and NON-generic custom types (records + enums: type defs,
// construction, field access). Still hard-failing: pattern match (EMatch),
// generics, lists/tuples/dicts, pipes, lambdas, builtins.
//
// `isSum` (below) is the type environment: a map from a custom type's content
// hash to whether it's a sum type (true) or record (false). The builtin builds
// it by fetching the transitive type-def closure; only types in the map lower.

open Prelude

module PT = LibExecution.ProgramTypes

// AST here is the airlifted compiler's AST (top-level module in LibCompiler).

/// The type environment: what a referenced custom type is. Records/enums lower
/// to a named TRecord/TSum + a TypeDef; a (non-generic) alias inlines to its
/// already-bridged target type.
type TypeEntry =
  | TERecord
  | TESum
  | TEAlias of AST.Type

let private err (category : string) (detail : string) : Result<'a, string> =
  Error $"unsupported-{category}: {detail}"

/// Collect a list of Results, short-circuiting on the first Error.
let private allOk (results : List<Result<'a, string>>) : Result<List<'a>, string> =
  (Ok [], results)
  ||> List.fold (fun acc r ->
    match acc, r with
    | Error e, _ -> Error e
    | Ok _, Error e -> Error e
    | Ok xs, Ok x -> Ok(xs @ [ x ]))

/// Deterministic compiler-side identifier for a package fn / type, from its
/// content hash — so a whole call/type graph lowers with consistent names.
let nameFor (hash : string) : string = "fn_" + hash
let nameForType (hash : string) : string = "T_" + hash

/// The package-type hash a type-name resolution points at.
let private typeHash
  (nr : PT.NameResolution<PT.FQTypeName.FQTypeName>)
  : Result<string, string> =
  match nr.resolved with
  | Error _ -> err "type" "unresolved type name"
  | Ok resolved ->
    match resolved.name with
    | PT.FQTypeName.Package(PT.Hash h) -> Ok h

let private typeHashOpt
  (nr : PT.NameResolution<PT.FQTypeName.FQTypeName>)
  : List<string> =
  match nr.resolved with
  | Ok resolved ->
    match resolved.name with
    | PT.FQTypeName.Package(PT.Hash h) -> [ h ]
  | Error _ -> []

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

let rec bridgeType
  (types : Map<string, TypeEntry>)
  (t : PT.TypeReference)
  : Result<AST.Type, string> =
  let recurse = bridgeType types
  match t with
  | PT.TInt64 -> Ok AST.TInt64
  | PT.TInt8 -> Ok AST.TInt8
  | PT.TInt16 -> Ok AST.TInt16
  | PT.TInt32 -> Ok AST.TInt32
  | PT.TInt128 -> Ok AST.TInt128
  | PT.TUInt8 -> Ok AST.TUInt8
  | PT.TUInt16 -> Ok AST.TUInt16
  | PT.TUInt32 -> Ok AST.TUInt32
  | PT.TUInt64 -> Ok AST.TUInt64
  | PT.TUInt128 -> Ok AST.TUInt128
  // Dark's default Int is arbitrary-precision; the compiler has no bigint, so
  // we lower it to Int64. Sound for values in range; wraps at the Int64 edges.
  | PT.TInt -> Ok AST.TInt64
  | PT.TBool -> Ok AST.TBool
  | PT.TString -> Ok AST.TString
  | PT.TFloat -> Ok AST.TFloat64
  | PT.TChar -> Ok AST.TChar
  | PT.TUnit -> Ok AST.TUnit
  | PT.TCustomType(nr, typeArgs) ->
    typeHash nr
    |> Result.bind (fun h ->
      match Map.tryFind h types with
      // Not in the type env => unfetched / unsupported: hard-fail cleanly.
      | None -> err "type" "TCustomType"
      | Some(TEAlias target) ->
        if List.isEmpty typeArgs then Ok target
        else err "generics" "generic type alias"
      | Some entry ->
        typeArgs
        |> List.map recurse
        |> allOk
        |> Result.map (fun args ->
          match entry with
          | TESum -> AST.TSum(nameForType h, args)
          | _ -> AST.TRecord(nameForType h, args)))
  | PT.TVariable v -> Ok(AST.TVar v)
  | PT.TList inner -> recurse inner |> Result.map AST.TList
  | PT.TTuple(a, b, rest) ->
    (a :: b :: rest) |> List.map recurse |> allOk |> Result.map AST.TTuple
  | PT.TDict _ -> err "type" "TDict"
  | PT.TFn(args, ret) ->
    (NEList.toList args |> List.map recurse |> allOk, recurse ret)
    |> fun (a, r) ->
      a |> Result.bind (fun args' -> r |> Result.map (fun ret' -> AST.TFunction(args', ret')))
  | PT.TDB _ -> err "type" "TDB"
  | PT.TUuid -> err "type" "TUuid"
  | PT.TDateTime -> err "type" "TDateTime"
  | other -> err "type" (other.GetType().Name)

// ---------------------------------------------------------------------------
// Infix operators
// ---------------------------------------------------------------------------

let private bridgeInfix (op : PT.Infix) : Result<AST.BinOp, string> =
  match op with
  | PT.InfixFnCall PT.ArithmeticPlus -> Ok AST.Add
  | PT.InfixFnCall PT.ArithmeticMinus -> Ok AST.Sub
  | PT.InfixFnCall PT.ArithmeticMultiply -> Ok AST.Mul
  | PT.InfixFnCall PT.ArithmeticDivide -> Ok AST.Div
  | PT.InfixFnCall PT.ArithmeticModulo -> Ok AST.Mod
  | PT.InfixFnCall PT.ComparisonGreaterThan -> Ok AST.Gt
  | PT.InfixFnCall PT.ComparisonGreaterThanOrEqual -> Ok AST.Gte
  | PT.InfixFnCall PT.ComparisonLessThan -> Ok AST.Lt
  | PT.InfixFnCall PT.ComparisonLessThanOrEqual -> Ok AST.Lte
  | PT.InfixFnCall PT.ComparisonEquals -> Ok AST.Eq
  | PT.InfixFnCall PT.ComparisonNotEquals -> Ok AST.Neq
  | PT.InfixFnCall PT.StringConcat -> Ok AST.StringConcat
  | PT.InfixFnCall PT.ArithmeticPower -> err "infix" "power (no compiler BinOp)"
  | PT.BinOp PT.BinOpAnd -> Ok AST.And
  | PT.BinOp PT.BinOpOr -> Ok AST.Or

// ---------------------------------------------------------------------------
// Expressions
//
// `paramNames[i]` gives the compiler-side name for the i-th parameter; Dark
// bodies reference params positionally via EArg(id, index).
// ---------------------------------------------------------------------------

/// Multi-field enum payloads / record constructions become a single tuple
/// payload on the compiler side (its Constructor takes one optional payload).
let private tuplePayload (xs : List<AST.Expr>) : AST.Expr option =
  match xs with
  | [] -> None
  | [ x ] -> Some x
  | many -> Some(AST.TupleLiteral many)

/// Multi-field enum patterns become a single tuple sub-pattern (mirrors the
/// tuple-payload convention used when constructing enums).
let private tuplePatPayload (ps : List<AST.Pattern>) : AST.Pattern option =
  match ps with
  | [] -> None
  | [ p ] -> Some p
  | many -> Some(AST.PTuple many)

let rec bridgePattern (p : PT.MatchPattern) : Result<AST.Pattern, string> =
  let r = bridgePattern
  match p with
  | PT.MPUnit _ -> Ok AST.PUnit
  | PT.MPBool(_, b) -> Ok(AST.PBool b)
  | PT.MPInt64(_, n) -> Ok(AST.PInt64 n)
  | PT.MPInt(_, big) ->
    if big >= bigint System.Int64.MinValue && big <= bigint System.Int64.MaxValue then
      Ok(AST.PInt64(int64 big))
    else
      err "pattern" "Int outside Int64 range"
  | PT.MPInt8(_, n) -> Ok(AST.PInt8Literal n)
  | PT.MPInt16(_, n) -> Ok(AST.PInt16Literal n)
  | PT.MPInt32(_, n) -> Ok(AST.PInt32Literal n)
  | PT.MPInt128(_, n) -> Ok(AST.PInt128Literal n)
  | PT.MPUInt8(_, n) -> Ok(AST.PUInt8Literal n)
  | PT.MPUInt16(_, n) -> Ok(AST.PUInt16Literal n)
  | PT.MPUInt32(_, n) -> Ok(AST.PUInt32Literal n)
  | PT.MPUInt64(_, n) -> Ok(AST.PUInt64Literal n)
  | PT.MPUInt128(_, n) -> Ok(AST.PUInt128Literal n)
  | PT.MPString(_, s) -> Ok(AST.PString s)
  | PT.MPChar(_, c) -> Ok(AST.PChar c)
  | PT.MPVariable(_, "_") -> Ok AST.PWildcard
  | PT.MPVariable(_, name) -> Ok(AST.PVar name)
  | PT.MPTuple(_, a, b, rest) ->
    (a :: b :: rest) |> List.map r |> allOk |> Result.map AST.PTuple
  | PT.MPList(_, pats) -> pats |> List.map r |> allOk |> Result.map AST.PList
  | PT.MPListCons(_, head, tail) ->
    r head
    |> Result.bind (fun h -> r tail |> Result.map (fun t -> AST.PListCons([ h ], t)))
  | PT.MPEnum(_, caseName, fieldPats) ->
    fieldPats
    |> List.map r
    |> allOk
    |> Result.map (fun ps -> AST.PConstructor(caseName, tuplePatPayload ps))
  | PT.MPFloat _ -> err "pattern" "MPFloat"
  | PT.MPOr _ -> err "pattern" "nested MPOr"

/// Bind a let-pattern over an already-bridged value and body. Tuple patterns
/// desugar to a temp binding plus per-element tuple-access lets. The temp name
/// is reused across nesting levels safely: each RHS is evaluated (reading the
/// outer temp) before the inner temp shadows it.
let rec private bindPattern
  (pat : PT.LetPattern)
  (value : AST.Expr)
  (body : AST.Expr)
  : Result<AST.Expr, string> =
  match pat with
  | PT.LPVariable(_, name) -> Ok(AST.Let(name, value, body))
  | PT.LPWildcard _ -> Ok(AST.Let("__dark_wild", value, body))
  | PT.LPUnit _ -> Ok(AST.Let("__dark_unit", value, body))
  | PT.LPTuple(_, first, second, rest) ->
    let tmp = "__dark_tuple_tmp"
    let subs = first :: second :: rest
    let rec build (i : int) (ps : List<PT.LetPattern>) : Result<AST.Expr, string> =
      match ps with
      | [] -> Ok body
      | p :: more ->
        build (i + 1) more
        |> Result.bind (fun inner -> bindPattern p (AST.TupleAccess(AST.Var tmp, i)) inner)
    build 0 subs |> Result.map (fun inner -> AST.Let(tmp, value, inner))

let rec bridgeExpr (paramNames : string[]) (e : PT.Expr) : Result<AST.Expr, string> =
  let recurse = bridgeExpr paramNames
  match e with
  | PT.EInt64(_, n) -> Ok(AST.Int64Literal n)
  | PT.EInt(_, big) ->
    if big >= bigint System.Int64.MinValue && big <= bigint System.Int64.MaxValue then
      Ok(AST.Int64Literal(int64 big))
    else
      err "literal" "Int outside Int64 range"
  | PT.EBool(_, b) -> Ok(AST.BoolLiteral b)
  | PT.EUnit _ -> Ok AST.UnitLiteral
  | PT.EString(_, [ PT.StringText s ]) -> Ok(AST.StringLiteral s)
  | PT.EString(_, segments) ->
    segments
    |> List.map (fun seg ->
      match seg with
      | PT.StringText s -> Ok(AST.StringText s)
      | PT.StringInterpolation e -> recurse e |> Result.map AST.StringExpr)
    |> allOk
    |> Result.map AST.InterpolatedString
  | PT.EChar(_, c) -> Ok(AST.CharLiteral c)
  | PT.EVariable(_, name) -> Ok(AST.Var name)
  | PT.EArg(_, i) ->
    if i >= 0 && i < paramNames.Length then Ok(AST.Var paramNames[i])
    else err "arg" $"EArg index {i} out of range"
  | PT.EInfix(_, op, l, r) ->
    bridgeInfix op
    |> Result.bind (fun o ->
      recurse l
      |> Result.bind (fun bl -> recurse r |> Result.map (fun br -> AST.BinOp(o, bl, br))))
  | PT.EIf(_, cond, thenE, Some elseE) ->
    recurse cond
    |> Result.bind (fun c ->
      recurse thenE
      |> Result.bind (fun t -> recurse elseE |> Result.map (fun el -> AST.If(c, t, el))))
  | PT.EIf(_, _, _, None) -> err "if" "if without else"
  | PT.ELet(_, pat, value, body) ->
    recurse value
    |> Result.bind (fun v -> recurse body |> Result.bind (fun b -> bindPattern pat v b))
  // Records (type args, if any, are inferred by the compiler's monomorphizer)
  | PT.ERecord(_, nr, _, fields) ->
    typeHash nr
    |> Result.bind (fun h ->
      fields
      |> List.map (fun (fname, fexpr) -> recurse fexpr |> Result.map (fun be -> (fname, be)))
      |> allOk
      |> Result.map (fun fs -> AST.RecordLiteral(nameForType h, fs)))
  | PT.ERecordFieldAccess(_, record, fieldName) ->
    recurse record |> Result.map (fun r -> AST.RecordAccess(r, fieldName))
  | PT.ERecordUpdate(_, record, updates) ->
    recurse record
    |> Result.bind (fun r ->
      updates
      |> NEList.toList
      |> List.map (fun (fname, fexpr) -> recurse fexpr |> Result.map (fun be -> (fname, be)))
      |> allOk
      |> Result.map (fun ups -> AST.RecordUpdate(r, ups)))
  // Enum construction
  | PT.EEnum(_, nr, _, caseName, fields) ->
    typeHash nr
    |> Result.bind (fun h ->
      fields
      |> List.map recurse
      |> allOk
      |> Result.map (fun fs -> AST.Constructor(nameForType h, caseName, tuplePayload fs)))
  // Cross-fn calls: a direct call to a package fn lowers to AST.Call, or
  // AST.TypeApp when the call site carries type args (the compiler monomorphizes
  // it). The callee itself is bridged separately (the builtin walks the graph).
  | PT.EApply(_, PT.EFnName(_, nr), typeArgs, args) ->
    match nr.resolved with
    | Error _ -> err "call" "unresolved fn name"
    | Ok resolved ->
      match resolved.name with
      | PT.FQFnName.Builtin b -> err "builtin" $"{b.name}_v{b.version}"
      | PT.FQFnName.Package(PT.Hash h) ->
        let bridgedArgs = args |> NEList.toList |> List.map recurse |> allOk
        let bridgedTypeArgs = typeArgs |> List.map (bridgeType Map.empty)
        // NB: type args reference only TVar/prims here; a custom type in a type
        // arg would need the type env (rare) — falls through as an error then.
        match allOk bridgedTypeArgs, bridgedArgs with
        | Error e, _ -> Error e
        | _, Error e -> Error e
        | Ok tas, Ok bas ->
          if List.isEmpty tas then
            Ok(AST.Call(nameFor h, AST.NonEmptyList.fromList bas))
          else
            Ok(AST.TypeApp(nameFor h, tas, AST.NonEmptyList.fromList bas))
  // Higher-order application: apply a fn value (variable/lambda/expr) to args.
  | PT.EApply(_, funcExpr, typeArgs, args) ->
    if not (List.isEmpty typeArgs) then
      err "generics" "type args on higher-order apply"
    else
      recurse funcExpr
      |> Result.bind (fun f ->
        args
        |> NEList.toList
        |> List.map recurse
        |> allOk
        |> Result.map (fun bas -> AST.Apply(f, AST.NonEmptyList.fromList bas)))
  | PT.EMatch(_, arg, cases) ->
    recurse arg
    |> Result.bind (fun scrut ->
      cases
      |> List.map (bridgeCase paramNames)
      |> allOk
      |> Result.map (fun cs -> AST.Match(scrut, cs)))
  | PT.EList(_, elems) -> elems |> List.map recurse |> allOk |> Result.map AST.ListLiteral
  | PT.ETuple(_, a, b, rest) ->
    (a :: b :: rest) |> List.map recurse |> allOk |> Result.map AST.TupleLiteral
  | PT.EPipe(_, lhs, parts) ->
    recurse lhs
    |> Result.bind (fun start ->
      (Ok start, parts)
      ||> List.fold (fun accR part ->
        accR |> Result.bind (fun acc -> bridgePipePart paramNames acc part)))
  // Lambda: untyped Dark params get a fresh TVar (unique per node id) that the
  // compiler's inference/monomorphizer resolves from the call context.
  | PT.ELambda(lamId, pats, body) ->
    pats
    |> NEList.toList
    |> List.map (fun p ->
      match p with
      | PT.LPVariable(_, name) -> Ok(name, AST.TVar $"__lam_{lamId}_{name}")
      | _ -> err "lambda" "non-variable lambda param")
    |> allOk
    |> Result.bind (fun ps ->
      recurse body
      |> Result.map (fun b -> AST.Lambda(AST.NonEmptyList.fromList ps, b)))
  | _ -> err "expr" (e.GetType().Name)

/// Lower one match case. A PT case has a single pattern (alternatives live in
/// MPOr); the compiler groups alternatives in MatchCase.Patterns.
and bridgeCase
  (paramNames : string[])
  (c : PT.MatchCase)
  : Result<AST.MatchCase, string> =
  let patterns =
    match c.pat with
    | PT.MPOr(_, alts) -> alts |> NEList.toList |> List.map bridgePattern |> allOk
    | p -> bridgePattern p |> Result.map (fun x -> [ x ])
  patterns
  |> Result.bind (fun pats ->
    let guard =
      match c.whenCondition with
      | None -> Ok None
      | Some g -> bridgeExpr paramNames g |> Result.map Some
    guard
    |> Result.bind (fun gd ->
      bridgeExpr paramNames c.rhs
      |> Result.map (fun body ->
        ({ Patterns = AST.NonEmptyList.fromList pats
           Guard = gd
           Body = body }
        : AST.MatchCase))))

/// Desugar one pipe stage: the accumulated value `piped` becomes the stage's
/// first input. `x |> f a` -> f(x, a); `x |> (+) a` -> x + a; `x |> Some` ->
/// Some x; `x |> v a` -> v(x, a) (v is a fn value). Lambda stages need lambdas.
and bridgePipePart
  (paramNames : string[])
  (piped : AST.Expr)
  (part : PT.PipeExpr)
  : Result<AST.Expr, string> =
  let recurse = bridgeExpr paramNames
  match part with
  | PT.EPipeInfix(_, op, rhs) ->
    bridgeInfix op
    |> Result.bind (fun o -> recurse rhs |> Result.map (fun r -> AST.BinOp(o, piped, r)))
  | PT.EPipeFnCall(_, nr, typeArgs, args) ->
    match nr.resolved with
    | Error _ -> err "call" "unresolved fn name (pipe)"
    | Ok resolved ->
      match resolved.name with
      | PT.FQFnName.Builtin b -> err "builtin" $"{b.name}_v{b.version}"
      | PT.FQFnName.Package(PT.Hash h) ->
        let bargs = args |> List.map recurse |> allOk
        let tas = typeArgs |> List.map (bridgeType Map.empty) |> allOk
        match tas, bargs with
        | Error e, _ -> Error e
        | _, Error e -> Error e
        | Ok tl, Ok bas ->
          let full = piped :: bas
          if List.isEmpty tl then Ok(AST.Call(nameFor h, AST.NonEmptyList.fromList full))
          else Ok(AST.TypeApp(nameFor h, tl, AST.NonEmptyList.fromList full))
  | PT.EPipeEnum(_, nr, caseName, fields) ->
    typeHash nr
    |> Result.bind (fun h ->
      fields
      |> List.map recurse
      |> allOk
      |> Result.map (fun fs ->
        AST.Constructor(nameForType h, caseName, tuplePayload (piped :: fs))))
  | PT.EPipeVariable(_, varName, args) ->
    args
    |> List.map recurse
    |> allOk
    |> Result.map (fun bas ->
      AST.Apply(AST.Var varName, AST.NonEmptyList.fromList (piped :: bas)))
  // `x |> fun p -> body` beta-reduces to binding p = x in body.
  | PT.EPipeLambda(_, pats, body) ->
    match NEList.toList pats with
    | [ single ] -> recurse body |> Result.bind (fun b -> bindPattern single piped b)
    | _ -> err "pipe" "multi-param pipe lambda"

// ---------------------------------------------------------------------------
// Type definitions
// ---------------------------------------------------------------------------

/// Lower a package type (non-generic record/enum) to a compiler TypeDef under
/// the given compiler-side name.
let bridgeTypeDef
  (types : Map<string, TypeEntry>)
  (name : string)
  (pt : PT.PackageType.PackageType)
  : Result<AST.TypeDef, string> =
  let d = pt.declaration
  let tps = d.typeParams
  match d.definition with
  | PT.TypeDeclaration.Alias _ -> err "type" "type alias"
  | PT.TypeDeclaration.Record fields ->
    fields
    |> NEList.toList
    |> List.map (fun (f : PT.TypeDeclaration.RecordField) ->
      bridgeType types f.typ |> Result.map (fun t -> (f.name, t)))
    |> allOk
    |> Result.map (fun fs -> AST.RecordDef(name, tps, fs))
  | PT.TypeDeclaration.Enum cases ->
    cases
    |> NEList.toList
    |> List.map (fun (c : PT.TypeDeclaration.EnumCase) ->
      c.fields
      |> List.map (fun (ef : PT.TypeDeclaration.EnumField) -> bridgeType types ef.typ)
      |> allOk
      |> Result.map (fun fieldTypes ->
        let payload =
          match fieldTypes with
          | [] -> None
          | [ t ] -> Some t
          | ts -> Some(AST.TTuple ts)
        ({ Name = c.name; Payload = payload } : AST.Variant)))
    |> allOk
    |> Result.map (fun variants -> AST.SumTypeDef(name, tps, variants))

// ---------------------------------------------------------------------------
// Functions
// ---------------------------------------------------------------------------

/// Lower a package function to a compiler FunctionDef under the given
/// compiler-side name. Params keep their Dark names (positional EArg refs and
/// any by-name refs both resolve). Generics are not yet supported.
let bridgeFn
  (types : Map<string, TypeEntry>)
  (compiledName : string)
  (fn : PT.PackageFn.PackageFn)
  : Result<AST.FunctionDef, string> =
  let paramList = NEList.toList fn.parameters
  let paramNames = paramList |> List.map (fun p -> p.name) |> Array.ofList
  let bridgedParams =
    paramList
    |> List.map (fun p -> bridgeType types p.typ |> Result.map (fun t -> (p.name, t)))
    |> allOk
  bridgedParams
  |> Result.bind (fun ps ->
    bridgeType types fn.returnType
    |> Result.bind (fun retType ->
      bridgeExpr paramNames fn.body
      |> Result.map (fun body ->
        ({ Name = compiledName
           TypeParams = fn.typeParams
           Params = AST.NonEmptyList.fromList ps
           ReturnType = retType
           Body = body } : AST.FunctionDef))))

// ---------------------------------------------------------------------------
// Reference collectors (for the transitive fetch closures in the builtin)
// ---------------------------------------------------------------------------

/// The package-fn hashes a body calls directly (over the supported subset).
let rec referencedPackageFns (e : PT.Expr) : List<string> =
  let r = referencedPackageFns
  match e with
  | PT.EInfix(_, _, l, rhs) -> r l @ r rhs
  | PT.EIf(_, c, t, Some el) -> r c @ r t @ r el
  | PT.EIf(_, c, t, None) -> r c @ r t
  | PT.ELet(_, _, v, body) -> r v @ r body
  | PT.ERecord(_, _, _, fields) -> fields |> List.collect (snd >> r)
  | PT.ERecordFieldAccess(_, record, _) -> r record
  | PT.EEnum(_, _, _, _, fields) -> fields |> List.collect r
  | PT.EMatch(_, arg, cases) ->
    r arg
    @ (cases
       |> List.collect (fun c ->
         r c.rhs @ (match c.whenCondition with Some g -> r g | None -> [])))
  | PT.EApply(_, PT.EFnName(_, nr), _, args) ->
    let here =
      match nr.resolved with
      | Ok resolved ->
        match resolved.name with
        | PT.FQFnName.Package(PT.Hash h) -> [ h ]
        | PT.FQFnName.Builtin _ -> []
      | Error _ -> []
    here @ (args |> NEList.toList |> List.collect r)
  | PT.EString(_, segs) ->
    segs |> List.collect (fun s -> match s with PT.StringInterpolation e -> r e | PT.StringText _ -> [])
  | PT.ERecordUpdate(_, record, ups) ->
    r record @ (ups |> NEList.toList |> List.collect (snd >> r))
  | PT.EPipe(_, lhs, parts) ->
    r lhs
    @ (parts
       |> List.collect (fun p ->
         match p with
         | PT.EPipeFnCall(_, nr, _, args) ->
           (match nr.resolved with
            | Ok res ->
              match res.name with
              | PT.FQFnName.Package(PT.Hash h) -> [ h ]
              | PT.FQFnName.Builtin _ -> []
            | Error _ -> [])
           @ (args |> List.collect r)
         | PT.EPipeInfix(_, _, e) -> r e
         | PT.EPipeEnum(_, _, _, fields) -> fields |> List.collect r
         | PT.EPipeVariable(_, _, args) -> args |> List.collect r
         | PT.EPipeLambda(_, _, body) -> r body))
  | PT.EList(_, elems) -> elems |> List.collect r
  | PT.ETuple(_, a, b, rest) -> (a :: b :: rest) |> List.collect r
  | PT.ELambda(_, _, body) -> r body
  | PT.EApply(_, f, _, args) -> r f @ (args |> NEList.toList |> List.collect r)
  | _ -> []

/// The custom-type hashes a TypeReference mentions.
let rec typeRefsInType (t : PT.TypeReference) : List<string> =
  let r = typeRefsInType
  match t with
  | PT.TCustomType(nr, args) -> typeHashOpt nr @ List.collect r args
  | PT.TList inner -> r inner
  | PT.TTuple(a, b, rest) -> r a @ r b @ List.collect r rest
  | PT.TDict inner -> r inner
  | PT.TFn(args, ret) -> (NEList.toList args |> List.collect r) @ r ret
  | _ -> []

/// The custom-type hashes a body mentions (record/enum constructions).
let rec typeRefsInExpr (e : PT.Expr) : List<string> =
  let r = typeRefsInExpr
  match e with
  | PT.ERecord(_, nr, _, fields) -> typeHashOpt nr @ (fields |> List.collect (snd >> r))
  | PT.EEnum(_, nr, _, _, fields) -> typeHashOpt nr @ (fields |> List.collect r)
  | PT.ERecordFieldAccess(_, record, _) -> r record
  | PT.EInfix(_, _, l, rhs) -> r l @ r rhs
  | PT.EIf(_, c, t, Some el) -> r c @ r t @ r el
  | PT.EIf(_, c, t, None) -> r c @ r t
  | PT.ELet(_, _, v, body) -> r v @ r body
  | PT.EMatch(_, arg, cases) ->
    r arg
    @ (cases
       |> List.collect (fun c ->
         r c.rhs @ (match c.whenCondition with Some g -> r g | None -> [])))
  | PT.EString(_, segs) ->
    segs |> List.collect (fun s -> match s with PT.StringInterpolation e -> r e | PT.StringText _ -> [])
  | PT.ERecordUpdate(_, record, ups) ->
    r record @ (ups |> NEList.toList |> List.collect (snd >> r))
  | PT.EPipe(_, lhs, parts) ->
    r lhs
    @ (parts
       |> List.collect (fun p ->
         match p with
         | PT.EPipeFnCall(_, nr, _, args) ->
           (match nr.resolved with
            | Ok res ->
              match res.name with
              | PT.FQFnName.Package(PT.Hash h) -> [ h ]
              | PT.FQFnName.Builtin _ -> []
            | Error _ -> [])
           @ (args |> List.collect r)
         | PT.EPipeInfix(_, _, e) -> r e
         | PT.EPipeEnum(_, _, _, fields) -> fields |> List.collect r
         | PT.EPipeVariable(_, _, args) -> args |> List.collect r
         | PT.EPipeLambda(_, _, body) -> r body))
  | PT.EList(_, elems) -> elems |> List.collect r
  | PT.ETuple(_, a, b, rest) -> (a :: b :: rest) |> List.collect r
  | PT.ELambda(_, _, body) -> r body
  | PT.EApply(_, f, _, args) -> r f @ (args |> NEList.toList |> List.collect r)
  | _ -> []

/// The custom-type hashes a whole package fn mentions (signature + body).
let typeRefsInFn (fn : PT.PackageFn.PackageFn) : List<string> =
  (fn.parameters |> NEList.toList |> List.collect (fun p -> typeRefsInType p.typ))
  @ typeRefsInType fn.returnType
  @ typeRefsInExpr fn.body

/// The custom-type hashes a type definition mentions (field/case types).
let typeRefsInTypeDef (pt : PT.PackageType.PackageType) : List<string> =
  match pt.declaration.definition with
  | PT.TypeDeclaration.Alias t -> typeRefsInType t
  | PT.TypeDeclaration.Record fields ->
    fields |> NEList.toList |> List.collect (fun f -> typeRefsInType f.typ)
  | PT.TypeDeclaration.Enum cases ->
    cases
    |> NEList.toList
    |> List.collect (fun c -> c.fields |> List.collect (fun ef -> typeRefsInType ef.typ))
