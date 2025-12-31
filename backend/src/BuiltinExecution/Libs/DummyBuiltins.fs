/// Dummy implementations of builtins that are normally provided by other projects
/// (like BuiltinPM) but are disabled for the bootstrap CLI.
///
/// These return reasonable defaults or None to allow the CLI to run without
/// the full package manager infrastructure.
module BuiltinExecution.Libs.DummyBuiltins

open LibExecution.RuntimeTypes
open Prelude
open LibExecution.Builtin.Shortcuts
module VT = LibExecution.ValueType
module Dval = LibExecution.Dval
module PackageIDs = LibExecution.PackageIDs


// Helper to create an empty SearchResults record
let private emptySearchResults () : Dval =
  let typeName =
    FQTypeName.fqPackage
      PackageIDs.Type.LanguageTools.ProgramTypes.Search.searchResults

  // LocatedItem is a generic wrapper type
  let locatedItemTypeName =
    FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.locatedItem

  let packageTypeTypeName =
    FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.PackageType.packageType
  let packageValueTypeName =
    FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.PackageValue.packageValue
  let packageFnTypeName =
    FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.PackageFn.packageFn

  let fields =
    [ "submodules", Dval.list (KTList VT.string) []
      "types", Dval.list (KTCustomType(locatedItemTypeName, [ VT.customType packageTypeTypeName [] ])) []
      "values", Dval.list (KTCustomType(locatedItemTypeName, [ VT.customType packageValueTypeName [] ])) []
      "fns", Dval.list (KTCustomType(locatedItemTypeName, [ VT.customType packageFnTypeName [] ])) [] ]
  DRecord(typeName, typeName, [], Map fields)


let fns : List<BuiltInFn> =
  [ // From BuiltinPM/Libs/Accounts.fs
    { name = fn "pmGetAccountByName" 0
      typeParams = []
      parameters = [ Param.make "name" TString "" ]
      returnType = TypeReference.option TUuid
      description = "Get account UUID by name (dummy: always returns None)"
      fn =
        (function
        | _, _, _, [ DString _name ] ->
          Ply(Dval.optionNone KTUuid)
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "pmGetAccountNameById" 0
      typeParams = []
      parameters = [ Param.make "id" TUuid "" ]
      returnType = TypeReference.option TString
      description = "Get account name by UUID (dummy: always returns None)"
      fn =
        (function
        | _, _, _, [ DUuid _id ] ->
          Ply(Dval.optionNone KTString)
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // From BuiltinPM/Libs/Packages.fs
    { name = fn "pmSearch" 0
      typeParams = []
      parameters =
        [ Param.make "accountID" (TypeReference.option TUuid) ""
          Param.make "branchID" (TypeReference.option TUuid) ""
          Param.make "query" (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.Search.searchQuery), [])) "" ]
      returnType = TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.Search.searchResults), [])
      description = "Search for packages (dummy: returns empty results)"
      fn =
        (function
        | _, _, _, [ _accountID; _branchID; _query ] ->
          Ply(emptySearchResults ())
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "pmFindType" 0
      typeParams = []
      parameters =
        [ Param.make "accountID" (TypeReference.option TUuid) ""
          Param.make "branchID" (TypeReference.option TUuid) ""
          Param.make "location" (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation), [])) "" ]
      returnType = TypeReference.option TUuid
      description = "Find type by location (dummy: returns None)"
      fn =
        (function
        | _, _, _, [ _; _; _ ] -> Ply(Dval.optionNone KTUuid)
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "pmFindValue" 0
      typeParams = []
      parameters =
        [ Param.make "accountID" (TypeReference.option TUuid) ""
          Param.make "branchID" (TypeReference.option TUuid) ""
          Param.make "location" (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation), [])) "" ]
      returnType = TypeReference.option TUuid
      description = "Find value by location (dummy: returns None)"
      fn =
        (function
        | _, _, _, [ _; _; _ ] -> Ply(Dval.optionNone KTUuid)
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "pmFindFn" 0
      typeParams = []
      parameters =
        [ Param.make "accountID" (TypeReference.option TUuid) ""
          Param.make "branchID" (TypeReference.option TUuid) ""
          Param.make "location" (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation), [])) "" ]
      returnType = TypeReference.option TUuid
      description = "Find function by location (dummy: returns None)"
      fn =
        (function
        | _, _, _, [ _; _; _ ] -> Ply(Dval.optionNone KTUuid)
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "pmGetType" 0
      typeParams = []
      parameters = [ Param.make "id" TUuid "" ]
      returnType = TypeReference.option (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.PackageType.packageType), []))
      description = "Get type by ID (dummy: returns None)"
      fn =
        (function
        | _, _, _, [ DUuid _ ] ->
          Ply(Dval.optionNone (KTCustomType(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.PackageType.packageType, [])))
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "pmGetValue" 0
      typeParams = []
      parameters = [ Param.make "id" TUuid "" ]
      returnType = TypeReference.option (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.PackageValue.packageValue), []))
      description = "Get value by ID (dummy: returns None)"
      fn =
        (function
        | _, _, _, [ DUuid _ ] ->
          Ply(Dval.optionNone (KTCustomType(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.PackageValue.packageValue, [])))
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "pmGetFn" 0
      typeParams = []
      parameters = [ Param.make "id" TUuid "" ]
      returnType = TypeReference.option (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.PackageFn.packageFn), []))
      description = "Get function by ID (dummy: returns None)"
      fn =
        (function
        | _, _, _, [ DUuid _ ] ->
          Ply(Dval.optionNone (KTCustomType(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.PackageFn.packageFn, [])))
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "pmGetLocationByType" 0
      typeParams = []
      parameters =
        [ Param.make "accountID" (TypeReference.option TUuid) ""
          Param.make "branchID" (TypeReference.option TUuid) ""
          Param.make "id" TUuid "" ]
      returnType = TypeReference.option (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation), []))
      description = "Get location by type ID (dummy: returns None)"
      fn =
        (function
        | _, _, _, [ _; _; DUuid _ ] ->
          Ply(Dval.optionNone (KTCustomType(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation, [])))
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "pmGetLocationByValue" 0
      typeParams = []
      parameters =
        [ Param.make "accountID" (TypeReference.option TUuid) ""
          Param.make "branchID" (TypeReference.option TUuid) ""
          Param.make "id" TUuid "" ]
      returnType = TypeReference.option (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation), []))
      description = "Get location by value ID (dummy: returns None)"
      fn =
        (function
        | _, _, _, [ _; _; DUuid _ ] ->
          Ply(Dval.optionNone (KTCustomType(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation, [])))
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "pmGetLocationByFn" 0
      typeParams = []
      parameters =
        [ Param.make "accountID" (TypeReference.option TUuid) ""
          Param.make "branchID" (TypeReference.option TUuid) ""
          Param.make "id" TUuid "" ]
      returnType = TypeReference.option (TCustomType(Ok(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation), []))
      description = "Get location by function ID (dummy: returns None)"
      fn =
        (function
        | _, _, _, [ _; _; DUuid _ ] ->
          Ply(Dval.optionNone (KTCustomType(FQTypeName.fqPackage PackageIDs.Type.LanguageTools.ProgramTypes.packageLocation, [])))
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // From BuiltinPM/Libs/Scripts.fs
    { name = fn "pmScriptsList" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TList TString
      description = "List scripts (dummy: returns empty list)"
      fn =
        (function
        | _, _, _, [ DUnit ] -> Ply(Dval.list KTString [])
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "pmScriptsGet" 0
      typeParams = []
      parameters = [ Param.make "name" TString "" ]
      returnType = TypeReference.option TString
      description = "Get script by name (dummy: returns None)"
      fn =
        (function
        | _, _, _, [ DString _ ] -> Ply(Dval.optionNone KTString)
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "pmScriptsAdd" 0
      typeParams = []
      parameters =
        [ Param.make "name" TString ""
          Param.make "content" TString "" ]
      returnType = TUnit
      description = "Add script (dummy: no-op)"
      fn =
        (function
        | _, _, _, [ DString _; DString _ ] -> Ply DUnit
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "pmScriptsUpdate" 0
      typeParams = []
      parameters =
        [ Param.make "name" TString ""
          Param.make "content" TString "" ]
      returnType = TUnit
      description = "Update script (dummy: no-op)"
      fn =
        (function
        | _, _, _, [ DString _; DString _ ] -> Ply DUnit
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "pmScriptsDelete" 0
      typeParams = []
      parameters = [ Param.make "name" TString "" ]
      returnType = TUnit
      description = "Delete script (dummy: no-op)"
      fn =
        (function
        | _, _, _, [ DString _ ] -> Ply DUnit
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated } ]


let builtins = LibExecution.Builtin.make [] fns
