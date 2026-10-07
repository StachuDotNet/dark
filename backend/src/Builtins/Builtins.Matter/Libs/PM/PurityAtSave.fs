/// Decide, when a batch is saved, whether each of its fns reaches anything that takes an effect
/// ordinal, and write the answer onto the fn so it is hashed with it.
///
/// Runs after `TraitCalls` has pinned the trait calls, so a resolved operator is an ordinary call
/// to the implementation it names, and before hashing. A fn stored with a verdict already is
/// taken at its word, so a save reads one level of callees rather than its whole closure.
module Builtins.Matter.Libs.PM.PurityAtSave

open Prelude

module PT = LibExecution.ProgramTypes
module RT = LibExecution.RuntimeTypes
module Calls = LibExecution.CallGraph


/// The rule a spread chunk refuses by, on the `conc-mechanism` branch (`Interpreter.spreadRefuses`):
/// a call that is logged, or one of the three awaits. Repeated here only because that branch has
/// not merged; with it, this is that function.
let private refuses (fn : RT.BuiltInFn) : bool =
  let orchestrates =
    List.exists (fun (p : RT.BuiltInParam) -> p.typ.isFn ()) fn.parameters
  let awaits = Set.contains fn.name.name (set [ "execAwait"; "execAwaitWithin"; "execSelect" ])
  not (Set.isEmpty fn.callEffects)
  || (fn.previewable = RT.Impure && not orchestrates)
  || awaits


type private Verdict =
  | Pure
  | Impure
  | Unknown

let private worse (a : Verdict) (b : Verdict) : Verdict =
  match a, b with
  | Impure, _
  | _, Impure -> Impure
  | Unknown, _
  | _, Unknown -> Unknown
  | Pure, Pure -> Pure


/// What each fn in <param ops> reaches, judged against <param builtins>. Package values are not
/// followed: none is evaluated yet on a reload, so a fn reading one is `None`.
/// Measurement only: how many fns each judgement visited, which is what the runtime's
/// `PackagePermissions.purity` would load for the same root without a stored verdict.
let closureSizes = System.Collections.Generic.List<int>()

let decideWith
  (alsoRefused : Set<string>)
  (builtins : System.Collections.Generic.Dictionary<RT.FQFnName.Builtin, RT.BuiltInFn>)
  (pm : PT.PackageManager)
  (ops : List<PT.PackageOp>)
  : Ply<List<PT.PackageOp>> =
  uply {
    let batch =
      ops
      |> List.choose (function
        | PT.PackageOp.AddFn fn -> Some(fn.hash, fn)
        | _ -> None)
      |> Map.ofList

    /// Every fn the batch reaches, with its body's calls and, for a stored fn, the verdict it was
    /// saved with. A stored verdict stands for everything beneath it, so the walk stops there.
    let members = System.Collections.Generic.Dictionary<PT.Hash, Option<Calls.Analysis * Option<Verdict>>>()
    let rec load (h : PT.Hash) : Ply<unit> =
      uply {
        if not (members.ContainsKey h) then
          let! found =
            match Map.tryFind h batch with
            | Some fn -> Ply(Some(fn, true))
            | None ->
              uply {
                let! fn = pm.getFn h
                return fn |> Option.map (fun fn -> (fn, false))
              }
          match found with
          | None -> members[h] <- None
          | Some(fn, inBatch) ->
            let calls = Calls.analyzeFn Set.empty fn
            let stored =
              match fn.purity with
              | Some PT.Purity.Pure when not inBatch -> Some Pure
              | Some PT.Purity.Impure when not inBatch -> Some Impure
              | _ -> None
            members[h] <- Some(calls, stored)
            if Option.isNone stored then
              for callee in calls.names do
                match callee with
                | PT.FQFnName.Package dependency -> do! load dependency
                | _ -> ()
      }
    for KeyValue(h, _) in batch do
      do! load h

    let defers (h : PT.Hash) =
      match members.TryGetValue h with
      | true, Some(calls, _) -> calls.defersToTypeParam
      | _ -> false

    /// `CallGraph.Requirements.forFunctionWith true`, stopping at a stored verdict: the root's
    /// own callbacks and type-param deferrals are its caller's to supply, a dependency entered
    /// with nothing recorded for the deferral it makes is not.
    let judge (root : PT.Hash) : Verdict =
      let visited = System.Collections.Generic.HashSet<PT.Hash>()
      let mutable answer = Pure
      let rec visit (h : PT.Hash) =
        if answer <> Impure && visited.Add h then
          match members.TryGetValue h with
          | true, Some(_, Some stored) -> answer <- worse answer stored
          | true, Some(calls, None) ->
            if not calls.complete then answer <- worse answer Unknown
            for called in calls.names do
              match called with
              | PT.FQFnName.Builtin b ->
                match builtins.TryGetValue({ name = b.name; version = b.version }) with
                | true, fn ->
                  if refuses fn || Set.contains fn.name.name alsoRefused then
                    answer <- Impure
                | _ -> answer <- worse answer Unknown
              | PT.FQFnName.Package p ->
                if Set.contains p calls.calledWithoutBounds && defers p then
                  answer <- worse answer Unknown
                visit p
              | PT.FQFnName.TraitMethod _ -> answer <- worse answer Unknown
          | _ -> answer <- worse answer Unknown
      visit root
      closureSizes.Add visited.Count
      answer

    return
      ops
      |> List.map (fun op ->
        match op with
        | PT.PackageOp.AddFn fn ->
          let purity =
            match judge fn.hash with
            | Pure -> Some PT.Purity.Pure
            | Impure -> Some PT.Purity.Impure
            | Unknown -> None
          PT.PackageOp.AddFn { fn with purity = purity }
        | _ -> op)
  }


let decide builtins pm ops = decideWith Set.empty builtins pm ops
