module StdLibExperimental.Libs.TreeSitter

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Tablecloth
open LibExecution.RuntimeTypes

module Errors = LibExecution.Errors

let fn = FQFnName.stdlibFnName

let incorrectArgs = Errors.incorrectArgs
module PT = LibExecution.ProgramTypes



// Ideally, all of this would be in Dark code
module SyntaxTree =
  // This is what's sent to us from the frontend
  type TreeSitterNode =
    { typ : string
      text : string
      fieldName : Option<string>
      children : List<TreeSitterNode> }

  type TreeSitterTreeTpl = Node of (string * string * List<TreeSitterTreeTpl>)

  let rec toTuple (node : TreeSitterNode) : TreeSitterTreeTpl =
    Node(node.typ, node.text, (node.children |> List.map toTuple))

  // Ideally this would return the _Dark_ PT.Expr, not the F# one
  let toExpr (node : TreeSitterNode) : PT.Expr =
    let t = toTuple node

    match t with
    | Node ("string", s, []) -> PT.Expr.EString(gid (), [ PT.StringText s ])
    | Node ("int", s, []) -> PT.Expr.EInt(gid (), int s)
    //| Node ("float", s, []) -> PT.Expr.EFloat (gid(), float s)
    //| Node ("bool", s, []) -> PT.Expr.EBool (gid(), bool s)
    | _ -> Exception.raiseInternal $"Couldn't parse expression" [ "node", node ]


//type toFunction (node: TreeSitterNode) : PT.UserFunction.T



let fns : List<BuiltInFn> = []
