/// Standard libraries for SQLite
module Builtins.Cli.Libs.Sqlite

open System.Threading.Tasks
open FSharp.Control.Tasks
open Microsoft.Data.Sqlite

open Prelude
open LibExecution.RuntimeTypes
open LibExecution.Effects
module VT = LibExecution.ValueType
module Dval = LibExecution.Dval
module Builtin = LibExecution.Builtin
module PackageRefs = LibExecution.PackageRefs
module NR = LibExecution.RuntimeTypes.NameResolution
module Bytes = LibExecution.Bytes
open Builtin.Shortcuts


let private errorTypeName () =
  FQTypeName.fqPackage (PackageRefs.Type.Stdlib.Cli.Sqlite.error ())

let private valueTypeName () =
  FQTypeName.fqPackage (PackageRefs.Type.Stdlib.Cli.Sqlite.value ())

let private errorTypeRef () = TCustomType(NR.ok (errorTypeName ()), [])
let private valueTypeRef () = TCustomType(NR.ok (valueTypeName ()), [])
let private errorKT () = KTCustomType(errorTypeName (), [])
let private valueKT () = KTCustomType(valueTypeName (), [])
let private rowKT () = KTDict(VT.string, VT.known (valueKT ()))
let private rowTypeRef () = TDict(TString, valueTypeRef ())

/// SQLite's own result code (-1 when the failure is not SQLite's) and message.
let private dError (e : exn) : Dval =
  let code =
    match e with
    | :? SqliteException as e -> int64 e.SqliteErrorCode
    | _ -> -1L
  let tn = errorTypeName ()
  DRecord(
    tn,
    tn,
    [],
    RecordFields.ofList [ "code", Dval.dint64 code; "message", DString e.Message ]
  )

let private dValue (value : obj) : Dval =
  let case name fields =
    let tn = valueTypeName ()
    DEnum(tn, tn, [], name, fields)
  match value with
  | :? int64 as i -> case "Integer" [ Dval.dint64 i ]
  | :? double as d -> case "Real" [ DFloat d ]
  | :? string as s -> case "Text" [ DString s ]
  | :? (byte array) as bytes -> case "Blob" [ Bytes.newEphemeral bytes ]
  | _ -> case "Null" []

let private dRow (reader : SqliteDataReader) : Dval =
  List.init reader.FieldCount (fun i -> reader.GetName i, dValue (reader.GetValue i))
  |> Dval.stringDict (valueKT ())

/// Opens the database at [path], hands [run] a command on it, and closes it again.
let private withCommand
  (path : string)
  (mode : string)
  (okType : KnownType)
  (run : SqliteCommand -> Task<Dval>)
  : Ply<Dval> =
  uply {
    try
      use connection = new SqliteConnection($"Data Source={path};Mode={mode}")
      do! connection.OpenAsync()
      use command = connection.CreateCommand()
      let! result = run command
      return Dval.resultOk okType (errorKT ()) result
    with e ->
      return Dval.resultError okType (errorKT ()) (dError e)
  }

let private readRows (command : SqliteCommand) : Task<List<Dval>> =
  task {
    use! reader = command.ExecuteReaderAsync()
    let rec loop (acc : List<Dval>) : Task<List<Dval>> =
      task {
        let! hasRow = reader.ReadAsync()
        if hasRow then return! loop (dRow reader :: acc) else return List.rev acc
      }
    return! loop []
  }

let private sqlParams =
  [ Param.make "path" TString "The database file"
    Param.make "sql" TString "The SQL to run" ]

// the SQL can ATTACH any file, so the path scopes nothing (see Effect.Native)
let private effects = set [ Effect.Native ]

let fns () : List<BuiltInFn> =
  [ { name = fn "sqliteQuery" 0
      typeParams = []
      parameters = sqlParams
      returnType = TypeReference.result (TList(rowTypeRef ())) (errorTypeRef ())
      description =
        "Runs the query <param sql> against the SQLite database at <param path> and returns every row, keyed by column name."
      fn =
        (function
        | _, _, _, [| DString path; DString sql |] ->
          withCommand path "ReadWrite" (KTList(VT.known (rowKT ()))) (fun command ->
            task {
              command.CommandText <- sql
              let! rows = readRows command
              return Dval.list (rowKT ()) rows
            })
        | _ -> incorrectArgs ())
      previewable = Impure
      callEffects = effects
      deprecated = NotDeprecated }


    { name = fn "sqliteQueryOne" 0
      typeParams = []
      parameters = sqlParams
      returnType =
        TypeReference.result
          (TypeReference.option (rowTypeRef ()))
          (errorTypeRef ())
      description =
        "Runs the query <param sql> against the SQLite database at <param path> and returns its first row, or {{None}} if there is none."
      fn =
        (function
        | _, _, _, [| DString path; DString sql |] ->
          let optionKT = KTCustomType(Dval.optionType (), [ VT.known (rowKT ()) ])
          withCommand path "ReadWrite" optionKT (fun command ->
            task {
              command.CommandText <- sql
              use! reader = command.ExecuteReaderAsync()
              let! hasRow = reader.ReadAsync()
              return
                if hasRow then
                  Dval.optionSome (rowKT ()) (dRow reader)
                else
                  Dval.optionNone (rowKT ())
            })
        | _ -> incorrectArgs ())
      previewable = Impure
      callEffects = effects
      deprecated = NotDeprecated }


    { name = fn "sqliteExecute" 0
      typeParams = []
      parameters = sqlParams
      returnType = TypeReference.result TInt64 (errorTypeRef ())
      description =
        "Runs the statement <param sql> (INSERT, UPDATE, DELETE, CREATE, ...) against the SQLite database at <param path>, creating the file if needed. Returns the number of rows changed."
      fn =
        (function
        | _, _, _, [| DString path; DString sql |] ->
          withCommand path "ReadWriteCreate" KTInt64 (fun command ->
            task {
              command.CommandText <- sql
              let! changed = command.ExecuteNonQueryAsync()
              return Dval.dint64 (int64 changed)
            })
        | _ -> incorrectArgs ())
      previewable = Impure
      callEffects = effects
      deprecated = NotDeprecated } ]


let builtins () : Builtins = Builtin.make [] (fns ())
