/// Parses F# files into a TestModule type, which can be used to run tests.
module TestUtils.TestFileParser

// refer to https://fsharp.github.io/fsharp-compiler-docs

open FSharp.Compiler
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax

open Prelude
open Tablecloth

module P = Parser
module PT = LibExecution.ProgramTypes

module Test =
  type T = { name : string; lineNumber : int; actual : PT.Expr; expected : PT.Expr }

  let fromSynExpr availableTypes (ast : SynExpr) : T =
    let convert (x : SynExpr) : PT.Expr = P.Expr.fromSynExpr availableTypes x

    match ast with
    | SynExpr.App (_,
                   _,
                   SynExpr.App (_,
                                _,
                                SynExpr.LongIdent (_,
                                                   SynLongIdent ([ ident ], _, _),
                                                   _,
                                                   _),
                                actual,
                                _),
                   expected,
                   range) when ident.idText = "op_Equality" ->
      // Exception.raiseInternal $"whole thing: {actual}"
      { name = "test"
        lineNumber = range.Start.Line
        actual = convert actual
        expected = convert expected }

    | _ -> Exception.raiseInternal "Test case not in format `x = y`" [ "ast", ast ]

module TestModule =
  type T =
    { types : List<PT.UserType.T>
      dbs : List<PT.DB.T>
      fns : List<PT.UserFunction.T>
      packageFns : List<PT.Package.Fn>
      modules : List<string * T>
      exprs : List<Test.T> }

  let emptyModule =
    { types = []; dbs = []; fns = []; modules = []; exprs = []; packageFns = [] }

  type TypeDefs =
    list<PT.FQTypeName.T * PT.UserType.Definition>

  let longIdentToList (li : LongIdent) : List<string> =
    li |> List.map (fun id -> id.idText)

  let parseTypeDecl
    (availableTypes : List<PT.FQTypeName.T * PT.CustomType.T>)
    (typeDef : SynTypeDefn)
    : List<PT.DB.T> * List<PT.UserType.T> =
    match typeDef with
    | SynTypeDefn (SynComponentInfo (attrs, _, _, _, _, _, _, _), _, _, _, _, _) ->
      let attrs = attrs |> List.map (fun attr -> attr.Attributes) |> List.concat
      let isDB =
        attrs
        |> List.exists (fun attr ->
          longIdentToList attr.TypeName.LongIdent = [ "DB" ])
      if isDB then
        [ P.UserDB.fromSynTypeDefn availableTypes typeDef ], []
      else
        [], [ P.UserType.fromSynTypeDefn availableTypes typeDef ]

  let getPackage (attrs : SynAttributes) : Option<string * string * string> =
    attrs
    |> List.map (fun attr -> attr.Attributes)
    |> List.concat
    |> List.filterMap (fun (attr : SynAttribute) ->
      if longIdentToList attr.TypeName.LongIdent = [ "Package" ] then
        match attr.ArgExpr with
        | SynExpr.Paren (SynExpr.Tuple (_,
                                        [ SynExpr.Const (SynConst.String (p1, _, _),
                                                         _)
                                          SynExpr.Const (SynConst.String (p2, _, _),
                                                         _)
                                          SynExpr.Const (SynConst.String (p3, _, _),
                                                         _) ],
                                        _,
                                        _),
                         _,
                         _,
                         _) -> Some(p1, p2, p3)
        | _ -> None
      else
        None)
    |> List.tryHead

  let rec parseModule
    (parseExprFn : TypeDefs -> SynExpr -> Test.T)
    (stdlibTypes : List<PT.FQTypeName.T * PT.CustomType.T>)
    (parent : T)
    (attrs : SynAttributes)
    (decls : List<SynModuleDecl>)
    : T =
    let package : Option<string * string * string> = getPackage attrs

    List.fold
      { types = parent.types
        fns = parent.fns
        packageFns = parent.packageFns
        dbs = parent.dbs
        modules = []
        exprs = [] }
      (fun m decl ->
        let availableTypes =
          (m.types @ parent.types)
          |> List.map (fun t -> PT.FQTypeName.User t.name, t.definition)
          |> (@) stdlibTypes

        match decl with
        | SynModuleDecl.Let (_, bindings, _) ->
          match package with
          | Some package ->
            let newPackageFns = List.map (P.PackageFn.fromSynBinding package) bindings
            { m with packageFns = m.packageFns @ newPackageFns }

          | None ->
            let newUserFns =
              List.map (P.UserFunction.fromSynBinding availableTypes) bindings
            { m with fns = m.fns @ newUserFns }

        | SynModuleDecl.Types (defns, _) ->
          let (dbs, types) =
            List.map (parseTypeDecl availableTypes) defns |> List.unzip
          { m with
              types = m.types @ List.concat types
              dbs = m.dbs @ List.concat dbs }

        | SynModuleDecl.Expr (expr, _) ->
          { m with exprs = m.exprs @ [ parseExprFn availableTypes expr ] }

        | SynModuleDecl.NestedModule (SynComponentInfo (attrs,
                                                        _,
                                                        _,
                                                        [ name ],
                                                        _,
                                                        _,
                                                        _,
                                                        _),
                                      _,
                                      decls,
                                      _,
                                      _,
                                      _) ->
          let nested = parseModule parseExprFn stdlibTypes m attrs decls
          { m with modules = m.modules @ [ (name.idText, nested) ] }
        | _ -> Exception.raiseInternal $"Unsupported declaration" [ "decl", decl ])
      decls

  let fromFSharpModule
    (stdlibTypes : List<PT.FQTypeName.T * PT.CustomType.T>)
    (modul : SynModuleOrNamespace)
    : T =

    match modul with
    | SynModuleOrNamespace (_, _, _, decls, _, attrs, _, _, _) ->
      parseModule Test.fromSynExpr stdlibTypes emptyModule attrs decls


let parseTestFile
  (availableTypes : List<PT.FQTypeName.T * PT.CustomType.T>)
  (sourceFilename : string)
  (sourceCode : string)
  : TestModule.T =
  P.parseFSharpModule sourceCode
  |> TestModule.fromFSharpModule availableTypes
