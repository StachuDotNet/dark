/// Raw SQLite access from Darklang — the `Stdlib.Sqlite` floor primitive that lets sync/SCM policy live in
/// Dark over an ordinary database, plus the native op-log read/append + blob-channel builtins sync rides on.
///
/// Surface: `sqliteExec` (DDL/DML → rows affected) and `sqliteQuery` (SELECT → each row as a typed
/// `Dict<Value>`), each taking a `params` list bound to @p0..@pN (injection-safe; [] for none). The `.dark`
/// `Stdlib.Sqlite.exec/execP/query/queryP` wrappers layer the with/without-params convenience on top, so
/// there's one builtin per operation rather than four.
///
/// The `sqlite*` builtins open a caller-supplied file path, so they declare `Needs.fileReadWrite` (the CLI host
/// grants it; untrusted `dark run` under a narrowed grant is denied). They still don't scope to a SPECIFIC
/// path in-body — that (a `sqlite:open:<glob>` cap) is a follow-up. Minimal extras also deferred: an opaque
/// connection-registry `Db` handle, a typed `Sqlite.Value` enum, and `transact`.
/// CLEANUP(sqlite-scope): add the in-body path/glob capability check.
module Builtins.Matter.Libs.Sqlite

open FSharp.Control.Tasks

open Prelude
open LibExecution.RuntimeTypes
open LibExecution.Builtin.Shortcuts

open Microsoft.Data.Sqlite

module Dval = LibExecution.Dval
module PackageRefs = LibExecution.PackageRefs
module NR = LibExecution.RuntimeTypes.NameResolution
module Blob = LibExecution.Blob

let private connStr (path : string) : string = $"Data Source={path}"

/// Marshal a SQLite cell (the ADO reader's `obj`) into a typed `Stdlib.Sqlite.Value` DEnum, so
/// ints/reals/blobs keep their types across the round-trip (esp. Bytes for BLOB columns). Mirrors the
/// `Int.fs` ParseError.toDT pattern: build the FQ type name from PackageRefs, then a `DEnum`.
module Value =
  let typeName = FQTypeName.fqPackage (PackageRefs.Type.Stdlib.sqliteValue ())
  let knownType : KnownType = KTCustomType(typeName, [])
  let typeRef : TypeReference = TCustomType(NR.ok typeName, [])

  let toDT (cell : obj) : Dval =
    let (caseName, fields) =
      match cell with
      | null -> "Null", []
      | :? System.DBNull -> "Null", [] // ADO returns DBNull.Value (not null) for a SQL NULL
      | :? int64 as i -> "Int", [ DInt64 i ]
      | :? double as f -> "Real", [ DFloat f ]
      | :? string as s -> "Text", [ DString s ]
      | :? (byte[]) as b -> "Bytes", [ Blob.newEphemeral b ]
      | other -> "Text", [ DString(string other) ]
    DEnum(typeName, typeName, [], caseName, fields)

/// Bind a positional param list to @p0..@pN — parameterized statements, so values can't be SQL-injected.
let private bindParams (cmd : SqliteCommand) (parameters : List<string>) : unit =
  parameters
  |> List.iteri (fun i p ->
    cmd.Parameters.AddWithValue($"@p{i}", box p) |> ignore<SqliteParameter>)

let private paramStrings (dvals : List<Dval>) : List<string> =
  dvals
  |> List.map (fun d ->
    match d with
    | DString s -> s
    | other -> string other)

// exec returns a `Result<Int, String>` (Ok rows-affected / Error message) so a bad path, a locked db, a
// mid-copy failure, etc. surface as a value Dark can handle — never an uncaught throw. This is what lets
// `Sync.pull`/the daemon skip a bad-or-offline peer gracefully.
let private execImpl
  (path : string)
  (sql : string)
  (parameters : List<string>)
  : Ply<Dval> =
  uply {
    try
      use conn = new SqliteConnection(connStr path)
      do! conn.OpenAsync()
      use cmd = conn.CreateCommand()
      cmd.CommandText <- sql
      bindParams cmd parameters
      let! affected = cmd.ExecuteNonQueryAsync()
      return Dval.resultOk KTInt KTString (Dval.int (bigint affected))
    with e ->
      return Dval.resultError KTInt KTString (DString e.Message)
  }

// query returns a `Result<List<Dict<Value>>, String>` (Ok rows / Error message) to match exec — a bad path
// or malformed SQL surfaces as a value, never an uncaught throw. Lets a caller read a peer's Release / op
// count (or its own config) without a throw taking down the caller.
let private queryImpl
  (path : string)
  (sql : string)
  (parameters : List<string>)
  : Ply<Dval> =
  let rowsKT = KTList(ValueType.Known(KTDict(ValueType.Known Value.knownType)))

  uply {
    try
      use conn = new SqliteConnection(connStr path)
      do! conn.OpenAsync()
      use cmd = conn.CreateCommand()
      cmd.CommandText <- sql
      bindParams cmd parameters
      let! readerObj = cmd.ExecuteReaderAsync()
      use reader = readerObj
      let rows = System.Collections.Generic.List<Dval>()

      let rec loop () : Ply<unit> =
        uply {
          let! hasRow = reader.ReadAsync()
          if hasRow then
            let cells =
              [ for i in 0 .. reader.FieldCount - 1 ->
                  (reader.GetName i, Value.toDT (reader.GetValue i)) ]
            rows.Add(Dval.dict Value.knownType cells)
            return! loop ()
        }

      do! loop ()
      let listDval =
        Dval.list (KTDict(ValueType.Known Value.knownType)) (List.ofSeq rows)
      return Dval.resultOk rowsKT KTString listDval
    with e ->
      return Dval.resultError rowsKT KTString (DString e.Message)
  }

module EventLogRefs = LibExecution.PackageRefs.Type.Stdlib.EventLog
let private eventLogEventType () = FQTypeName.fqPackage (EventLogRefs.event ())
let private eventLogCommitType () = FQTypeName.fqPackage (EventLogRefs.commit ())
let private eventLogCursorType () = FQTypeName.fqPackage (EventLogRefs.cursor ())

/// Build the `Stdlib.EventLog.Event` record for one op row — natively, so a 1000-op batch never pays the
/// per-row Dark interpreter cost the native read exists to avoid.
let private eventRecord
  ((id, op, br, ch, ts) : string * string * string * string * string)
  : Dval =
  let t = eventLogEventType ()
  DRecord(
    t,
    t,
    [],
    Map
      [ "id", DString id
        "op", DString op
        "branchId", DString br
        "commitHash", DString ch
        "originTs", DString ts ]
  )

let private commitRecord
  ((hash, msg, br, acct, at) : string * string * string * string * string)
  : Dval =
  let t = eventLogCommitType ()
  DRecord(
    t,
    t,
    [],
    Map
      [ "hash", DString hash
        "message", DString msg
        "branchId", DString br
        "accountId", DString acct
        "createdAt", DString at ]
  )

let private cursorValue (n : int64) : Dval =
  let t = eventLogCursorType ()
  DEnum(t, t, [], "Cursor", [ DInt64 n ])

let private branchOpEventType () =
  FQTypeName.fqPackage (EventLogRefs.branchOpEvent ())

/// Build the `Stdlib.EventLog.BranchOpEvent` record for one branch_ops row — natively (no per-row Dark cost).
let private branchOpEventRecord
  ((id, op, originTs) : string * string * string)
  : Dval =
  let t = branchOpEventType ()
  DRecord(
    t,
    t,
    [],
    Map [ "id", DString id; "op", DString op; "originTs", DString originTs ]
  )

let private resolutionEventType () =
  FQTypeName.fqPackage (EventLogRefs.resolutionEvent ())

/// Build the `Stdlib.EventLog.ResolutionEvent` record for one resolutions row.
let private resolutionEventRecord
  ((id, branchId, location, itemKind, chosenHash, resolvedBy, at) :
    string * string * string * string * string * string * string)
  : Dval =
  let t = resolutionEventType ()

  DRecord(
    t,
    t,
    [],
    Map
      [ "id", DString id
        "branchId", DString branchId
        "location", DString location
        "itemKind", DString itemKind
        "chosenHash", DString chosenHash
        "resolvedBy", DString resolvedBy
        "at", DString at ]
  )

/// Read a string field out of an EventLog Event/Commit record (built by the peer + parsed from JSON) —
/// natively, so appending a 1000-op batch never pays the per-row Dark interpreter cost.
let private recField (name : string) (fields : Map<string, Dval>) : string =
  match Map.tryFind name fields with
  | Some(DString s) -> s
  | _ ->
    Exception.raiseInternal
      "eventLog record missing expected string field"
      [ "field", name ]

/// A peer sent a `kind` row (op / commit / resolution) we can't parse. Peers are fully trusted for now, so
/// the append skips it rather than crash the pull — but that must be VISIBLE, not silent: a skipped row is
/// dropped while the pull cursor still advances past it, so on divergence the operator needs to see it.
/// (The proper hardening — quarantine + retry per-op instead of skip — is tracked in CLEANUP(sync-security).)
let private warnSkippedSyncRow (kind : string) : unit =
  System.Console.Error.WriteLine(
    $"sync: skipped an unparseable {kind} from a peer (dropped, not applied). This can diverge the store; "
    + "re-pull once the peer is fixed."
  )

let fns () : List<BuiltInFn> =
  // Two raw-SQLite primitives — exec (DDL/DML) and query (SELECT) — each binding @p0..@pN params
  // (injection-safe; pass [] for none). The `.dark` Stdlib.Sqlite.exec/execP/query/queryP wrappers layer the
  // with-params / without-params convenience on top, so there's one builtin per operation rather than four.
  [ { name = fn "sqliteExec" 0
      typeParams = []
      parameters =
        [ Param.make "path" TString "the SQLite file to open"
          Param.make "sql" TString "a statement to run (CREATE/INSERT/UPDATE/DELETE…)"
          Param.make
            "params"
            (TList TString)
            "values bound to @p0..@pN placeholders, in order (injection-safe); [] for none" ]
      returnType = TypeReference.result TInt TString
      description =
        "Opens the SQLite file at <param path> and runs <param sql>, binding <param params> to @p0..@pN placeholders (injection-safe; pass [] for none). Ok = rows affected; Error = the SQLite message. Never throws."
      fn =
        (function
        | _, _, _, [ DString path; DString sql; DList(_, ps) ] ->
          execImpl path sql (paramStrings ps)
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.Needs.fileReadWrite
      deprecated = NotDeprecated }

    { name = fn "sqliteQuery" 0
      typeParams = []
      parameters =
        [ Param.make "path" TString "the SQLite file to open"
          Param.make "sql" TString "a SELECT to run"
          Param.make
            "params"
            (TList TString)
            "values bound to @p0..@pN placeholders, in order (injection-safe); [] for none" ]
      returnType = TypeReference.result (TList(TDict Value.typeRef)) TString
      description =
        "Opens the SQLite file at <param path> and runs the SELECT <param sql>, binding <param params> to @p0..@pN placeholders (injection-safe; pass [] for none). Ok = each row as a dict of column-name to its typed value; Error = the SQLite message. Never throws."
      fn =
        (function
        | _, _, _, [ DString path; DString sql; DList(_, ps) ] ->
          queryImpl path sql (paramStrings ps)
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.Needs.fileReadWrite
      deprecated = NotDeprecated }

    // This instance's OWN package store path (data.db). The op-log builtins write ops here; the sync config
    // tables (sync_peers/sync_cursors) live here too — the daemon/CLI don't have to know the path.
    { name = fn "localDbPath" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TString
      description = "The file path of this instance's own package store (data.db)."
      fn =
        (function
        | _, _, _, [ DUnit ] -> uply { return DString LibConfig.Config.dbPath }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    // The Release (store format/version coordinate: language + op-format + schema + hashing) this Dark
    // binary speaks. Compared against the store's stamped Release for the upgrade/`dark version` surface.
    { name = fn "currentRelease" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TInt
      description = "The Release (store format/version) this Dark binary speaks."
      fn =
        (function
        | _, _, _, [ DUnit ] ->
          uply { return Dval.int (bigint LibDB.Releases.currentRelease) }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    // ── native op-log read/append + blob-channel builtins (the ops-queue seam sync rides on) ──
    // CLEANUP(sync-builtins): these operate on the FIXED local store (not an arbitrary path) and still declare
    // noCaps, so untrusted `dark run` can read/append the op log. Lower-risk than the arbitrary-path sqlite* above
    // (can't touch other files), but when these are reorganized around a first-class ops-queue they should carry
    // a real store capability too.
    //
    // The TYPED, stream-shaped read of the package op log: Event/Commit records + the resume Cursor, all built
    // NATIVELY so a 1000-op batch never pays the per-row Dark interpreter cost. `EventLog.readSince` wraps this;
    // the events Stream is drained (native loop) for the wire, and is filterable for branch-scoped reads.
    { name = fn "packageOpsReadNative" 0
      typeParams = [ "c"; "e"; "cur" ]
      parameters =
        [ Param.make
            "cursor"
            TInt64
            "read events after this cursor (0 = from the start)"
          Param.make "limit" TInt64 "at most this many events — one bounded batch" ]
      returnType =
        TTuple(TList(TVariable "c"), TStream(TVariable "e"), [ TVariable "cur" ])
      description =
        "This instance's committed events after <param cursor> (at most <param limit>) as a Stream of Event records, the Commit records they reference, and the resume Cursor. Built natively — the fast typed read half of the op-log seam."
      fn =
        (function
        | _, _, _, [ DInt64 cursor; DInt64 limit ] ->
          uply {
            let! (commits, events, newCursor) = LibDB.Seed.eventsSince cursor limit

            let commitsList =
              List.map commitRecord commits
              |> Dval.list (KTCustomType(eventLogCommitType (), []))

            let remaining = ref (List.map eventRecord events)

            let nextFn () : Ply<Option<Dval>> =
              uply {
                match remaining.Value with
                | head :: tail ->
                  remaining.Value <- tail
                  return Some head
                | [] -> return None
              }

            let eventStream =
              LibExecution.Stream.newFromIO
                (ValueType.Known(KTCustomType(eventLogEventType (), [])))
                nextFn
                None

            return DTuple(commitsList, eventStream, [ cursorValue newCursor ])
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    // The TYPED append: Commit + Event records received from a peer (parsed from JSON natively) folded into the
    // package op log. Field extraction is native — a 1000-op pull never pays the per-row Dark cost. Returns the
    // count newly applied (idempotent). `EventLog.append` wraps this.
    { name = fn "packageOpsAppendNative" 0
      typeParams = [ "c"; "e" ]
      parameters =
        [ Param.make
            "commits"
            (TList(TVariable "c"))
            "Commit records the events reference"
          Param.make
            "events"
            (TList(TVariable "e"))
            "Event records received from a peer" ]
      returnType = TInt
      description =
        "Append received Commit + Event records to the op log (reconciling origin_ts to the MIN stamp for LWW convergence) + fold. Returns the count of ops newly applied. Idempotent. Extracts fields natively."
      fn =
        (function
        | _, _, _, [ DList(_, commits); DList(_, events) ] ->
          uply {
            // Receive must be TOTAL against a malformed/hostile peer: skip any event/commit that won't parse
            // (bad guid/hex), and catch a batch-level DB error (e.g. an FK to a row not in this batch) so the
            // pull loop + background daemon survive instead of crashing. CLEANUP(sync-security): peers are
            // FULLY TRUSTED for now — a future hardening should verify op ids against content + reject bad ops
            // per-op (not skip the whole batch) + not trust the peer's account_id/commit_hash.
            let parsedCommits =
              commits
              |> List.choose (fun c ->
                match c with
                | DRecord(_, _, _, f) ->
                  try
                    Some(
                      recField "hash" f,
                      recField "message" f,
                      System.Guid.Parse(recField "branchId" f),
                      System.Guid.Parse(recField "accountId" f),
                      recField "createdAt" f
                    )
                  with _ ->
                    warnSkippedSyncRow "commit"
                    None
                | _ ->
                  warnSkippedSyncRow "commit"
                  None)

            let parsedEvents =
              events
              |> List.choose (fun ev ->
                match ev with
                | DRecord(_, _, _, f) ->
                  try
                    Some(
                      System.Guid.Parse(recField "id" f),
                      System.Convert.FromHexString(recField "op" f),
                      System.Guid.Parse(recField "branchId" f),
                      recField "commitHash" f,
                      recField "originTs" f
                    )
                  with _ ->
                    warnSkippedSyncRow "package op"
                    None
                | _ ->
                  warnSkippedSyncRow "package op"
                  None)

            try
              let! applied = LibDB.Seed.receiveOps parsedCommits parsedEvents
              return Dval.int (bigint applied)
            with _ ->
              // -1 = a DB error applying the batch (distinct from 0 = idempotent/nothing new). The puller
              // must NOT advance the cursor on this, or the ops are silently skipped (divergence). Any
              // individually unparseable ops were skipped above and surfaced via warnSkippedSyncRow — they are
              // dropped (not applied) while the cursor still advances, so they're logged, not silently lost.
              return Dval.int (bigint -1L)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    // The branch-structure log (CreateBranch/CreateCommit/Rebase/Merge/Archive), read as a Stream of
    // BranchOpEvent records + the resume Cursor. Branch ops are self-contained (no side commits). Peers apply
    // these to LEARN branches — the structure that `packageOps` events reference by branch_id.
    { name = fn "branchOpsReadNative" 0
      typeParams = [ "e"; "cur" ]
      parameters =
        [ Param.make
            "cursor"
            TInt64
            "read branch ops after this cursor (0 = from the start)"
          Param.make "limit" TInt64 "at most this many — one bounded batch" ]
      returnType = TTuple(TStream(TVariable "e"), TVariable "cur", [])
      description =
        "This instance's branch ops after <param cursor> as a Stream of BranchOpEvent records + the resume Cursor. Built natively."
      fn =
        (function
        | _, _, _, [ DInt64 cursor; DInt64 limit ] ->
          uply {
            let! (events, newCursor) = LibDB.Seed.branchOpsSince cursor limit

            let remaining = ref (List.map branchOpEventRecord events)

            let nextFn () : Ply<Option<Dval>> =
              uply {
                match remaining.Value with
                | head :: tail ->
                  remaining.Value <- tail
                  return Some head
                | [] -> return None
              }

            let stream =
              LibExecution.Stream.newFromIO
                (ValueType.Known(KTCustomType(branchOpEventType (), [])))
                nextFn
                None

            return DTuple(stream, cursorValue newCursor, [])
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    // Apply branch ops RECEIVED from a peer (BranchOpEvent records) — fold into branches/commits. Idempotent.
    { name = fn "branchOpsAppendNative" 0
      typeParams = [ "e" ]
      parameters =
        [ Param.make
            "events"
            (TList(TVariable "e"))
            "BranchOpEvent records received from a peer" ]
      returnType = TInt
      description =
        "Apply received BranchOpEvent records to the branch-ops log (fold into branches/commits). Returns the count processed. Idempotent."
      fn =
        (function
        | _, _, _, [ DList(_, events) ] ->
          uply {
            // Total against a bad peer (see packageOpsAppendNative): skip unparseable events, catch batch errors.
            let parsed =
              events
              |> List.choose (fun ev ->
                match ev with
                | DRecord(_, _, _, f) ->
                  try
                    Some(
                      recField "id" f,
                      System.Convert.FromHexString(recField "op" f),
                      recField "originTs" f
                    )
                  with _ ->
                    warnSkippedSyncRow "branch op"
                    None
                | _ ->
                  warnSkippedSyncRow "branch op"
                  None)

            try
              let! applied = LibDB.Seed.receiveBranchOps parsed
              return Dval.int (bigint applied)
            with _ ->
              // -1 = DB error (vs 0 = idempotent) so the puller stops without advancing this log's cursor.
              return Dval.int (bigint -1L)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    // The resolutions log — synced override overlays. Peers apply these AFTER package ops so a human's
    // "keep mine" decision converges everywhere. Read as a Stream of ResolutionEvent records + the cursor.
    { name = fn "resolutionsReadNative" 0
      typeParams = [ "e"; "cur" ]
      parameters =
        [ Param.make
            "cursor"
            TInt64
            "read resolutions after this cursor (0 = from the start)"
          Param.make "limit" TInt64 "at most this many — one bounded batch" ]
      returnType = TTuple(TStream(TVariable "e"), TVariable "cur", [])
      description =
        "This instance's resolutions after <param cursor> as a Stream of ResolutionEvent records + the resume Cursor. Built natively."
      fn =
        (function
        | _, _, _, [ DInt64 cursor; DInt64 limit ] ->
          uply {
            let! (events, newCursor) =
              LibDB.Resolutions.resolutionsSince cursor limit

            let remaining = ref (List.map resolutionEventRecord events)

            let nextFn () : Ply<Option<Dval>> =
              uply {
                match remaining.Value with
                | head :: tail ->
                  remaining.Value <- tail
                  return Some head
                | [] -> return None
              }

            let stream =
              LibExecution.Stream.newFromIO
                (ValueType.Known(KTCustomType(resolutionEventType (), [])))
                nextFn
                None

            return DTuple(stream, cursorValue newCursor, [])
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    // Apply resolutions RECEIVED from a peer (ResolutionEvent records) — record + overlay onto locations.
    // Idempotent (id-keyed) + LWW-gated by each resolution's `at`.
    { name = fn "resolutionsAppendNative" 0
      typeParams = [ "e" ]
      parameters =
        [ Param.make
            "events"
            (TList(TVariable "e"))
            "ResolutionEvent records received from a peer" ]
      returnType = TInt
      description =
        "Apply received ResolutionEvent records (record + overlay onto locations). Returns the count processed. Idempotent."
      fn =
        (function
        | _, _, _, [ DList(_, events) ] ->
          uply {
            let parsed =
              events
              |> List.choose (fun ev ->
                match ev with
                | DRecord(_, _, _, f) ->
                  try
                    Some(
                      recField "id" f,
                      recField "branchId" f,
                      recField "location" f,
                      recField "itemKind" f,
                      recField "chosenHash" f,
                      recField "resolvedBy" f,
                      recField "at" f
                    )
                  with _ ->
                    warnSkippedSyncRow "resolution"
                    None
                | _ ->
                  warnSkippedSyncRow "resolution"
                  None)

            try
              let! applied = LibDB.Resolutions.receiveResolutions parsed
              return Dval.int (bigint applied)
            with _ ->
              // -1 = DB error (vs 0 = idempotent) so the puller stops without advancing this log's cursor.
              return Dval.int (bigint -1L)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }


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
