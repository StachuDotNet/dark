namespace Elmish

module internal Log =
  let onError (text : string, ex : exn) =
    System.Console.Error.WriteLine("{0}: {1}", text, ex)

  let toConsole (text : string, o : #obj) = printfn "%s: %A" text o
