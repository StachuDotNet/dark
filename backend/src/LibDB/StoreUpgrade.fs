/// Moving a STORE from one op-log format to the next, in place: after the flip there is no
/// `packages/` to rebuild one from. Two cases, told apart per op rather than assumed:
///
///   FORMAT-ONLY   the binary layout changed (`LibSerialization/Binary/*`). Op ids derive from the
///                 DECODED op (`Hashing.computeOpRowId`), so no identity moves: decode with the old
///                 reader, re-encode with the new writer, re-fold. This module.
///
///   IDENTITY      the hashing changed (`LibSerialization/Hashing/*`). Every id moves. Not built;
///                 the note at the bottom says what it needs.
///
/// An op whose re-derived id differs from the stored one means the hashing moved, and this refuses
/// rather than rewriting blobs under ids that would then be lies.
module LibDB.StoreUpgrade

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude

open Fumble
open LibDB.Sqlite

module PT = LibExecution.ProgramTypes
module BS = LibSerialization.Binary.Serialization
module BaseFormat = LibSerialization.Binary.BaseFormat
module Hashing = LibSerialization.Hashing.Hashing


type Report =
  {
    from : uint32
    to_ : uint32
    /// Ops decoded and written back out.
    rewritten : int
    /// Ops this build cannot read at all: a peer's newer format, stored inert. Left EXACTLY as
    /// they are. Re-encoding is impossible and dropping them would lose work a later build can
    /// still apply, which is the promise the log makes.
    unreadable : int
    /// Where the pre-migration store was copied to.
    backup : string
  }


let private storedFormat () : uint32 =
  Releases.storedFormat () |> Option.defaultValue 1u


/// The pre-migration copy, beside the store, named for the version moved TO: "the store from
/// before v2" is what someone rolling back looks for.
let backupPathFor (target : uint32) : string =
  $"{Sqlite.currentDbPath}.pre-v{target}"


/// Move this store to the format this build writes. The backup lands first, through SQLite's
/// backup API, so the rollback target exists before anything is touched; the rewrite is then one
/// transaction, since a half-converted store has no reader. Projections are re-folded, not
/// converted.
let upgrade () : Task<Result<Report, string>> =
  task {
    let from = storedFormat ()
    let target = BaseFormat.currentVersion

    if from = target then
      return Error $"this store is already format {target}; nothing to do"
    elif from > target then
      // `Releases.runPending` refuses this at open, so reaching here means someone called directly.
      return
        Error
          $"this store is format {from} and this build writes {target}. A store from a NEWER \
            format cannot be read by trying harder; upgrade the binary instead."
    else

      let backup = backupPathFor target

      match Sqlite.Backup.toFile backup with
      | Error e -> return Error $"could not back the store up to {backup}: {e}"
      | Ok() ->

        // Read the whole log first, outside the write transaction: the decode is the part that can
        // throw, and it must not do so with the rewrite half-applied.
        let! rows =
          Sql.query "SELECT id, op_blob FROM package_ops ORDER BY rowid"
          |> Sql.executeAsync (fun read -> (read.uuid "id", read.bytes "op_blob"))

        let rewrites = ResizeArray<System.Guid * byte[]>()
        let mutable unreadable = 0
        let mutable identityMoved : Option<System.Guid> = None

        for (id, blob) in rows do
          match BS.PT.PackageOp.tryDeserialize id blob with
          | None -> unreadable <- unreadable + 1
          | Some op ->
            if Hashing.computeOpRowId op <> id then
              if identityMoved = None then identityMoved <- Some id
            else
              rewrites.Add(id, BS.PT.PackageOp.serialize id op)

        match identityMoved with
        | Some id ->
          return
            Error
              $"op {id} re-derives a different id than the store filed it under, so this store's ids \
            were minted by a different hashing than this build uses. That is an identity-changing \
            migration, which re-mints the whole log; this only rewrites blobs. The store is \
            untouched and a copy is at {backup}."
        | None ->

          // One `executeTransactionSync`: connections are pooled, so a hand-written BEGIN and the
          // statements after it need not share one. The format stamp goes in the same transaction
          // as the bytes it describes.
          let statements =
            [ ("CREATE TABLE IF NOT EXISTS store_meta \
                  (key TEXT PRIMARY KEY, value TEXT NOT NULL)",
               [ [] ])
              ("INSERT OR REPLACE INTO store_meta (key, value) VALUES ('format', @v)",
               [ [ "v", Sql.string (string target) ] ]) ]
            // An empty log is a real state; skip the statement rather than run it with no rows.
            @ (if rewrites.Count = 0 then
                 []
               else
                 [ ("UPDATE package_ops SET op_blob = @blob WHERE id = @id",
                    rewrites
                    |> Seq.map (fun (id, blob) ->
                      [ "blob", Sql.bytes blob; "id", Sql.uuid id ])
                    |> List.ofSeq) ])

          let mutable failure : Option<string> = None

          try
            statements |> Sql.executeTransactionSync |> ignore<List<int>>
          with e ->
            failure <- Some e.Message

          match failure with
          | Some why ->
            return Error $"the rewrite failed and nothing was written: {why}"
          | None ->

            // Outside the transaction: re-folding runs the playback path, which opens its own connections.
            let! _ = Seed.rebuildProjections ()

            return
              Ok
                { from = from
                  to_ = target
                  rewritten = rewrites.Count
                  unreadable = unreadable
                  backup = backup }
  }


/// Put back the copy `upgrade` made on its way to <param target>. Contents, not the file, so open
/// connections see the restored data; what is already in memory is still the new store, so the
/// caller says to restart, as `LocalStore.restoreFrom` does.
let rollback (target : uint32) : Task<Result<string, string>> =
  task {
    let backup = backupPathFor target

    if not (System.IO.File.Exists backup) then
      return
        Error
          $"no pre-v{target} copy at {backup}. A rollback can only undo an upgrade this store \
            actually ran."
    else
      match Sqlite.Backup.fromFile backup with
      | Error e -> return Error $"could not restore {backup}: {e}"
      | Ok() -> return Ok backup
  }


// The identity-changing case is NOT built. It is reached by a change under `LibSerialization/
// Hashing/`, which moves every derived id at once, and the shape is settled even if the cost is
// not: (1) walk the log in order, decoding with the old reader; (2) rewrite every hash reference
// inside each op through the remap so far; (3) re-encode, re-derive the id, record old -> new;
// (4) rebuild `commits` parent first, since a commit's id depends on its parent's; (5) rewrite
// every table holding an id or hash as a foreign key: `op_owners`, `op_branches`, `sync_pushed`,
// `seed_ops`, `commits.parent`, `package_ops.commit_hash`; (6) drop projections and re-fold.
// Deterministic by construction, since ids are content hashes, so two machines re-minting the same
// log agree; test by re-minting two copies and diffing every id. A missed table in step 5 fails
// quietly, and `SCM.StoreHealth` is the check to run after.
