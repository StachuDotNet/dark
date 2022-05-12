namespace Wasm

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Tablecloth

open Eval // todo: do we need this?

open System
open System.Reflection

#nowarn "988"

type GetGlobalObjectDelegate = delegate of string -> obj

type InvokeDelegate = delegate of m : string * [<ParamArray>] ps : obj [] -> obj

/// Responsible for interacting with the JS world
///
/// Exposes fns to be called to evaluate expressions,
/// and results back to JS
type EvalWorker =
  // This only exists to get the string "GetGlobalObject"
  // should we just remove it?
  static member GetGlobalObject(_globalObjectName : string) : unit = ()

  // todo: rename to selfDelegate
  static member selfDelegate =
    let typ =
      let sourceAssembly : Assembly = Assembly.Load "System.Private.Runtime.InteropServices.JavaScript"
      sourceAssembly.GetType "System.Runtime.InteropServices.JavaScript.Runtime"

    // I do not have any clue what this does
    let method = typ.GetMethod(nameof (EvalWorker.GetGlobalObject))
    let delegate_ = method.CreateDelegate<GetGlobalObjectDelegate>()
    let target = delegate_.Invoke("self")

    // Get a `postMessage` method from the `self` object
    let typ = target.GetType()
    let invokeMethod = typ.GetMethod("Invoke")

    System.Delegate.CreateDelegate(typeof<InvokeDelegate>, target, invokeMethod)
    :?> InvokeDelegate

  /// Call `self.postMessage` in JS land
  ///
  /// Used in order to send the results of an expression back to the JS host
  static member postMessage(message : string) : unit =
    let (_ : obj) = EvalWorker.selfDelegate.Invoke("postMessage", message)
    ()

  /// Called from js to do sanity check that wasm build is working ok
  /// TODO: delete this
  static member SanityCheck(message : string) =
    printfn "Message Received %s" message

  static member EvaluateExpression(message : string) =
    let args =
      try
        Ok(
          Json.Vanilla.deserialize<ClientInterop.performAnalysisParams>
            message)
      with
      | e ->
        let metadata = Exception.toMetadata e
        System.Console.WriteLine("Error parsing analysis in Blazor")
        System.Console.WriteLine($"called with message: {message}")
        System.Console.WriteLine($"caught exception: \"{e.Message}\" \"{metadata}\"")
        Error($"exception: {e.Message}, metdata: {metadata}")

    task {
      match args with
      | Error e -> return Error e
      | Ok args ->
        try
          let! result = Eval.performAnalysis args
          return Ok result
        with
        | e ->
          let metadata = Exception.toMetadata e
          System.Console.WriteLine("Error running analysis in Blazor")
          System.Console.WriteLine($"called with message: {message}")
          System.Console.WriteLine(
            $"caught exception: \"{e.Message}\" \"{metadata}\""
          )
          return Error($"exception: {e.Message}, metadata: {metadata}")
    }


  /// Receive request from JS host to be evaluated
  ///
  /// Once evaluated, an async call to `self.postMessage` will be made
  static member HandleEvalRequestAndPostBack(message : string) =
    printfn "Message received %s" message
    task {
      let! result = EvalWorker.EvaluateExpression message
      printfn "Got result %A" result

      let serialized = Json.Vanilla.serialize result
      printfn "Serialized %s" serialized

      EvalWorker.postMessage serialized
      printfn "Posted message back to JS"
    }
