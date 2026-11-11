/// Where the CLI can stop with a runtime error: every call from the CLI's own code to a builtin
/// that declares `Raise`, or to a stdlib function that reaches one.
///
/// The stdlib keeps its raising functions for users, so a call into it is where the CLI chooses
/// to raise; a call between two CLI functions is not a site, the raise is inside the callee.
module LibDB.RaiseSites

open Prelude
open Fumble
open LibDB.Sqlite

module PT = LibExecution.ProgramTypes
module Effects = LibExecution.Effects
module Requirements = LibExecution.CallGraph.Requirements

/// One call, by name: `caller` is the CLI's, `callee` a raising builtin or stdlib function.
type Site = { caller : string; callee : string }

/// Where the walk starts: what a person running `dark` can reach.
let entryPoints : List<string> =
  [ "Darklang.Cli.executeCliCommand"
    "Darklang.Cli.Registry.allCommands"
    "Darklang.Cli.initState"
    "Darklang.Cli.Workbench.execute" ]

/// The library users call too. Everything else the CLI reaches ships as part of the CLI.
let isLibrary (name : string) : bool = name.StartsWith "Darklang.Stdlib."

/// Stdlib functions that cannot raise, though they call something that declares `Raise`. The walk
/// cannot see why, so each kind is listed with its reason.
///
/// The numeric impls, for their own type: the polymorphic builtin they call declares `Raise` for
/// another (`Builtin.add` raises past Decimal's range). As `LibExecution.NumOps` has it, the
/// fixed-width integers wrap, BigInt grows, and Float and Float32 follow IEEE.
///
/// The checked functions, which test their arguments for every case where what they call raises,
/// and return an `Error` instead.
let private cannotRaise : Set<string> =
  let guarded =
    [ "Int64.checkedDivide"
      "Int64.checkedRemainder"
      "Int64.checkedMod"
      "String.checkedRepeat"
      "List.checkedRepeat"
      "List.checkedRange" ]
    |> List.map (fun f -> $"Darklang.Stdlib.{f}")
  let ints =
    [ "Int8"
      "Int16"
      "Int32"
      "Int64"
      "Int128"
      "UInt8"
      "UInt16"
      "UInt32"
      "UInt64"
      "UInt128" ]
  let impls (types : List<string>) (ops : List<string * string>) =
    types
    |> List.collect (fun t ->
      ops
      |> List.map (fun (traitName, op) -> $"Darklang.Stdlib.{t}.{traitName}.{op}"))
  let wrapping =
    [ ("Add", "add")
      ("Subtract", "subtract")
      ("Multiply", "multiply")
      ("Negate", "negate") ]
  Set.ofList (
    impls
      ints
      (wrapping @ [ ("ShiftLeft", "shiftLeft"); ("ShiftRight", "shiftRight") ])
    @ impls [ "BigInt" ] wrapping
    @ impls
        [ "Float"; "Float32" ]
        (wrapping @ [ ("Divide", "divide"); ("Power", "power") ])
    @ guarded
  )

/// Each live fn's name. A hash with several names keeps the first in order.
let fnNames () : Ply<Map<PT.Hash, string>> =
  uply {
    let! rows =
      Sql.query
        "SELECT item_hash, owner, modules, name FROM locations
         WHERE live = 1 AND item_hash <> '' AND item_type = 'fn'"
      |> Sql.executeAsync (fun read ->
        (read.string "item_hash",
         read.string "owner",
         read.string "modules",
         read.string "name"))
    return
      rows
      |> List.map (fun (h, o, m, n) ->
        (PT.Hash h, (if m = "" then $"{o}.{n}" else $"{o}.{m}.{n}")))
      |> List.groupBy fst
      |> Map.map (fun named -> named |> List.map snd |> List.min)
  }

/// The CLI's fns named in a string in its source and resolved at run time, such as a live view's
/// `init = "Darklang.Cli.TraceWatch.init"`. No body references them, so they are roots too.
let private namedInStrings (sourceRoot : string) : List<string> =
  let re = System.Text.RegularExpressions.Regex "\"(Darklang(?:\\.[A-Za-z0-9_]+)+)\""
  [ "packages/darklang/cli"; "packages/darklang/languageTools" ]
  |> List.map (fun dir -> System.IO.Path.Combine(sourceRoot, dir))
  |> List.filter System.IO.Directory.Exists
  |> List.collect (fun dir ->
    System.IO.Directory.EnumerateFiles(
      dir,
      "*.dark",
      System.IO.SearchOption.AllDirectories
    )
    |> List.ofSeq)
  |> List.collect (fun f ->
    re.Matches(System.IO.File.ReadAllText f)
    |> Seq.map (fun m -> m.Groups[1].Value)
    |> List.ofSeq)
  |> List.distinct

/// The package fns reachable from the entry points, each body analyzed once.
let closure
  (sourceRoot : string)
  (names : Map<PT.Hash, string>)
  (load : PackagePermissions.Load)
  : Ply<Requirements.Closure> =
  let byName = names |> Map.toList |> List.map (fun (h, n) -> (n, h)) |> Map.ofList
  entryPoints @ namedInStrings sourceRoot
  |> List.choose (fun n -> Map.tryFind n byName)
  |> List.distinct
  |> Ply.List.foldSequentially
    (fun loaded root ->
      if Map.containsKey root loaded then
        Ply loaded
      else
        PackagePermissions.loadClosure load root
        |> Ply.map (fun more ->
          Map.fold (fun acc k v -> Map.add k v acc) loaded more))
    Map.empty

/// What the analysis found: each raising call, and for each callee, the raising builtins it
/// reaches, which say why it is on the list.
type Found = { sites : List<Site>; reasons : Map<string, Set<string>> }

let find
  (callEffectsFor : PackagePermissions.CallEffectsFor)
  (names : Map<PT.Hash, string>)
  (closure : Requirements.Closure)
  : Found =
  let nameOf (h : PT.Hash) =
    match Map.tryFind h names with
    | Some n -> n
    | None ->
      let (PT.Hash raw) = h
      raw

  let builtinRaises (b : PT.FQFnName.Builtin) =
    callEffectsFor (b.name, b.version)
    |> Option.exists (Set.contains Effects.Effect.Raise)

  let direct (calls : LibExecution.CallGraph.Analysis) : Set<string> =
    calls.names
    |> List.choose (function
      | PT.FQFnName.Builtin b when builtinRaises b -> Some $"Builtin.{b.name}"
      | _ -> None)
    |> Set.ofList

  let callees (calls : LibExecution.CallGraph.Analysis) : List<PT.Hash> =
    calls.names
    |> List.choose (function
      | PT.FQFnName.Package p -> Some p
      | _ -> None)

  // Each fn's raising builtins, own and reached, settled by iterating until nothing grows.
  let rec settle (reached : Map<PT.Hash, Set<string>>) : Map<PT.Hash, Set<string>> =
    let next =
      reached
      |> Map.mapWithIndex (fun h own ->
        if Set.contains (nameOf h) cannotRaise then
          Set.empty
        else
          match Map.tryFind h closure with
          | None -> own
          | Some(_, calls) ->
            callees calls
            |> List.choose (fun c -> Map.tryFind c reached)
            |> List.fold Set.union own)
    let size (m : Map<PT.Hash, Set<string>>) =
      m |> Map.values |> List.sumBy Set.count
    if size next = size reached then reached else settle next

  let reached =
    closure
    |> Map.mapWithIndex (fun h (_, calls) ->
      if Set.contains (nameOf h) cannotRaise then Set.empty else direct calls)
    |> settle

  let raises (h : PT.Hash) =
    Map.tryFind h reached |> Option.exists (fun s -> not (Set.isEmpty s))

  let sites =
    closure
    |> Map.toList
    |> List.collect (fun (h, (_, calls)) ->
      let caller = nameOf h
      if isLibrary caller then
        []
      else
        calls.names
        |> List.choose (function
          | PT.FQFnName.Builtin b when builtinRaises b ->
            Some { caller = caller; callee = $"Builtin.{b.name}" }
          | PT.FQFnName.Package p when raises p && isLibrary (nameOf p) ->
            Some { caller = caller; callee = nameOf p }
          | _ -> None))

  let reasons =
    closure
    |> Map.toList
    |> List.choose (fun (h, _) ->
      let name = nameOf h
      if isLibrary name && raises h then
        Some(name, Map.tryFind h reached |> Option.defaultValue Set.empty)
      else
        None)
    |> Map.ofList

  { sites = sites; reasons = reasons }

/// The baseline's lines: `<count> <caller> <callee>`, sorted, one per caller and callee.
let baselineLines (sites : List<Site>) : List<string> =
  sites
  |> List.countBy identity
  |> List.map (fun (s, count) -> $"{count} {s.caller} {s.callee}")
  |> List.sort
