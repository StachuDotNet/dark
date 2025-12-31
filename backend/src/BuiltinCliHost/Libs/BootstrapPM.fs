/// Package Manager builtins that use PackagesBootstrap
/// These provide real implementations for package browsing in the bootstrap CLI
module BuiltinCliHost.Libs.BootstrapPM

open LibExecution.RuntimeTypes
open Prelude
open LibExecution.Builtin.Shortcuts
module VT = LibExecution.ValueType
module Dval = LibExecution.Dval
module PackageIDs = LibExecution.PackageIDs
module PT = LibExecution.ProgramTypes
module PT2DT = LibExecution.ProgramTypesToDarkTypes

// Access the PackagesBootstrap store
let private store = PackagesBootstrap.PackageManager.lazyStore.Force()


// Helper to create SearchResults from store data
let private searchResults
  (submodules : List<List<string>>)
  (types : List<PT.LocatedItem<PT.PackageType.PackageType>>)
  (values : List<PT.LocatedItem<PT.PackageValue.PackageValue>>)
  (fns : List<PT.LocatedItem<PT.PackageFn.PackageFn>>)
  : Dval =
  PT2DT.Search.SearchResults.toDT
    { PT.Search.SearchResults.submodules = submodules
      types = types
      values = values
      fns = fns }


let fns : List<BuiltInFn> =
  [ // Real pmSearch implementation using PackagesBootstrap
    { name = fn "pmSearch" 0
      typeParams = []
      parameters =
        [ Param.make "accountID" (TypeReference.option TUuid) ""
          Param.make "branchID" (TypeReference.option TUuid) ""
          Param.make "query" (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.Search.searchQuery), [])) "" ]
      returnType = TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.Search.searchResults), [])
      description = "Search for packages in the bootstrap store"
      fn =
        (function
        | _, _, _, [ _accountID; _branchID; query ] ->
          uply {
            let searchQuery = PT2DT.Search.SearchQuery.fromDT query

            let currentModule = searchQuery.currentModule
            let searchText = searchQuery.text
            let searchDepth = searchQuery.searchDepth
            let entityTypes = searchQuery.entityTypes
            let exactMatch = searchQuery.exactMatch

            // Helper to check if a location matches the current module prefix
            let locationMatchesModule (loc : PT.PackageLocation) =
              let locModules = loc.owner :: loc.modules
              let currentParts = currentModule
              if List.isEmpty currentParts then
                true // At root, everything matches
              else
                // Check if location starts with currentModule
                let locPath = String.concat "." locModules
                let currentPath = String.concat "." currentParts
                locPath.StartsWith(currentPath) ||
                locPath = currentPath

            // Helper to check if name matches search text
            let nameMatches (name : string) =
              if System.String.IsNullOrEmpty searchText then true
              else if exactMatch then name = searchText
              else name.Contains(searchText, System.StringComparison.OrdinalIgnoreCase)

            // Helper to check depth constraint
            let depthMatches (loc : PT.PackageLocation) =
              match searchDepth with
              | PT.Search.SearchDepth.AllDescendants -> true
              | PT.Search.SearchDepth.OnlyDirectDescendants ->
                let locModules = loc.owner :: loc.modules
                let currentParts = currentModule
                // Direct descendant means exactly one level deeper
                locModules.Length = currentParts.Length + 1 ||
                (List.isEmpty currentParts && locModules.Length = 1)

            // Collect submodules from ALL items matching the module prefix
            // This shows the namespace hierarchy even when no direct items match
            let submodules = System.Collections.Generic.HashSet<string list>()

            // Helper to get the submodule path at the next level
            let getSubmodulePath (loc : PT.PackageLocation) =
              let locModules = loc.owner :: loc.modules
              let currentLen = List.length currentModule
              // Get the path up to currentLen + 1 (next level)
              if locModules.Length > currentLen then
                Some (List.take (currentLen + 1) locModules)
              else
                None

            // Collect submodules from all items matching current module
            for kvp in store.types do
              match store.typeIdToLocation.TryGetValue(kvp.Key) with
              | true, loc when locationMatchesModule loc ->
                match getSubmodulePath loc with
                | Some path -> submodules.Add(path) |> ignore<bool>
                | None -> ()
              | _ -> ()

            for kvp in store.values do
              match store.valueIdToLocation.TryGetValue(kvp.Key) with
              | true, loc when locationMatchesModule loc ->
                match getSubmodulePath loc with
                | Some path -> submodules.Add(path) |> ignore<bool>
                | None -> ()
              | _ -> ()

            for kvp in store.fns do
              match store.fnIdToLocation.TryGetValue(kvp.Key) with
              | true, loc when locationMatchesModule loc ->
                match getSubmodulePath loc with
                | Some path -> submodules.Add(path) |> ignore<bool>
                | None -> ()
              | _ -> ()

            // Search types (with depth filter for items)
            let types : List<PT.LocatedItem<PT.PackageType.PackageType>> =
              if entityTypes.IsEmpty || List.contains PT.Search.EntityType.Type entityTypes then
                store.types
                |> Seq.choose (fun kvp ->
                  match store.typeIdToLocation.TryGetValue(kvp.Key) with
                  | true, loc when locationMatchesModule loc && nameMatches loc.name && depthMatches loc ->
                    Some { PT.LocatedItem.entity = kvp.Value; PT.LocatedItem.location = loc }
                  | _ -> None)
                |> Seq.toList
              else []

            // Search values
            let values : List<PT.LocatedItem<PT.PackageValue.PackageValue>> =
              if entityTypes.IsEmpty || List.contains PT.Search.EntityType.Value entityTypes then
                store.values
                |> Seq.choose (fun kvp ->
                  match store.valueIdToLocation.TryGetValue(kvp.Key) with
                  | true, loc when locationMatchesModule loc && nameMatches loc.name && depthMatches loc ->
                    Some { PT.LocatedItem.entity = kvp.Value; PT.LocatedItem.location = loc }
                  | _ -> None)
                |> Seq.toList
              else []

            // Search functions
            let fns : List<PT.LocatedItem<PT.PackageFn.PackageFn>> =
              if entityTypes.IsEmpty || List.contains PT.Search.EntityType.Fn entityTypes then
                store.fns
                |> Seq.choose (fun kvp ->
                  match store.fnIdToLocation.TryGetValue(kvp.Key) with
                  | true, loc when locationMatchesModule loc && nameMatches loc.name && depthMatches loc ->
                    Some { PT.LocatedItem.entity = kvp.Value; PT.LocatedItem.location = loc }
                  | _ -> None)
                |> Seq.toList
              else []

            return searchResults (submodules |> Seq.toList) types values fns
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // Real pmFindType
    { name = fn "pmFindType" 0
      typeParams = []
      parameters =
        [ Param.make "accountID" (TypeReference.option TUuid) ""
          Param.make "branchID" (TypeReference.option TUuid) ""
          Param.make "location" (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation), [])) "" ]
      returnType = TypeReference.option TUuid
      description = "Find type ID by location"
      fn =
        (function
        | _, _, _, [ _; _; locDval ] ->
          uply {
            let loc = PT2DT.PackageLocation.fromDT locDval
            match store.typeLocationToId.TryGetValue(loc) with
            | true, id -> return Dval.optionSome KTUuid (DUuid id)
            | false, _ -> return Dval.optionNone KTUuid
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // Real pmFindValue
    { name = fn "pmFindValue" 0
      typeParams = []
      parameters =
        [ Param.make "accountID" (TypeReference.option TUuid) ""
          Param.make "branchID" (TypeReference.option TUuid) ""
          Param.make "location" (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation), [])) "" ]
      returnType = TypeReference.option TUuid
      description = "Find value ID by location"
      fn =
        (function
        | _, _, _, [ _; _; locDval ] ->
          uply {
            let loc = PT2DT.PackageLocation.fromDT locDval
            match store.valueLocationToId.TryGetValue(loc) with
            | true, id -> return Dval.optionSome KTUuid (DUuid id)
            | false, _ -> return Dval.optionNone KTUuid
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // Real pmFindFn
    { name = fn "pmFindFn" 0
      typeParams = []
      parameters =
        [ Param.make "accountID" (TypeReference.option TUuid) ""
          Param.make "branchID" (TypeReference.option TUuid) ""
          Param.make "location" (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation), [])) "" ]
      returnType = TypeReference.option TUuid
      description = "Find function ID by location"
      fn =
        (function
        | _, _, _, [ _; _; locDval ] ->
          uply {
            let loc = PT2DT.PackageLocation.fromDT locDval
            match store.fnLocationToId.TryGetValue(loc) with
            | true, id -> return Dval.optionSome KTUuid (DUuid id)
            | false, _ -> return Dval.optionNone KTUuid
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // Real pmGetType
    { name = fn "pmGetType" 0
      typeParams = []
      parameters = [ Param.make "id" TUuid "" ]
      returnType = TypeReference.option (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.PackageType.packageType), []))
      description = "Get type by ID"
      fn =
        (function
        | _, _, _, [ DUuid id ] ->
          uply {
            match store.types.TryGetValue(id) with
            | true, t ->
              let typeName = FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.PackageType.packageType
              return Dval.optionSome (KTCustomType(typeName, [])) (PT2DT.PackageType.toDT t)
            | false, _ ->
              let typeName = FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.PackageType.packageType
              return Dval.optionNone (KTCustomType(typeName, []))
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // Real pmGetValue
    { name = fn "pmGetValue" 0
      typeParams = []
      parameters = [ Param.make "id" TUuid "" ]
      returnType = TypeReference.option (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.PackageValue.packageValue), []))
      description = "Get value by ID"
      fn =
        (function
        | _, _, _, [ DUuid id ] ->
          uply {
            match store.values.TryGetValue(id) with
            | true, v ->
              let typeName = FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.PackageValue.packageValue
              return Dval.optionSome (KTCustomType(typeName, [])) (PT2DT.PackageValue.toDT v)
            | false, _ ->
              let typeName = FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.PackageValue.packageValue
              return Dval.optionNone (KTCustomType(typeName, []))
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // Real pmGetFn
    { name = fn "pmGetFn" 0
      typeParams = []
      parameters = [ Param.make "id" TUuid "" ]
      returnType = TypeReference.option (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.PackageFn.packageFn), []))
      description = "Get function by ID"
      fn =
        (function
        | _, _, _, [ DUuid id ] ->
          uply {
            match store.fns.TryGetValue(id) with
            | true, f ->
              let typeName = FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.PackageFn.packageFn
              return Dval.optionSome (KTCustomType(typeName, [])) (PT2DT.PackageFn.toDT f)
            | false, _ ->
              let typeName = FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.PackageFn.packageFn
              return Dval.optionNone (KTCustomType(typeName, []))
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // Real pmGetLocationByType
    { name = fn "pmGetLocationByType" 0
      typeParams = []
      parameters =
        [ Param.make "accountID" (TypeReference.option TUuid) ""
          Param.make "branchID" (TypeReference.option TUuid) ""
          Param.make "id" TUuid "" ]
      returnType = TypeReference.option (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation), []))
      description = "Get location by type ID"
      fn =
        (function
        | _, _, _, [ _; _; DUuid id ] ->
          uply {
            match store.typeIdToLocation.TryGetValue(id) with
            | true, loc ->
              let typeName = FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation
              return Dval.optionSome (KTCustomType(typeName, [])) (PT2DT.PackageLocation.toDT loc)
            | false, _ ->
              let typeName = FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation
              return Dval.optionNone (KTCustomType(typeName, []))
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // Real pmGetLocationByValue
    { name = fn "pmGetLocationByValue" 0
      typeParams = []
      parameters =
        [ Param.make "accountID" (TypeReference.option TUuid) ""
          Param.make "branchID" (TypeReference.option TUuid) ""
          Param.make "id" TUuid "" ]
      returnType = TypeReference.option (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation), []))
      description = "Get location by value ID"
      fn =
        (function
        | _, _, _, [ _; _; DUuid id ] ->
          uply {
            match store.valueIdToLocation.TryGetValue(id) with
            | true, loc ->
              let typeName = FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation
              return Dval.optionSome (KTCustomType(typeName, [])) (PT2DT.PackageLocation.toDT loc)
            | false, _ ->
              let typeName = FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation
              return Dval.optionNone (KTCustomType(typeName, []))
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // Real pmGetLocationByFn
    { name = fn "pmGetLocationByFn" 0
      typeParams = []
      parameters =
        [ Param.make "accountID" (TypeReference.option TUuid) ""
          Param.make "branchID" (TypeReference.option TUuid) ""
          Param.make "id" TUuid "" ]
      returnType = TypeReference.option (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation), []))
      description = "Get location by function ID"
      fn =
        (function
        | _, _, _, [ _; _; DUuid id ] ->
          uply {
            match store.fnIdToLocation.TryGetValue(id) with
            | true, loc ->
              let typeName = FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation
              return Dval.optionSome (KTCustomType(typeName, [])) (PT2DT.PackageLocation.toDT loc)
            | false, _ ->
              let typeName = FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation
              return Dval.optionNone (KTCustomType(typeName, []))
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated } ]


let builtins = LibExecution.Builtin.make [] fns
