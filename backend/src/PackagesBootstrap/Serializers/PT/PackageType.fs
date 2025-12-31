module PackagesBootstrap.Serializers.PT.PackageType

open System.IO
open Prelude

open LibExecution.ProgramTypes

open PackagesBootstrap.BinaryFormat
open PackagesBootstrap.Serializers.Common
open PackagesBootstrap.Serializers.PT.Common


module TypeDeclaration =
  module RecordField =
    let read (r : BinaryReader) : TypeDeclaration.RecordField =
      let name = String.read r
      let typ = TypeReference.read r
      let description = String.read r
      { name = name; typ = typ; description = description }

  module EnumField =
    let read (r : BinaryReader) : TypeDeclaration.EnumField =
      let typ = TypeReference.read r
      let label = Option.read r String.read
      let description = String.read r
      { typ = typ; label = label; description = description }

  module EnumCase =
    let read (r : BinaryReader) : TypeDeclaration.EnumCase =
      let name = String.read r
      let fields = List.read r EnumField.read
      let description = String.read r
      { name = name; fields = fields; description = description }

  module Definition =
    let read (r : BinaryReader) : TypeDeclaration.Definition =
      match r.ReadByte() with
      | 0uy -> TypeDeclaration.Alias(TypeReference.read r)
      | 1uy -> TypeDeclaration.Record(NEList.read RecordField.read r)
      | 2uy -> TypeDeclaration.Enum(NEList.read EnumCase.read r)
      | b ->
        raise (BinaryFormatException(CorruptedData $"Invalid TypeDeclaration.Definition tag: {b}"))

  let read (r : BinaryReader) : TypeDeclaration.T =
    let typeParams = List.read r String.read
    let definition = Definition.read r
    { typeParams = typeParams; definition = definition }


let read (r : BinaryReader) : PackageType.PackageType =
  let id = Guid.read r
  let declaration = TypeDeclaration.read r
  let description = String.read r
  let deprecated = Deprecation.read r FQTypeName.read
  { id = id
    declaration = declaration
    description = description
    deprecated = deprecated }
