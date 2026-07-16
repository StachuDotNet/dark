module Builtins.Compiler.Bridge

// The ProgramTypes -> compiler-AST bridge (plan §6), the durable path that
// avoids re-parsing text. It is a TOTAL function: every construct it
// understands it lowers; everything else returns a structured, categorized
// hard-fail (never a guess, never a silent drop). Each `Error` string is a
// blocker record: "unsupported-<what>: <detail>" — greppable and rank-able.
//
// Scope today: the pure-core leaf subset (Int/Bool arithmetic, let, if, vars,
// params). Expands reactively, driven by which hard-fails block the most
// entities — not speculatively.

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

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

let rec bridgeType (t : PT.TypeReference) : Result<AST.Type, string> =
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
  // we lower it to Int64. Sound for values in range (which a pure-core leaf's
  // literals/params are); wrapping semantics would differ at the Int64 edges.
  | PT.TInt -> Ok AST.TInt64
  | PT.TBool -> Ok AST.TBool
  | PT.TString -> Ok AST.TString
  | PT.TFloat -> Ok AST.TFloat64
  | PT.TChar -> Ok AST.TChar
  | PT.TUnit -> Ok AST.TUnit
  // Clean, short blocker tags (the raw `string` dump of e.g. TCustomType is a
  // multi-line record — useless as a rankable blocker key).
  | PT.TCustomType _ -> err "type" "TCustomType"
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

/// Deterministic compiler-side identifier for a package fn, from its content
/// hash. Used to name each bridged FunctionDef and every call to it, so a whole
/// call-graph lowers with consistent names.
let nameFor (hash : string) : string = "fn_" + hash

// ---------------------------------------------------------------------------
// Expressions
//
// `paramNames[i]` gives the compiler-side name for the i-th parameter; Dark
// bodies reference params positionally via EArg(id, index).
// ---------------------------------------------------------------------------

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
  | PT.EMatch(_, _, _) -> err "expr" "EMatch"
  | PT.EList(_, _) -> err "expr" "EList"
  | PT.ETuple(_, _, _, _) -> err "expr" "ETuple"
  | PT.EPipe(_, _, _) -> err "expr" "EPipe"
  | _ -> err "expr" (e.GetType().Name)

// ---------------------------------------------------------------------------
// Functions
// ---------------------------------------------------------------------------

/// Lower a package function to a compiler FunctionDef under the given
/// compiler-side name. Params keep their Dark names (positional EArg refs and
/// any by-name refs both resolve). Generics are not yet supported.
let bridgeFn
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
      |> List.map (fun p -> bridgeType p.typ |> Result.map (fun t -> (p.name, t)))
      |> allOk
    bridgedParams
    |> Result.bind (fun ps ->
      bridgeType fn.returnType
      |> Result.bind (fun retType ->
        bridgeExpr paramNames fn.body
        |> Result.map (fun body ->
          ({ Name = compiledName
             TypeParams = []
             Params = AST.NonEmptyList.fromList ps
             ReturnType = retType
             Body = body } : AST.FunctionDef))))

/// The package-fn hashes a body calls directly (over the supported subset),
/// so the builtin can walk the call graph and bridge every callee. Only needs
/// to be correct for nodes bridgeExpr supports — an unsupported node hard-fails
/// during bridging anyway.
let rec referencedPackageFns (e : PT.Expr) : List<string> =
  let r = referencedPackageFns
  match e with
  | PT.EInfix(_, _, l, rhs) -> r l @ r rhs
  | PT.EIf(_, c, t, Some el) -> r c @ r t @ r el
  | PT.EIf(_, c, t, None) -> r c @ r t
  | PT.ELet(_, _, v, body) -> r v @ r body
  | PT.EApply(_, PT.EFnName(_, nr), _, args) ->
    let here =
      match nr.resolved with
      | Ok resolved ->
        match resolved.name with
        | PT.FQFnName.Package(PT.Hash h) -> [ h ]
        | _ -> []
      | Error _ -> []
    here @ (args |> NEList.toList |> List.collect r)
  | PT.EApply(_, f, _, args) -> r f @ (args |> NEList.toList |> List.collect r)
  | _ -> []
