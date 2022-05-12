[<EntryPoint>]
let main argv =
  printfn "WASM-compiled Dark backend running...FOOBAR"

#if DEBUG
  printfn "in Debug mode"
#else
  printfn "in Release mode"
#endif

  0
