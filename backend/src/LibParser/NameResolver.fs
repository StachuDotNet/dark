/// CLEANUP still feels like this can be tidied/shortened a bit.
module LibParser.NameResolver

open Prelude
open LibExecution.ProgramTypes

module WT = WrittenTypes
module PT = LibExecution.ProgramTypes
module RT = LibExecution.RuntimeTypes
type NRE = PT.NameResolutionError

// Name-format helpers: a package fn/value is written `name` or `name_vN`; a type is
// `Name` or `Name_v0`.
let private parseFnNameString (fnName : string) : Result<string * int, string> =
  match fnName with
  | Regex.Regex "^([a-z][a-z0-9A-Z]*[']?)_v(\d+)$" [ name; version ] ->
    // TryParse (not `int`) so an absurdly long version string is a format error, not
    // an OverflowException raised from a function that otherwise returns Result
    match System.Int32.TryParse version with
    | true, v -> Ok(name, v)
    | false, _ -> Error "Bad format in fn name"
  | Regex.Regex "^([a-z][a-z0-9A-Z]*[']?)$" [ name ] -> Ok(name, 0)
  | _ -> Error "Bad format in fn name"

let private parseTypeNameString (typeName : string) : Result<string, string> =
  match typeName with
  | Regex.Regex "^([A-Z][a-z0-9A-Z]*[']?)_v0$" [ name ] -> Ok name
  | Regex.Regex "^([A-Z][a-z0-9A-Z]*[']?)$" [ name ] -> Ok name
  | _ -> Error "Bad format in type name"


/// If a name is not found, should we raise an exception?
///
/// - when the local package DB is fully empty, and we're filling it in for the
///   first time, we want to allow all names to be NotFound -- other package
///   items won't be there yet
/// - sometimes when parsing, we're not sure whether something is:
///   - a variable
///   - or something else, like a value or fn.
///   During these times, we _also_ want to allow errors as well, so we can
///   parse it as a variable as a fallback if nothing is found under that name.
[<RequireQualifiedAccess>]
type OnMissing =
  | ThrowError
  | Allow

// TODO: we should probably just return the Result, and let the caller
// handle the error if they want to...
let throwIfRelevant
  (onMissing : OnMissing)
  (currentModule : List<string>)
  (given : NEList<string>)
  (nr : PT.NameResolution<'a>)
  : PT.NameResolution<'a> =
  { nr with
      resolved =
        nr.resolved
        |> Result.mapError (fun err ->
          match onMissing with
          | OnMissing.ThrowError ->
            Exception.raiseInternal
              "Unresolved name when not allowed"
              [ "error", err; "given", given; "currentModule", currentModule ]
          | OnMissing.Allow -> err) }


type GenericName = LibDB.NameLookup.GenericName

let namesToTry = LibDB.NameLookup.namesToTry


/// Generic name resolution that handles the common pattern across Type/Value/Fn resolution.
/// Returns the matched location alongside the resolved name (None for
/// builtins / unresolved). The location is what the user typed, after
/// `namesToTry` candidate expansion — captured at resolution time so
/// dep-edge inserts can populate location columns directly without a
/// post-hoc lookup.
/// `branchId` selects which branch's package view the lookups see (WIP included).
/// Package loading and tests pass `mainBranchId`; CLI-script parsing passes the
/// run's branch so intra-branch WIP resolves.
let resolveGenericName<'FQName, 'Builtin when 'Builtin : comparison>
  (builtins : Option<Set<'Builtin>>)
  (onMissing : OnMissing)
  (branchId : PT.BranchId)
  (currentModule : List<string>)
  (given : NEList<string>)
  (parseName : string -> Result<string * int, string>)
  (findInPM : (PT.BranchId * PT.PackageLocation) -> Ply<Option<Hash>>)
  (makePackageFQName : Hash -> 'FQName)
  (makeBuiltinFQName : string * int -> 'FQName)
  (builtinToRT : string * int -> 'Builtin)
  : Ply<PT.NameResolution<'FQName>> =
  uply {
    let originalName = NEList.toList given
    let notFoundError = Error NRE.NotFound
    let (modules, name) = NEList.splitLast given

    match parseName name with
    | Error _ ->
      // a malformed name goes through throwIfRelevant too, so under ThrowError
      // (package loading) it fails the same way a NotFound does, rather than being
      // silently swallowed as InvalidName
      return
        throwIfRelevant
          onMissing
          currentModule
          given
          { originalName = originalName; resolved = Error NRE.InvalidName }
    | Ok(name, version) ->
      let genericName : GenericName =
        { modules = modules; name = name; version = version }

      // Try a candidate. Returns the resolved FQName paired with the
      // matched PackageLocation (None for builtins).
      let tryResolve
        (nameToTry : GenericName)
        : Ply<Result<'FQName * Option<PT.PackageLocation>, unit>> =
        uply {
          match nameToTry.modules with
          | [] -> return Error()
          | owner :: modules ->
            // Check builtins if applicable (values/fns only, not types)
            match builtins with
            | Some builtinSet when owner = "Builtin" && modules = [] ->
              let builtInRT = builtinToRT (nameToTry.name, nameToTry.version)
              if Set.contains builtInRT builtinSet then
                let fqName = makeBuiltinFQName (nameToTry.name, nameToTry.version)
                return Ok(fqName, None)
              else
                return Error()
            | _ ->
              // Try package manager lookup
              let location : PT.PackageLocation =
                { owner = owner; modules = modules; name = nameToTry.name }
              match! findInPM (branchId, location) with
              | Some id -> return Ok(makePackageFQName id, Some location)
              | None -> return Error()
        }

      let! (result, location) =
        Ply.List.foldSequentially
          (fun (currentResult, currentLoc) nameToTry ->
            match currentResult with
            | Ok _ -> Ply((currentResult, currentLoc))
            | Error _ ->
              uply {
                match! tryResolve nameToTry with
                | Error() -> return (currentResult, currentLoc)
                | Ok(success, loc) -> return (Ok success, loc)
              })
          (notFoundError, None)
          (namesToTry currentModule genericName)

      let resolved =
        result |> Result.map (fun name -> { name = name; location = location })
      return
        throwIfRelevant
          onMissing
          currentModule
          given
          { originalName = originalName; resolved = resolved }
  }


let resolveTypeName
  (packageManager : PT.PackageManager)
  (onMissing : OnMissing)
  (branchId : PT.BranchId)
  (currentModule : List<string>)
  (name : WT.Name)
  : Ply<PT.NameResolution<PT.FQTypeName.FQTypeName>> =
  let warning = "Builtin types don't exist"
  let emptyBuiltins = None // irrelevant for types

  match name with
  // TODO remodel things appropriately so this is not needed
  | WT.KnownBuiltin(_name, _version) -> Exception.raiseInternal warning []
  | WT.Unresolved given ->
    // Types don't have builtins, so pass None
    // parseTypeName returns just name (version always 0 for types)
    let parseTypeName name = parseTypeNameString name |> Result.map (fun n -> (n, 0))

    resolveGenericName
      emptyBuiltins
      onMissing
      branchId
      currentModule
      given
      parseTypeName
      packageManager.findType
      PT.FQTypeName.FQTypeName.Package
      (fun _ -> Exception.raiseInternal warning [])
      (fun _ -> Exception.raiseInternal warning [])



let resolveValueName
  (builtins : Set<RT.FQValueName.Builtin>)
  (packageManager : PT.PackageManager)
  (onMissing : OnMissing)
  (branchId : PT.BranchId)
  (currentModule : List<string>)
  (name : WT.Name)
  : Ply<PT.NameResolution<PT.FQValueName.FQValueName>> =
  match name with
  | WT.KnownBuiltin(name, version) ->
    Ply(
      { originalName = [ name ]
        resolved =
          Ok { name = PT.FQValueName.fqBuiltIn name version; location = None } }
      : PT.NameResolution<_>
    )
  | WT.Unresolved given ->
    resolveGenericName
      (Some builtins)
      onMissing
      branchId
      currentModule
      given
      parseFnNameString
      packageManager.findValue
      PT.FQValueName.FQValueName.Package
      (fun (n, v) -> PT.FQValueName.Builtin { name = n; version = v })
      (fun (n, v) -> { RT.FQValueName.Builtin.name = n; version = v })


let resolveFnName
  (builtinFns : Set<RT.FQFnName.Builtin>)
  (packageManager : PT.PackageManager)
  (onMissing : OnMissing)
  (branchId : PT.BranchId)
  (currentModule : List<string>)
  (name : WT.Name)
  : Ply<PT.NameResolution<PT.FQFnName.FQFnName>> =
  match name with
  | WT.KnownBuiltin(n, v) ->
    Ply(
      { originalName = [ n ]
        resolved = Ok { name = PT.FQFnName.fqBuiltIn n v; location = None } }
      : PT.NameResolution<_>
    )
  | WT.Unresolved given ->
    resolveGenericName
      (Some builtinFns)
      onMissing
      branchId
      currentModule
      given
      parseFnNameString
      packageManager.findFn
      PT.FQFnName.FQFnName.Package
      (fun (n, v) -> PT.FQFnName.Builtin { name = n; version = v })
      (fun (n, v) -> { RT.FQFnName.Builtin.name = n; version = v })
