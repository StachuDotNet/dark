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

/// Threaded through expression bridging. `Params` maps EArg indices to the
/// enclosing fn's compiler-side param names; `Self` is the current fn's compiled
/// name (for ESelf recursion); `Values` inlines referenced package constants.
/// How a wire-marshalable builtin arg is stringified on the compiled side.
type WireArg =
  | WAString
  | WAInt
  | WABool
  | WAFloat
  | WAUnit // a Unit param carries no data; sent as the literal "unit"

/// How a builtin's wire response is unmarshaled back into a native value.
type WireRet =
  | WRUnit
  | WRString
  | WRInt // Int64
  | WRBool
  /// Any container (Option/Result/List of marshalable types), decoded recursively
  /// over the bridged return type. Replaces the old fixed Option/Result-of-String
  /// cases with a general, composable path.
  | WRTyped of AST.Type

type BridgeCtx =
  { Params : string[]
    Self : string
    Values : Map<string, AST.Expr>
    /// Effectful builtins routable through the host RPC seam: name -> (per-arg
    /// wire types, return wire type). Built from the live runtime.
    Effectful : Map<string, WireArg list * WireRet> }

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
// The "fn." prefix is deliberately dotted: the compiler's typechecker treats
// dotted names as module-scoped (like `Stdlib.List.map`) and INFERS type args
// for bare generic calls, whereas a dotless name is a "local user-defined
// generic" that `RequireExplicitTypeArgsForBareCalls` demands explicit type args
// for. Package fns are qualified, so module-scoped is correct — and it unblocks
// every bare generic call site. The dot is only a resolver signal: x64 labels are
// internal Map<string,pos> keys (never emitted as assembler text), so it's
// codegen-safe and normalizes to the same binary.
let nameFor (hash : string) : string = "fn." + hash
let nameForType (hash : string) : string = "T_" + hash

// Dark's Option/Result are ordinary package types, but the compiler ships its OWN
// native Stdlib.Option.Option / Stdlib.Result.Result (its stdlib and the daemon's
// unmarshaller use them). Bridging Dark's versions to a distinct T_<hash> splits
// Option/Result into two non-unifying types AND double-defines the Some/None/Ok/
// Error constructors. So map both hashes to the native names everywhere and skip
// emitting a TypeDef for them (buildPieces filters via isNativeType).
let optionTypeHash : string = LibExecution.PackageRefs.Type.Stdlib.option ()
let resultTypeHash : string = LibExecution.PackageRefs.Type.Stdlib.result ()

let isNativeType (h : string) : bool = h = optionTypeHash || h = resultTypeHash

/// The compiler-side type name for a package type hash: the native Option/Result
/// for those two, else a T_<hash>.
let compilerTypeName (h : string) : string =
  if h = optionTypeHash then "Stdlib.Option.Option"
  elif h = resultTypeHash then "Stdlib.Result.Result"
  else nameForType h

/// Pure-tier builtin migration (row 1 of the seam's dispatch table, see
/// notes/compiler-merge/BUILTINS-ARCHITECTURE.md): a call to one of these
/// main-repo builtins lowers to a call to the compiler's OWN native stdlib fn,
/// which implements the same Darklang semantics on ~12 intrinsics. Curated and
/// conservative — the type checker rejects any signature mismatch (safe), and
/// each entry must be differential-tested (compiled vs interpreter) before it's
/// trusted for execution. Effectful builtins are NOT here — those await the
/// runtime seam (daemon). Keyed by the main-repo builtin's bare name.
let builtinToStdlib : Map<string, string> =
  Map
    [ // String
      "stringLength", "Stdlib.String.length"
      "stringIsEmpty", "Stdlib.String.isEmpty"
      "stringContains", "Stdlib.String.contains"
      "stringJoin", "Stdlib.String.join"
      "stringSplit", "Stdlib.String.split"
      "stringSlice", "Stdlib.String.slice"
      "stringTrim", "Stdlib.String.trim"
      "stringTrimStart", "Stdlib.String.trimStart"
      "stringTrimEnd", "Stdlib.String.trimEnd"
      "stringReverse", "Stdlib.String.reverse"
      "stringToLowercase", "Stdlib.String.toLowerCase"
      "stringToUppercase", "Stdlib.String.toUpperCase"
      "stringStartsWith", "Stdlib.String.startsWith"
      "stringEndsWith", "Stdlib.String.endsWith"
      "stringPrepend", "Stdlib.String.prepend"
      "stringIndexOf", "Stdlib.String.indexOf"
      "stringPadStart", "Stdlib.String.padStart"
      "stringPadEnd", "Stdlib.String.padEnd"
      "stringRepeat", "Stdlib.String.repeat"
      // List
      "listLength", "Stdlib.List.length"
      "listAppend", "Stdlib.List.append"
      "listReverse", "Stdlib.List.reverse"
      "listMap", "Stdlib.List.map"
      "listFilter", "Stdlib.List.filter"
      "listFold", "Stdlib.List.fold"
      "listFlatten", "Stdlib.List.flatten"
      "listIsEmpty", "Stdlib.List.isEmpty"
      "listTake", "Stdlib.List.take"
      "listDrop", "Stdlib.List.drop"
      "listPush", "Stdlib.List.push"
      "listPushBack", "Stdlib.List.pushBack"
      "listSingleton", "Stdlib.List.singleton"
      "listGetAt", "Stdlib.List.getAt"
      "listMember", "Stdlib.List.member"
      // Bool
      "boolNot", "Stdlib.Bool.not"
      "boolAnd", "Stdlib.Bool.and"
      "boolOr", "Stdlib.Bool.or"
      "boolXor", "Stdlib.Bool.xor"
      // Int (lowered to Int64, matching TInt->TInt64)
      "intToString", "Stdlib.Int64.toString"
      "int64ToString", "Stdlib.Int64.toString"
      "intGreaterThan", "Stdlib.Int64.greaterThan"
      "int64GreaterThan", "Stdlib.Int64.greaterThan"
      "intGreaterThanOrEqualTo", "Stdlib.Int64.greaterThanOrEqualTo"
      "intLessThan", "Stdlib.Int64.lessThan"
      "int64LessThan", "Stdlib.Int64.lessThan"
      "intDivide", "Stdlib.Int64.div"
      "int64Divide", "Stdlib.Int64.div"
      "intMax", "Stdlib.Int64.max"
      "intMin", "Stdlib.Int64.min"
      "intAbsoluteValue", "Stdlib.Int64.absoluteValue"
      // Option / Result
      "optionMap", "Stdlib.Option.map"
      "optionWithDefault", "Stdlib.Option.withDefault"
      "optionAndThen", "Stdlib.Option.andThen"
      "optionIsSome", "Stdlib.Option.isSome"
      "optionIsNone", "Stdlib.Option.isNone"
      "resultMap", "Stdlib.Result.map"
      "resultWithDefault", "Stdlib.Result.withDefault"
      "resultAndThen", "Stdlib.Result.andThen"
      "resultIsOk", "Stdlib.Result.isOk"
      "resultIsError", "Stdlib.Result.isError"
      // Int8
      "int8LessThan", "Stdlib.Int8.lessThan"
      "int8GreaterThan", "Stdlib.Int8.greaterThan"
      "int8GreaterThanOrEqualTo", "Stdlib.Int8.greaterThanOrEqualTo"
      "int8LessThanOrEqualTo", "Stdlib.Int8.lessThanOrEqualTo"
      "int8ToString", "Stdlib.Int8.toString"
      "int8Add", "Stdlib.Int8.add"
      "int8Subtract", "Stdlib.Int8.sub"
      "int8Multiply", "Stdlib.Int8.mul"
      "int8Divide", "Stdlib.Int8.div"
      "int8Mod", "Stdlib.Int8.mod"
      // Int16
      "int16LessThan", "Stdlib.Int16.lessThan"
      "int16GreaterThan", "Stdlib.Int16.greaterThan"
      "int16GreaterThanOrEqualTo", "Stdlib.Int16.greaterThanOrEqualTo"
      "int16LessThanOrEqualTo", "Stdlib.Int16.lessThanOrEqualTo"
      "int16ToString", "Stdlib.Int16.toString"
      "int16Add", "Stdlib.Int16.add"
      "int16Subtract", "Stdlib.Int16.sub"
      "int16Multiply", "Stdlib.Int16.mul"
      "int16Divide", "Stdlib.Int16.div"
      "int16Mod", "Stdlib.Int16.mod"
      // Int32
      "int32LessThan", "Stdlib.Int32.lessThan"
      "int32GreaterThan", "Stdlib.Int32.greaterThan"
      "int32GreaterThanOrEqualTo", "Stdlib.Int32.greaterThanOrEqualTo"
      "int32LessThanOrEqualTo", "Stdlib.Int32.lessThanOrEqualTo"
      "int32ToString", "Stdlib.Int32.toString"
      "int32Add", "Stdlib.Int32.add"
      "int32Subtract", "Stdlib.Int32.sub"
      "int32Multiply", "Stdlib.Int32.mul"
      "int32Divide", "Stdlib.Int32.div"
      "int32Mod", "Stdlib.Int32.mod"
      // UInt8
      "uint8LessThan", "Stdlib.UInt8.lessThan"
      "uint8GreaterThan", "Stdlib.UInt8.greaterThan"
      "uint8GreaterThanOrEqualTo", "Stdlib.UInt8.greaterThanOrEqualTo"
      "uint8LessThanOrEqualTo", "Stdlib.UInt8.lessThanOrEqualTo"
      "uint8ToString", "Stdlib.UInt8.toString"
      "uint8Add", "Stdlib.UInt8.add"
      "uint8Subtract", "Stdlib.UInt8.sub"
      "uint8Multiply", "Stdlib.UInt8.mul"
      "uint8Divide", "Stdlib.UInt8.div"
      "uint8Mod", "Stdlib.UInt8.mod"
      // UInt16
      "uint16LessThan", "Stdlib.UInt16.lessThan"
      "uint16GreaterThan", "Stdlib.UInt16.greaterThan"
      "uint16GreaterThanOrEqualTo", "Stdlib.UInt16.greaterThanOrEqualTo"
      "uint16LessThanOrEqualTo", "Stdlib.UInt16.lessThanOrEqualTo"
      "uint16ToString", "Stdlib.UInt16.toString"
      "uint16Add", "Stdlib.UInt16.add"
      "uint16Subtract", "Stdlib.UInt16.sub"
      "uint16Multiply", "Stdlib.UInt16.mul"
      "uint16Divide", "Stdlib.UInt16.div"
      "uint16Mod", "Stdlib.UInt16.mod"
      // UInt32
      "uint32LessThan", "Stdlib.UInt32.lessThan"
      "uint32GreaterThan", "Stdlib.UInt32.greaterThan"
      "uint32GreaterThanOrEqualTo", "Stdlib.UInt32.greaterThanOrEqualTo"
      "uint32LessThanOrEqualTo", "Stdlib.UInt32.lessThanOrEqualTo"
      "uint32ToString", "Stdlib.UInt32.toString"
      "uint32Add", "Stdlib.UInt32.add"
      "uint32Subtract", "Stdlib.UInt32.sub"
      "uint32Multiply", "Stdlib.UInt32.mul"
      "uint32Divide", "Stdlib.UInt32.div"
      "uint32Mod", "Stdlib.UInt32.mod"
      // UInt64
      "uint64LessThan", "Stdlib.UInt64.lessThan"
      "uint64GreaterThan", "Stdlib.UInt64.greaterThan"
      "uint64GreaterThanOrEqualTo", "Stdlib.UInt64.greaterThanOrEqualTo"
      "uint64LessThanOrEqualTo", "Stdlib.UInt64.lessThanOrEqualTo"
      "uint64ToString", "Stdlib.UInt64.toString"
      "uint64Add", "Stdlib.UInt64.add"
      "uint64Subtract", "Stdlib.UInt64.sub"
      "uint64Multiply", "Stdlib.UInt64.mul"
      "uint64Divide", "Stdlib.UInt64.div"
      "uint64Mod", "Stdlib.UInt64.mod"
      // Float
      "floatMultiply", "Stdlib.Float.multiply"
      // Int64 (full)
      "int64LessThan", "Stdlib.Int64.lessThan"
      "int64GreaterThan", "Stdlib.Int64.greaterThan"
      "int64GreaterThanOrEqualTo", "Stdlib.Int64.greaterThanOrEqualTo"
      "int64LessThanOrEqualTo", "Stdlib.Int64.lessThanOrEqualTo"
      "int64ToString", "Stdlib.Int64.toString"
      "int64Add", "Stdlib.Int64.add"
      "int64Subtract", "Stdlib.Int64.sub"
      "int64Multiply", "Stdlib.Int64.mul"
      "int64Divide", "Stdlib.Int64.div"
      "int64Mod", "Stdlib.Int64.mod"
      "int64Power", "Stdlib.Int64.power"
      "int64Negate", "Stdlib.Int64.negate"
      "int64AbsoluteValue", "Stdlib.Int64.absoluteValue"
      "int64Max", "Stdlib.Int64.max"
      "int64Min", "Stdlib.Int64.min"
      "int64Clamp", "Stdlib.Int64.clamp"
      "int64BitwiseAnd", "Stdlib.Int64.bitwiseAnd"
      "int64BitwiseOr", "Stdlib.Int64.bitwiseOr"
      "int64BitwiseXor", "Stdlib.Int64.bitwiseXor"
      "int64BitwiseNot", "Stdlib.Int64.bitwiseNot"
      "int64ShiftLeft", "Stdlib.Int64.shiftLeft"
      "int64ShiftRight", "Stdlib.Int64.shiftRight"
      "int64IsEven", "Stdlib.Int64.isEven"
      "int64IsOdd", "Stdlib.Int64.isOdd"
      // Int8 (extra ops)
      "int8Power", "Stdlib.Int8.power"
      "int8Negate", "Stdlib.Int8.negate"
      "int8AbsoluteValue", "Stdlib.Int8.absoluteValue"
      "int8Max", "Stdlib.Int8.max"
      "int8Min", "Stdlib.Int8.min"
      "int8Clamp", "Stdlib.Int8.clamp"
      "int8BitwiseAnd", "Stdlib.Int8.bitwiseAnd"
      "int8BitwiseOr", "Stdlib.Int8.bitwiseOr"
      "int8BitwiseXor", "Stdlib.Int8.bitwiseXor"
      "int8BitwiseNot", "Stdlib.Int8.bitwiseNot"
      "int8ShiftLeft", "Stdlib.Int8.shiftLeft"
      "int8ShiftRight", "Stdlib.Int8.shiftRight"
      "int8IsEven", "Stdlib.Int8.isEven"
      "int8IsOdd", "Stdlib.Int8.isOdd"
      // Int16 (extra ops)
      "int16Power", "Stdlib.Int16.power"
      "int16Negate", "Stdlib.Int16.negate"
      "int16AbsoluteValue", "Stdlib.Int16.absoluteValue"
      "int16Max", "Stdlib.Int16.max"
      "int16Min", "Stdlib.Int16.min"
      "int16Clamp", "Stdlib.Int16.clamp"
      "int16BitwiseAnd", "Stdlib.Int16.bitwiseAnd"
      "int16BitwiseOr", "Stdlib.Int16.bitwiseOr"
      "int16BitwiseXor", "Stdlib.Int16.bitwiseXor"
      "int16BitwiseNot", "Stdlib.Int16.bitwiseNot"
      "int16ShiftLeft", "Stdlib.Int16.shiftLeft"
      "int16ShiftRight", "Stdlib.Int16.shiftRight"
      "int16IsEven", "Stdlib.Int16.isEven"
      "int16IsOdd", "Stdlib.Int16.isOdd"
      // Int32 (extra ops)
      "int32Power", "Stdlib.Int32.power"
      "int32Negate", "Stdlib.Int32.negate"
      "int32AbsoluteValue", "Stdlib.Int32.absoluteValue"
      "int32Max", "Stdlib.Int32.max"
      "int32Min", "Stdlib.Int32.min"
      "int32Clamp", "Stdlib.Int32.clamp"
      "int32BitwiseAnd", "Stdlib.Int32.bitwiseAnd"
      "int32BitwiseOr", "Stdlib.Int32.bitwiseOr"
      "int32BitwiseXor", "Stdlib.Int32.bitwiseXor"
      "int32BitwiseNot", "Stdlib.Int32.bitwiseNot"
      "int32ShiftLeft", "Stdlib.Int32.shiftLeft"
      "int32ShiftRight", "Stdlib.Int32.shiftRight"
      "int32IsEven", "Stdlib.Int32.isEven"
      "int32IsOdd", "Stdlib.Int32.isOdd"
      // UInt8 (extra ops)
      "uint8Power", "Stdlib.UInt8.power"
      "uint8Negate", "Stdlib.UInt8.negate"
      "uint8AbsoluteValue", "Stdlib.UInt8.absoluteValue"
      "uint8Max", "Stdlib.UInt8.max"
      "uint8Min", "Stdlib.UInt8.min"
      "uint8Clamp", "Stdlib.UInt8.clamp"
      "uint8BitwiseAnd", "Stdlib.UInt8.bitwiseAnd"
      "uint8BitwiseOr", "Stdlib.UInt8.bitwiseOr"
      "uint8BitwiseXor", "Stdlib.UInt8.bitwiseXor"
      "uint8BitwiseNot", "Stdlib.UInt8.bitwiseNot"
      "uint8ShiftLeft", "Stdlib.UInt8.shiftLeft"
      "uint8ShiftRight", "Stdlib.UInt8.shiftRight"
      "uint8IsEven", "Stdlib.UInt8.isEven"
      "uint8IsOdd", "Stdlib.UInt8.isOdd"
      // UInt16 (extra ops)
      "uint16Power", "Stdlib.UInt16.power"
      "uint16Negate", "Stdlib.UInt16.negate"
      "uint16AbsoluteValue", "Stdlib.UInt16.absoluteValue"
      "uint16Max", "Stdlib.UInt16.max"
      "uint16Min", "Stdlib.UInt16.min"
      "uint16Clamp", "Stdlib.UInt16.clamp"
      "uint16BitwiseAnd", "Stdlib.UInt16.bitwiseAnd"
      "uint16BitwiseOr", "Stdlib.UInt16.bitwiseOr"
      "uint16BitwiseXor", "Stdlib.UInt16.bitwiseXor"
      "uint16BitwiseNot", "Stdlib.UInt16.bitwiseNot"
      "uint16ShiftLeft", "Stdlib.UInt16.shiftLeft"
      "uint16ShiftRight", "Stdlib.UInt16.shiftRight"
      "uint16IsEven", "Stdlib.UInt16.isEven"
      "uint16IsOdd", "Stdlib.UInt16.isOdd"
      // UInt32 (extra ops)
      "uint32Power", "Stdlib.UInt32.power"
      "uint32Negate", "Stdlib.UInt32.negate"
      "uint32AbsoluteValue", "Stdlib.UInt32.absoluteValue"
      "uint32Max", "Stdlib.UInt32.max"
      "uint32Min", "Stdlib.UInt32.min"
      "uint32Clamp", "Stdlib.UInt32.clamp"
      "uint32BitwiseAnd", "Stdlib.UInt32.bitwiseAnd"
      "uint32BitwiseOr", "Stdlib.UInt32.bitwiseOr"
      "uint32BitwiseXor", "Stdlib.UInt32.bitwiseXor"
      "uint32BitwiseNot", "Stdlib.UInt32.bitwiseNot"
      "uint32ShiftLeft", "Stdlib.UInt32.shiftLeft"
      "uint32ShiftRight", "Stdlib.UInt32.shiftRight"
      "uint32IsEven", "Stdlib.UInt32.isEven"
      "uint32IsOdd", "Stdlib.UInt32.isOdd"
      // UInt64 (extra ops)
      "uint64Power", "Stdlib.UInt64.power"
      "uint64Negate", "Stdlib.UInt64.negate"
      "uint64AbsoluteValue", "Stdlib.UInt64.absoluteValue"
      "uint64Max", "Stdlib.UInt64.max"
      "uint64Min", "Stdlib.UInt64.min"
      "uint64Clamp", "Stdlib.UInt64.clamp"
      "uint64BitwiseAnd", "Stdlib.UInt64.bitwiseAnd"
      "uint64BitwiseOr", "Stdlib.UInt64.bitwiseOr"
      "uint64BitwiseXor", "Stdlib.UInt64.bitwiseXor"
      "uint64BitwiseNot", "Stdlib.UInt64.bitwiseNot"
      "uint64ShiftLeft", "Stdlib.UInt64.shiftLeft"
      "uint64ShiftRight", "Stdlib.UInt64.shiftRight"
      "uint64IsEven", "Stdlib.UInt64.isEven"
      "uint64IsOdd", "Stdlib.UInt64.isOdd"
      // Char
      "charToString", "Stdlib.Char.toString"
      "charToLowercase", "Stdlib.Char.toLowercase"
      "charToUppercase", "Stdlib.Char.toUppercase"
      "charToAsciiCode", "Stdlib.Char.toCode"
      "charFromAsciiCode", "Stdlib.Char.fromCode"
      "charIsDigit", "Stdlib.Char.isDigit"
      "charIsLetter", "Stdlib.Char.isLetter"
      "charIsWhitespace", "Stdlib.Char.isWhitespace"
      "charIsAlphanumeric", "Stdlib.Char.isAlphanumeric"
      "charIsLowercase", "Stdlib.Char.isLowercase"
      "charIsUppercase", "Stdlib.Char.isUppercase"
      "boolToString", "Stdlib.Bool.toString" ]

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
      // Option/Result -> native compiler sum types, independent of the type env.
      if isNativeType h then
        typeArgs
        |> List.map recurse
        |> allOk
        |> Result.map (fun args -> AST.TSum(compilerTypeName h, args))
      else
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
  | PT.TBlob -> Ok AST.TBytes
  | PT.TDB _ -> err "type" "TDB"
  // Host types with no compiler representation are carried as their canonical
  // string form. Sound for well-typed programs: Dark treats a Uuid/DateTime
  // opaquely (pass, store, compare-by-equality), and every builtin that produces
  // or consumes one marshals through the daemon (Guid/ISO string round-trip), so
  // the compiled value stays in lockstep with the interpreter's.
  | PT.TUuid -> Ok AST.TString
  | PT.TDateTime -> Ok AST.TString
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

/// Whether a bridged return type can be recursively unmarshaled from the wire.
let rec isMarshalableType (t : AST.Type) : bool =
  match t with
  | AST.TString | AST.TInt64 | AST.TBool | AST.TUnit -> true
  | AST.TSum("Stdlib.Option.Option", [ inner ]) -> isMarshalableType inner
  | AST.TSum("Stdlib.Result.Result", [ a; b ]) -> isMarshalableType a && isMarshalableType b
  | AST.TList inner -> isMarshalableType inner
  | _ -> false

/// Recursively decode a wire response (an AST String expr) into a native value of
/// compiler type `t`. Compound children were escaped by the daemon (escWire), so
/// each is unescaped before decoding. `d` keeps the let-bound temp unique per depth.
let rec unmarshalTyped (d : int) (t : AST.Type) (src : AST.Expr) : AST.Expr =
  let rv = "__rpc_" + string d
  let r = AST.Var rv
  let len e = AST.Call("Stdlib.String.length", AST.NonEmptyList.singleton e)
  let slice e a b = AST.Call("Stdlib.String.slice", AST.NonEmptyList.fromList [ e; a; b ])
  let starts e p = AST.Call("Stdlib.String.startsWith", AST.NonEmptyList.fromList [ e; AST.StringLiteral p ])
  let unesc e = AST.Call("Stdlib.hostRpcUnescape", AST.NonEmptyList.singleton e)
  let parseInt e = AST.Call("Stdlib.hostRpcParseInt", AST.NonEmptyList.singleton e)
  let optT = "Stdlib.Option.Option"
  let resT = "Stdlib.Result.Result"
  match t with
  | AST.TString -> src
  | AST.TInt64 -> parseInt src
  | AST.TBool -> AST.BinOp(AST.Eq, src, AST.StringLiteral "true")
  | AST.TUnit -> AST.Let(rv, src, AST.UnitLiteral)
  | AST.TSum("Stdlib.Option.Option", [ inner ]) ->
    AST.Let(
      rv,
      src,
      AST.If(
        starts r "Some\n",
        AST.Constructor(optT, "Some", Some(unmarshalTyped (d + 1) inner (unesc (slice r (AST.Int64Literal 5L) (len r))))),
        AST.Constructor(optT, "None", None)))
  | AST.TSum("Stdlib.Result.Result", [ okT; errT ]) ->
    AST.Let(
      rv,
      src,
      AST.If(
        starts r "Error\n",
        AST.Constructor(resT, "Error", Some(unmarshalTyped (d + 1) errT (unesc (slice r (AST.Int64Literal 6L) (len r))))),
        AST.Constructor(resT, "Ok", Some(unmarshalTyped (d + 1) okT (unesc (slice r (AST.Int64Literal 3L) (len r)))))))
  | AST.TList inner ->
    let ev = "__rpce_" + string d
    let lam =
      AST.Lambda(
        AST.NonEmptyList.fromList [ (ev, AST.TString) ],
        unmarshalTyped (d + 1) inner (unesc (AST.Var ev)))
    let split = AST.Call("Stdlib.String.split", AST.NonEmptyList.fromList [ r; AST.StringLiteral "\n" ])
    AST.Let(
      rv,
      src,
      AST.If(
        AST.BinOp(AST.Eq, r, AST.StringLiteral ""),
        AST.ListLiteral [],
        AST.TypeApp("Stdlib.List.map", [ AST.TString; inner ], AST.NonEmptyList.fromList [ split; lam ])))
  | _ -> src // unreachable: buildEffectfulMap only routes isMarshalableType returns

/// Unmarshal a host-RPC wire response into a native value per its return wire
/// type. Scalars are decoded inline; every container routes through the recursive
/// unmarshalTyped over the bridged return type.
let private unmarshalReturn (ret : WireRet) (call : AST.Expr) : AST.Expr =
  match ret with
  | WRString -> call
  | WRInt -> AST.Call("Stdlib.hostRpcParseInt", AST.NonEmptyList.singleton call)
  | WRBool -> AST.BinOp(AST.Eq, call, AST.StringLiteral "true")
  | WRUnit -> AST.Let("__rpc_ignore", call, AST.UnitLiteral)
  | WRTyped t -> unmarshalTyped 0 t call

let rec bridgeExpr (ctx : BridgeCtx) (e : PT.Expr) : Result<AST.Expr, string> =
  let recurse = bridgeExpr ctx
  match e with
  | PT.EInt64(_, n) -> Ok(AST.Int64Literal n)
  | PT.EInt8(_, n) -> Ok(AST.Int8Literal n)
  | PT.EUInt8(_, n) -> Ok(AST.UInt8Literal n)
  | PT.EInt16(_, n) -> Ok(AST.Int16Literal n)
  | PT.EUInt16(_, n) -> Ok(AST.UInt16Literal n)
  | PT.EInt32(_, n) -> Ok(AST.Int32Literal n)
  | PT.EUInt32(_, n) -> Ok(AST.UInt32Literal n)
  | PT.EUInt64(_, n) -> Ok(AST.UInt64Literal n)
  | PT.EInt128(_, n) -> Ok(AST.Int128Literal n)
  | PT.EUInt128(_, n) -> Ok(AST.UInt128Literal n)
  | PT.EInt(_, big) ->
    if big >= bigint System.Int64.MinValue && big <= bigint System.Int64.MaxValue then
      Ok(AST.Int64Literal(int64 big))
    else
      err "literal" "Int outside Int64 range"
  | PT.EBool(_, b) -> Ok(AST.BoolLiteral b)
  | PT.EFloat(_, sign, whole, fraction) ->
    // Same construction the interpreter uses (Prelude.makeFloat), so a compiled
    // float literal is bit-identical to the interpreted one.
    Ok(AST.FloatLiteral(makeFloat sign whole fraction))
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
    if i >= 0 && i < ctx.Params.Length then Ok(AST.Var ctx.Params[i])
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
      |> Result.map (fun fs -> AST.Constructor(compilerTypeName h, caseName, tuplePayload fs)))
  // Cross-fn calls: a direct call to a package fn lowers to AST.Call, or
  // AST.TypeApp when the call site carries type args (the compiler monomorphizes
  // it). The callee itself is bridged separately (the builtin walks the graph).
  | PT.EApply(_, PT.EFnName(_, nr), typeArgs, args) ->
    match nr.resolved with
    | Error _ -> err "call" "unresolved fn name"
    | Ok resolved ->
      match resolved.name with
      | PT.FQFnName.Builtin b ->
        // Pure builtins route to the compiler's native stdlib (dispatch row 1).
        match Map.tryFind b.name builtinToStdlib with
        | Some stdlibFn ->
          args
          |> NEList.toList
          |> List.map recurse
          |> allOk
          |> Result.map (fun bas -> AST.Call(stdlibFn, AST.NonEmptyList.fromList bas))
        | None ->
          // Effectful builtins route through the host RPC seam (Stdlib.hostRpc):
          // request = "name\narg0\narg1…" (all args String), result per the
          // builtin's return type.
          match Map.tryFind b.name ctx.Effectful with
          | Some(argWires, wireRet) ->
            args
            |> NEList.toList
            |> List.map recurse
            |> allOk
            |> Result.bind (fun bas ->
              if List.length bas <> List.length argWires then
                err "builtin" $"{b.name}: arg count mismatch"
              else
                // stringify each arg per its wire type
                let marshaled =
                  List.map2
                    (fun w a ->
                      match w with
                      | WAString -> a
                      | WAInt -> AST.Call("Stdlib.Int64.toString", AST.NonEmptyList.singleton a)
                      | WABool -> AST.Call("Stdlib.Bool.toString", AST.NonEmptyList.singleton a)
                      | WAFloat -> AST.Call("Stdlib.Float.toString", AST.NonEmptyList.singleton a)
                      | WAUnit -> AST.Let("__rpc_unit", a, AST.StringLiteral "unit"))
                    argWires
                    bas
                let request =
                  (AST.StringLiteral b.name, marshaled)
                  ||> List.fold (fun acc arg ->
                    AST.BinOp(
                      AST.StringConcat,
                      AST.BinOp(AST.StringConcat, acc, AST.StringLiteral "\n"),
                      arg))
                let call = AST.Call("Stdlib.hostRpc", AST.NonEmptyList.singleton request)
                Ok(unmarshalReturn wireRet call))
          | None -> err "builtin" $"{b.name}_v{b.version}"
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
  // Self-recursion: `self(args)` calls the current fn by its compiled name.
  | PT.EApply(_, PT.ESelf _, _, args) ->
    args
    |> NEList.toList
    |> List.map recurse
    |> allOk
    |> Result.map (fun bas -> AST.Call(ctx.Self, AST.NonEmptyList.fromList bas))
  // A referenced package constant is inlined (its bridged body, from ctx.Values).
  | PT.EValue(_, nr) ->
    match nr.resolved with
    | Error _ -> err "value" "unresolved value name"
    | Ok resolved ->
      match resolved.name with
      | PT.FQValueName.Builtin b -> err "value-builtin" b.name
      | PT.FQValueName.Package(PT.Hash h) ->
        match Map.tryFind h ctx.Values with
        | Some e -> Ok e
        | None -> err "value" "package value not resolved"
  | PT.ESelf _ -> err "expr" "bare ESelf (self as a value)"
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
      |> List.map (bridgeCase ctx)
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
        accR |> Result.bind (fun acc -> bridgePipePart ctx acc part)))
  // Lambda: untyped Dark params get a fresh TVar (unique per node id) that the
  // compiler's inference/monomorphizer resolves from the call context. A
  // non-variable param (e.g. `fun (a, b) -> …`) binds a fresh name and the body
  // destructures it via bindPattern.
  | PT.ELambda(lamId, pats, body) ->
    let named =
      pats
      |> NEList.toList
      |> List.mapi (fun i p ->
        match p with
        | PT.LPVariable(_, name) -> (name, None)
        | _ -> ($"__lamarg_{lamId}_{i}", Some p))
    let compilerParams =
      named |> List.map (fun (nm, _) -> (nm, AST.TVar $"__lamt_{lamId}_{nm}"))
    recurse body
    |> Result.bind (fun b ->
      // wrap the body with a destructuring bind for each non-variable param
      (Ok b, named)
      ||> List.fold (fun accR (nm, patOpt) ->
        match patOpt with
        | None -> accR
        | Some pat -> accR |> Result.bind (fun acc -> bindPattern pat (AST.Var nm) acc))
      |> Result.map (fun wb -> AST.Lambda(AST.NonEmptyList.fromList compilerParams, wb)))
  | PT.EStatement(_, first, next) ->
    // `first; next` — evaluate first, discard, then next.
    recurse first
    |> Result.bind (fun f -> recurse next |> Result.map (fun n -> AST.Let("__dark_stmt", f, n)))
  // A bare function reference (passed as a value, e.g. `List.map(xs, someFn)`)
  // lowers to a captureless Closure — the compiler's first-class function value.
  // A package fn references its bridged name; a pure builtin references the native
  // stdlib fn it routes to. Effectful builtins have no direct function value (they
  // need the hostRpc wrapper), so a bare reference to one hard-fails.
  | PT.EFnName(_, nr) ->
    match nr.resolved with
    | Error _ -> err "fnref" "unresolved fn name"
    | Ok resolved ->
      match resolved.name with
      | PT.FQFnName.Package(PT.Hash h) -> Ok(AST.Closure(nameFor h, []))
      | PT.FQFnName.Builtin b ->
        match Map.tryFind b.name builtinToStdlib with
        | Some stdlibFn -> Ok(AST.Closure(stdlibFn, []))
        | None -> err "fnref" $"effectful builtin as value: {b.name}"
  | _ -> err "expr" (e.GetType().Name)

/// Lower one match case. A PT case has a single pattern (alternatives live in
/// MPOr); the compiler groups alternatives in MatchCase.Patterns.
and bridgeCase
  (ctx : BridgeCtx)
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
      | Some g -> bridgeExpr ctx g |> Result.map Some
    guard
    |> Result.bind (fun gd ->
      bridgeExpr ctx c.rhs
      |> Result.map (fun body ->
        ({ Patterns = AST.NonEmptyList.fromList pats
           Guard = gd
           Body = body }
        : AST.MatchCase))))

/// Desugar one pipe stage: the accumulated value `piped` becomes the stage's
/// first input. `x |> f a` -> f(x, a); `x |> (+) a` -> x + a; `x |> Some` ->
/// Some x; `x |> v a` -> v(x, a) (v is a fn value). Lambda stages need lambdas.
and bridgePipePart
  (ctx : BridgeCtx)
  (piped : AST.Expr)
  (part : PT.PipeExpr)
  : Result<AST.Expr, string> =
  let recurse = bridgeExpr ctx
  match part with
  | PT.EPipeInfix(_, op, rhs) ->
    bridgeInfix op
    |> Result.bind (fun o -> recurse rhs |> Result.map (fun r -> AST.BinOp(o, piped, r)))
  | PT.EPipeFnCall(_, nr, typeArgs, args) ->
    match nr.resolved with
    | Error _ -> err "call" "unresolved fn name (pipe)"
    | Ok resolved ->
      match resolved.name with
      | PT.FQFnName.Builtin b ->
        match Map.tryFind b.name builtinToStdlib with
        | Some stdlibFn ->
          args
          |> List.map recurse
          |> allOk
          |> Result.map (fun bas ->
            AST.Call(stdlibFn, AST.NonEmptyList.fromList (piped :: bas)))
        | None -> err "builtin" $"{b.name}_v{b.version}"
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

/// Every type variable appearing in a compiler type. Darklang signatures carry
/// free type vars (e.g. `'v`) that PT's `typeParams` list doesn't always
/// enumerate; the compiler FunctionDef must declare all of them or the reachability
/// harness can't monomorphize (no zero literal for an undeclared TVar).
let rec freeTVars (t : AST.Type) : Set<string> =
  match t with
  | AST.TVar v -> Set.singleton v
  | AST.TRecord(_, args)
  | AST.TSum(_, args) -> args |> List.map freeTVars |> Set.unionMany
  | AST.TList inner -> freeTVars inner
  | AST.TTuple ts -> ts |> List.map freeTVars |> Set.unionMany
  | AST.TDict(k, v) -> Set.union (freeTVars k) (freeTVars v)
  | AST.TFunction(args, ret) ->
    Set.union (args |> List.map freeTVars |> Set.unionMany) (freeTVars ret)
  | _ -> Set.empty

/// Lower a package function to a compiler FunctionDef under the given
/// compiler-side name. Params keep their Dark names (positional EArg refs and
/// any by-name refs both resolve). Generics are not yet supported.
let bridgeFn
  (types : Map<string, TypeEntry>)
  (values : Map<string, AST.Expr>)
  (effectful : Map<string, WireArg list * WireRet>)
  (compiledName : string)
  (fn : PT.PackageFn.PackageFn)
  : Result<AST.FunctionDef, string> =
  let paramList = NEList.toList fn.parameters
  let paramNames = paramList |> List.map (fun p -> p.name) |> Array.ofList
  let ctx =
    { Params = paramNames
      Self = compiledName
      Values = values
      Effectful = effectful }
  let bridgedParams =
    paramList
    |> List.map (fun p -> bridgeType types p.typ |> Result.map (fun t -> (p.name, t)))
    |> allOk
  bridgedParams
  |> Result.bind (fun ps ->
    bridgeType types fn.returnType
    |> Result.bind (fun retType ->
      bridgeExpr ctx fn.body
      |> Result.map (fun body ->
        // Declare every type var the signature actually uses — declared params
        // first (preserving order), then any free tvars PT didn't enumerate.
        let sigTVars =
          (ps |> List.map (snd >> freeTVars) |> Set.unionMany)
          |> Set.union (freeTVars retType)
        let declared = fn.typeParams
        let extra =
          sigTVars |> Set.toList |> List.filter (fun v -> not (List.contains v declared))
        ({ Name = compiledName
           TypeParams = declared @ extra
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
  | PT.EStatement(_, first, next) -> r first @ r next
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
  | PT.EStatement(_, first, next) -> r first @ r next
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

/// The package-value hashes an expression references (for the value closure).
let rec valueRefsInExpr (e : PT.Expr) : List<string> =
  let r = valueRefsInExpr
  match e with
  | PT.EValue(_, nr) ->
    match nr.resolved with
    | Ok resolved ->
      match resolved.name with
      | PT.FQValueName.Package(PT.Hash h) -> [ h ]
      | PT.FQValueName.Builtin _ -> []
    | Error _ -> []
  | PT.EInfix(_, _, l, rhs) -> r l @ r rhs
  | PT.EIf(_, c, t, Some el) -> r c @ r t @ r el
  | PT.EIf(_, c, t, None) -> r c @ r t
  | PT.ELet(_, _, v, body) -> r v @ r body
  | PT.EList(_, elems) -> elems |> List.collect r
  | PT.ETuple(_, a, b, rest) -> (a :: b :: rest) |> List.collect r
  | PT.ERecord(_, _, _, fields) -> fields |> List.collect (snd >> r)
  | PT.ERecordFieldAccess(_, record, _) -> r record
  | PT.ERecordUpdate(_, record, ups) ->
    r record @ (ups |> NEList.toList |> List.collect (snd >> r))
  | PT.EEnum(_, _, _, _, fields) -> fields |> List.collect r
  | PT.EMatch(_, arg, cases) ->
    r arg @ (cases |> List.collect (fun c -> r c.rhs))
  | PT.EString(_, segs) ->
    segs |> List.collect (fun s -> match s with PT.StringInterpolation e -> r e | PT.StringText _ -> [])
  | PT.ELambda(_, _, body) -> r body
  | PT.EStatement(_, first, next) -> r first @ r next
  | PT.EApply(_, f, _, args) -> r f @ (args |> NEList.toList |> List.collect r)
  | _ -> []
