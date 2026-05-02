module LibDB.PackageManager.RuntimeTypes

open Prelude
open LibExecution.RuntimeTypes

open Microsoft.Data.Sqlite
open Fumble
open LibSqlite.Db

module RT = LibExecution.RuntimeTypes
module BS = LibSerialization.Binary.Serialization


module Type =
  let get (hash : Hash) : Ply<Option<RT.PackageType.PackageType>> =
    uply {
      let (Hash hashStr) = hash
      return!
        Sql.query
          """
          SELECT rt_def
          FROM package_types
          WHERE hash = @hash
          """
        |> Sql.parameters [ "hash", Sql.string hashStr ]
        |> Sql.executeRowOptionAsync (fun read -> read.bytes "rt_def")
        |> Task.map (Option.map (BS.RT.PackageType.deserialize hash))
    }


module Value =
  let get (hash : Hash) : Ply<Option<RT.PackageValue.PackageValue>> =
    uply {
      let (Hash hashStr) = hash
      return!
        Sql.query
          """
          SELECT rt_dval
          FROM package_values
          WHERE hash = @hash
          """
        |> Sql.parameters [ "hash", Sql.string hashStr ]
        |> Sql.executeRowOptionAsync (fun read -> read.bytes "rt_dval")
        |> Task.map (Option.map (BS.RT.PackageValue.deserialize hash))
    }

  /// Find all value hashes that have the given ValueType (exact match)
  let findByValueType (vt : RT.ValueType) : Ply<List<Hash>> =
    uply {
      let vtBytes = BS.RT.ValueType.serialize vt
      return!
        Sql.query
          """
          SELECT hash
          FROM package_values
          WHERE value_type = @value_type
          """
        |> Sql.parameters [ "value_type", Sql.bytes vtBytes ]
        |> Sql.executeAsync (fun read -> Hash(read.string "hash"))
    }


module Fn =
  let get (hash : Hash) : Ply<Option<RT.PackageFn.PackageFn>> =
    uply {
      let (Hash hashStr) = hash
      return!
        Sql.query
          """
          SELECT rt_instrs
          FROM package_functions
          WHERE hash = @hash
          """
        |> Sql.parameters [ "hash", Sql.string hashStr ]
        |> Sql.executeRowOptionAsync (fun read -> read.bytes "rt_instrs")
        |> Task.map (Option.map (BS.RT.PackageFn.deserialize hash))
    }


/// Content-addressed blob storage — bytes keyed by SHA-256 hash.
module Blob =
  /// Look up bytes by hash. Returns [None] when the row doesn't exist.
  let get (hash : string) : Ply<Option<byte[]>> =
    uply {
      return!
        Sql.query
          """
          SELECT bytes
          FROM package_blobs
          WHERE hash = @hash
          """
        |> Sql.parameters [ "hash", Sql.string hash ]
        |> Sql.executeRowOptionAsync (fun read -> read.bytes "bytes")
    }

  /// Insert bytes under [hash]. If the row already exists (same hash
  /// = same content, by content-addressing invariant), this is a no-op
  /// — `INSERT OR IGNORE` handles dedup.
  let insert (hash : string) (bytes : byte[]) : Ply<unit> =
    uply {
      let! _ =
        Sql.query
          """
          INSERT OR IGNORE INTO package_blobs (hash, length, bytes)
          VALUES (@hash, @length, @bytes)
          """
        |> Sql.parameters
          [ "hash", Sql.string hash
            "length", Sql.int64 (int64 bytes.Length)
            "bytes", Sql.bytes bytes ]
        |> Sql.executeNonQueryAsync
      return ()
    }


  /// Walk a Dval tree and collect every `Persistent` blob hash it
  /// references. Ephemeral blobs aren't rows in `package_blobs` — they
  /// live in the per-ExecutionState byte-store and don't need sweeping.
  let private collectBlobHashes (dv : RT.Dval) : Set<string> =
    let rec go (acc : Set<string>) (dv : RT.Dval) : Set<string> =
      match dv with
      | RT.DBlob(RT.Persistent(hash, _)) -> Set.add hash acc
      | RT.DBlob(RT.Ephemeral _) -> acc
      | RT.DStream _
      | RT.DUnit
      | RT.DBool _
      | RT.DInt8 _
      | RT.DUInt8 _
      | RT.DInt16 _
      | RT.DUInt16 _
      | RT.DInt32 _
      | RT.DUInt32 _
      | RT.DInt64 _
      | RT.DUInt64 _
      | RT.DInt128 _
      | RT.DUInt128 _
      | RT.DFloat _
      | RT.DChar _
      | RT.DString _
      | RT.DDateTime _
      | RT.DUuid _
      | RT.DApplicable _
      | RT.DDB _ -> acc
      | RT.DList(_, items) -> items |> List.fold go acc
      | RT.DTuple(a, b, rest) ->
        let acc = go acc a
        let acc = go acc b
        rest |> List.fold go acc
      | RT.DDict(_, entries) -> entries |> Map.values |> Seq.fold go acc
      | RT.DRecord(_, _, _, fields) -> fields |> Map.values |> Seq.fold go acc
      | RT.DEnum(_, _, _, _, fields) -> fields |> List.fold go acc
    go Set.empty dv


  /// Delete `package_blobs` rows whose hashes aren't referenced by any
  /// materialised Dval in either `package_values.rt_dval` or in the
  /// trace store (`traces.input_value_json` +
  /// `trace_fn_calls.{args_json, result_json}` +
  /// `trace_expr_values.dval_json`). Returns the count of rows deleted.
  ///
  /// Tracing started promoting ephemeral blobs to persistent at
  /// trace-record time (see `LibDB.Tracing.promoteBlobs`); without
  /// scanning trace tables, the sweep would happily delete bytes that
  /// were the only thing letting `traces view` / `gen-test` reconstruct
  /// request/response bodies.
  ///
  /// Other tables that might later hold Dvals (User DB rows) will need
  /// to register here too.
  ///
  /// Idempotent: re-running after a clean sweep deletes nothing. Safe
  /// to run while the system is live — worst-case race is a concurrent
  /// promote racing the delete, which the foreign-key-style orphan
  /// check prevents (content-addressed re-insert is cheap).
  ///
  /// For a canvas with N package values, T traces, and M blobs, cost is
  /// O(N+T+M) deserialise passes plus one DELETE per orphan.
  let sweepOrphans () : Ply<int64> =
    uply {
      // Pull every materialised rt_dval — deserialise and collect
      // hashes referenced anywhere in the tree.
      let! valueRows =
        Sql.query
          """
          SELECT hash, rt_dval
          FROM package_values
          WHERE rt_dval IS NOT NULL
          """
        |> Sql.executeAsync (fun r -> (r.string "hash", r.bytes "rt_dval"))

      let valueRefs : Set<string> =
        valueRows
        |> List.fold
          (fun acc (valueHash, rtDvalBytes) ->
            try
              let pv = BS.RT.PackageValue.deserialize (Hash valueHash) rtDvalBytes
              Set.union acc (collectBlobHashes pv.body)
            with _ ->
              // Corrupt / stale row — don't let one bad row block the
              // sweep; skip and carry on.
              acc)
          Set.empty

      // Trace inputs: one Dval per trace, JSON-encoded.
      let! traceInputs =
        Sql.query "SELECT input_value_json FROM traces"
        |> Sql.executeAsync (fun r -> r.string "input_value_json")

      let parseAndCollect (json : string) : Set<string> =
        try
          json
          |> LibDB.DvalRepr.Roundtrippable.parseJsonV0
          |> collectBlobHashes
        with _ ->
          Set.empty

      let traceInputRefs : Set<string> =
        traceInputs
        |> List.fold (fun acc j -> Set.union acc (parseAndCollect j)) Set.empty

      // Per-fn-call: args_json is a JSON array of dvals; result_json is one.
      let! fnCallRows =
        Sql.query "SELECT args_json, result_json FROM trace_fn_calls"
        |> Sql.executeAsync (fun r ->
          (r.string "args_json", r.string "result_json"))

      let collectFromArgsJson (argsJson : string) : Set<string> =
        try
          use doc = System.Text.Json.JsonDocument.Parse(argsJson)
          doc.RootElement.EnumerateArray()
          |> Seq.fold
            (fun acc el -> Set.union acc (parseAndCollect (el.GetRawText())))
            Set.empty
        with _ ->
          Set.empty

      let traceCallRefs : Set<string> =
        fnCallRows
        |> List.fold
          (fun acc (argsJson, resultJson) ->
            acc
            |> Set.union (collectFromArgsJson argsJson)
            |> Set.union (parseAndCollect resultJson))
          Set.empty

      // Per-AST-node values (let RHS, if branch result, match arm,
      // pipe stage). The recorder runs `Blob.promote` over these too,
      // so they can hold persistent blob refs that nothing else does.
      let! exprValueJsons =
        Sql.query "SELECT dval_json FROM trace_expr_values"
        |> Sql.executeAsync (fun r -> r.string "dval_json")

      let traceExprValueRefs : Set<string> =
        exprValueJsons
        |> List.fold (fun acc j -> Set.union acc (parseAndCollect j)) Set.empty

      let referenced : Set<string> =
        valueRefs
        |> Set.union traceInputRefs
        |> Set.union traceCallRefs
        |> Set.union traceExprValueRefs

      // List of candidate hashes in storage.
      let! allHashes =
        Sql.query "SELECT hash FROM package_blobs"
        |> Sql.executeAsync (fun r -> r.string "hash")

      let orphans =
        allHashes |> List.filter (fun h -> not (Set.contains h referenced))

      for h in orphans do
        do!
          Sql.query "DELETE FROM package_blobs WHERE hash = @hash"
          |> Sql.parameters [ "hash", Sql.string h ]
          |> Sql.executeStatementAsync

      return int64 (List.length orphans)
    }
