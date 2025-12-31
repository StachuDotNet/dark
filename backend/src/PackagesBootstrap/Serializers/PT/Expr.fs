module rec PackagesBootstrap.Serializers.PT.Expr

open System.IO
open Prelude

open LibExecution.ProgramTypes

open PackagesBootstrap.BinaryFormat
open PackagesBootstrap.Serializers.Common
open PackagesBootstrap.Serializers.PT.Common


module InfixFnName =
  let read (r : BinaryReader) : InfixFnName =
    match r.ReadByte() with
    | 0uy -> ArithmeticPlus
    | 1uy -> ArithmeticMinus
    | 2uy -> ArithmeticMultiply
    | 3uy -> ArithmeticDivide
    | 4uy -> ArithmeticModulo
    | 5uy -> ArithmeticPower
    | 6uy -> ComparisonGreaterThan
    | 7uy -> ComparisonGreaterThanOrEqual
    | 8uy -> ComparisonLessThan
    | 9uy -> ComparisonLessThanOrEqual
    | 10uy -> ComparisonEquals
    | 11uy -> ComparisonNotEquals
    | 12uy -> StringConcat
    | b -> raise (BinaryFormatException(CorruptedData $"Invalid InfixFnName tag: {b}"))


module BinaryOperation =
  let read (r : BinaryReader) : BinaryOperation =
    match r.ReadByte() with
    | 0uy -> BinOpAnd
    | 1uy -> BinOpOr
    | b -> raise (BinaryFormatException(CorruptedData $"Invalid BinaryOperation tag: {b}"))


module Infix =
  let read (r : BinaryReader) : Infix =
    match r.ReadByte() with
    | 0uy -> InfixFnCall(InfixFnName.read r)
    | 1uy -> BinOp(BinaryOperation.read r)
    | b -> raise (BinaryFormatException(CorruptedData $"Invalid Infix tag: {b}"))


module LetPattern =
  let rec read (r : BinaryReader) : LetPattern =
    match r.ReadByte() with
    | 0uy ->
      let id = r.ReadUInt64()
      let name = String.read r
      LPVariable(id, name)
    | 1uy ->
      let id = r.ReadUInt64()
      LPUnit id
    | 2uy ->
      let id = r.ReadUInt64()
      let first = read r
      let second = read r
      let rest = List.read r read
      LPTuple(id, first, second, rest)
    | b -> raise (BinaryFormatException(CorruptedData $"Invalid LetPattern tag: {b}"))


module MatchPattern =
  let rec read (r : BinaryReader) : MatchPattern =
    match r.ReadByte() with
    | 0uy ->
      let id = r.ReadUInt64()
      let name = String.read r
      MPVariable(id, name)
    | 1uy ->
      let id = r.ReadUInt64()
      let caseName = String.read r
      let fieldPats = List.read r read
      MPEnum(id, caseName, fieldPats)
    | 2uy ->
      let id = r.ReadUInt64()
      let value = r.ReadInt64()
      MPInt64(id, value)
    | 3uy ->
      let id = r.ReadUInt64()
      let value = r.ReadUInt64()
      MPUInt64(id, value)
    | 4uy ->
      let id = r.ReadUInt64()
      let value = r.ReadSByte()
      MPInt8(id, value)
    | 5uy ->
      let id = r.ReadUInt64()
      let value = r.ReadByte()
      MPUInt8(id, value)
    | 6uy ->
      let id = r.ReadUInt64()
      let value = r.ReadInt16()
      MPInt16(id, value)
    | 7uy ->
      let id = r.ReadUInt64()
      let value = r.ReadUInt16()
      MPUInt16(id, value)
    | 8uy ->
      let id = r.ReadUInt64()
      let value = r.ReadInt32()
      MPInt32(id, value)
    | 9uy ->
      let id = r.ReadUInt64()
      let value = r.ReadUInt32()
      MPUInt32(id, value)
    | 10uy ->
      let id = r.ReadUInt64()
      let value = String.read r |> System.Int128.Parse
      MPInt128(id, value)
    | 11uy ->
      let id = r.ReadUInt64()
      let value = String.read r |> System.UInt128.Parse
      MPUInt128(id, value)
    | 12uy ->
      let id = r.ReadUInt64()
      let value = r.ReadBoolean()
      MPBool(id, value)
    | 13uy ->
      let id = r.ReadUInt64()
      let value = String.read r
      MPChar(id, value)
    | 14uy ->
      let id = r.ReadUInt64()
      let value = String.read r
      MPString(id, value)
    | 15uy ->
      let id = r.ReadUInt64()
      let sign = Sign.read r
      let whole = String.read r
      let fractional = String.read r
      MPFloat(id, sign, whole, fractional)
    | 16uy ->
      let id = r.ReadUInt64()
      MPUnit id
    | 17uy ->
      let id = r.ReadUInt64()
      let first = read r
      let second = read r
      let rest = List.read r read
      MPTuple(id, first, second, rest)
    | 18uy ->
      let id = r.ReadUInt64()
      let patterns = List.read r read
      MPList(id, patterns)
    | 19uy ->
      let id = r.ReadUInt64()
      let head = read r
      let tail = read r
      MPListCons(id, head, tail)
    | 20uy ->
      let id = r.ReadUInt64()
      let patterns = NEList.read read r
      MPOr(id, patterns)
    | b -> raise (BinaryFormatException(CorruptedData $"Invalid MatchPattern tag: {b}"))


module StringSegment =
  let read (r : BinaryReader) : StringSegment =
    match r.ReadByte() with
    | 0uy -> StringText(String.read r)
    | 1uy -> StringInterpolation(Expr.read r)
    | b -> raise (BinaryFormatException(CorruptedData $"Invalid StringSegment tag: {b}"))


module MatchCase =
  let read (r : BinaryReader) : MatchCase =
    let pat = MatchPattern.read r
    let whenCondition = Option.read r Expr.read
    let rhs = Expr.read r
    { pat = pat; whenCondition = whenCondition; rhs = rhs }


module PipeExpr =
  let read (r : BinaryReader) : PipeExpr =
    match r.ReadByte() with
    | 0uy ->
      let id = r.ReadUInt64()
      let name = String.read r
      let args = List.read r Expr.read
      EPipeVariable(id, name, args)
    | 1uy ->
      let id = r.ReadUInt64()
      let pats = NEList.read LetPattern.read r
      let body = Expr.read r
      EPipeLambda(id, pats, body)
    | 2uy ->
      let id = r.ReadUInt64()
      let infix = Infix.read r
      let expr = Expr.read r
      EPipeInfix(id, infix, expr)
    | 3uy ->
      let id = r.ReadUInt64()
      let fnName = NameResolution.read FQFnName.read r
      let typeArgs = List.read r TypeReference.read
      let args = List.read r Expr.read
      EPipeFnCall(id, fnName, typeArgs, args)
    | 4uy ->
      let id = r.ReadUInt64()
      let typeName = NameResolution.read FQTypeName.read r
      let caseName = String.read r
      let fields = List.read r Expr.read
      EPipeEnum(id, typeName, caseName, fields)
    | b -> raise (BinaryFormatException(CorruptedData $"Invalid PipeExpr tag: {b}"))


module Expr =
  let rec read (r : BinaryReader) : Expr =
    match r.ReadByte() with
    | 0uy ->
      let id = r.ReadUInt64()
      let value = r.ReadInt64()
      EInt64(id, value)
    | 1uy ->
      let id = r.ReadUInt64()
      let value = r.ReadUInt64()
      EUInt64(id, value)
    | 2uy ->
      let id = r.ReadUInt64()
      let value = r.ReadSByte()
      EInt8(id, value)
    | 3uy ->
      let id = r.ReadUInt64()
      let value = r.ReadByte()
      EUInt8(id, value)
    | 4uy ->
      let id = r.ReadUInt64()
      let value = r.ReadInt16()
      EInt16(id, value)
    | 5uy ->
      let id = r.ReadUInt64()
      let value = r.ReadUInt16()
      EUInt16(id, value)
    | 6uy ->
      let id = r.ReadUInt64()
      let value = r.ReadInt32()
      EInt32(id, value)
    | 7uy ->
      let id = r.ReadUInt64()
      let value = r.ReadUInt32()
      EUInt32(id, value)
    | 8uy ->
      let id = r.ReadUInt64()
      let value = String.read r |> System.Int128.Parse
      EInt128(id, value)
    | 9uy ->
      let id = r.ReadUInt64()
      let value = String.read r |> System.UInt128.Parse
      EUInt128(id, value)
    | 10uy ->
      let id = r.ReadUInt64()
      let value = r.ReadBoolean()
      EBool(id, value)
    | 11uy ->
      let id = r.ReadUInt64()
      let segments = List.read r StringSegment.read
      EString(id, segments)
    | 12uy ->
      let id = r.ReadUInt64()
      let value = String.read r
      EChar(id, value)
    | 13uy ->
      let id = r.ReadUInt64()
      let sign = Sign.read r
      let whole = String.read r
      let fractional = String.read r
      EFloat(id, sign, whole, fractional)
    | 14uy ->
      let id = r.ReadUInt64()
      EUnit id
    | 15uy ->
      let id = r.ReadUInt64()
      let nameRes = NameResolution.read FQValueName.read r
      EValue(id, nameRes)
    | 16uy ->
      let id = r.ReadUInt64()
      let pattern = LetPattern.read r
      let rhs = read r
      let body = read r
      ELet(id, pattern, rhs, body)
    | 17uy ->
      let id = r.ReadUInt64()
      let cond = read r
      let thenExpr = read r
      let elseExpr = Option.read r read
      EIf(id, cond, thenExpr, elseExpr)
    | 18uy ->
      let id = r.ReadUInt64()
      let pats = NEList.read LetPattern.read r
      let body = read r
      ELambda(id, pats, body)
    | 19uy ->
      let id = r.ReadUInt64()
      let expr = read r
      let field = String.read r
      ERecordFieldAccess(id, expr, field)
    | 20uy ->
      let id = r.ReadUInt64()
      let name = String.read r
      EVariable(id, name)
    | 21uy ->
      let id = r.ReadUInt64()
      let fn = read r
      let typeArgs = List.read r TypeReference.read
      let args = NEList.read read r
      EApply(id, fn, typeArgs, args)
    | 22uy ->
      let id = r.ReadUInt64()
      let exprs = List.read r read
      EList(id, exprs)
    | 23uy ->
      let id = r.ReadUInt64()
      let typeName = NameResolution.read FQTypeName.read r
      let typeArgs = List.read r TypeReference.read
      let fields =
        List.read r (fun r ->
          let name = String.read r
          let expr = read r
          (name, expr))
      ERecord(id, typeName, typeArgs, fields)
    | 24uy ->
      let id = r.ReadUInt64()
      let record = read r
      let updates =
        NEList.read
          (fun r ->
            let name = String.read r
            let expr = read r
            (name, expr))
          r
      ERecordUpdate(id, record, updates)
    | 25uy ->
      let id = r.ReadUInt64()
      let expr = read r
      let pipes = List.read r PipeExpr.read
      EPipe(id, expr, pipes)
    | 26uy ->
      let id = r.ReadUInt64()
      let typeName = NameResolution.read FQTypeName.read r
      let typeArgs = List.read r TypeReference.read
      let caseName = String.read r
      let fields = List.read r read
      EEnum(id, typeName, typeArgs, caseName, fields)
    | 27uy ->
      let id = r.ReadUInt64()
      let expr = read r
      let cases = List.read r MatchCase.read
      EMatch(id, expr, cases)
    | 28uy ->
      let id = r.ReadUInt64()
      let first = read r
      let second = read r
      let rest = List.read r read
      ETuple(id, first, second, rest)
    | 29uy ->
      let id = r.ReadUInt64()
      let op = Infix.read r
      let left = read r
      let right = read r
      EInfix(id, op, left, right)
    | 30uy ->
      let id = r.ReadUInt64()
      let pairs =
        List.read r (fun r ->
          let key = String.read r
          let value = read r
          (key, value))
      EDict(id, pairs)
    | 31uy ->
      let id = r.ReadUInt64()
      let nameRes = NameResolution.read FQFnName.read r
      EFnName(id, nameRes)
    | 32uy ->
      let id = r.ReadUInt64()
      let first = read r
      let next = read r
      EStatement(id, first, next)
    | 33uy ->
      let id = r.ReadUInt64()
      ESelf id
    | 34uy ->
      let id = r.ReadUInt64()
      let index = r.ReadInt32()
      EArg(id, index)
    | b -> raise (BinaryFormatException(CorruptedData $"Invalid Expr tag: {b}"))
