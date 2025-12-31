module PackagesBootstrap.Serializers.PT.TypeReference

open System.IO
open Prelude

open LibExecution.ProgramTypes

open PackagesBootstrap.BinaryFormat
open PackagesBootstrap.Serializers.Common
open PackagesBootstrap.Serializers.PT.Common


let rec read (r : BinaryReader) : TypeReference =
  match r.ReadByte() with
  | 0uy -> TInt64
  | 1uy -> TUInt64
  | 2uy -> TInt8
  | 3uy -> TUInt8
  | 4uy -> TInt16
  | 5uy -> TUInt16
  | 6uy -> TInt32
  | 7uy -> TUInt32
  | 8uy -> TInt128
  | 9uy -> TUInt128
  | 10uy -> TFloat
  | 11uy -> TBool
  | 12uy -> TUnit
  | 13uy -> TString
  | 14uy -> TList(read r)
  | 15uy -> TDict(read r)
  | 16uy -> TDB(read r)
  | 17uy -> TDateTime
  | 18uy -> TChar
  | 19uy -> TUuid
  | 20uy ->
    let typeName = NameResolution.read FQTypeName.read r
    let typeArgs = List.read r read
    TCustomType(typeName, typeArgs)
  | 21uy ->
    let name = String.read r
    TVariable name
  | 22uy ->
    let paramTypes = NEList.read read r
    let returnType = read r
    TFn(paramTypes, returnType)
  | 23uy ->
    let first = read r
    let second = read r
    let rest = List.read r read
    TTuple(first, second, rest)
  | b ->
    raise (BinaryFormatException(CorruptedData $"Invalid TypeReference tag: {b}"))
