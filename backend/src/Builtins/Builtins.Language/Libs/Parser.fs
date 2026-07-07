module Builtins.Language.Libs.Parser

open System.Text

open Prelude
open LibExecution.RuntimeTypes
open LibExecution.Builtin.Shortcuts

module VT = LibExecution.ValueType
module Dval = LibExecution.Dval
module PackageRefs = LibExecution.PackageRefs.Type.LanguageTools.Parser
module NR = LibExecution.RuntimeTypes.NameResolution

module Tok = LibParser.Tokenizer
module ITok = LibParser.InterpTokenizer
module P = LibParser.Parser
module WT = LibParser.WrittenTypes
module WTRefs = LibExecution.PackageRefs.Type.LanguageTools.WrittenTypes
module WT2PT = LibParser.WrittenTypesToProgramTypes
module PT = LibExecution.ProgramTypes
module PT2RT = LibExecution.ProgramTypesToRuntimeTypes
module PT2DT = LibExecution.ProgramTypesToDarkTypes
module StdlibRefs = LibExecution.PackageRefs.Type.Stdlib
module Exe = LibExecution.Execution

/// Lexical kind for an interpreter-dialect token — drives syntax highlighting.
let private tokenKind (t : Tok.Token) : string =
  match t with
  | Tok.TLet
  | Tok.TVal
  | Tok.TIn
  | Tok.TIf
  | Tok.TElif
  | Tok.TThen
  | Tok.TElse
  | Tok.TDef
  | Tok.TType
  | Tok.TOf
  | Tok.TMatch
  | Tok.TWith
  | Tok.TFun
  | Tok.TWhen
  | Tok.TTrue
  | Tok.TFalse -> "keyword"
  | Tok.TPlus
  | Tok.TPlusPlus
  | Tok.TMinus
  | Tok.TStar
  | Tok.TSlash
  | Tok.TPercent
  | Tok.TEqEq
  | Tok.TNeq
  | Tok.TLt
  | Tok.TGt
  | Tok.TLte
  | Tok.TGte
  | Tok.TAnd
  | Tok.TOr
  | Tok.TNot
  | Tok.TPipe
  | Tok.TArrow
  | Tok.TShl
  | Tok.TShr
  | Tok.TBitAnd
  | Tok.TBitOr
  | Tok.TBitXor
  | Tok.TBitNot
  | Tok.TAt
  | Tok.TEquals -> "operator"
  | Tok.TLParen
  | Tok.TRParen
  | Tok.TLBrace
  | Tok.TRBrace
  | Tok.TLBracket
  | Tok.TRBracket
  | Tok.TColon
  | Tok.TComma
  | Tok.TSemicolon
  | Tok.TDot
  | Tok.TBar
  | Tok.TDotDotDot
  | Tok.TUnderscore
  | Tok.TCons -> "symbol"
  | Tok.TStringLit _
  | Tok.TCharLit _
  | Tok.TInterpString -> "string"
  | Tok.TInt _
  | Tok.TInt64 _
  | Tok.TInt128 _
  | Tok.TInt8 _
  | Tok.TInt16 _
  | Tok.TInt32 _
  | Tok.TUInt8 _
  | Tok.TUInt16 _
  | Tok.TUInt32 _
  | Tok.TUInt64 _
  | Tok.TUInt128 _
  | Tok.TFloat _ -> "number"
  | Tok.TIdent _ -> "identifier"
  | Tok.TEOF -> "symbol"


let pointTypeName () = FQTypeName.fqPackage (PackageRefs.point ())
let rangeTypeName () = FQTypeName.fqPackage (PackageRefs.range ())
let signTypeName () =
  FQTypeName.fqPackage (LibExecution.PackageRefs.Type.LanguageTools.sign ())
let parsedNodeTypeName () = FQTypeName.fqPackage (PackageRefs.parsedNode ())

/// Converts the range-complete `WrittenTypes` tree the parser produces into the
/// Dark `WrittenTypes.ParsedFile` Dval that the semantic-token highlighter / LSP
/// consume.
module WrittenTypesToDarkTypes =
  let private tn (r : unit -> string) : FQTypeName.FQTypeName =
    FQTypeName.fqPackage (r ())

  let private pointToDT (p : Tok.Pos) : Dval =
    DRecord(
      pointTypeName (),
      pointTypeName (),
      [],
      // `Point` is `{ row: Int; column: Int }` in WrittenTypes
      Map [ "row", Dval.int (bigint p.row); "column", Dval.int (bigint p.column) ]
    )

  let private rangeToDT (r : Tok.TokenRange) : Dval =
    DRecord(
      rangeTypeName (),
      rangeTypeName (),
      [],
      Map [ "start", pointToDT r.start; "end_", pointToDT r.end_ ]
    )

  let private binaryOperationToDT (b : WT.BinaryOperation) : Dval =
    let t = tn WTRefs.binaryOperation
    let case =
      match b with
      | WT.BinOpAnd -> "BinOpAnd"
      | WT.BinOpOr -> "BinOpOr"
    DEnum(t, t, [], case, [])

  let private infixFnNameToDT (n : WT.InfixFnName) : Dval =
    let t = tn WTRefs.infixFnName
    let case =
      match n with
      | WT.ArithmeticPlus -> "ArithmeticPlus"
      | WT.ArithmeticMinus -> "ArithmeticMinus"
      | WT.ArithmeticMultiply -> "ArithmeticMultiply"
      | WT.ArithmeticDivide -> "ArithmeticDivide"
      | WT.ArithmeticModulo -> "ArithmeticModulo"
      | WT.ArithmeticPower -> "ArithmeticPower"
      | WT.ComparisonGreaterThan -> "ComparisonGreaterThan"
      | WT.ComparisonGreaterThanOrEqual -> "ComparisonGreaterThanOrEqual"
      | WT.ComparisonLessThan -> "ComparisonLessThan"
      | WT.ComparisonLessThanOrEqual -> "ComparisonLessThanOrEqual"
      | WT.ComparisonEquals -> "ComparisonEquals"
      | WT.ComparisonNotEquals -> "ComparisonNotEquals"
      | WT.StringConcat -> "StringConcat"
    DEnum(t, t, [], case, [])

  let private infixToDT (i : WT.Infix) : Dval =
    let t = tn WTRefs.infix
    match i with
    | WT.InfixFnCall n -> DEnum(t, t, [], "InfixFnCall", [ infixFnNameToDT n ])
    | WT.BinOp b -> DEnum(t, t, [], "BinOp", [ binaryOperationToDT b ])

  let rec private letPatternToDT (p : WT.LetPattern) : Dval =
    let t = tn WTRefs.letPattern
    match p with
    | WT.LPUnit r -> DEnum(t, t, [], "LPUnit", [ rangeToDT r ])
    | WT.LPVariable(r, name) ->
      DEnum(t, t, [], "LPVariable", [ rangeToDT r; DString name ])
    | WT.LPWildcard r -> DEnum(t, t, [], "LPWildcard", [ rangeToDT r ])
    | WT.LPTuple(r, first, comma, second, rest, openP, closeP) ->
      let restVT =
        VT.tuple
          (VT.customType (rangeTypeName ()) [])
          (VT.customType (tn WTRefs.letPattern) [])
          []
      let restDval =
        DList(
          restVT,
          rest
          |> List.map (fun (cr, p) -> DTuple(rangeToDT cr, letPatternToDT p, []))
        )
      DEnum(
        t,
        t,
        [],
        "LPTuple",
        [ rangeToDT r
          letPatternToDT first
          rangeToDT comma
          letPatternToDT second
          restDval
          rangeToDT openP
          rangeToDT closeP ]
      )

  let private identifierToDT (refFn : unit -> string) (i : WT.Identifier) : Dval =
    let t = tn refFn
    DRecord(t, t, [], Map [ "range", rangeToDT i.range; "name", DString i.name ])

  // The `modules` path of a qualified name: each module segment paired with the range
  // of its trailing `.`. Shared by the qualified fn/type identifier converters.
  let private modulesToDT (modules : List<WT.Identifier * Tok.TokenRange>) : Dval =
    let modVT =
      VT.tuple
        (VT.customType (tn WTRefs.moduleIdentifier) [])
        (VT.customType (rangeTypeName ()) [])
        []
    DList(
      modVT,
      modules
      |> List.map (fun (m, dotR) ->
        DTuple(identifierToDT WTRefs.moduleIdentifier m, rangeToDT dotR, []))
    )

  let rec qualifiedTypeIdentifierToDT (q : WT.QualifiedTypeIdentifier) : Dval =
    DRecord(
      tn WTRefs.qualifiedTypeIdentifier,
      tn WTRefs.qualifiedTypeIdentifier,
      [],
      Map
        [ "range", rangeToDT q.range
          "modules", modulesToDT q.modules
          "typ", identifierToDT WTRefs.typeIdentifier q.typ
          "typeArgs",
          DList(
            VT.customType (tn WTRefs.typeReference) [],
            List.map typeReferenceToDT q.typeArgs
          ) ]
    )

  and typeReferenceToDT (t : WT.TypeReference) : Dval =
    let trTN = tn WTRefs.typeReference
    let bTN = tn WTRefs.typeReferenceBuiltin
    let builtin (case : string) (fields : List<Dval>) =
      DEnum(trTN, trTN, [], "Builtin", [ DEnum(bTN, bTN, [], case, fields) ])
    match t with
    | WT.TUnit r -> builtin "TUnit" [ rangeToDT r ]
    | WT.TBool r -> builtin "TBool" [ rangeToDT r ]
    | WT.TInt8 r -> builtin "TInt8" [ rangeToDT r ]
    | WT.TUInt8 r -> builtin "TUInt8" [ rangeToDT r ]
    | WT.TInt16 r -> builtin "TInt16" [ rangeToDT r ]
    | WT.TUInt16 r -> builtin "TUInt16" [ rangeToDT r ]
    | WT.TInt32 r -> builtin "TInt32" [ rangeToDT r ]
    | WT.TUInt32 r -> builtin "TUInt32" [ rangeToDT r ]
    | WT.TInt64 r -> builtin "TInt64" [ rangeToDT r ]
    | WT.TUInt64 r -> builtin "TUInt64" [ rangeToDT r ]
    | WT.TInt128 r -> builtin "TInt128" [ rangeToDT r ]
    | WT.TUInt128 r -> builtin "TUInt128" [ rangeToDT r ]
    | WT.TInt r -> builtin "TInt" [ rangeToDT r ]
    | WT.TFloat r -> builtin "TFloat" [ rangeToDT r ]
    | WT.TChar r -> builtin "TChar" [ rangeToDT r ]
    | WT.TString r -> builtin "TString" [ rangeToDT r ]
    | WT.TDateTime r -> builtin "TDateTime" [ rangeToDT r ]
    | WT.TUuid r -> builtin "TUuid" [ rangeToDT r ]
    | WT.TBlob r -> builtin "TBlob" [ rangeToDT r ]
    | WT.TList(r, kw, openB, inner, closeB) ->
      builtin
        "TList"
        [ rangeToDT r
          rangeToDT kw
          rangeToDT openB
          typeReferenceToDT inner
          rangeToDT closeB ]
    | WT.TDict(r, kw, openB, inner, closeB) ->
      builtin
        "TDict"
        [ rangeToDT r
          rangeToDT kw
          rangeToDT openB
          typeReferenceToDT inner
          rangeToDT closeB ]
    | WT.TVariable(r, tick, (nameR, nm)) ->
      builtin
        "TVariable"
        [ rangeToDT r; rangeToDT tick; DTuple(rangeToDT nameR, DString nm, []) ]
    | WT.TTuple(r, first, star1, second, rest, openP, closeP) ->
      let restVT =
        VT.tuple
          (VT.customType (rangeTypeName ()) [])
          (VT.customType (tn WTRefs.typeReference) [])
          []
      let restDval =
        DList(
          restVT,
          rest
          |> List.map (fun (sr, t) -> DTuple(rangeToDT sr, typeReferenceToDT t, []))
        )
      builtin
        "TTuple"
        [ rangeToDT r
          typeReferenceToDT first
          rangeToDT star1
          typeReferenceToDT second
          restDval
          rangeToDT openP
          rangeToDT closeP ]
    | WT.TFn(r, args, ret) ->
      let argsVT =
        VT.tuple
          (VT.customType (tn WTRefs.typeReference) [])
          (VT.customType (rangeTypeName ()) [])
          []
      let argsDval =
        DList(
          argsVT,
          args
          |> List.map (fun (t, arrowR) ->
            DTuple(typeReferenceToDT t, rangeToDT arrowR, []))
        )
      builtin "TFn" [ rangeToDT r; argsDval; typeReferenceToDT ret ]
    | WT.TCustom q ->
      DEnum(trTN, trTN, [], "QualifiedName", [ qualifiedTypeIdentifierToDT q ])

  // Just the name — a fn call's type args live on the surrounding call node (`EApply`
  // and `EPipeFnCall` each carry their own `typeArgs`), not on the identifier.
  and qualifiedFnIdentifierToDT (q : WT.QualifiedFnIdentifier) : Dval =
    DRecord(
      tn WTRefs.qualifiedFnIdentifier,
      tn WTRefs.qualifiedFnIdentifier,
      [],
      Map
        [ "range", rangeToDT q.range
          "modules", modulesToDT q.modules
          "fn", identifierToDT WTRefs.fnIdentifier q.fn ]
    )

  let private fnParamToDT (p : WT.FnParam) : Dval =
    let pt = tn WTRefs.fnParameter
    match p with
    | WT.FPUnit r ->
      let ut = tn WTRefs.fnUnitParameter
      DEnum(
        pt,
        pt,
        [],
        "Unit",
        [ DRecord(ut, ut, [], Map [ "range", rangeToDT r ]) ]
      )
    | WT.FPNormal(r, name, typ, lp, colon, rp) ->
      let nt = tn WTRefs.fnNormalParameter
      let normal =
        DRecord(
          nt,
          nt,
          [],
          Map
            [ "range", rangeToDT r
              "name", identifierToDT WTRefs.variableIdentifier name
              "typ", typeReferenceToDT typ
              "symbolLeftParen", rangeToDT lp
              "symbolColon", rangeToDT colon
              "symbolRightParen", rangeToDT rp ]
        )
      DEnum(pt, pt, [], "Normal", [ normal ])

  let rec matchPatternToDT (p : WT.MatchPattern) : Dval =
    let t = tn WTRefs.matchPattern
    let rangeKT = KTCustomType(rangeTypeName (), [])
    let mpListVT = VT.customType (tn WTRefs.matchPattern) []
    match p with
    | WT.MPVariable(r, name) ->
      DEnum(t, t, [], "MPVariable", [ rangeToDT r; DString name ])
    | WT.MPInt(r, (ipR, v)) ->
      DEnum(
        t,
        t,
        [],
        "MPInt",
        [ rangeToDT r; DTuple(rangeToDT ipR, DInt(DarkInt.ofBigInt v), []) ]
      )
    | WT.MPInt64(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "MPInt64",
        [ rangeToDT r; DTuple(rangeToDT ipR, DInt64 v, []); rangeToDT sufR ]
      )
    | WT.MPInt32(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "MPInt32",
        [ rangeToDT r; DTuple(rangeToDT ipR, DInt32 v, []); rangeToDT sufR ]
      )
    | WT.MPInt8(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "MPInt8",
        [ rangeToDT r; DTuple(rangeToDT ipR, DInt8 v, []); rangeToDT sufR ]
      )
    | WT.MPUInt8(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "MPUInt8",
        [ rangeToDT r; DTuple(rangeToDT ipR, DUInt8 v, []); rangeToDT sufR ]
      )
    | WT.MPInt16(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "MPInt16",
        [ rangeToDT r; DTuple(rangeToDT ipR, DInt16 v, []); rangeToDT sufR ]
      )
    | WT.MPUInt16(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "MPUInt16",
        [ rangeToDT r; DTuple(rangeToDT ipR, DUInt16 v, []); rangeToDT sufR ]
      )
    | WT.MPUInt32(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "MPUInt32",
        [ rangeToDT r; DTuple(rangeToDT ipR, DUInt32 v, []); rangeToDT sufR ]
      )
    | WT.MPUInt64(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "MPUInt64",
        [ rangeToDT r; DTuple(rangeToDT ipR, DUInt64 v, []); rangeToDT sufR ]
      )
    | WT.MPInt128(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "MPInt128",
        [ rangeToDT r; DTuple(rangeToDT ipR, DInt128 v, []); rangeToDT sufR ]
      )
    | WT.MPUInt128(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "MPUInt128",
        [ rangeToDT r; DTuple(rangeToDT ipR, DUInt128 v, []); rangeToDT sufR ]
      )
    | WT.MPFloat(r, neg, whole, frac) ->
      let st = signTypeName ()
      let signDval = DEnum(st, st, [], (if neg then "Negative" else "Positive"), [])
      DEnum(
        t,
        t,
        [],
        "MPFloat",
        [ rangeToDT r; signDval; DString whole; DString frac ]
      )
    | WT.MPBool(r, b) -> DEnum(t, t, [], "MPBool", [ rangeToDT r; DBool b ])
    | WT.MPUnit r -> DEnum(t, t, [], "MPUnit", [ rangeToDT r ])
    | WT.MPString(r, contents, openQ, closeQ) ->
      let contentsKT = KTTuple(VT.customType (rangeTypeName ()) [], VT.string, [])
      let contentsDval =
        match contents with
        | Some(cr, cs) ->
          Dval.optionSome contentsKT (DTuple(rangeToDT cr, DString cs, []))
        | None -> Dval.optionNone contentsKT
      DEnum(
        t,
        t,
        [],
        "MPString",
        [ rangeToDT r; contentsDval; rangeToDT openQ; rangeToDT closeQ ]
      )
    | WT.MPChar(r, contents, openQ, closeQ) ->
      let contentsKT = KTTuple(VT.customType (rangeTypeName ()) [], VT.string, [])
      let contentsDval =
        match contents with
        | Some(cr, cs) ->
          Dval.optionSome contentsKT (DTuple(rangeToDT cr, DString cs, []))
        | None -> Dval.optionNone contentsKT
      DEnum(
        t,
        t,
        [],
        "MPChar",
        [ rangeToDT r; contentsDval; rangeToDT openQ; rangeToDT closeQ ]
      )
    | WT.MPEnum(r, (caseNameR, caseName), fieldPats) ->
      DEnum(
        t,
        t,
        [],
        "MPEnum",
        [ rangeToDT r
          DTuple(rangeToDT caseNameR, DString caseName, [])
          DList(mpListVT, List.map matchPatternToDT fieldPats) ]
      )
    | WT.MPTuple(r, first, comma, second, rest, openP, closeP) ->
      let restVT = VT.tuple (VT.customType (rangeTypeName ()) []) mpListVT []
      let restDval =
        DList(
          restVT,
          rest
          |> List.map (fun (cr, p) -> DTuple(rangeToDT cr, matchPatternToDT p, []))
        )
      DEnum(
        t,
        t,
        [],
        "MPTuple",
        [ rangeToDT r
          matchPatternToDT first
          rangeToDT comma
          matchPatternToDT second
          restDval
          rangeToDT openP
          rangeToDT closeP ]
      )
    | WT.MPList(r, contents, openB, closeB) ->
      let elemVT = VT.tuple mpListVT VT.unknownTODO []
      let contentsDval =
        DList(
          elemVT,
          contents
          |> List.map (fun (p, sep) ->
            let sepDval =
              match sep with
              | Some sr -> Dval.optionSome rangeKT (rangeToDT sr)
              | None -> Dval.optionNone rangeKT
            DTuple(matchPatternToDT p, sepDval, []))
        )
      DEnum(
        t,
        t,
        [],
        "MPList",
        [ rangeToDT r; contentsDval; rangeToDT openB; rangeToDT closeB ]
      )
    | WT.MPListCons(r, head, tail, consR) ->
      DEnum(
        t,
        t,
        [],
        "MPListCons",
        [ rangeToDT r
          matchPatternToDT head
          matchPatternToDT tail
          rangeToDT consR ]
      )
    | WT.MPOr(r, pats) ->
      DEnum(
        t,
        t,
        [],
        "MPOr",
        [ rangeToDT r; DList(mpListVT, List.map matchPatternToDT pats) ]
      )
    | WT.MPError r -> DEnum(t, t, [], "MPError", [ rangeToDT r ])

  let rec exprToDT (e : WT.Expr) : Dval =
    let t = tn WTRefs.expr
    match e with
    | WT.EBool(r, b) -> DEnum(t, t, [], "EBool", [ rangeToDT r; DBool b ])
    | WT.EInt(r, (ipR, v)) ->
      DEnum(
        t,
        t,
        [],
        "EInt",
        [ rangeToDT r; DTuple(rangeToDT ipR, DInt(DarkInt.ofBigInt v), []) ]
      )
    | WT.EInt64(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "EInt64",
        [ rangeToDT r; DTuple(rangeToDT ipR, DInt64 v, []); rangeToDT sufR ]
      )
    | WT.EInt8(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "EInt8",
        [ rangeToDT r; DTuple(rangeToDT ipR, DInt8 v, []); rangeToDT sufR ]
      )
    | WT.EUInt8(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "EUInt8",
        [ rangeToDT r; DTuple(rangeToDT ipR, DUInt8 v, []); rangeToDT sufR ]
      )
    | WT.EInt16(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "EInt16",
        [ rangeToDT r; DTuple(rangeToDT ipR, DInt16 v, []); rangeToDT sufR ]
      )
    | WT.EUInt16(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "EUInt16",
        [ rangeToDT r; DTuple(rangeToDT ipR, DUInt16 v, []); rangeToDT sufR ]
      )
    | WT.EInt32(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "EInt32",
        [ rangeToDT r; DTuple(rangeToDT ipR, DInt32 v, []); rangeToDT sufR ]
      )
    | WT.EUInt32(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "EUInt32",
        [ rangeToDT r; DTuple(rangeToDT ipR, DUInt32 v, []); rangeToDT sufR ]
      )
    | WT.EUInt64(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "EUInt64",
        [ rangeToDT r; DTuple(rangeToDT ipR, DUInt64 v, []); rangeToDT sufR ]
      )
    | WT.EInt128(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "EInt128",
        [ rangeToDT r; DTuple(rangeToDT ipR, DInt128 v, []); rangeToDT sufR ]
      )
    | WT.EUInt128(r, (ipR, v), sufR) ->
      DEnum(
        t,
        t,
        [],
        "EUInt128",
        [ rangeToDT r; DTuple(rangeToDT ipR, DUInt128 v, []); rangeToDT sufR ]
      )
    | WT.EFloat(r, neg, whole, frac) ->
      let st = signTypeName ()
      let signDval = DEnum(st, st, [], (if neg then "Negative" else "Positive"), [])
      DEnum(
        t,
        t,
        [],
        "EFloat",
        [ rangeToDT r; signDval; DString whole; DString frac ]
      )
    | WT.EChar(r, contents, openQ, closeQ) ->
      let contentsKT = KTTuple(VT.customType (rangeTypeName ()) [], VT.string, [])
      let contentsDval =
        match contents with
        | Some(cr, cs) ->
          Dval.optionSome contentsKT (DTuple(rangeToDT cr, DString cs, []))
        | None -> Dval.optionNone contentsKT
      DEnum(
        t,
        t,
        [],
        "EChar",
        [ rangeToDT r; contentsDval; rangeToDT openQ; rangeToDT closeQ ]
      )
    | WT.EVariable(r, name) ->
      DEnum(t, t, [], "EVariable", [ rangeToDT r; DString name ])
    | WT.EInfix(r, (opR, op), l, rt) ->
      DEnum(
        t,
        t,
        [],
        "EInfix",
        [ rangeToDT r
          DTuple(rangeToDT opR, infixToDT op, [])
          exprToDT l
          exprToDT rt ]
      )
    | WT.ELet(r, pat, value, body, kwLet, symEq) ->
      DEnum(
        t,
        t,
        [],
        "ELet",
        [ rangeToDT r
          letPatternToDT pat
          exprToDT value
          exprToDT body
          rangeToDT kwLet
          rangeToDT symEq ]
      )
    | WT.EUnit r -> DEnum(t, t, [], "EUnit", [ rangeToDT r ])
    | WT.ERecordFieldAccess(r, e, (fR, field), dot) ->
      DEnum(
        t,
        t,
        [],
        "ERecordFieldAccess",
        [ rangeToDT r
          exprToDT e
          DTuple(rangeToDT fR, DString field, [])
          rangeToDT dot ]
      )
    | WT.EIf(r, cond, thenE, elseE, kwIf, kwThen, kwElse) ->
      DEnum(
        t,
        t,
        [],
        "EIf",
        [ rangeToDT r
          exprToDT cond
          exprToDT thenE
          optionExprToDT elseE
          rangeToDT kwIf
          rangeToDT kwThen
          optionRangeToDT kwElse ]
      )
    | WT.EList(r, contents, openB, closeB) ->
      let elemVT = VT.tuple (VT.customType (tn WTRefs.expr) []) VT.unknownTODO []
      let contentsDval =
        DList(
          elemVT,
          contents
          |> List.map (fun (e, sep) -> DTuple(exprToDT e, optionRangeToDT sep, []))
        )
      DEnum(
        t,
        t,
        [],
        "EList",
        [ rangeToDT r; contentsDval; rangeToDT openB; rangeToDT closeB ]
      )
    | WT.ETuple(r, first, comma, second, rest, openP, closeP) ->
      let restVT =
        VT.tuple
          (VT.customType (rangeTypeName ()) [])
          (VT.customType (tn WTRefs.expr) [])
          []
      let restDval =
        DList(
          restVT,
          rest |> List.map (fun (cr, e) -> DTuple(rangeToDT cr, exprToDT e, []))
        )
      DEnum(
        t,
        t,
        [],
        "ETuple",
        [ rangeToDT r
          exprToDT first
          rangeToDT comma
          exprToDT second
          restDval
          rangeToDT openP
          rangeToDT closeP ]
      )
    | WT.ELambda(r, pats, body, kwFun, arrow) ->
      DEnum(
        t,
        t,
        [],
        "ELambda",
        [ rangeToDT r
          DList(
            VT.customType (tn WTRefs.letPattern) [],
            List.map letPatternToDT pats
          )
          exprToDT body
          rangeToDT kwFun
          rangeToDT arrow ]
      )
    | WT.ERecord(r, typeName, fields, openB, closeB) ->
      let fieldVT =
        VT.tuple
          (VT.customType (rangeTypeName ()) [])
          VT.unknownTODO
          [ VT.unknownTODO ]
      let fieldsDval =
        DList(
          fieldVT,
          fields
          |> List.map (fun (fr, (nr, fname), value) ->
            DTuple(
              rangeToDT fr,
              DTuple(rangeToDT nr, DString fname, []),
              [ exprToDT value ]
            ))
        )
      DEnum(
        t,
        t,
        [],
        "ERecord",
        [ rangeToDT r
          qualifiedTypeIdentifierToDT typeName
          fieldsDval
          rangeToDT openB
          rangeToDT closeB ]
      )
    | WT.EDict(r, contents, keywordDict, openB, closeB) ->
      let entryVT =
        VT.tuple
          (VT.customType (rangeTypeName ()) [])
          VT.unknownTODO
          [ VT.unknownTODO ]
      let contentsDval =
        DList(
          entryVT,
          contents
          |> List.map (fun (er, (kr, key), value) ->
            DTuple(
              rangeToDT er,
              DTuple(rangeToDT kr, DString key, []),
              [ exprToDT value ]
            ))
        )
      DEnum(
        t,
        t,
        [],
        "EDict",
        [ rangeToDT r
          contentsDval
          rangeToDT keywordDict
          rangeToDT openB
          rangeToDT closeB ]
      )
    | WT.ERecordUpdate(r, record, updates, openB, closeB, kwWith) ->
      let updVT =
        VT.tuple
          (VT.tuple (VT.customType (rangeTypeName ()) []) VT.string [])
          (VT.customType (rangeTypeName ()) [])
          [ VT.customType (tn WTRefs.expr) [] ]
      let updatesDval =
        DList(
          updVT,
          updates
          |> List.map (fun ((nr, fname), eqR, value) ->
            DTuple(
              DTuple(rangeToDT nr, DString fname, []),
              rangeToDT eqR,
              [ exprToDT value ]
            ))
        )
      DEnum(
        t,
        t,
        [],
        "ERecordUpdate",
        [ rangeToDT r
          exprToDT record
          updatesDval
          rangeToDT openB
          rangeToDT closeB
          rangeToDT kwWith ]
      )
    | WT.EEnum(r, typeName, (caseNameR, caseName), fields, symbolDot) ->
      DEnum(
        t,
        t,
        [],
        "EEnum",
        [ rangeToDT r
          qualifiedTypeIdentifierToDT typeName
          DTuple(rangeToDT caseNameR, DString caseName, [])
          DList(VT.customType (tn WTRefs.expr) [], List.map exprToDT fields)
          rangeToDT symbolDot ]
      )
    | WT.EMatch(r, expr, cases, kwMatch, kwWith) ->
      DEnum(
        t,
        t,
        [],
        "EMatch",
        [ rangeToDT r
          exprToDT expr
          DList(VT.customType (tn WTRefs.matchCase) [], List.map matchCaseToDT cases)
          rangeToDT kwMatch
          rangeToDT kwWith ]
      )
    | WT.EPipe(r, expr, pipeExprs) ->
      let pipeExprVT =
        VT.tuple
          (VT.customType (rangeTypeName ()) [])
          (VT.customType (tn WTRefs.pipeExpr) [])
          []
      let pipeExprsDval =
        DList(
          pipeExprVT,
          pipeExprs
          |> List.map (fun (pr, pe) -> DTuple(rangeToDT pr, pipeExprToDT pe, []))
        )
      DEnum(t, t, [], "EPipe", [ rangeToDT r; exprToDT expr; pipeExprsDval ])
    | WT.EStatement(r, first, next) ->
      DEnum(t, t, [], "EStatement", [ rangeToDT r; exprToDT first; exprToDT next ])
    | WT.EFnName(r, q) ->
      DEnum(t, t, [], "EFnName", [ rangeToDT r; qualifiedFnIdentifierToDT q ])
    | WT.EApply(r, lhs, typeArgs, args) ->
      // Every Darklang fn has ≥1 parameter (a "no-arg" fn `f () =` has one Unit param),
      // so a call always has ≥1 arg and EApply's args is a NEList. A type-args-only
      // `f<T>` is really `f<T> ()` — seed the implicit unit arg. (Same in the WT2PT lowering.)
      let argsDval =
        match args with
        | [] -> [ exprToDT (WT.EUnit r) ]
        | _ -> args |> List.map exprToDT
      DEnum(
        t,
        t,
        [],
        "EApply",
        [ rangeToDT r
          exprToDT lhs
          DList(
            VT.customType (tn WTRefs.typeReference) [],
            List.map typeReferenceToDT typeArgs
          )
          DList(VT.customType (tn WTRefs.expr) [], argsDval) ]
      )
    | WT.EString(r, dollar, contents, openQ, closeQ) ->
      let rangeKT = KTCustomType(rangeTypeName (), [])
      let dollarDval =
        match dollar with
        | Some dr -> Dval.optionSome rangeKT (rangeToDT dr)
        | None -> Dval.optionNone rangeKT
      DEnum(
        t,
        t,
        [],
        "EString",
        [ rangeToDT r
          dollarDval
          DList(
            VT.customType (tn WTRefs.stringSegment) [],
            List.map stringSegmentToDT contents
          )
          rangeToDT openQ
          rangeToDT closeQ ]
      )
    | WT.EError r -> DEnum(t, t, [], "EError", [ rangeToDT r ])

  and stringSegmentToDT (s : WT.StringSegment) : Dval =
    let t = tn WTRefs.stringSegment
    match s with
    | WT.StringText(r, text) ->
      DEnum(t, t, [], "StringText", [ rangeToDT r; DString text ])
    | WT.StringInterpolation(r, e, ob, cb) ->
      DEnum(
        t,
        t,
        [],
        "StringInterpolation",
        [ rangeToDT r; exprToDT e; rangeToDT ob; rangeToDT cb ]
      )

  and optionRangeToDT (o : Option<Tok.TokenRange>) : Dval =
    let kt = KTCustomType(rangeTypeName (), [])
    match o with
    | Some r -> Dval.optionSome kt (rangeToDT r)
    | None -> Dval.optionNone kt

  and optionExprToDT (o : Option<WT.Expr>) : Dval =
    let kt = KTCustomType(tn WTRefs.expr, [])
    match o with
    | Some e -> Dval.optionSome kt (exprToDT e)
    | None -> Dval.optionNone kt

  and pipeExprToDT (pe : WT.PipeExpr) : Dval =
    let t = tn WTRefs.pipeExpr
    let exprListVT = VT.customType (tn WTRefs.expr) []
    match pe with
    | WT.EPipeFnCall(r, q, typeArgs, args) ->
      let typeRefVT = VT.customType (tn WTRefs.typeReference) []
      DEnum(
        t,
        t,
        [],
        "EPipeFnCall",
        [ rangeToDT r
          qualifiedFnIdentifierToDT q
          DList(typeRefVT, List.map typeReferenceToDT typeArgs)
          DList(exprListVT, List.map exprToDT args) ]
      )
    | WT.EPipeVariableOrFnCall(r, name) ->
      DEnum(t, t, [], "EPipeVariableOrFnCall", [ rangeToDT r; DString name ])
    | WT.EPipeLambda(r, pats, body, kf, ar) ->
      DEnum(
        t,
        t,
        [],
        "EPipeLambda",
        [ rangeToDT r
          DList(
            VT.customType (tn WTRefs.letPattern) [],
            List.map letPatternToDT pats
          )
          exprToDT body
          rangeToDT kf
          rangeToDT ar ]
      )
    | WT.EPipeEnum(r, tname, (caseNameR, caseName), fields, dot) ->
      DEnum(
        t,
        t,
        [],
        "EPipeEnum",
        [ rangeToDT r
          qualifiedTypeIdentifierToDT tname
          DTuple(rangeToDT caseNameR, DString caseName, [])
          DList(exprListVT, List.map exprToDT fields)
          rangeToDT dot ]
      )
    | WT.EPipeInfix(r, (opR, op), e) ->
      DEnum(
        t,
        t,
        [],
        "EPipeInfix",
        [ rangeToDT r; DTuple(rangeToDT opR, infixToDT op, []); exprToDT e ]
      )

  and matchCaseToDT (c : WT.MatchCase) : Dval =
    let t = tn WTRefs.matchCase
    // pat: (Range * MatchPattern * Range) = (| range, pattern, -> range)
    let patDval =
      DTuple(
        rangeToDT c.barRange,
        matchPatternToDT c.pat,
        [ rangeToDT c.arrowRange ]
      )
    let whenKT =
      KTTuple(
        VT.customType (rangeTypeName ()) [],
        VT.customType (tn WTRefs.expr) [],
        []
      )
    let whenDval =
      match c.whenCondition with
      | Some(wr, g) -> Dval.optionSome whenKT (DTuple(rangeToDT wr, exprToDT g, []))
      | None -> Dval.optionNone whenKT
    DRecord(
      t,
      t,
      [],
      Map [ "pat", patDval; "whenCondition", whenDval; "rhs", exprToDT c.rhs ]
    )

  // `<'a, 'b>` type params serialize as `List<(Range * String)>` (the WT shape).
  let private typeParamsToDT (tps : List<string * Tok.TokenRange>) : Dval =
    let elemVT = VT.tuple (VT.customType (rangeTypeName ()) []) VT.string []
    DList(
      elemVT,
      tps |> List.map (fun (name, r) -> DTuple(rangeToDT r, DString name, []))
    )

  let private fnDeclToDT (f : WT.FnDecl) : Dval =
    let t = tn WTRefs.fnDeclaration
    DRecord(
      t,
      t,
      [],
      Map
        [ "range", rangeToDT f.range
          "name", identifierToDT WTRefs.fnIdentifier f.name
          "typeParams", typeParamsToDT f.typeParams
          "parameters",
          DList(
            VT.customType (tn WTRefs.fnParameter) [],
            List.map fnParamToDT f.parameters
          )
          "returnType", typeReferenceToDT f.returnType
          "body", exprToDT f.body
          "description", DString f.description
          "keywordLet", rangeToDT f.keywordLet
          "symbolColon", rangeToDT f.symbolColon
          "symbolEquals", rangeToDT f.symbolEquals ]
    )

  let private valueDeclToDT (v : WT.ValueDecl) : Dval =
    let t = tn WTRefs.valueDeclaration
    DRecord(
      t,
      t,
      [],
      Map
        [ "range", rangeToDT v.range
          "name", identifierToDT WTRefs.valueIdentifier v.name
          "body", exprToDT v.body
          "description", DString v.description
          "keywordVal", rangeToDT v.keywordLet
          "symbolEquals", rangeToDT v.symbolEquals ]
    )

  let private recordFieldToDT (f : WT.RecordFieldSyntax) : Dval =
    let t = tn WTRefs.typeDeclRecordField
    let (nr, nm) = f.name
    DRecord(
      t,
      t,
      [],
      Map
        [ "range", rangeToDT f.range
          "name", DTuple(rangeToDT nr, DString nm, [])
          "typ", typeReferenceToDT f.typ
          "description", DString ""
          "symbolColon", rangeToDT f.symbolColon ]
    )

  let private enumFieldToDT (f : WT.EnumFieldSyntax) : Dval =
    let t = tn WTRefs.typeDeclEnumField
    let rangeKT = KTCustomType(rangeTypeName (), [])
    let labelKT = KTTuple(VT.customType (rangeTypeName ()) [], VT.string, [])
    let labelDval =
      match f.label with
      | Some(lr, ln) ->
        Dval.optionSome labelKT (DTuple(rangeToDT lr, DString ln, []))
      | None -> Dval.optionNone labelKT
    let colonDval =
      match f.symbolColon with
      | Some r -> Dval.optionSome rangeKT (rangeToDT r)
      | None -> Dval.optionNone rangeKT
    DRecord(
      t,
      t,
      [],
      Map
        [ "range", rangeToDT f.range
          "typ", typeReferenceToDT f.typ
          "label", labelDval
          "description", DString ""
          "symbolColon", colonDval ]
    )

  let private enumCaseToDT (c : WT.EnumCaseSyntax) : Dval =
    let t = tn WTRefs.typeDeclEnumCase
    let (nr, nm) = c.name
    let rangeKT = KTCustomType(rangeTypeName (), [])
    let ofDval =
      match c.keywordOf with
      | Some r -> Dval.optionSome rangeKT (rangeToDT r)
      | None -> Dval.optionNone rangeKT
    DRecord(
      t,
      t,
      [],
      Map
        [ "range", rangeToDT c.range
          "name", DTuple(rangeToDT nr, DString nm, [])
          "fields",
          DList(
            VT.customType (tn WTRefs.typeDeclEnumField) [],
            List.map enumFieldToDT c.fields
          )
          "description", DString ""
          "keywordOf", ofDval ]
    )

  let private definitionToDT (d : WT.TypeDefinition) : Dval =
    let t = tn WTRefs.typeDeclDefinition
    let rangeKT = KTCustomType(rangeTypeName (), [])
    match d with
    | WT.TDAlias tr -> DEnum(t, t, [], "Alias", [ typeReferenceToDT tr ])
    | WT.TDRecord fields ->
      let elemVT =
        VT.tuple (VT.customType (tn WTRefs.typeDeclRecordField) []) VT.unknownTODO []
      let fieldsDval =
        DList(
          elemVT,
          fields
          |> List.map (fun (f, sep) ->
            let sepDval =
              match sep with
              | Some r -> Dval.optionSome rangeKT (rangeToDT r)
              | None -> Dval.optionNone rangeKT
            DTuple(recordFieldToDT f, sepDval, []))
        )
      DEnum(t, t, [], "Record", [ fieldsDval ])
    | WT.TDEnum cases ->
      let elemVT =
        VT.tuple
          (VT.customType (rangeTypeName ()) [])
          (VT.customType (tn WTRefs.typeDeclEnumCase) [])
          []
      let casesDval =
        DList(
          elemVT,
          cases |> List.map (fun (br, c) -> DTuple(rangeToDT br, enumCaseToDT c, []))
        )
      DEnum(t, t, [], "Enum", [ casesDval ])

  let private typeDeclToDT (td : WT.TypeDecl) : Dval =
    let t = tn WTRefs.typeDeclaration
    DRecord(
      t,
      t,
      [],
      Map
        [ "range", rangeToDT td.range
          "name", identifierToDT WTRefs.typeIdentifier td.name
          "typeParams", typeParamsToDT td.typeParams
          "definition", definitionToDT td.definition
          "description", DString td.description
          "keywordType", rangeToDT td.keywordType
          "symbolEquals", rangeToDT td.symbolEquals ]
    )

  // A module's declarations serialize as `ModuleDeclaration.Declaration` (nested
  // modules become `SubModule`); mutually recursive with `moduleDeclToDT`.
  let rec private moduleDeclToDT (m : WT.ModuleDecl) : Dval =
    let t = tn WTRefs.moduleDeclaration
    let (nameRange, nameStr) = m.name
    DRecord(
      t,
      t,
      [],
      Map
        [ "range", rangeToDT m.range
          "name", DTuple(rangeToDT nameRange, DString nameStr, [])
          "declarations",
          DList(
            VT.customType (tn WTRefs.moduleDeclarationDeclaration) [],
            m.declarations |> List.map moduleItemToDT
          )
          "keywordModule", rangeToDT m.keywordModule ]
    )

  and private moduleItemToDT (d : WT.Declaration) : Dval =
    let t = tn WTRefs.moduleDeclarationDeclaration
    match d with
    | WT.DFunction f -> DEnum(t, t, [], "Function", [ fnDeclToDT f ])
    | WT.DValue v -> DEnum(t, t, [], "Value", [ valueDeclToDT v ])
    | WT.DType td -> DEnum(t, t, [], "Type", [ typeDeclToDT td ])
    | WT.DModule m -> DEnum(t, t, [], "SubModule", [ moduleDeclToDT m ])
    | WT.DExpr e -> DEnum(t, t, [], "Expr", [ exprToDT e ])
    // test-mode-only declarations never reach the highlighter serializer
    | WT.DTypeDB _
    | WT.DTest _ -> Exception.raiseInternal "test-mode declaration in serializer" []

  let private sourceFileDeclarationToDT (d : WT.Declaration) : Dval =
    let t = tn WTRefs.sourceFileDeclaration
    match d with
    | WT.DFunction f -> DEnum(t, t, [], "Function", [ fnDeclToDT f ])
    | WT.DValue v -> DEnum(t, t, [], "Value", [ valueDeclToDT v ])
    | WT.DModule m -> DEnum(t, t, [], "Module", [ moduleDeclToDT m ])
    | WT.DType td -> DEnum(t, t, [], "Type", [ typeDeclToDT td ])
    // file-level trailing exprs are carried in `exprsToEval`, not as declarations;
    // test-mode declarations never reach the highlighter serializer
    | WT.DExpr _
    | WT.DTypeDB _
    | WT.DTest _ -> Exception.raiseInternal "unexpected declaration in serializer" []

  let parsedFileToDT (pf : WT.ParsedFile) : Dval =
    match pf with
    | WT.SourceFile sf ->
      let sfRecord =
        DRecord(
          tn WTRefs.sourceFile,
          tn WTRefs.sourceFile,
          [],
          Map
            [ "range", rangeToDT sf.range
              "declarations",
              DList(
                VT.customType (tn WTRefs.sourceFileDeclaration) [],
                List.map sourceFileDeclarationToDT sf.declarations
              )
              "exprsToEval",
              DList(
                VT.customType (tn WTRefs.expr) [],
                List.map exprToDT sf.exprsToEval
              ) ]
        )
      let pfT = tn WTRefs.parsedFile
      DEnum(pfT, pfT, [], "SourceFile", [ sfRecord ])

  // the new parser's syntax diagnostics as a `List<(Range * String)>` for the LSP
  let diagnosticsToDT (diagnostics : List<P.Diagnostic>) : Dval =
    let elemVT = VT.tuple (VT.customType (rangeTypeName ()) []) VT.string []
    DList(
      elemVT,
      diagnostics
      |> List.map (fun d -> DTuple(rangeToDT d.range, DString d.message, []))
    )

let fns () : List<BuiltInFn> =
  [ { name = fn "parserLexToTokens" 0
      typeParams = []
      parameters = [ Param.make "sourceCode" TString "" ]
      returnType = TList(TCustomType(NR.ok (parsedNodeTypeName ()), []))
      description =
        "Lexes Darklang code into a flat list of tokens, each with its source range. "
        + "Each token is returned as a childless ParsedNode whose `typ` is its lexical "
        + "kind (keyword/operator/string/number/identifier/symbol)."
      fn =
        (function
        | _, _, _, [ DString sourceCode ] ->
          let mkPoint (row : int) (col : int) =
            DRecord(
              pointTypeName (),
              pointTypeName (),
              [],
              // `Point` is `{ row: Int; column: Int }` in WrittenTypes — emit Int, not Int64
              Map [ "row", Dval.int (bigint row); "column", Dval.int (bigint col) ]
            )

          let mkRange (r : Tok.TokenRange) =
            DRecord(
              rangeTypeName (),
              rangeTypeName (),
              [],
              Map
                [ "start", mkPoint r.start.row r.start.column
                  "end_", mkPoint r.end_.row r.end_.column ]
            )

          let mkNode (st : ITok.SpannedToken) =
            DRecord(
              parsedNodeTypeName (),
              parsedNodeTypeName (),
              [],
              Map
                [ "fieldName", Dval.optionNone KTString
                  "typ", DString(tokenKind st.token)
                  "text", DString st.text
                  "range", mkRange st.range
                  "children", DList(VT.customType (parsedNodeTypeName ()) [], []) ]
            )

          let nodes =
            match ITok.tokenize sourceCode with
            | Ok(tokens, _) ->
              tokens
              |> List.filter (fun st -> st.token <> Tok.TEOF)
              |> List.map mkNode
            | Error _ -> []

          DList(VT.customType (parsedNodeTypeName ()) [], nodes) |> Ply
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "parserParseToWrittenTypes" 0
      typeParams = []
      parameters = [ Param.make "sourceCode" TString "" ]
      returnType =
        TCustomType(
          NR.ok (
            FQTypeName.fqPackage (LibExecution.PackageRefs.Type.Stdlib.option ())
          ),
          [ TCustomType(NR.ok (FQTypeName.fqPackage (WTRefs.parsedFile ())), []) ]
        )
      description =
        "Parses Darklang source into the range-complete WrittenTypes.ParsedFile "
        + "for the semantic-token highlighter / LSP. Returns Some with a best-effort "
        + "tree whenever lexing succeeds (parse diagnostics are reported separately); "
        + "None only when the source cannot be tokenized at all."
      fn =
        (function
        | _, _, _, [ DString sourceCode ] ->
          let pfKT = KTCustomType(FQTypeName.fqPackage (WTRefs.parsedFile ()), [])
          let r = P.parse sourceCode
          // best-effort: return the parse tree whenever the tokenizer succeeded, even
          // with diagnostics — the parser recovers from parse errors, so partial files
          // still highlight (with precise diagnostics reported separately). A total
          // tokenizer failure (`parsed = None`) yields None.
          let result =
            match r.parsed with
            | Some pf ->
              Dval.optionSome pfKT (WrittenTypesToDarkTypes.parsedFileToDT pf)
            | None -> Dval.optionNone pfKT
          Ply result
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "parserParseDiagnostics" 0
      typeParams = []
      parameters = [ Param.make "sourceCode" TString "" ]
      returnType =
        TList(
          TTuple(
            TCustomType(NR.ok (FQTypeName.fqPackage (PackageRefs.range ())), []),
            TString,
            []
          )
        )
      description =
        "The new parser's syntax diagnostics as (range, message) pairs, for precise "
        + "LSP error reporting. Empty list on a clean parse."
      fn =
        (function
        | _, _, _, [ DString sourceCode ] ->
          Ply(
            WrittenTypesToDarkTypes.diagnosticsToDT (P.parse sourceCode).diagnostics
          )
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated } ]


let builtins () = LibExecution.Builtin.make [] (fns ())
