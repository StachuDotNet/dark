module BuiltinExecution.Libs.LanguageTools

open Prelude
open LibExecution.RuntimeTypes
open LibExecution.Builtin.Shortcuts

module VT = ValueType
module Dval = LibExecution.Dval
module Interpreter = LibExecution.Interpreter
module TypeChecker = LibExecution.TypeChecker
module DvalReprDeveloper = LibExecution.DvalReprDeveloper




let constants : List<BuiltInConstant> = []

let typ
  (addlModules : List<string>)
  (name : string)
  (version : int)
  : FQTypeName.FQTypeName =
  FQTypeName.fqPackage "Darklang" ([ "LanguageTools" ] @ addlModules) name version


let fns : List<BuiltInFn> =
  [ { name = fn "languageToolsAllBuiltinFns" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TList(TCustomType(Ok(typ [] "BuiltinFunction" 0), []))
      description =
        "Returns a list of Function records, representing the functions available in the standard library."
      fn =
        (function
        | state, _, [ DUnit ] ->
          let typeNameToStr = LibExecution.DvalReprDeveloper.typeName

          let fnParamTypeName = typ [] "BuiltinFunctionParameter" 0
          let fnTypeName = typ [] "BuiltinFunction" 0

          let fns =
            state.builtIns.fns
            |> Map.toList
            |> List.map (fun (key, data) ->
              let parameters =
                data.parameters
                |> List.map (fun p ->
                  let fields =
                    [ "name", DString p.name
                      "type", DString(typeNameToStr p.typ) ]
                  DRecord(fnParamTypeName, fnParamTypeName, [], Map fields))
                |> Dval.list (KTCustomType(fnParamTypeName, []))

              let fields =
                [ "name", DString(FQFnName.builtinToString key)
                  "description", DString data.description
                  "parameters", parameters
                  "returnType", DString(typeNameToStr data.returnType) ]

              DRecord(fnTypeName, fnTypeName, [], Map fields))

          DList(VT.customType fnTypeName [], fns) |> Ply
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated } ]

let contents : Builtins = LibExecution.Builtin.fromContents constants fns
