/// Dummy implementations of builtins that are normally provided by other projects
/// (like BuiltinPM) but are disabled for the bootstrap CLI.
///
/// These return reasonable defaults or None to allow the CLI to run without
/// the full package manager infrastructure.
///
/// Note: pm* builtins for package browsing (pmSearch, pmFind*, pmGet*, pmGetLocationBy*)
/// are implemented in BuiltinCliHost/Libs/BootstrapPM.fs using PackagesBootstrap.
/// This file only contains stubs for account and script management.
module BuiltinExecution.Libs.DummyBuiltins

open LibExecution.RuntimeTypes
open Prelude
open LibExecution.Builtin.Shortcuts
module Dval = LibExecution.Dval


let fns : List<BuiltInFn> =
  [ // From BuiltinPM/Libs/Accounts.fs - stub implementations
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


    // From BuiltinPM/Libs/Scripts.fs - stub implementations
    // Note: pm* package builtins (pmSearch, pmFind*, pmGet*, pmGetLocationBy*)
    // are implemented in BuiltinCliHost/Libs/BootstrapPM.fs
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
