module LibDB.PackageItem

open LibExecution.ProgramTypes

module PT = LibExecution.ProgramTypes


let fnPackageHash (fn : PT.FQFnName.FQFnName) : Option<Hash> =
  match fn with
  | PT.FQFnName.Package hash -> Some hash
  | PT.FQFnName.Builtin _ -> None
  // PDD: Pending fns don't have package hashes yet — they're materialized
  // at call time and only get a hash after promotion.
  | PT.FQFnName.Pending _ -> None


let typePackageHash (typ : PT.FQTypeName.FQTypeName) : Option<Hash> =
  match typ with
  | PT.FQTypeName.Package hash -> Some hash


let valuePackageHash (value : PT.FQValueName.FQValueName) : Option<Hash> =
  match value with
  | PT.FQValueName.Package hash -> Some hash
  | PT.FQValueName.Builtin _ -> None
