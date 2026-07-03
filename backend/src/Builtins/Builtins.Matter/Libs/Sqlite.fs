/// Raw SQLite access from Darklang — a SPIKE of the `Stdlib.Sqlite` floor primitive (SYNCING-PR-DESIGN.md
/// PW-1), the keystone that lets sync/SCM policy move out of F# and into Dark over an ordinary database.
///
/// This spike is deliberately minimal: it opens an arbitrary file per call and runs SQL, returning affected
/// rows (exec) or rows as `Dict<String>` (query — cells are stringified so each row is homogeneous). The
/// production version (its own `Builtins.Sqlite` assembly) adds: an opaque connection-registry-backed `Db`
/// handle (so `open` is idempotent and connections are pooled), a typed `Sqlite.Value` enum
/// (Null|Int|Real|Text|Bytes) so columns keep their types, parameterized statements, `transact`, and a
/// `sqlite:open:<glob>` capability gate. Kept here on Builtins.Matter for the spike; it should be carved out.
module Builtins.Matter.Libs.Sqlite

open FSharp.Control.Tasks

open Prelude
open LibExecution.RuntimeTypes
open LibExecution.Builtin.Shortcuts

open Microsoft.Data.Sqlite

module VT = LibExecution.ValueType
module Dval = LibExecution.Dval

let private connStr (path : string) : string = $"Data Source={path}"

/// Spike marshalling: every SQLite cell becomes a string, so a row is a homogeneous `Dict<String>`. (The
/// production version returns a typed `Sqlite.Value` per cell so ints/reals/blobs keep their types.)
let private cellToString (v : obj) : string =
  match v with
  | null -> ""
  | :? (byte[]) as b -> System.Convert.ToHexString b
  | other -> string other

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
        | _, _, _, [ DString path; DString sql ] ->
          uply {
            use conn = new SqliteConnection(connStr path)
            do! conn.OpenAsync()
            use cmd = conn.CreateCommand()
            cmd.CommandText <- sql
            let! affected = cmd.ExecuteNonQueryAsync()
            return Dval.int (bigint affected)
          }
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
      returnType = TList(TDict TString)
      description =
        "Opens the SQLite file at <param path> and runs the SELECT <param sql>, returning each row as a dict "
        + "of column-name to its (stringified) value."
      fn =
        (function
        | _, _, _, [ DString path; DString sql ] ->
          uply {
            use conn = new SqliteConnection(connStr path)
            do! conn.OpenAsync()
            use cmd = conn.CreateCommand()
            cmd.CommandText <- sql
            let! readerObj = cmd.ExecuteReaderAsync()
            use reader = readerObj
            let rows = System.Collections.Generic.List<Dval>()

            let rec loop () : Ply<unit> =
              uply {
                let! hasRow = reader.ReadAsync()
                if hasRow then
                  let cells =
                    [ for i in 0 .. reader.FieldCount - 1 ->
                        (reader.GetName i, DString(cellToString (reader.GetValue i))) ]
                  rows.Add(Dval.dict KTString cells)
                  return! loop ()
              }

            do! loop ()
            return Dval.list (KTDict VT.string) (List.ofSeq rows)
          }
        | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      capabilities = LibExecution.Capabilities.noCaps
      deprecated = NotDeprecated } ]

let builtins () = LibExecution.Builtin.make [] (fns ())
