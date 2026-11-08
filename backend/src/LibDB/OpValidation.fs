/// Checks a batch of PackageOps must pass before anything is written.
///
/// These run against the ops themselves, and none of them write. They exist so an authoring
/// surface can refuse a batch and say why, while the paths that must converge
/// without asking anyone (sync's fold, historical replay) go on accepting
/// everything and resolving it by their own rules.
module LibDB.OpValidation

open Prelude
open LibExecution.ProgramTypes

module PT = LibExecution.ProgramTypes
open LibSerialization.Hashing


/// The hashes in <param ops> that more than one body claims, as ready-to-print
/// messages. Empty when the batch is consistent.
///
/// A hash is a content identity, so two bodies under one hash means whichever lands
/// second silently becomes the first. This is the early check, for callers that can
/// still refuse the batch before it reaches the op log. `PackageOpPlayback` enforces
/// the same invariant against already-stored content, where raising is all that's left.
let hashClashes (ops : List<PT.PackageOp>) : List<string> =
  // Fingerprint by canonical content, not by the declared hash: authoring metadata and
  // alpha-renamed binders must not make two copies of one body look like two bodies.
  let claim (op : PT.PackageOp) : Option<(string * Hash) * Hash> =
    match op with
    | PT.PackageOp.AddType t ->
      Some(("type", t.hash), Hashing.computeTypeHash Hashing.Normal t)
    | PT.PackageOp.AddFn f ->
      Some(("fn", f.hash), Hashing.computeFnHash Hashing.Normal f)
    | PT.PackageOp.AddValue v ->
      Some(("value", v.hash), Hashing.computeValueHash Hashing.Normal v)
    | PT.PackageOp.AddTrait t ->
      Some(("trait", t.hash), Hashing.computeTraitHash Hashing.Normal t)
    | PT.PackageOp.AddTraitImpl i ->
      Some(("impl", i.hash), Hashing.computeImplHash Hashing.Normal i)
    | PT.PackageOp.AddTest test ->
      Some(("test", test.hash), Hashing.computeTestHash Hashing.Normal test)
    | PT.PackageOp.SetName _
    | PT.PackageOp.Unbind _
    | PT.PackageOp.Deprecate _
    | PT.PackageOp.Undeprecate _
    | PT.PackageOp.Rename _
    | PT.PackageOp.SetReferenceNames _
    | PT.PackageOp.UpdateDoc _
    | PT.PackageOp.Decision _ -> None

  ops
  |> List.choose claim
  // An empty hash is a placeholder awaiting stabilization; it claims nothing yet.
  |> List.filter (fun ((_, Hash declared), _) -> declared <> "")
  // Prelude's List.groupBy returns a Map, so this is keyed and sorted, not raw pairs.
  |> List.groupBy fst
  |> Map.toList
  |> List.choose (fun ((kind, Hash declared), claims) ->
    let bodies = claims |> List.map snd |> List.distinct |> List.length

    if bodies > 1 then
      Some $"{kind} hash {declared} is claimed by {bodies} different bodies"
    else
      None)
