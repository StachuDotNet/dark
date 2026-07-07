/// Parse + lower package files with the hand-written parser. `parse` is the public
/// entrypoint used by the package loader (LocalExec).
module LibParser.Package

open Prelude
open LibExecution.ProgramTypes

module P = LibParser.Parser
module WT = WrittenTypes
module WT2PT = WrittenTypesToProgramTypes
module PT = LibExecution.ProgramTypes
module RT = LibExecution.RuntimeTypes
module NR = NameResolver
module PackageLocation = LibDB.PackageLocation
open LibSerialization.Hashing


type private WTPackageModule =
  { fns : List<WT.PackageFn.PackageFn>
    types : List<WT.PackageType.PackageType>
    values : List<WT.PackageValue.PackageValue> }
/// Lower a WT package module to PackageOps (WT2PT lowering + AddX/SetName op
/// generation).
let private wtModuleToOps
  (builtins : RT.Builtins)
  (pm : PT.PackageManager)
  (onMissing : NR.OnMissing)
  (modul : WTPackageModule)
  : Ply<List<PT.PackageOp>> =
  uply {
    let! fns =
      modul.fns
      |> Ply.List.mapSequentially (fun fn ->
        WT2PT.PackageFn.toPT
          builtins
          pm
          onMissing
          PT.mainBranchId
          (WT2PT.PackageFn.Name.toModules fn.name)
          fn)

    let! types =
      modul.types
      |> Ply.List.mapSequentially (fun typ ->
        WT2PT.PackageType.toPT
          pm
          onMissing
          PT.mainBranchId
          (WT2PT.PackageType.Name.toModules typ.name)
          typ)

    let! values =
      modul.values
      |> Ply.List.mapSequentially (fun value ->
        WT2PT.PackageValue.toPT
          builtins
          pm
          onMissing
          PT.mainBranchId
          (WT2PT.PackageValue.Name.toModules value.name)
          value)

    // Compute a deterministic name-based placeholder hash for Set*Name ops.
    // The real hash replaces this in LoadPackagesFromDisk.computeRealHashes.
    let nameBasedHash (loc : PT.PackageLocation) : Hash =
      let nameKey = PackageLocation.toFQN loc
      let nameBytes =
        System.Security.Cryptography.SHA256.HashData(
          System.Text.Encoding.UTF8.GetBytes(nameKey)
        )
      Hash(
        System.BitConverter.ToString(nameBytes).Replace("-", "").ToLowerInvariant()
      )

    let ops : List<PT.PackageOp> =
      [ for (wtType, ptType) in List.zip modul.types types do
          yield PT.PackageOp.AddType ptType
          let loc = WT2PT.PackageType.Name.toLocation wtType.name
          yield PT.PackageOp.SetName(loc, PT.PackageType(nameBasedHash loc))

        for (wtValue, ptValue) in List.zip modul.values values do
          yield PT.PackageOp.AddValue ptValue
          let loc = WT2PT.PackageValue.Name.toLocation wtValue.name
          yield PT.PackageOp.SetName(loc, PT.PackageValue(nameBasedHash loc))

        for (wtFn, ptFn) in List.zip modul.fns fns do
          yield PT.PackageOp.AddFn ptFn
          let loc = WT2PT.PackageFn.Name.toLocation wtFn.name
          yield PT.PackageOp.SetName(loc, PT.PackageFn(nameBasedHash loc)) ]

    return ops
  }

// --- extract package declarations from a parsed package file ---
//
// Flatten the nested module tree into owner-qualified package items (a fn `map`
// in `module Darklang.Stdlib.List` → `Darklang.Stdlib.List.map`; the path's first
// segment is the owner, the rest the modules — via the shared `WT.package*`
// helpers). A decl that can't be a package item surfaces as an ERROR rather than
// silently vanishing (a dropped decl looks like "fn not found" later).

type private PkgItem =
  | PFn of WT.PackageFn.PackageFn
  | PType of WT.PackageType.PackageType
  | PValue of WT.PackageValue.PackageValue
  | PErr of WT.Range * string

let private noOwner (kind : string) (name : string) : string =
  $"{kind} '{name}' is outside any 'module Owner.…' — package declarations must live inside an owner module"

let rec private collectPackage
  (path : List<string>)
  (decls : List<WT.Declaration>)
  : List<PkgItem> =
  decls
  |> List.collect (fun d ->
    match d with
    | WT.DModule m ->
      let parts =
        (snd m.name).Split('.') |> Array.toList |> List.filter (fun s -> s <> "")
      collectPackage (path @ parts) m.declarations
    | WT.DFunction fn ->
      match path with
      | owner :: modules -> [ PFn(WT.packageFn owner modules fn) ]
      | [] -> [ PErr(fn.range, noOwner "function" fn.name.name) ]
    | WT.DType t ->
      match path with
      | owner :: modules -> [ PType(WT.packageType owner modules t) ]
      | [] -> [ PErr(t.range, noOwner "type" t.name.name) ]
    | WT.DValue v ->
      match path with
      | owner :: modules -> [ PValue(WT.packageValue owner modules v) ]
      | [] -> [ PErr(v.range, noOwner "value" v.name.name) ]
    | WT.DExpr e ->
      [ PErr(WT.exprRange e, "expressions are not allowed in package files") ]
    | WT.DTypeDB t ->
      [ PErr(t.range, "[<DB>] declarations are not allowed in package files") ]
    | WT.DTest t ->
      [ PErr(t.range, "test assertions are not allowed in package files") ])

/// Lower a parsed package file to module-qualified package declarations, plus
/// errors for declarations a package file can't hold.
let private packageDecls
  (sf : WT.SourceFile)
  : List<WT.PackageFn.PackageFn> *
    List<WT.PackageType.PackageType> *
    List<WT.PackageValue.PackageValue> *
    List<WT.Range * string>
  =
  let items = collectPackage [] sf.declarations
  let fns =
    items
    |> List.choose (function
      | PFn f -> Some f
      | _ -> None)
  let types =
    items
    |> List.choose (function
      | PType t -> Some t
      | _ -> None)
  let values =
    items
    |> List.choose (function
      | PValue v -> Some v
      | _ -> None)
  let errors =
    items
    |> List.choose (function
      | PErr(r, msg) -> Some(r, msg)
      | _ -> None)
  (fns, types, values, errors)

/// Parse + lower a package file: the nested module tree gives module-qualified
/// names. Returns `Error diagnostics` on parse failure.
let parse
  (builtins : RT.Builtins)
  (pm : PT.PackageManager)
  (onMissing : NR.OnMissing)
  (contents : string)
  : Ply<Result<List<PT.PackageOp>, List<string>>> =
  uply {
    let result = P.parse contents
    match result.parsed with
    | Some(WT.SourceFile sf) when List.isEmpty result.diagnostics ->
      let (fns, types, values, declErrors) = packageDecls sf
      let exprErrors =
        sf.exprsToEval
        |> List.map (fun e ->
          (WT.exprRange e, "expressions are not allowed in package files"))
      match declErrors @ exprErrors with
      | [] ->
        let modul : WTPackageModule = { fns = fns; types = types; values = values }
        let! ops = wtModuleToOps builtins pm onMissing modul
        return Ok ops
      | errors ->
        return
          Error(
            errors
            |> List.map (fun (r, msg) ->
              $"error at {r.start.row + 1}:{r.start.column + 1}: {msg}")
          )
    | _ ->
      let msgs = result.diagnostics |> List.map (P.renderDiagnostic contents)
      // guard against a failure with no diagnostics (shouldn't happen) surfacing as
      // a message-less `Error []`
      return
        Error(if List.isEmpty msgs then [ "failed to parse package file" ] else msgs)
  }
