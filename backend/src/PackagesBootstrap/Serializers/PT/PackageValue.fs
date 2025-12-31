module PackagesBootstrap.Serializers.PT.PackageValue

open System.IO
open Prelude

open LibExecution.ProgramTypes

open PackagesBootstrap.BinaryFormat
open PackagesBootstrap.Serializers.Common
open PackagesBootstrap.Serializers.PT.Common


let read (r : BinaryReader) : PackageValue.PackageValue =
  let id = Guid.read r
  let body = Expr.Expr.read r
  let description = String.read r
  let deprecated = Deprecation.read r FQValueName.read
  { id = id; body = body; description = description; deprecated = deprecated }
