/// Parses F# files into a TestModule type, which can be used to run tests.
module CanvasHack.SingleFileCanvasParser

// refer to https://fsharp.github.io/fsharp-compiler-docs

open FSharp.Compiler
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax

open Prelude
open Tablecloth

module P = Parser
module PT = LibExecution.ProgramTypes

type Canvas =
  { handlers : List<PT.Handler.T>
    dbs : List<PT.DB.T>
    functions : List<PT.UserFunction.T>
    types : List<PT.UserType.T> }

let emptyCanvas = { handlers = []; dbs = []; functions = []; types = [] }


// type TypeDefs =
//   list<PT.FQTypeName.T * PT.UserType.Definition>

// let longIdentToList (li : LongIdent) : List<string> =
//   li |> List.map (fun id -> id.idText)


//P.UserType.fromSynTypeDefn availableTypes typeDef


let parseCanvas
  (availableTypes : List<PT.FQTypeName.T * PT.CustomType.T>)
  (modul : SynModuleOrNamespace)
  : Canvas =

  let decls =
    match modul with
    | SynModuleOrNamespace (_, _, _, decls, _, attrs, _, _, _) -> decls
      //parseModule Test.fromSynExpr stdlibTypes emptyModule attrs decls

  List.fold
    emptyCanvas
    (fun m decl ->
      // concat 'above-defined' available types with
      // types already defined within the module
      let availableTypes =
        m.types // "types so far"
        |> List.map (fun t -> PT.FQTypeName.User t.name, t.definition)
        |> (@) availableTypes


      match decl with

      // Custom type declarations (enums, records, etc.)
      | SynModuleDecl.Types (defns, _) ->
        let newTypes = List.map (P.UserType.fromSynTypeDefn availableTypes) defns
        { m with types = newTypes @ m.types }

      // // TODO: based on attributes, probably map some of these to handlers
    // // HTTP, REPL, etc.
      // // e.g. [<REPL("nameOfRepl")>]
      // // otherwise they might just be fns
      // // (we could yell if _no_ attributes are present)
      // also some of them are really fn definitions I think
      | SynModuleDecl.Let (_, bindings, _) ->

        let newFns =
          List.map (P.UserFunction.fromSynBinding availableTypes) bindings
        { m with functions = m.functions @ newFns }


      // // I guess these are "TODO"s to be executed as we parse?
      // | SynModuleDecl.Expr (expr, _) ->
      //   { m with exprs = m.exprs @ [ parseExprFn availableTypes expr ] }

      | _ -> Exception.raiseInternal $"Unsupported declaration" [ "decl", decl ])
    decls


// let fromFSharpModule
//   (stdlibTypes : List<PT.FQTypeName.T * PT.CustomType.T>)
//   (modul : SynModuleOrNamespace)
//   : T =

//   match modul with
//   | SynModuleOrNamespace (_, _, _, decls, _, attrs, _, _, _) ->
//     parseModule Test.fromSynExpr stdlibTypes emptyModule attrs decls


// let parseTestFile
//   (availableTypes : List<PT.FQTypeName.T * PT.CustomType.T>)
//   (sourceCode : string)
//   : TestModule.T =
//   P.parseFSharpModule sourceCode
//   |> TestModule.fromFSharpModule availableTypes
