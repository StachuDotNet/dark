module PackagesBootstrap.Serializers.PT.PackageFn

open System.IO
open Prelude

open LibExecution.ProgramTypes

open PackagesBootstrap.BinaryFormat
open PackagesBootstrap.Serializers.Common
open PackagesBootstrap.Serializers.PT.Common


module Parameter =
  let read (r : BinaryReader) : PackageFn.Parameter =
    let name = String.read r
    let typ = TypeReference.read r
    let description = String.read r
    { name = name; typ = typ; description = description }


let read (r : BinaryReader) : PackageFn.PackageFn =
  let id = Guid.read r
  let body = Expr.Expr.read r
  let typeParams = List.read r String.read
  let parameters = NEList.read Parameter.read r
  let returnType = TypeReference.read r
  let description = String.read r
  let deprecated = Deprecation.read r FQFnName.read
  { id = id
    body = body
    typeParams = typeParams
    parameters = parameters
    returnType = returnType
    description = description
    deprecated = deprecated }
