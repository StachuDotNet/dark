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

let private execImpl (path : string) (sql : string) (parameters : List<string>) : Ply<Dval> =
  uply {
    use conn = new SqliteConnection(connStr path)
    do! conn.OpenAsync()
    use cmd = conn.CreateCommand()
    cmd.CommandText <- sql
    bindParams cmd parameters
    let! affected = cmd.ExecuteNonQueryAsync()
    return Dval.int (bigint affected)
  }

let private queryImpl (path : string) (sql : string) (parameters : List<string>) : Ply<Dval> =
  uply {
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
    return Dval.list (KTDict(ValueType.Known Value.knownType)) (List.ofSeq rows)
  }

let fns () : List<BuiltInFn> =
  [ { name = fn "sqliteExec" 0
      typeParams = []
      parameters =
        [ Param.make "path" TString "the SQLite file to open"
          Param.make "sql" TString "a statement to run (CREATE/INSERT/UPDATE/DELETE…)" ]
      returnType = TInt
      description =
        "Opens the SQLite file at <param path> and runs <param sql>, returning the number of rows affected."
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
      returnType = TInt
      description =
        "Like <fn sqliteExec> but binds <param params> to @p0..@pN placeholders (injection-safe)."
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
      returnType = TList(TDict Value.typeRef)
      description =
        "Opens the SQLite file at <param path> and runs the SELECT <param sql>, returning each row as a dict "
        + "of column-name to its (stringified) value."
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
      returnType = TList(TDict Value.typeRef)
      description =
        "Like <fn sqliteQuery> but binds <param params> to @p0..@pN placeholders (injection-safe)."
      fn =
        (function
        | _, _, _, [ DString path; DString sql; DList(_, ps) ] ->
          queryImpl path sql (paramStrings ps)
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated }

    // Op-log core (not sqlite-specific; sits here for now). The append/fold seam: a sync pull APPENDS a
    // peer's ops (INSERT applied=0 — e.g. via the ATTACH copy) then calls this to FOLD them into the
    // projections, exactly as a local edit does. Idempotent (only unapplied ops fold). TODO: relocate to a
    // dedicated Sync/core builtin lib alongside detectConflicts/resolution-apply.
    { name = fn "foldOps" 0
      typeParams = []
      parameters = [ Param.make "unit" TUnit "" ]
      returnType = TInt
      description =
        "Fold any unapplied ops in the local package_ops log into the projections. Idempotent; returns the number of ops applied."
      fn =
        (function
        | _, _, _, [ DUnit ] ->
          uply {
            let! n = LibDB.Seed.applyUnappliedOps ()
            return Dval.int (bigint n)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated } ]

let builtins () = LibExecution.Builtin.make [] (fns ())
