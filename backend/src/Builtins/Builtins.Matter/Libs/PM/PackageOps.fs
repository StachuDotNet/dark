module Builtins.Matter.Libs.PM.PackageOps

open Prelude
open LibExecution.RuntimeTypes

module PT = LibExecution.ProgramTypes
module PT2DT = LibExecution.ProgramTypesToDarkTypes
module Builtin = LibExecution.Builtin
module PackageRefs = LibExecution.PackageRefs
module Dval = LibExecution.Dval
module VT = LibExecution.ValueType
module NR = LibExecution.RuntimeTypes.NameResolution
module BS = LibSerialization.Binary.Serialization

open Builtin.Shortcuts


let packageOpTypeName () =
  FQTypeName.fqPackage (PackageRefs.Type.LanguageTools.ProgramTypes.packageOp ())

let packageOpKT () = KTCustomType(packageOpTypeName (), [])


/// Author a BranchEvent op so what happened to a branch travels the way everything else does.
///
/// The projections are already updated by the caller's own SQL; this is not how the local store learns
/// what happened. It is how the OTHER machine learns. The fold is idempotent for these events (each sets a
/// column only when it is still NULL), so the op landing here as well changes nothing locally.
let private recordBranchEvent
  (branchId : PT.BranchId)
  (event : PT.BranchEventKind)
  : Ply<unit> =
  uply {
    let at = System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    let op = PT.PackageOp.BranchEvent(branchId, event, at)
    let! _ = LibDB.Inserts.insertAndApplyOps [ op ]
    return ()
  }


// TODO: review/reconsider the accessibility of these fns
let fns (pm : PT.PackageManager) : List<BuiltInFn> =
  [ { name = fn "pmStabilizeHashes" 0
      typeParams = []
      parameters =
        [ Param.make "ops" (TList(TCustomType(NR.ok (packageOpTypeName ()), []))) "" ]
      returnType = TList(TCustomType(NR.ok (packageOpTypeName ()), []))
      description =
        "Compute real content-addressed hashes for package ops (SCC-aware)."
      fn =
        (function
        | _, _, _, [| DList(_vt, ops) |] ->
          uply {
            let ptOps = ops |> List.choose PT2DT.PackageOp.fromDT
            let stabilized = LibDB.HashStabilization.computeRealHashes ptOps
            return
              Dval.list
                (packageOpKT ())
                (stabilized |> List.map PT2DT.PackageOp.toDT)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }


    { name = fn "pmDuplicateDeclarations" 0
      typeParams = []
      parameters =
        [ Param.make "ops" (TList(TCustomType(NR.ok (packageOpTypeName ()), []))) "" ]
      returnType = TList TString
      description =
        "The names that more than one declaration in this batch would bind, as "
        + "\"fn Owner.Module.name\" strings. Stabilizing such a batch would store one "
        + "body under the other's hash, so authoring surfaces refuse it."
      fn =
        (function
        | _, _, _, [| DList(_vt, ops) |] ->
          uply {
            let ptOps = ops |> List.choose PT2DT.PackageOp.fromDT
            return
              LibDB.OpValidation.duplicateDeclarations ptOps
              |> List.map Dval.string
              |> Dval.list KTString
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }


    { name = fn "pmUnresolvedNames" 0
      typeParams = []
      parameters =
        [ Param.make "ops" (TList(TCustomType(NR.ok (packageOpTypeName ()), []))) "" ]
      returnType = TList(TTuple(TString, TList TString, []))
      description =
        "For each op still holding unresolved name references, its content hash and those names."
      fn =
        (function
        | _, _, _, [| DList(_vt, ops) |] ->
          uply {
            // Reports; decides nothing. Whether an unresolved name should stop a commit is a decision, and
            // decisions live in Dark -- see `Cli.Commit`.
            let found =
              ops
              |> List.choose PT2DT.PackageOp.fromDT
              |> List.choose LibDB.UnresolvedCheck.inOp
              |> List.map (fun (hash, names) ->
                DTuple(
                  DString hash,
                  Dval.list KTString (names |> List.map DString),
                  []
                ))
            return Dval.list (KTTuple(VT.string, VT.list VT.string, [])) found
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }




    { name = fn "scmAddOps" 0
      typeParams = []
      parameters =
        [ Param.make
            "branchId"
            TUuid
            "the branch these ops land on; \"\" is main. Passed rather than ambient so a caller can author onto a branch it isn't sitting on -- which is what sync does"
          Param.make "ops" (TList(TCustomType(NR.ok (packageOpTypeName ()), []))) "" ]
      returnType = TypeReference.result TInt TString
      description =
        "Add package ops to <branchId> (\"\" = main), uncommitted. Returns the "
        + "number inserted; duplicates are skipped, since an op's id is its content."
      fn =
        let resultOk = Dval.resultOk KTInt KTString
        let resultError = Dval.resultError KTInt KTString
        (function
        | exeState, _, _, [| DUuid branchIdGuid; DList(_vtTODO, ops) |] ->
          uply {
            try
              let ops = ops |> List.choose PT2DT.PackageOp.fromDT

              let branchId = PT.BranchId.Id branchIdGuid

              // Branch: the edit lands on the BRANCH, stored effective=0 and tagged, never folded into
              // main. Hashes stabilize exactly as the main path does, or a merged value's
              // `package_values` (keyed by AddValue) and `locations` (keyed by SetName) disagree and the
              // value cannot be found.
              if not branchId.IsMain then
                // Refuse, rather than write: `createBranch` here used to REVIVE a merged or archived
                // branch, so a workbench still holding the id after a merge in another shell put its next
                // edit on a branch nothing would ever merge again.
                match! LibDB.Branches.isFinished branchId with
                | true ->
                  return
                    resultError (
                      Dval.string
                        $"branch {branchId} has been merged or archived; `dark switch <name>` starts a new one"
                    )
                | false ->

                  do! LibDB.Branches.registerIfNew branchId "" PT.BranchId.Main

                  let stabilized = LibDB.HashStabilization.computeRealHashes ops
                  let! n = LibDB.Branches.storeDeltaOps branchId stabilized
                  // The parent's current hash per name touched, so a later merge can tell whether the
                  // parent moved the same name.
                  let! parentId = LibDB.Branches.parentOf branchId
                  do! LibDB.Branches.recordNameBases branchId parentId stabilized
                  // Content (Add*, never SetName) folds into the shared content tables; the NAME layer is
                  // what a branch keeps to itself. Needed so an expression-valued branch value has an
                  // rt_dval to eval, and so propagation can see the branch item's dependency edges.
                  let contentOps =
                    stabilized
                    |> List.filter (fun op ->
                      match op with
                      | PT.PackageOp.AddValue _
                      | PT.PackageOp.AddFn _
                      | PT.PackageOp.AddType _ -> true
                      | _ -> false)
                  if not (List.isEmpty contentOps) then
                    do! LibDB.PackageOpPlayback.applyOps contentOps
                    let builtins : Builtins =
                      { values = exeState.values.builtIn; fns = exeState.fns.builtIn }
                    let! _ =
                      LibDB.Seed.evaluateAllValues builtins LibDB.PackageManager.rt
                    ()
                  // Move the overlay only for the branch this process is on; writing to another branch
                  // must not change what this caller resolves against. Other branches are memoized, so
                  // forget them rather than leave a stale answer.
                  if LibDB.PackageManager.currentBranchId () = branchId then
                    let! all = LibDB.Branches.loadDeltaOps branchId
                    LibDB.PackageManager.setBranchOverlay all
                  else
                    LibDB.PackageManager.forgetBranch branchId
                  return resultOk (Dval.int (bigint (int n)))

              else
                // Stabilize before inserting. Insert raw ops and their SetName targets are provisional,
                // so `WipRefresh.refresh` assigns real hashes by rewriting the ENTIRE log on every author.
                let stabilizedOps = LibDB.HashStabilization.computeRealHashes ops

                // All ops are added as WIP - use scmCommitWipOpsByIds to commit them
                let! insertedCount =
                  LibDB.Inserts.insertAndApplyOpsAsWip stabilizedOps

                // Auto-refresh existing WIP items: re-resolve names and
                // recompute SCC-aware hashes now that new items exist (still needed for the forward-ref case:
                // an earlier WIP item that references THIS newly-authored one).
                let! _refreshed = LibDB.WipRefresh.refresh pm

                // Populate `rt_dval` for any package_values rows still
                // NULL after this insert+refresh. `applyAddValue` always
                // inserts NULL and Phase-3 `evaluateAllValues` only runs
                // at startup when there are unapplied ops. Without this
                // step, a CLI-added value that references another value
                // (qualified or bare) would fail at eval with a NULL
                // rt_dval until the next cold restart.
                let! _ =
                  LibDB.Seed.evaluateAllValues
                    exeState.builtins
                    LibDB.PackageManager.rt

                return resultOk (Dval.int (bigint insertedCount))
            with ex ->
              return resultError (Dval.string ex.Message)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }


    // Which branch is THIS process on. Set by `--branch <id>` or the persistent
    // `current_branch`, both resolved in the CLI entry point before any Dark runs. Dark can't read it any
    // other way: it's process state, not a row, and `configGet "current_branch"` misses the flag form.
    { name = fn "scmCurrentBranch" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TUuid
      description = "The branch this process is on, as an id."
      fn =
        (function
        | _, _, _, [| DUnit |] ->
          uply { return DUuid (LibDB.PackageManager.currentBranchId ()).Guid }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }


    // Turn a branch NAME into the id everything below the CLI refers to, starting the branch if that
    // name has none.
    //
    // The two are separate on purpose. A name is what a person types and reads, so it is renameable and
    // reusable: archive `fix-auth`, start another, and both want the label. An id is what op tags,
    // per-name bases, relay bundles and parent links point at, so it must survive a rename and must never
    // join two unrelated branches that happened to reuse a label -- including two machines that each
    // started a `fix-auth`, which sync has to keep apart.
    //
    // One implementation, called from both languages, because a second one that resolved names even
    // slightly differently would hand the same name two ids and split a branch in half.
    { name = fn "scmResolveBranch" 0
      typeParams = []
      parameters =
        [ Param.make "name" TString "the branch name a person typed"
          Param.make
            "parentId"
            TUuid
            "the branch id to parent a NEW branch to (\"main\" at top level)" ]
      returnType = TTuple(TUuid, TBool, [])
      description =
        "Resolves a branch name to its id, creating the branch if the name has no "
        + "live one. Returns (id, wasCreated)."
      fn =
        (function
        | _, _, _, [| DString name; DUuid parentIdGuid |] ->
          uply {
            let parentId = PT.BranchId.Id parentIdGuid
            let! (id, created) = LibDB.Branches.resolveOrCreate name parentId
            return DTuple(DUuid id.Guid, DBool created, [])
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }


    // The name to SHOW for a branch id. Falls back to the id, which is all an imported branch that
    // arrived as tagged ops with no registry row of its own has to show.
    // The id a person means when they type <name> at a branch verb: the most recent branch still listed
    // under it, merged or not. Never creates -- unlike `scmResolveBranch`, this backs the paths that
    // should refuse rather than quietly start something.
    //
    // Merged branches are INCLUDED on purpose. `dark branches` lists them, so `dark diff <that name>` has
    // to find them; refusing a name you just read off the listing is the worst of both answers.
    // Whether a branch's work is already in its parent. `merge` asks before doing anything, because
    // merging an already-merged branch flips nothing and reports "Merged 0 op(s)", which reads like a
    // failure of the merge rather than an answer to a question you already had.
    // Change which branch THIS process is on, without restarting it.
    //
    // Boot (`--branch`, or `current_branch`) covers the one-shot case, but it can't be the only way in:
    // the interactive REPL is a single long-lived process, so `ops switch` there has to move the overlay
    // that name resolution and authoring actually read. Writing the config key alone would leave the
    // display saying one thing and the behaviour doing another.
    //
    //. Returns the branch it ended up on, so a caller reports what happened rather than what it
    // asked for.
    { name = fn "scmSelectBranch" 0
      typeParams = []
      parameters =
        [ Param.make "branchId" TUuid "the branch to move this process to" ]
      returnType = TUuid
      description =
        "Moves this process onto <param branchId>, loading that branch's delta ops "
        + "as the overlay used for name resolution and execution. Returns the branch "
        + "now active."
      fn =
        (function
        | _, _, _, [| DUuid branchIdGuid |] ->
          uply {
            let branchId = PT.BranchId.Id branchIdGuid
            LibDB.PackageManager.selectBranch branchId
            return DUuid branchId.Guid
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }


    // Does this op bind a name, without decoding it?
    //
    // A sync import plans conflicts by decoding every incoming op and asking which name it moved. Only
    // `SetName` and `Decision` move one; `AddFn` and friends carry content and answer nothing, and they
    // are also the big ones, so nearly all of that decoding was spent producing "no". This reads the tag
    // and returns, which measured a fifth of the pull off on its own.
    { name = fn "packageOpBindsAName" 0
      typeParams = []
      parameters = [ Param.make "blob" TBlob "a package_ops op_blob" ]
      returnType = TBool
      description =
        "Whether <param blob> is an op that binds a name (SetName or Decision), read from "
        + "its tag without decoding it. False for content ops and for anything unreadable."
      fn =
        function
        | exeState, _, _, [| DBlob blobRef |] ->
          uply {
            let! bytes = LibExecution.Blob.readBytes exeState blobRef
            return DBool(LibDB.Queries.opBindsAName bytes)
          }
        | _ -> incorrectArgs ()
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }


    // Decode one op_blob into a PackageOp, or None when this build cannot read it. Option rather
    // than raise, and deliberately with no raising variant: a synced store holds ops from other
    // builds in its own log on purpose, so every local reader meets them.
    { name = fn "packageOpFromBlobOption" 0
      typeParams = []
      parameters = [ Param.make "id" TUuid ""; Param.make "blob" TBlob "" ]
      returnType =
        TypeReference.option (TCustomType(NR.ok (packageOpTypeName ()), []))
      description =
        "Deserialize an op_blob, or None when this build cannot read it. For blobs "
        + "received from a peer, where an unreadable one must be skipped rather than "
        + "fatal."
      fn =
        function
        | exeState, _, _, [| DUuid id; DBlob blobRef |] ->
          uply {
            let! bytes = LibExecution.Blob.readBytes exeState blobRef

            let decoded =
              try
                Some(LibDB.Queries.deserializeOp id bytes)
              with _ ->
                None

            match decoded with
            | Some op ->
              return
                Dval.optionSome
                  (KTCustomType(packageOpTypeName (), []))
                  (PT2DT.PackageOp.toDT op)
            | None -> return Dval.optionNone (KTCustomType(packageOpTypeName (), []))
          }
        | _ -> incorrectArgs ()
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }


    // Bulk-import synced ops (id, op_blob-hex, origin_ts) in ONE transaction, then FOLD them
    // so they take effect. The perf path for transport: Dark's per-op insert crawls on a real
    // log, so hex-decode + bulk INSERT + fold live in F#. (Sync moves ops and they apply --
    // no approval gate; that's a later effort.) Returns count newly inserted.
    { name = fn "scmImportOps" 0
      typeParams = []
      parameters =
        [ Param.make
            "commitHash"
            TString
            "commit the arriving ops into this commit (\"\" = leave uncommitted)"
          Param.make
            "records"
            (TList(TTuple(TString, TString, [ TString ])))
            "(id, blobHex, originTs) triples" ]
      returnType = TypeReference.result TInt TString
      description =
        "Bulk-import synced ops in one transaction, then fold them in. Returns count inserted."
      fn =
        let resultOk = Dval.resultOk KTInt KTString
        let resultError = Dval.resultError KTInt KTString
        (function
        | _, _, _, [| DString commitHash; DList(_, records) |] ->
          uply {
            try
              let rows =
                records
                |> List.choose (fun d ->
                  match d with
                  | DTuple(DString id, DString hex, [ DString ts ]) ->
                    Some(id, hex, ts)
                  | _ -> None)
              let! n = LibDB.Inserts.importOpsBulk commitHash rows
              let! _ = LibDB.Seed.applyUnappliedOps () // fold the just-inserted (effective=1) ops
              return resultOk (Dval.int (bigint n))
            with ex ->
              return resultError (Dval.string ex.Message)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }


    // RELAY store: bulk-insert ops + record ownership (owner) in one transaction, NO fold
    // (a relay serves blobs, not projections). The perf path for a relay recording pushes.
    { name = fn "scmStoreOps" 0
      typeParams = []
      parameters =
        [ Param.make
            "owner"
            TString
            "the pusher's identity (\"\" = don't record ownership)"
          Param.make
            "records"
            (TList(TTuple(TString, TString, [ TString ])))
            "(id, blobHex, originTs) triples" ]
      returnType = TypeReference.result TInt TString
      description =
        "Relay store: bulk-insert ops + record ownership, no fold. Returns count stored."
      fn =
        let resultOk = Dval.resultOk KTInt KTString
        let resultError = Dval.resultError KTInt KTString
        (function
        | _, _, _, [| DString owner; DList(_, records) |] ->
          uply {
            try
              let rows =
                records
                |> List.choose (fun d ->
                  match d with
                  | DTuple(DString id, DString hex, [ DString ts ]) ->
                    Some(id, hex, ts)
                  | _ -> None)
              let! n = LibDB.Inserts.storeOpsWithOwner owner rows
              return resultOk (Dval.int (bigint n))
            with ex ->
              return resultError (Dval.string ex.Message)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }
    // One page of the sync wire format, rendered straight from the database.
    { name = fn "scmExportPageJson" 0
      typeParams = []
      parameters =
        [ Param.make "sinceSeq" TInt64 "Return ops after this rowid"
          Param.make "limit" TInt64 "How many ops at most"
          Param.make "formatVersion" TInt64 "Wire format version to declare"
          Param.make "darkBuild" TString "Build that wrote this bundle"
          Param.make "kernelHash" TString "ABI fingerprint of that build"
          Param.make "owner" TString "This instance's identity" ]
      returnType = TTuple(TString, TInt64, [])
      description =
        "One page of the sync wire format as JSON, plus the cursor to hand back. "
        + "Reads and encodes the ops without turning any of them into Dark values."
      fn =
        function
        | _,
          _,
          _,
          [| DInt64 sinceSeq
             DInt64 limit
             DInt64 formatVersion
             DString darkBuild
             DString kernelHash
             DString owner |] ->
          uply {
            let! (json, cursor) =
              LibDB.Queries.exportPageJson
                sinceSeq
                limit
                formatVersion
                darkBuild
                kernelHash
                owner

            return DTuple(DString json, DInt64 cursor, [])
          }
        | _ -> incorrectArgs ()
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }


    { name = fn "scmGetCommitNamedOps" 0
      typeParams = []
      parameters =
        [ Param.make "commitHash" TString "Commit hash"
          Param.make "limit" TInt "How many ops to return" ]
      returnType =
        TTuple(TList(TCustomType(NR.ok (packageOpTypeName ()), [])), TInt, [])
      description =
        "The ops in a commit that name or deprecate something, capped at "
        + "<param limit>, plus how many there are in total. A commit can hold "
        + "tens of thousands of ops and a caller showing a summary wants a "
        + "dozen, so the cap applies before they become Dark values."
      fn =
        function
        | _, vm, _, [| DString commitHash; DInt limit |] ->
          uply {
            let! ops = LibDB.Queries.getCommitOps (PT.Hash commitHash)
            let named =
              ops
              |> List.filter (fun op ->
                match op with
                | PT.PackageOp.SetName _
                | PT.PackageOp.Deprecate _ -> true
                | _ -> false)
            let shown =
              named
              |> List.truncate (max 0 (intToInt32 vm limit))
              |> List.map PT2DT.PackageOp.toDT
            return
              DTuple(
                Dval.list (packageOpKT ()) shown,
                Dval.int (bigint (List.length named)),
                []
              )
          }
        | _ -> incorrectArgs ()
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }
    // The REBUILD half of a draft rewrite, and the reason it is not in Dark: it re-mints every surviving
    // op's id (hashing) and re-inserts with the original stamps, then re-folds. The delete it performs
    // spares ops this build cannot decode, BY ID, which is the invariant that stops authoring eating a
    // peer's synced work. Dark decides what survives; this executes it.
    { name = fn "scmRebuildDraftKeeping" 0
      typeParams = []
      parameters =
        [ Param.make "keptIds" (TList TString) "op ids that survive the rewrite" ]
      returnType = TypeReference.result TUnit TString
      description =
        "Delete main's uncommitted ops and re-insert the ones named by <param "
        + "keptIds>, preserving their stamps, then re-fold. Ops this build cannot "
        + "decode are never deleted. Ok on success; Error with the message otherwise."
      fn =
        (function
        | _, _, _, [| DList(_, ids) |] ->
          uply {
            try
              let kept =
                ids
                |> List.choose (fun d ->
                  match d with
                  | DString s ->
                    match System.Guid.TryParse s with
                    | true, g -> Some g
                    | _ -> None
                  | _ -> None)
                |> Set.ofList

              do! LibDB.Draft.rebuild kept
              return Dval.resultOk KTUnit KTString DUnit
            with e ->
              return Dval.resultError KTUnit KTString (DString e.Message)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    // Dark edits `locations` directly on the surgical discard path, which is the one place outside the
    // fold that does. The in-memory caches key on what that table says, so whoever changes it has to say
    // so; F# does this inline (`Caching.invalidateAll`), and Dark needs the same reach.
    { name = fn "scmInvalidateCaches" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TUnit
      description =
        "Drop the in-memory package caches, after a write that changed `locations` "
        + "without going through the fold."
      fn =
        (function
        | _, _, _, [| DUnit |] ->
          uply {
            LibDB.Caching.invalidateAll ()
            return DUnit
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    // ARCHIVING a branch travels, for the same reason merging does: on the other machine the branch is
    // still sitting there looking like live work. The archive itself is Dark's -- `SCM.Branches.archive`
    // owns that column and has already written it -- so all this does is author the op that says so.
    // Idempotent on arrival (the fold sets `archived_at` only while it is NULL), which is what makes it
    // safe for the authoring machine to fold its own event too.
    //
    // Separate from the merge path rather than one builtin taking an event, because these are the only
    // two events there are, and a `BranchEventKind` crossing the boundary as data would need the DU
    // marshalled for one caller each.
    { name = fn "scmRecordBranchArchived" 0
      typeParams = []
      parameters = [ Param.make "branchId" TUuid "the branch that was archived" ]
      returnType = TUnit
      description =
        "Author the op that says this branch was archived, so other machines learn it."
      fn =
        (function
        | _, _, _, [| DUuid branchIdGuid |] ->
          uply {
            let branchId = PT.BranchId.Id branchIdGuid
            do! recordBranchEvent branchId PT.Archived
            return DUnit
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }


    // Fold the ops a merge just made effective, and evaluate any values among them.
    //
    // This is all that is left of merge in F#, and it is the part that has to be: replaying the op log
    // into main's projections, and evaluating a merged value so it has an `rt_dval` to run. Everything
    // around it -- whether a merge is allowed, which arm it takes, flipping the frontier effective,
    // marking it merged -- is decided and done in Dark (`SCM.PackageOps.mergeBranch`).
    { name = fn "scmApplyMergedOps" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TypeReference.result TUnit TString
      description =
        "Fold the newly-effective ops into main's projections and evaluate merged values."
      fn =
        (function
        | exeState, _, _, [| DUnit |] ->
          uply {
            try
              let! _ = LibDB.Seed.applyUnappliedOps ()
              let builtins : Builtins =
                { values = exeState.values.builtIn; fns = exeState.fns.builtIn }
              let! _ = LibDB.Seed.evaluateAllValues builtins LibDB.PackageManager.rt
              return Dval.resultOk KTUnit KTString DUnit
            with ex ->
              return Dval.resultError KTUnit KTString (DString ex.Message)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }


    { name = fn "scmRecordBranchMerged" 0
      typeParams = []
      parameters =
        [ Param.make "branchId" TUuid "the branch that was merged"
          Param.make "ops" (TList TUuid) "the ids of the ops the merge moved" ]
      returnType = TUnit
      description =
        "Author the op that says this branch was merged, naming what it moved, so other machines learn it."
      fn =
        (function
        | _, _, _, [| DUuid branchIdGuid; DList(_, ops) |] ->
          uply {
            let ids =
              ops
              |> List.choose (fun d ->
                match d with
                | DUuid g -> Some g
                | _ -> None)
            do! recordBranchEvent (PT.BranchId.Id branchIdGuid) (PT.Merged ids)
            return DUnit
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }


    // Store ONE op on a branch under a stamp the caller chose, effective=0 and tagged like any other
    // branch op. Serializing and hashing is the whole of what F# is here for; WHICH op, and what stamp
    // it deserves, are decided in Dark (`SCM.Branches.resolveKeepMine`).
    //
    // The stamp is a parameter rather than "now" because these ops lose or win by it. `storeDeltaOps`
    // stamps for you and is right for authoring; this is for an op whose stamp is the point.
    { name = fn "scmStoreBranchOpStamped" 0
      typeParams = []
      parameters =
        [ Param.make "branchId" TUuid "the branch the op lands on"
          Param.make
            "op"
            (TCustomType(NR.ok (packageOpTypeName ()), []))
            "the op to store"
          Param.make "stamp" TString "its origin_ts, which is what LWW compares" ]
      returnType = TypeReference.result TInt TString
      description =
        "Store one op on a branch under the given stamp. Returns the number stored."
      fn =
        let resultOk = Dval.resultOk KTInt KTString
        let resultError = Dval.resultError KTInt KTString
        (function
        | _, _, _, [| DUuid branchIdGuid; opDval; DString stamp |] ->
          uply {
            try
              match PT2DT.PackageOp.fromDT opDval with
              | None -> return resultError (Dval.string "not a package op")
              | Some op ->
                let! n =
                  LibDB.Branches.storeDeltaOpsStamped
                    (PT.BranchId.Id branchIdGuid)
                    [ (op, stamp) ]
                return resultOk (Dval.int (bigint (int n)))
            with ex ->
              return resultError (Dval.string ex.Message)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }


    // IMPORT a branch (from a portable bundle): register it, store its ops effective=0 + tag the
    // frontier (NOT folded into main), and re-derive the per-name bases against THIS instance's main
    // (recordNameBases -- the base is the destination's fork point). Cross-instance "branches follow
    // me". Returns count stored.
    { name = fn "scmImportBranchOps" 0
      typeParams = []
      parameters =
        [ Param.make "branchId" TUuid ""
          Param.make "name" TString ""
          Param.make "parent" TString ""
          Param.make
            "records"
            (TList(TTuple(TString, TString, [ TString ])))
            "(id, blobHex, originTs) triples" ]
      returnType = TypeReference.result TInt TString
      description =
        "Import a branch bundle: register + store its ops effective=0 + tag + re-base. Returns count."
      fn =
        let resultOk = Dval.resultOk KTInt KTString
        let resultError = Dval.resultError KTInt KTString
        (function
        | exeState,
          _,
          _,
          [| DUuid branchIdGuid; DString name; DString parentText; DList(_, records) |] ->
          uply {
            let branchId = PT.BranchId.Id branchIdGuid
            // The parent arrives inside a peer's bundle, so it is text this process did not write.
            // A value that is not an id means main, the same as a branch with no parent recorded;
            // raising here would fail an import over a field that is only used for the parent link.
            let parent =
              PT.BranchId.Parse parentText |> Option.defaultValue PT.BranchId.Main
            try
              // An op this build cannot decode is stored RAW and inert rather than refusing the bundle,
              // the way main sync stores such ops: present, a later build reads it. A branch three ops
              // short does resolve differently than on the sender, but that holds for main sync too and
              // was decided the other way there; refusing left the branch absent altogether.
              //
              // The record's `ts` is the op's ORIGIN stamp and must survive; re-stamping locally would make
              // this machine look like the author and resolve LWW by who imported last.
              let parsed =
                records
                |> List.choose (fun d ->
                  match d with
                  | DTuple(DString id, DString hex, [ DString ts ]) ->
                    Some(System.Guid.Parse id, System.Convert.FromHexString hex, ts)
                  | _ -> None)

              let decoded, raw =
                parsed
                |> List.map (fun (id, blob, ts) ->
                  match BS.PT.PackageOp.tryDeserialize id blob with
                  | Some op -> Choice1Of2(op, ts)
                  | None -> Choice2Of2(id, blob, ts))
                |> List.partition (fun c ->
                  match c with
                  | Choice1Of2 _ -> true
                  | Choice2Of2 _ -> false)
              let stamped =
                decoded
                |> List.choose (fun c ->
                  match c with
                  | Choice1Of2 x -> Some x
                  | Choice2Of2 _ -> None)
              let rawRecords =
                raw
                |> List.choose (fun c ->
                  match c with
                  | Choice2Of2 x -> Some x
                  | Choice1Of2 _ -> None)

              if not (List.isEmpty rawRecords) then
                System.Console.Error.WriteLine(
                  $"note: {List.length rawRecords} op(s) in this bundle were written in a format this build cannot "
                  + "read, and are stored inert. They are kept, not dropped, so a later build can apply them."
                )

              if List.length parsed <> List.length records then
                return
                  resultError (
                    Dval.string "(a record was not an (id, blobHex, originTs) triple). Nothing was imported."
                  )
              else

                do! LibDB.Branches.createBranch branchId name parent
                let ops = stamped |> List.map fst
                let! nDecoded = LibDB.Branches.storeDeltaOpsStamped branchId stamped
                let! nRaw = LibDB.Branches.storeDeltaBlobsStamped branchId rawRecords
                let n = nDecoded + nRaw
                // Re-derive bases against THIS instance's parent state (the bundle's bases don't travel).
                do! LibDB.Branches.recordNameBases branchId parent ops

                // Fold the CONTENT (Add*, never SetName) exactly as authoring onto a branch does. An
                // overlay binds names to hashes and holds no bodies, so without this the branch imports
                // "successfully" and is unusable: `branch list` and `diff` show the name, and evaluating it
                // fails with "Value couldn't be found", because the hash it resolves to was never written
                // to the content tables. Propagation needs the dependency edges for the same reason.
                let contentOps =
                  ops
                  |> List.filter (fun op ->
                    match op with
                    | PT.PackageOp.AddValue _
                    | PT.PackageOp.AddFn _
                    | PT.PackageOp.AddType _ -> true
                    | _ -> false)
                if not (List.isEmpty contentOps) then
                  do! LibDB.PackageOpPlayback.applyOps contentOps
                  let builtins : Builtins =
                    { values = exeState.values.builtIn; fns = exeState.fns.builtIn }
                  let! _ =
                    LibDB.Seed.evaluateAllValues builtins LibDB.PackageManager.rt
                  ()

                // An overlay this process is already holding predates the import, so drop it rather than
                // let a memoized read answer for the branch as it was before its ops arrived.
                if LibDB.PackageManager.currentBranchId () = branchId then
                  let! all = LibDB.Branches.loadDeltaOps branchId
                  LibDB.PackageManager.setBranchOverlay all
                else
                  LibDB.PackageManager.forgetBranch branchId
                return resultOk (Dval.int (bigint (int n)))
            with ex ->
              return resultError (Dval.string ex.Message)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated } ]


/// One constant, two languages. Dark compares against this rather than spelling main's id, for the same
/// reason F# compares against `BranchId.Main`: a spelling written twice drifts, and both times it did.
let values () : List<BuiltInValue> =
  [ { name = value "scmMainBranchId" 0
      typ = TUuid
      description =
        "Main's branch id: well-known, because main exists before anything creates it."
      deprecated = NotDeprecated
      body = DUuid PT.BranchId.Main.Guid } ]


let builtins (pm : PT.PackageManager) : Builtins =
  LibExecution.Builtin.make (values ()) (fns pm)
