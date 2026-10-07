module LibSerialization.Binary.Serializers.PT.PackageFn

open System
open System.IO
open Prelude

open LibExecution.ProgramTypes

open LibSerialization.Binary.Serializers.Common
open LibSerialization.Binary.Serializers.PT.Common

module PT = LibExecution.ProgramTypes


module Parameter =
  let write (w : BinaryWriter) (p : PackageFn.Parameter) : unit =
    String.write w p.name
    TypeReference.write w p.typ
    String.write w p.description

  let read (r : BinaryReader) : PackageFn.Parameter =
    let name = String.read r
    let typ = TypeReference.read r
    let description = String.read r
    { name = name; typ = typ; description = description }


module Purity =
  let write (w : BinaryWriter) (p : PT.Purity) : unit =
    match p with
    | PT.Purity.Pure -> w.Write(0uy)
    | PT.Purity.Impure -> w.Write(1uy)

  let read (r : BinaryReader) : PT.Purity =
    match r.ReadByte() with
    | 0uy -> PT.Purity.Pure
    | 1uy -> PT.Purity.Impure
    | b -> raiseFormatError $"Invalid Purity tag: {b}"


let write (w : BinaryWriter) (p : PackageFn.PackageFn) : unit =
  Hash.write w p.hash
  LibSerialization.Binary.Serializers.PT.Expr.Expr.write w p.body
  LibSerialization.Binary.Serializers.Common.List.write w String.write p.typeParams
  NEList.write Parameter.write w p.parameters
  TypeReference.write w p.returnType
  String.write w p.description
  Option.write
    w
    LibSerialization.Binary.Serializers.Effects.write
    p.permissionCeiling
  LibSerialization.Binary.Serializers.Common.List.write
    w
    TypeReference.Bound.write
    p.bounds
  Option.write w Purity.write p.purity

let read (version : uint32) (r : BinaryReader) : PackageFn.PackageFn =
  let hash = Hash.read r
  let body = LibSerialization.Binary.Serializers.PT.Expr.Expr.read version r
  let typeParams = LibSerialization.Binary.Serializers.Common.List.read r String.read
  let parameters = NEList.read Parameter.read r
  let returnType = TypeReference.read r
  let description = String.read r
  let permissionCeiling =
    Option.read r LibSerialization.Binary.Serializers.Effects.read
  let bounds = TypeReference.Bound.readList version r
  let purity = if version >= 6u then Option.read r Purity.read else None
  { hash = hash
    body = body
    typeParams = typeParams
    parameters = parameters
    returnType = returnType
    description = description
    permissionCeiling = permissionCeiling
    bounds = bounds
    purity = purity }
