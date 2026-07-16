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
  (isSum : Map<string, bool>)
  (t : PT.TypeReference)
  : Result<AST.Type, string> =
  let recurse = bridgeType isSum
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
      match Map.tryFind h isSum with
      // Not in the type env => generic / alias / unfetched: hard-fail cleanly.
      | None -> err "type" "TCustomType"
      | Some sum ->
        typeArgs
        |> List.map recurse
        |> allOk
        |> Result.map (fun args ->
          if sum then AST.TSum(nameForType h, args)
          else AST.TRecord(nameForType h, args)))
  | PT.TList _ -> err "type" "TList"
  | PT.TTuple _ -> err "type" "TTuple"
  | PT.TDict _ -> err "type" "TDict"
  | PT.TFn _ -> err "type" "TFn"
  | PT.TDB _ -> err "type" "TDB"
  | PT.TVariable v -> err "type" $"TVariable {v}"
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
  | PT.EString(_, _) -> err "expr" "EString with interpolation/multi-segment"
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
  | PT.ELet(_, PT.LPVariable(_, name), value, body) ->
    recurse value
    |> Result.bind (fun v -> recurse body |> Result.map (fun b -> AST.Let(name, v, b)))
  | PT.ELet(_, _, _, _) -> err "let" "non-variable let pattern"
  // Records
  | PT.ERecord(_, nr, typeArgs, fields) ->
    if not (List.isEmpty typeArgs) then
      err "generics" "type args on record literal"
    else
      typeHash nr
      |> Result.bind (fun h ->
        fields
        |> List.map (fun (fname, fexpr) -> recurse fexpr |> Result.map (fun be -> (fname, be)))
        |> allOk
        |> Result.map (fun fs -> AST.RecordLiteral(nameForType h, fs)))
  | PT.ERecordFieldAccess(_, record, fieldName) ->
    recurse record |> Result.map (fun r -> AST.RecordAccess(r, fieldName))
  // Enum construction
  | PT.EEnum(_, nr, typeArgs, caseName, fields) ->
    if not (List.isEmpty typeArgs) then
      err "generics" "type args on enum construction"
    else
      typeHash nr
      |> Result.bind (fun h ->
        fields
        |> List.map recurse
        |> allOk
        |> Result.map (fun fs -> AST.Constructor(nameForType h, caseName, tuplePayload fs)))
  // Cross-fn calls: a direct call to a package fn lowers to AST.Call; the
  // callee itself is bridged separately (the builtin walks the call graph).
  | PT.EApply(_, PT.EFnName(_, nr), typeArgs, args) ->
    if not (List.isEmpty typeArgs) then
      err "generics" "type args at call site"
    else
      match nr.resolved with
      | Error _ -> err "call" "unresolved fn name"
      | Ok resolved ->
        match resolved.name with
        | PT.FQFnName.Builtin b -> err "builtin" $"{b.name}_v{b.version}"
        | PT.FQFnName.Package(PT.Hash h) ->
          args
          |> NEList.toList
          |> List.map recurse
          |> allOk
          |> Result.map (fun bridged ->
            AST.Call(nameFor h, AST.NonEmptyList.fromList bridged))
  | PT.EApply(_, _, _, _) -> err "expr" "EApply of a non-fn-name (higher-order)"
  | PT.EMatch(_, arg, cases) ->
    recurse arg
    |> Result.bind (fun scrut ->
      cases
      |> List.map (bridgeCase paramNames)
      |> allOk
      |> Result.map (fun cs -> AST.Match(scrut, cs)))
  | PT.EList(_, _) -> err "expr" "EList"
  | PT.ETuple(_, _, _, _) -> err "expr" "ETuple"
  | PT.EPipe(_, _, _) -> err "expr" "EPipe"
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

// ---------------------------------------------------------------------------
// Type definitions
// ---------------------------------------------------------------------------

/// Lower a package type (non-generic record/enum) to a compiler TypeDef under
/// the given compiler-side name.
let bridgeTypeDef
  (isSum : Map<string, bool>)
  (name : string)
  (pt : PT.PackageType.PackageType)
  : Result<AST.TypeDef, string> =
  let d = pt.declaration
  if not (List.isEmpty d.typeParams) then
    err "generics" "generic type definition"
  else
    match d.definition with
    | PT.TypeDeclaration.Alias _ -> err "type" "type alias"
    | PT.TypeDeclaration.Record fields ->
      fields
      |> NEList.toList
      |> List.map (fun (f : PT.TypeDeclaration.RecordField) ->
        bridgeType isSum f.typ |> Result.map (fun t -> (f.name, t)))
      |> allOk
      |> Result.map (fun fs -> AST.RecordDef(name, [], fs))
    | PT.TypeDeclaration.Enum cases ->
      cases
      |> NEList.toList
      |> List.map (fun (c : PT.TypeDeclaration.EnumCase) ->
        c.fields
        |> List.map (fun (ef : PT.TypeDeclaration.EnumField) -> bridgeType isSum ef.typ)
        |> allOk
        |> Result.map (fun fieldTypes ->
          let payload =
            match fieldTypes with
            | [] -> None
            | [ t ] -> Some t
            | ts -> Some(AST.TTuple ts)
          ({ Name = c.name; Payload = payload } : AST.Variant)))
      |> allOk
      |> Result.map (fun variants -> AST.SumTypeDef(name, [], variants))

// ---------------------------------------------------------------------------
// Functions
// ---------------------------------------------------------------------------

/// Lower a package function to a compiler FunctionDef under the given
/// compiler-side name. Params keep their Dark names (positional EArg refs and
/// any by-name refs both resolve). Generics are not yet supported.
let bridgeFn
  (isSum : Map<string, bool>)
  (compiledName : string)
  (fn : PT.PackageFn.PackageFn)
  : Result<AST.FunctionDef, string> =
  if not (List.isEmpty fn.typeParams) then
    err "generics" $"function has type params: {fn.typeParams}"
  else
    let paramList = NEList.toList fn.parameters
    let paramNames = paramList |> List.map (fun p -> p.name) |> Array.ofList
    let bridgedParams =
      paramList
      |> List.map (fun p -> bridgeType isSum p.typ |> Result.map (fun t -> (p.name, t)))
      |> allOk
    bridgedParams
    |> Result.bind (fun ps ->
      bridgeType isSum fn.returnType
      |> Result.bind (fun retType ->
        bridgeExpr paramNames fn.body
        |> Result.map (fun body ->
          ({ Name = compiledName
             TypeParams = []
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
