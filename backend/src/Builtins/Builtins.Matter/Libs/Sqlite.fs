/// Raw SQLite access from Darklang — a SPIKE of the `Stdlib.Sqlite` floor primitive (SYNCING-PR-DESIGN.md
/// PW-1), the keystone that lets sync/SCM policy move out of F# and into Dark over an ordinary database.
///
/// Surface: `sqliteExec`/`sqliteQuery` (no params) and `sqliteExecP`/`sqliteQueryP` (positional params bound
/// to @p0..@pN — the injection-safe way to pass values). `exec` returns rows affected; `query` returns each
/// row as `Dict<String>` (cells stringified so the row is homogeneous).
///
/// Deliberately minimal (spike): opens a file per call, stringifies cells, uncapped, rides Builtins.Matter.
/// The production version (its own `Builtins.Sqlite` assembly) adds an opaque connection-registry `Db`
/// handle, a typed `Sqlite.Value` enum (Null|Int|Real|Text|Bytes), `transact`, and a `sqlite:open:<glob>`
/// capability. See the design doc.
module Builtins.Matter.Libs.Sqlite

open FSharp.Control.Tasks

open Prelude
open LibExecution.RuntimeTypes
open LibExecution.Builtin.Shortcuts

open Microsoft.Data.Sqlite

module VT = LibExecution.ValueType
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
let private execImpl (path : string) (sql : string) (parameters : List<string>) : Ply<Dval> =
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
let private queryImpl (path : string) (sql : string) (parameters : List<string>) : Ply<Dval> =
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
      let listDval = Dval.list (KTDict(ValueType.Known Value.knownType)) (List.ofSeq rows)
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
let private eventRecord ((id, op, br, ch, ts) : string * string * string * string * string) : Dval =
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

let private commitRecord ((hash, msg, br, acct, at) : string * string * string * string * string) : Dval =
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

let fns () : List<BuiltInFn> =
  [ { name = fn "sqliteExec" 0
      typeParams = []
      parameters =
        [ Param.make "path" TString "the SQLite file to open"
          Param.make "sql" TString "a statement to run (CREATE/INSERT/UPDATE/DELETE…)" ]
      returnType = TypeReference.result TInt TString
      description =
        "Opens the SQLite file at <param path> and runs <param sql>. Ok = rows affected; Error = the SQLite message. Never throws."
      fn =
        (function
        | _, _, _, [ DString path; DString sql ] -> execImpl path sql []
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "sqliteExecP" 0
      typeParams = []
      parameters =
        [ Param.make "path" TString "the SQLite file to open"
          Param.make "sql" TString "a statement with @p0..@pN placeholders"
          Param.make "params" (TList TString) "values bound to @p0..@pN, in order" ]
      returnType = TypeReference.result TInt TString
      description =
        "Like <fn sqliteExec> but binds <param params> to @p0..@pN placeholders (injection-safe). Ok = rows affected; Error = message."
      fn =
        (function
        | _, _, _, [ DString path; DString sql; DList(_, ps) ] ->
          execImpl path sql (paramStrings ps)
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "sqliteQuery" 0
      typeParams = []
      parameters =
        [ Param.make "path" TString "the SQLite file to open"
          Param.make "sql" TString "a SELECT to run" ]
      returnType = TypeReference.result (TList(TDict Value.typeRef)) TString
      description =
        "Opens the SQLite file at <param path> and runs the SELECT <param sql>. Ok = each row as a dict of "
        + "column-name to its typed value; Error = the SQLite message. Never throws."
      fn =
        (function
        | _, _, _, [ DString path; DString sql ] -> queryImpl path sql []
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "sqliteQueryP" 0
      typeParams = []
      parameters =
        [ Param.make "path" TString "the SQLite file to open"
          Param.make "sql" TString "a SELECT with @p0..@pN placeholders"
          Param.make "params" (TList TString) "values bound to @p0..@pN, in order" ]
      returnType = TypeReference.result (TList(TDict Value.typeRef)) TString
      description =
        "Like <fn sqliteQuery> but binds <param params> to @p0..@pN placeholders (injection-safe). Ok = rows; Error = message."
      fn =
        (function
        | _, _, _, [ DString path; DString sql; DList(_, ps) ] ->
          queryImpl path sql (paramStrings ps)
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    // This instance's OWN package store path (data.db). appendEvents/EventLog write ops here; the sync config
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

    // The general event-log APPEND (the "nice wheel" — sync is its first consumer, messaging/cron the next).
    // Takes events RECEIVED from a peer over HTTP — (id, op_blob-as-hex, branch_id, commit_hash, origin_ts) —
    // appends them to the op log preserving each op's original origin_ts, and folds (INVISIBLY, in F#). Returns
    // the new max-rowid cursor. Idempotent (content-addressed ids). No .db files, no ATTACH: pure ops-over-HTTP.
    // A named reference to an event log — the DDB-sibling `EventLog` value. The name selects the store
    // (v1: "package_ops"; branchOps / resolutions become named logs in the branch + resolution work).
    { name = fn "eventLogNamed" 0
      typeParams = [ "e" ]
      parameters =
        [ Param.make "name" TString "the log's name, e.g. \"package_ops\"" ]
      returnType = TEventLog(TVariable "e")
      description =
        "A named reference to an append-only event log. Reads (EventLog.readSince) yield a Stream; EventLog.append writes. The name selects the store."
      fn =
        (function
        | _, _, _, [ DString name ] -> uply { return DEventLog name }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Pure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    // The TYPED, stream-shaped read: Event/Commit records + the resume Cursor, all built NATIVELY so a 1000-op
    // batch never pays the per-row Dark interpreter cost. `EventLog.readSince` wraps this; the events Stream is
    // drained (native loop) for the wire, and is filterable for branch-scoped reads.
    { name = fn "eventLogReadNative" 0
      typeParams = [ "c"; "e"; "cur" ]
      parameters =
        [ Param.make "log" (TEventLog(TVariable "e")) "the log to read"
          Param.make "cursor" TInt64 "read events after this cursor (0 = from the start)"
          Param.make "limit" TInt64 "at most this many events — one bounded batch" ]
      returnType =
        TTuple(TList(TVariable "c"), TStream(TVariable "e"), [ TVariable "cur" ])
      description =
        "This instance's committed events after <param cursor> (at most <param limit>) as a Stream of Event records, the Commit records they reference, and the resume Cursor. Built natively — the fast typed read half of the event-log seam."
      fn =
        (function
        | _, _, _, [ DEventLog _name; DInt64 cursor; DInt64 limit ] ->
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

    { name = fn "eventLogSince" 0
      typeParams = []
      parameters =
        [ Param.make
            "cursor"
            TInt64
            "read events after this cursor (0 = from the start of the log)"
          Param.make "limit" TInt64 "at most this many events — one bounded batch" ]
      returnType =
        TTuple(
          TList(TTuple(TString, TString, [ TString; TString; TString ])),
          TList(TTuple(TString, TString, [ TString; TString; TString ])),
          [ TInt64 ]
        )
      description =
        "This instance's committed events after <param cursor> (at most <param limit>), the commits they reference, and the new cursor. Reads the op log natively — the READ half of the event-log seam, exactly what a peer serves."
      fn =
        (function
        | _, _, _, [ DInt64 cursor; DInt64 limit ] ->
          uply {
            let! (commits, events, newCursor) = LibDB.Seed.eventsSince cursor limit

            let strTupleKT =
              KTTuple(VT.string, VT.string, [ VT.string; VT.string; VT.string ])

            let toTuple
              ((a, b, c, d, e) : string * string * string * string * string)
              : Dval =
              DTuple(DString a, DString b, [ DString c; DString d; DString e ])

            let commitsD = Dval.list strTupleKT (List.map toTuple commits)
            let eventsD = Dval.list strTupleKT (List.map toTuple events)
            return DTuple(commitsD, eventsD, [ DInt64 newCursor ])
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    { name = fn "appendEvents" 0
      typeParams = []
      parameters =
        [ Param.make
            "commits"
            (TList(TTuple(TString, TString, [ TString; TString; TString ])))
            "(hash, message, branchId, accountId, createdAt) — the commits the events reference"
          Param.make
            "events"
            (TList(TTuple(TString, TString, [ TString; TString; TString ])))
            "(id, opBlobHex, branchId, commitHash, originTs) tuples received from a peer" ]
      returnType = TInt
      description =
        "Append received commits + events to the op log (preserving origin_ts) + fold. Returns the count of ops newly applied. Idempotent."
      fn =
        (function
        | _, _, _, [ DList(_, commits); DList(_, events) ] ->
          uply {
            let parsedCommits =
              commits
              |> List.map (fun c ->
                match c with
                | DTuple(DString hash,
                         DString message,
                         [ DString branchId; DString accountId; DString createdAt ]) ->
                  (hash,
                   message,
                   System.Guid.Parse branchId,
                   System.Guid.Parse accountId,
                   createdAt)
                | _ -> Exception.raiseInternal "appendEvents: malformed commit tuple" [])
            let parsedEvents =
              events
              |> List.map (fun ev ->
                match ev with
                | DTuple(DString id,
                         DString opBlobHex,
                         [ DString branchId; DString commitHash; DString originTs ]) ->
                  (System.Guid.Parse id,
                   System.Convert.FromHexString opBlobHex,
                   System.Guid.Parse branchId,
                   commitHash,
                   originTs)
                | _ -> Exception.raiseInternal "appendEvents: malformed event tuple" [])
            let! applied = LibDB.Seed.receiveOps parsedCommits parsedEvents
            return Dval.int (bigint applied)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated } ]

let builtins () = LibExecution.Builtin.make [] (fns ())
