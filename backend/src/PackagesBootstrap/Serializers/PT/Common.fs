module PackagesBootstrap.Serializers.PT.Common

open System.IO
open Prelude

open LibExecution.ProgramTypes

open PackagesBootstrap.BinaryFormat
open PackagesBootstrap.Serializers.Common


module Sign =
  let read (r : BinaryReader) : Sign =
    match r.ReadByte() with
    | 0uy -> Positive
    | 1uy -> Negative
    | b -> raise (BinaryFormatException(CorruptedData $"Invalid Sign tag: {b}"))


module NameResolutionError =
  let read (r : BinaryReader) : NameResolutionError =
    match r.ReadByte() with
    | 0uy -> NotFound(List.read r String.read)
    | 1uy -> InvalidName(List.read r String.read)
    | b ->
      raise (BinaryFormatException(CorruptedData $"Invalid NameResolutionError tag: {b}"))


module NameResolution =
  let read (readValue : BinaryReader -> 'a) (r : BinaryReader) : NameResolution<'a> =
    match r.ReadByte() with
    | 0uy -> Ok(readValue r)
    | 1uy -> Error(NameResolutionError.read r)
    | b ->
      raise (BinaryFormatException(CorruptedData $"Invalid NameResolution tag: {b}"))


module FQTypeName =
  module Package =
    let read (r : BinaryReader) : FQTypeName.Package = Guid.read r

  let read (r : BinaryReader) : FQTypeName.FQTypeName =
    match r.ReadByte() with
    | 0uy -> FQTypeName.Package(Package.read r)
    | b -> raise (BinaryFormatException(CorruptedData $"Invalid FQTypeName tag: {b}"))


module FQValueName =
  module Builtin =
    let read (r : BinaryReader) : FQValueName.Builtin =
      let name = String.read r
      let version = r.ReadInt32()
      { name = name; version = version }

  module Package =
    let read (r : BinaryReader) : FQValueName.Package = Guid.read r

  let read (r : BinaryReader) : FQValueName.FQValueName =
    match r.ReadByte() with
    | 0uy -> FQValueName.Builtin(Builtin.read r)
    | 1uy -> FQValueName.Package(Package.read r)
    | b -> raise (BinaryFormatException(CorruptedData $"Invalid FQValueName tag: {b}"))


module FQFnName =
  module Builtin =
    let read (r : BinaryReader) : FQFnName.Builtin =
      let name = String.read r
      let version = r.ReadInt32()
      { name = name; version = version }

  module Package =
    let read (r : BinaryReader) : FQFnName.Package = Guid.read r

  let read (r : BinaryReader) : FQFnName.FQFnName =
    match r.ReadByte() with
    | 0uy -> FQFnName.Builtin(Builtin.read r)
    | 1uy -> FQFnName.Package(Package.read r)
    | b -> raise (BinaryFormatException(CorruptedData $"Invalid FQFnName tag: {b}"))


module Deprecation =
  let read (r : BinaryReader) (readNameFn : BinaryReader -> 'name) : Deprecation<'name> =
    match r.ReadByte() with
    | 0uy -> NotDeprecated
    | 1uy -> RenamedTo(readNameFn r)
    | 2uy -> ReplacedBy(readNameFn r)
    | 3uy -> DeprecatedBecause(String.read r)
    | b -> raise (BinaryFormatException(CorruptedData $"Invalid Deprecation tag: {b}"))


module PackageLocation =
  let read (r : BinaryReader) : PackageLocation =
    let owner = String.read r
    let modules = List.read r String.read
    let name = String.read r
    { owner = owner; modules = modules; name = name }
