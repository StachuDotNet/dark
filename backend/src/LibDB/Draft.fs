/// The REBUILD half of rewriting the draft: delete main's uncommitted ops and re-insert the ones the
/// caller kept, preserving their stamps, then re-fold.
///
/// The rest of the draft lives in Dark (`SCM.Draft`), which decides WHAT survives a rewrite and does
/// the surgical path itself. This is what Dark cannot do: it re-mints every surviving op's id, which
/// is hashing, and re-inserts through the fold.
///
/// The invariant it exists to hold: `Inserts.discardWipOps` spares ops this build cannot decode, BY
/// ID. A synced store's draft holds a peer's ops on a newer format, stored and left unapplied on
/// purpose, and they are invisible to the Dark reader for exactly that reason. If the delete ever
/// stopped sparing them, authoring would silently eat a colleague's work with nothing to say so.
module LibDB.Draft

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude

open Fumble
open LibDB.Sqlite

module PT = LibExecution.ProgramTypes
module BS = LibSerialization.Binary.Serialization


/// Delete every main op and re-insert the ones that survive. The fallback when the surgical path
/// cannot identify what a dropped op wrote.
let rebuild (keptIds : Set<System.Guid>) : Task<unit> =
  task {
    let! ops = Queries.getWipOps ()
    let! preserveTs = Queries.getWipOpOriginTs ()
    let! preserveCommit = Queries.getWipOpCommits ()

    // An op survives if it was committed, or if the caller kept it.
    let surviving =
      ops
      |> List.filter (fun op ->
        let id = Inserts.computeOpHash op
        Map.containsKey id preserveCommit || Set.contains id keptIds)

    match! Inserts.discardWipOps () with
    | Error msg -> Exception.raiseInternal "draft rebuild failed" [ "msg", msg ]
    | Ok _ ->
      let! _ =
        Inserts.insertAndApplyOpsPreservingTs preserveTs preserveCommit surviving
      ()
  }
