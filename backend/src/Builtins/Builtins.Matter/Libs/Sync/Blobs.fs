/// The content-addressed blob channel: `package_blobs` (a value's large content) don't ride the op stream,
/// so after applying a peer's ops the puller fetches the blobs it lacks. Internal machinery under `Darklang.Sync.*`.
module Builtins.Matter.Libs.Sync.Blobs

open FSharp.Control.Tasks

open Prelude
open LibExecution.RuntimeTypes
open LibExecution.Builtin.Shortcuts

module Dval = LibExecution.Dval

let fns () : List<BuiltInFn> =
  [
    // ── HTTP blob channel — package_blobs (a value's large content) don't ride the op stream, so after
    //    applying a peer's ops the puller fetches the blobs it now lacks. Content-addressed = idempotent.

    // Sender: the blob MANIFEST — every content hash this instance holds, newline-joined (GET /sync/blobs).
    { name = fn "syncBlobManifest" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TString
      description =
        "The blob manifest (the GET /sync/blobs body): every content hash this instance holds, newline-joined."
      fn =
        (function
        | _, _, _, [ DUnit ] ->
          uply {
            let! hashes = LibDB.RuntimeTypes.Blob.allHashes ()
            return DString(String.concat "\n" hashes)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    // Sender: the bytes for one hash, base64 (GET /sync/blob?hash=), or empty if this instance lacks it.
    { name = fn "syncBlobBytes" 0
      typeParams = []
      parameters = [ Param.make "hash" TString "The content hash to fetch" ]
      returnType = TString
      description =
        "The bytes for one content hash, base64-encoded (the GET /sync/blob?hash= body), or empty if this instance lacks it."
      fn =
        (function
        | _, _, _, [ DString hash ] ->
          uply {
            match! LibDB.RuntimeTypes.Blob.get hash with
            | Some bytes -> return DString(System.Convert.ToBase64String bytes)
            | None -> return DString ""
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    // Receiver: of a peer's offered hashes, which this instance LACKS — exactly the blobs to fetch.
    { name = fn "syncBlobMissing" 0
      typeParams = []
      parameters =
        [ Param.make
            "hashes"
            (TList TString)
            "A peer's offered content hashes (its manifest)" ]
      returnType = TList TString
      description =
        "Of the peer's offered content hashes, which this instance lacks — a pure content-addressed set-difference (no cursor)."
      fn =
        (function
        | _, _, _, [ DList(_, hashDvals) ] ->
          uply {
            let hashes =
              hashDvals
              |> List.choose (fun d ->
                match d with
                | DString s -> Some s
                | _ -> None)
            let! missing = LibDB.RuntimeTypes.Blob.missing hashes
            return Dval.list KTString (missing |> List.map DString)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    // Receiver: store a fetched blob — base64-decode + insert under its content hash. Idempotent (dedup).
    { name = fn "syncBlobInsert" 0
      typeParams = []
      parameters =
        [ Param.make "hash" TString "The content hash"
          Param.make
            "base64Bytes"
            TString
            "The blob's bytes, base64-encoded (empty = skip)" ]
      returnType = TBool
      description =
        "Store a fetched blob: base64-decode + insert under its content hash. Idempotent. Returns true if non-empty bytes were inserted, false if the peer's body was empty."
      fn =
        (function
        | _, _, _, [ DString hash; DString b64 ] ->
          uply {
            if b64 = "" then
              return DBool false
            else
              // Total against a hostile/garbled peer body (bad base64 must not throw), and — the integrity
              // core of a content-addressed store — only store bytes that ACTUALLY hash to the claimed hash.
              // Without this a peer could serve arbitrary bytes for a legitimate hash and poison the store
              // (a value silently becomes different code) for every branch that references it.
              match
                (try
                  Some(System.Convert.FromBase64String b64)
                 with _ ->
                   None)
              with
              | None -> return DBool false
              | Some bytes ->
                if LibExecution.Blob.sha256Hex bytes = hash then
                  do! LibDB.RuntimeTypes.Blob.insert hash bytes
                  return DBool true
                else
                  return DBool false
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated } ]

let builtins () = LibExecution.Builtin.make [] (fns ())
