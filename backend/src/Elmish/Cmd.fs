namespace Elmish

open System

/// Dispatch - feed new message into the processing loop
type Dispatch<'msg> = 'msg -> unit

/// Subscription - return immediately, but may schedule dispatch of a message at any time
type Sub<'msg> = Dispatch<'msg> -> unit

/// Cmd - container for subscriptions that may produce messages
type Cmd<'msg> = Sub<'msg> list

/// Cmd module for creating and manipulating commands
[<RequireQualifiedAccess>]
module Cmd =
  /// Execute the commands using the supplied dispatcher
  let internal exec (dispatch : Dispatch<'msg>) (cmd : Cmd<'msg>) =
    cmd |> List.iter (fun sub -> sub dispatch)

  /// None - no commands, also known as `[]`
  let none : Cmd<'msg> = []

  /// When emitting the message, map to another type
  let map (f : 'a -> 'msg) (cmd : Cmd<'a>) : Cmd<'msg> =
    cmd |> List.map (fun g -> (fun dispatch -> f >> dispatch) >> g)

  /// Aggregate multiple commands
  let batch (cmds : #seq<Cmd<'msg>>) : Cmd<'msg> = cmds |> List.concat

  /// Command to call the subscriber
  let ofSub (sub : Sub<'msg>) : Cmd<'msg> = [ sub ]

  module OfFunc =
    /// Command to evaluate a simple function and map the result
    /// into success or error (of exception)
    let either
      (task : 'a -> _)
      (arg : 'a)
      (ofSuccess : _ -> 'msg)
      (ofError : _ -> 'msg)
      : Cmd<'msg> =
      let bind dispatch =
        try
          task arg |> (ofSuccess >> dispatch)
        with x ->
          x |> (ofError >> dispatch)

      [ bind ]

    /// Command to evaluate a simple function and map the success to a message
    /// discarding any possible error
    let perform (task : 'a -> _) (arg : 'a) (ofSuccess : _ -> 'msg) : Cmd<'msg> =
      let bind dispatch =
        try
          task arg |> (ofSuccess >> dispatch)
        with x ->
          ()

      [ bind ]

    /// Command to evaluate a simple function and map the error (in case of exception)
    let attempt (task : 'a -> unit) (arg : 'a) (ofError : _ -> 'msg) : Cmd<'msg> =
      let bind dispatch =
        try
          task arg
        with x ->
          x |> (ofError >> dispatch)

      [ bind ]

    /// Command to issue a specific message
    let result (msg : 'msg) : Cmd<'msg> = [ fun dispatch -> dispatch msg ]

  module OfAsync =
    /// Command that will evaluate an async block and map the result
    /// into success or error (of exception)
    let either
      (task : 'a -> Async<_>)
      (arg : 'a)
      (ofSuccess : _ -> 'msg)
      (ofError : _ -> 'msg)
      : Cmd<'msg> =
      let bind dispatch =
        async {
          let! r = task arg |> Async.Catch

          dispatch (
            match r with
            | Choice1Of2 x -> ofSuccess x
            | Choice2Of2 x -> ofError x
          )
        }

      [ bind >> Async.StartImmediate ]

    /// Command that will evaluate an async block and map the success
    let perform (task : 'a -> Async<_>) (arg : 'a) (ofSuccess : _ -> 'msg) =
      let bind dispatch =
        async {
          let! r = task arg |> Async.Catch

          match r with
          | Choice1Of2 x -> dispatch (ofSuccess x)
          | _ -> ()
        }

      [ bind >> Async.StartImmediate ]

    /// Command that will evaluate an async block and map the error (of exception)
    let attempt (task : 'a -> Async<_>) (arg : 'a) (ofError : _ -> 'msg) =
      let bind dispatch =
        async {
          let! r = task arg |> Async.Catch

          match r with
          | Choice2Of2 x -> dispatch (ofError x)
          | _ -> ()
        }

      [ bind >> Async.StartImmediate ]

    /// Command that will evaluate an async block to the message
    let result (task : Async<'msg>) =
      let bind dispatch =
        async {
          let! r = task |> Async.Catch

          match r with
          | Choice1Of2 x -> dispatch x
          | _ -> ()
        }

      [ bind >> Async.StartImmediate ]

  open System.Threading.Tasks

  module OfTask =
    /// Command to call a task and map the results
    let inline either
      (task : 'a -> Task<_>)
      (arg : 'a)
      (ofSuccess : _ -> 'msg)
      (ofError : _ -> 'msg)
      : Cmd<'msg> =
      OfAsync.either (task >> Async.AwaitTask) arg ofSuccess ofError

    /// Command to call a task and map the success
    let inline perform
      (task : 'a -> Task<_>)
      (arg : 'a)
      (ofSuccess : _ -> 'msg)
      : Cmd<'msg> =
      OfAsync.perform (task >> Async.AwaitTask) arg ofSuccess

    /// Command to call a task and map the error
    let inline attempt
      (task : 'a -> Task<_>)
      (arg : 'a)
      (ofError : _ -> 'msg)
      : Cmd<'msg> =
      OfAsync.attempt (task >> Async.AwaitTask) arg ofError

    /// Command and map the task success
    let inline result (task : Task<'msg>) : Cmd<'msg> =
      OfAsync.result (task |> Async.AwaitTask)

  // Synonymous with `OfFunc.result`, may be removed in the future
  let inline ofMsg (msg : 'msg) : Cmd<'msg> = OfFunc.result msg
