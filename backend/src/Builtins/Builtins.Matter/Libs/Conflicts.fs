/// Builtins for the sync Conflict/Resolution UX (`dark conflicts`). Conflicts are the LOCAL review log of
/// divergences; a human decision (`conflictKeep`) mints a SYNCED `Resolution` that overrides the op-fold and
/// converges on peers. Low-volume (a handful of divergences), so these return plain tuples — no native record
/// construction needed.
module Builtins.Matter.Libs.Conflicts

open FSharp.Control.Tasks

open Prelude
open LibExecution.RuntimeTypes
open LibExecution.Builtin.Shortcuts

module PT = LibExecution.ProgramTypes
module VT = LibExecution.ValueType
module Dval = LibExecution.Dval

/// The tuple shape a conflict crosses as: (id, location, itemKind, localHash, incomingHash, chosenHash,
/// resolvedBy, status).
let private conflictTupleKT =
  KTTuple(
    VT.string,
    VT.string,
    [ VT.string; VT.string; VT.string; VT.string; VT.string; VT.string ]
  )

let private toTuple (c : LibDB.Conflicts.Conflict) : Dval =
  DTuple(
    DString c.id,
    DString c.location,
    [ DString c.itemKind
      DString c.localHash
      DString c.incomingHash
      DString c.chosenHash
      DString c.resolvedBy
      DString c.status ]
  )

let fns () : List<BuiltInFn> =
  [ { name = fn "conflictsList" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType =
        TList(
          TTuple(
            TString,
            TString,
            [ TString; TString; TString; TString; TString; TString ]
          )
        )
      description =
        "This instance's recorded sync conflicts as (id, location, itemKind, localHash, incomingHash, chosenHash, resolvedBy, status). Local-only review log."
      fn =
        (function
        | _, _, _, [ DUnit ] ->
          uply {
            let! conflicts = LibDB.Conflicts.list ()
            return conflicts |> List.map toTuple |> Dval.list conflictTupleKT
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "conflictAcknowledge" 0
      typeParams = []
      parameters =
        [ Param.make
            "location"
            TString
            "the conflicted location (owner.modules.name)" ]
      returnType = TBool
      description =
        "Acknowledge the auto-resolved conflict at <param location> — the auto (last-writer-wins) choice stands. Returns whether one was found."
      fn =
        (function
        | _, _, _, [ DString location ] ->
          uply {
            let! conflicts = LibDB.Conflicts.list ()

            match
              conflicts
              |> List.tryFind (fun c ->
                c.location = location && c.status = "auto-resolved")
            with
            | Some c ->
              do! LibDB.Conflicts.acknowledge c.id
              return DBool true
            | None -> return DBool false
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "conflictKeep" 0
      typeParams = []
      parameters =
        [ Param.make
            "location"
            TString
            "the conflicted location (owner.modules.name)"
          Param.make
            "chosenHash"
            TString
            "the candidate content hash to keep (local or incoming)" ]
      returnType = TBool
      description =
        "Override the auto-resolution at <param location>: bind it to <param chosenHash> by minting a SYNCED Resolution (applied locally now; converges on peers). Marks the conflict overridden. Returns whether one was found."
      fn =
        (function
        | _, _, _, [ DString location; DString chosenHash ] ->
          uply {
            let! conflicts = LibDB.Conflicts.list ()

            match
              conflicts
              |> List.tryFind (fun c ->
                c.location = location && c.status = "auto-resolved")
            with
            // A resolution must bind one of the TWO conflicting candidates; refuse an arbitrary hash so a
            // caller can't mint a synced Resolution to content that was never in contention at this location.
            | Some c when chosenHash = c.localHash || chosenHash = c.incomingHash ->
              let loc = LibDB.Conflicts.parseLocation location
              let itemKind = PT.ItemKind.fromString c.itemKind
              let chosenRef =
                PT.Reference.fromHashAndKind (PT.Hash chosenHash, itemKind)
              // The resolution's `at` competes in the same string-LWW as op origin_ts, so it MUST be minted
              // by the one canonical stamp source (lock-guarded, monotonic, InvariantCulture) — not a raw
              // UtcNow, which can tie/regress and formats locale-dependently.
              let at = LibDB.Inserts.nextOriginTs ()
              let r = LibDB.Resolutions.mk loc chosenRef "human" c.branchId at
              do! LibDB.Resolutions.recordAndApply r
              do! LibDB.Conflicts.markOverridden c.id
              return DBool true
            | Some _
            | None -> return DBool false
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated } ]

let builtins () = LibExecution.Builtin.make [] (fns ())
