// Handles requests for evaluating expressions
namespace Wasm.Analysis

open System
open System.Threading.Tasks
open System.Reflection

open Prelude
open Tablecloth

open Microsoft.JSInterop

open System.Threading.Tasks
open Microsoft.AspNetCore.Components
open Microsoft.AspNetCore.Components.Routing
open Microsoft.Extensions.Logging
open System.Runtime.InteropServices.JavaScript

#nowarn "988"

type GetGlobalObjectDelegate = delegate of string -> obj

type InvokeDelegate = delegate of m : string * [<ParamArray>] ps : obj [] -> obj

/// Responsible for interacting with the JS world
///
/// Exposes fns to be called to evaluate expressions,
/// and results back to JS
type EvalWorker() =

  [<JSImport("globalThis.console.log")>]
  static member Log(message: string): unit = ()

  [<Inject>]
  member val jsRuntime = Unchecked.defaultof<IJSRuntime> with get, set

  static member GetGlobalObject(_globalObjectName : string) : unit = ()

  [<JSInvokable>]
  static member InitializeDarkRuntime() : unit =
    Environment.SetEnvironmentVariable("TZ", "UTC")
    EvalWorker.Log("testing")
    LibAnalysis.initSerializers ()

  // static member selfDelegate =



  // // I do not have any clue what this does
  // let method = typ.GetMethod(nameof (EvalWorker.GetGlobalObject))
  // let delegate_ = method.CreateDelegate<GetGlobalObjectDelegate>()
  // let target = delegate_.Invoke("self")

  // let typ = target.GetType()
  // let invokeMethod = typ.GetMethod("Invoke")

  // System.Delegate.CreateDelegate(typeof<InvokeDelegate>, target, invokeMethod)
  // :?> InvokeDelegate





  /// Call `self.postMessage` in JS land
  ///
  /// Used in order to send the results of an expression back to the JS host
  static member postMessage(message : string) : unit =
    let typ : System.Type =
      let sourceAssembly : Assembly =
        Assembly.Load "Microsoft.AspNetCore.Components.WebAssembly"
      sourceAssembly.GetType
        "Microsoft.AspNetCore.Components.WebAssembly.Services.DefaultWebAssemblyJSRuntime"

    //let baseType = typ.BaseType

    typ.GetMethods(BindingFlags.NonPublic)
    |> Seq.iter(fun meth -> System.Console.WriteLine($"- {meth}"))

    // "Microsoft.JSInterop.WebAssembly" / WebAssemblyJSRuntime

    // typ.GetMethods(BindingFlags.NonPublic)
    // |> Seq.iter(fun meth -> System.Console.WriteLine($"- {meth}"))


    let method = typ.GetMethod("Invoke")
    let method = method.MakeGenericMethod(typ);

    System.Console.WriteLine($"type lol {typ}")
    System.Console.WriteLine($"method lol {method}")
    let args : obj array = [| "console.log"; "{test: 'foo'}" |]
    let result = method.Invoke(method, args)
    System.Console.WriteLine($"result {result}")





    // System.Console.WriteLine($"really trying to post response: {message}")
    // let evalWorker = EvalWorker()
    // //let _ = evalWorker.jsRuntime.InvokeAsync<string>("maybeCallback", "testing");
    // //let (_ : obj) = EvalWorker.selfDelegate.Invoke("postMessage", message)

    // let allAssemblies =
    //   (Assembly.GetEntryAssembly()).GetReferencedAssemblies()
    //   |> List.ofSeq
    //   |> List.map (fun ass -> ass.FullName)
    // allAssemblies |> List.iter (fun ass -> System.Console.WriteLine(ass))



    ()

  /// Receive request from JS host to be evaluated
  ///
  /// Once evaluated, an async call to `self.postMessage` will be made
  [<Microsoft.JSInterop.JSInvokable>]
  static member OnMessage(input : string) : Task<unit> =
    // Just here to ensure type-safety (serializers require known/allowed types)
    let postResponse (response : ClientTypes.Analysis.AnalysisResult) : unit =
      EvalWorker.postMessage (Json.Vanilla.serialize (response))

    let reportException (preamble : string) (e : exn) : unit =
      let metadata = Exception.nestedMetadata e
      let errorMessage = Exception.getMessages e |> String.concat " "

      System.Console.WriteLine($"Blazor failure: {preamble}")
      System.Console.WriteLine($"called with message: {input}")
      System.Console.WriteLine(
        $"caught exception: \"{errorMessage}\" \"{metadata}\""
      )
      let message = ($"exception: {errorMessage}, metadata: {metadata}")
      postResponse (Error(message))

    try
      // parse an analysis request, in JSON, from the JS world (BlazorWorker)
      let args =
        Json.Vanilla.deserialize<ClientTypes.Analysis.PerformAnalysisParams> input
      task {
        try
          let! result = LibAnalysis.performAnalysis args
          try
            // post the result back to the JS world
            return postResponse (Ok result)
          with
          | e ->
            System.Console.WriteLine($"YOLO: {e}")
            return () //return reportException "Error returning results" e
        with
        | e -> return ()//return reportException "Error running analysis" e
      }
    with
    | e -> Task.FromResult(reportException "Error parsing analysis request" e)
