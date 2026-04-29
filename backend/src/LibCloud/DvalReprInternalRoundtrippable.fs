/// Ways of converting Dvals to/from strings, to be used exclusively internally.
///
/// That is, they should not be used in libraries, in the BwdServer, in HttpClient,
/// etc.
module LibExecution.DvalReprInternalRoundtrippable

open Prelude

// Note: we intentionally don't `open` RT here, so that we don't accidentally
// confuse the types defined here and the types defined in RT.
module RT = RuntimeTypes


module FormatV0 =
  // In the past, we used bespoke serialization formats, that were terrifying to
  // change. Going forward, if we want to use a format that we want to save and
  // reload, but don't need to search for, let's just use the simplest possible
  // format, using standard serializers.

  // We create our own format here because:
  // 1. we don't want to serialize some things, such as lambdas. Our own type allows
  //    us to be careful
  // 2. This needs to be backwards compatible, but we don't want to constrain how we
  //    change RT.Dval.

  module FQTypeName =
    type Package = RT.Hash

    type FQTypeName = Package of Package

    let toRT (t : FQTypeName) : RT.FQTypeName.FQTypeName =
      match t with
      | Package h -> RT.FQTypeName.Package h

    let fromRT (t : RT.FQTypeName.FQTypeName) : FQTypeName =
      match t with
      | RT.FQTypeName.Package h -> FQTypeName.Package h



  module rec ValueType =
    module KnownType =
      type KnownType =
        | KTUnit

        | KTBool

        | KTInt8
        | KTUInt8
        | KTInt16
        | KTUInt16
        | KTInt32
        | KTUInt32
        | KTInt64
        | KTUInt64
        | KTInt128
        | KTUInt128

        | KTFloat

        | KTChar
        | KTString

        | KTUuid

        | KTDateTime

        | KTBlob

        | KTStream of ValueType

        | KTTuple of ValueType * ValueType * List<ValueType>
        | KTList of ValueType
        | KTDict of ValueType

        | KTCustomType of FQTypeName.FQTypeName * typeArgs : List<ValueType>

        | KTFn of NEList<ValueType> * ValueType

        | KTDB of ValueType

      let rec toRT (kt : KnownType) : RT.KnownType =
        match kt with
        | KTUnit -> RT.KTUnit
        | KTBool -> RT.KTBool
        | KTInt64 -> RT.KTInt64
        | KTUInt64 -> RT.KTUInt64
        | KTInt8 -> RT.KTInt8
        | KTUInt8 -> RT.KTUInt8
        | KTInt16 -> RT.KTInt16
        | KTUInt16 -> RT.KTUInt16
        | KTInt32 -> RT.KTInt32
        | KTUInt32 -> RT.KTUInt32
        | KTInt128 -> RT.KTInt128
        | KTUInt128 -> RT.KTUInt128
        | KTFloat -> RT.KTFloat
        | KTChar -> RT.KTChar
        | KTString -> RT.KTString
        | KTUuid -> RT.KTUuid
        | KTDateTime -> RT.KTDateTime
        | KTBlob -> RT.KTBlob
        | KTStream vt -> RT.KTStream(ValueType.toRT vt)

        | KTList vt -> RT.KTList(ValueType.toRT vt)
        | KTTuple(vt1, vt2, vts) ->
          RT.KTTuple(
            ValueType.toRT vt1,
            ValueType.toRT vt2,
            List.map ValueType.toRT vts
          )
        | KTDict vt -> RT.KTDict(ValueType.toRT vt)

        | KTFn(argTypes, returnType) ->
          RT.KTFn(NEList.map ValueType.toRT argTypes, ValueType.toRT returnType)

        | KTCustomType(typeName, typeArgs) ->
          RT.KTCustomType(FQTypeName.toRT typeName, List.map ValueType.toRT typeArgs)

        | KTDB vt -> RT.KTDB(ValueType.toRT vt)

      let rec fromRT (kt : RT.KnownType) : KnownType =
        match kt with
        | RT.KTUnit -> KTUnit
        | RT.KTBool -> KTBool
        | RT.KTInt64 -> KTInt64
        | RT.KTUInt64 -> KTUInt64
        | RT.KTInt8 -> KTInt8
        | RT.KTUInt8 -> KTUInt8
        | RT.KTInt16 -> KTInt16
        | RT.KTUInt16 -> KTUInt16
        | RT.KTInt32 -> KTInt32
        | RT.KTUInt32 -> KTUInt32
        | RT.KTInt128 -> KTInt128
        | RT.KTUInt128 -> KTUInt128
        | RT.KTFloat -> KTFloat
        | RT.KTChar -> KTChar
        | RT.KTString -> KTString
        | RT.KTUuid -> KTUuid
        | RT.KTDateTime -> KTDateTime
        | RT.KTBlob -> KTBlob
        | RT.KTStream vt -> KTStream(ValueType.fromRT vt)

        | RT.KTList vt -> KTList(ValueType.fromRT vt)
        | RT.KTTuple(vt1, vt2, vts) ->
          KTTuple(
            ValueType.fromRT vt1,
            ValueType.fromRT vt2,
            List.map ValueType.fromRT vts
          )
        | RT.KTDict vt -> KTDict(ValueType.fromRT vt)

        | RT.KTFn(argTypes, returnType) ->
          KTFn(NEList.map ValueType.fromRT argTypes, ValueType.fromRT returnType)

        | RT.KTCustomType(typeName, typeArgs) ->
          KTCustomType(
            FQTypeName.fromRT typeName,
            List.map ValueType.fromRT typeArgs
          )

        | RT.KTDB vt -> KTDB(ValueType.fromRT vt)

    [<RequireQualifiedAccess>]
    type ValueType =
      | Unknown
      | Known of KnownType.KnownType

    let toRT (vt : ValueType) : RT.ValueType =
      match vt with
      | ValueType.Unknown -> RT.ValueType.Unknown
      | ValueType.Known kt -> RT.ValueType.Known(ValueType.KnownType.toRT kt)

    let fromRT (vt : RT.ValueType) : ValueType =
      match vt with
      | RT.ValueType.Unknown -> ValueType.ValueType.Unknown
      | RT.ValueType.Known kt ->
        ValueType.ValueType.Known(ValueType.KnownType.fromRT kt)

  let valueTypeTODO = ValueType.ValueType.Unknown


  type DvalMap = Map<string, Dval>

  and Dval =
    | DUnit
    | DBool of bool
    | DInt8 of int8
    | DUInt8 of uint8
    | DInt16 of int16
    | DUInt16 of uint16
    | DInt32 of int32
    | DUInt32 of uint32
    | DInt64 of int64
    | DUInt64 of uint64
    | DInt128 of System.Int128
    | DUInt128 of System.UInt128
    | DFloat of double
    | DChar of string
    | DString of string
    | DDateTime of NodaTime.LocalDateTime
    | DUuid of System.Guid
    | DTuple of Dval * Dval * List<Dval>
    | DList of ValueType.ValueType * List<Dval>
    | DDict of ValueType.ValueType * DvalMap
    | DRecord of
      runtimeTypeName : FQTypeName.FQTypeName *
      sourceTypeName : FQTypeName.FQTypeName *
      typeArgs : List<ValueType.ValueType> *
      fields : DvalMap
    | DEnum of
      runtimeTypeName : FQTypeName.FQTypeName *
      sourceTypeName : FQTypeName.FQTypeName *
      typeArgs : List<ValueType.ValueType> *
      caseName : string *
      fields : List<Dval>
    | DLambda // See docs/dblock-serialization.md
    | DDB of string
    | DBlobPersistent of hash : string * length : int64
    | DBlobEphemeral of id : System.Guid
    // DStream is not persistable — this tag is a stub that exists only
    // so the exhaustiveness check holds; `toRT` raises on it.
    | DStreamStub


  let rec toRT (dv : Dval) : RT.Dval =
    match dv with
    | DUnit -> RT.DUnit

    | DBool b -> RT.DBool b

    | DInt8 i -> RT.DInt8 i
    | DUInt8 i -> RT.DUInt8 i
    | DInt16 i -> RT.DInt16 i
    | DUInt16 i -> RT.DUInt16 i
    | DInt32 i -> RT.DInt32 i
    | DUInt32 i -> RT.DUInt32 i
    | DInt64 i -> RT.DInt64 i
    | DUInt64 i -> RT.DUInt64 i
    | DInt128 i -> RT.DInt128 i
    | DUInt128 i -> RT.DUInt128 i

    | DFloat f -> RT.DFloat f

    | DChar c -> RT.DChar c
    | DString s -> RT.DString s

    | DDateTime d -> RT.DDateTime d

    | DUuid uuid -> RT.DUuid uuid

    | DTuple(first, second, theRest) ->
      RT.DTuple(toRT first, toRT second, List.map toRT theRest)

    | DList(typ, l) -> RT.DList(ValueType.toRT typ, List.map toRT l)

    | DDict(typ, entries) -> RT.DDict(ValueType.toRT typ, Map.map toRT entries)


    | DRecord(typeName, original, typeArgs, o) ->
      RT.DRecord(
        FQTypeName.toRT typeName,
        FQTypeName.toRT original,
        List.map ValueType.toRT typeArgs,
        Map.map toRT o
      )

    | DEnum(typeName, original, typeArgs, caseName, fields) ->
      RT.DEnum(
        FQTypeName.toRT typeName,
        FQTypeName.toRT original,
        List.map ValueType.toRT typeArgs,
        caseName,
        List.map toRT fields
      )

    | DLambda ->
      RT.DApplicable(
        RT.AppLambda
          { exprId = gid ()
            closedRegisters = []
            argsSoFar = []
            typeSymbolTable = Map.empty }
      )

    | DDB name -> RT.DDB name

    | DBlobPersistent(hash, length) -> RT.DBlob(RT.Persistent(hash, length))
    | DBlobEphemeral id -> RT.DBlob(RT.Ephemeral id)
    | DStreamStub ->
      Exception.raiseInternal
        "DStream is not persistable — can't deserialize a stub to a live stream"
        []



  let rec fromRT (dv : RT.Dval) : Dval =
    match dv with
    | RT.DUnit -> DUnit

    | RT.DBool b -> DBool b

    | RT.DInt8 i -> DInt8 i
    | RT.DUInt8 i -> DUInt8 i
    | RT.DInt16 i -> DInt16 i
    | RT.DUInt16 i -> DUInt16 i
    | RT.DInt32 i -> DInt32 i
    | RT.DUInt32 i -> DUInt32 i
    | RT.DInt64 i -> DInt64 i
    | RT.DUInt64 i -> DUInt64 i
    | RT.DInt128 i -> DInt128 i
    | RT.DUInt128 i -> DUInt128 i

    | RT.DFloat f -> DFloat f

    | RT.DChar c -> DChar c
    | RT.DString s -> DString s

    | RT.DDateTime d -> DDateTime d

    | RT.DUuid uuid -> DUuid uuid

    | RT.DTuple(first, second, theRest) ->
      DTuple(fromRT first, fromRT second, List.map fromRT theRest)
    | RT.DList(typ, l) -> DList(ValueType.fromRT typ, List.map fromRT l)
    | RT.DDict(typ, entries) -> DDict(ValueType.fromRT typ, Map.map fromRT entries)

    | RT.DRecord(typeName, original, typeArgs, o) ->
      DRecord(
        FQTypeName.fromRT typeName,
        FQTypeName.fromRT original,
        List.map ValueType.fromRT typeArgs,
        Map.map fromRT o
      )

    | RT.DEnum(typeName, original, typeArgs, caseName, fields) ->
      DEnum(
        FQTypeName.fromRT typeName,
        FQTypeName.fromRT original,
        List.map ValueType.fromRT typeArgs,
        caseName,
        List.map fromRT fields
      )

    | RT.DApplicable _ -> DLambda

    | RT.DDB name -> DDB name

    | RT.DBlob(RT.Persistent(hash, length)) -> DBlobPersistent(hash, length)
    | RT.DBlob(RT.Ephemeral id) -> DBlobEphemeral id
    | RT.DStream _ ->
      // Streams aren't persistable by design. For the rt_dval column
      // that's strictly a no-op target (a stream can't live past its
      // VM anyway), so we emit a stub that round-trips to an error on
      // read-back rather than raising at capture time. This lets the
      // trace pipeline (which captures every intermediate dval) pass
      // a stream through without aborting the eval.
      DStreamStub


/// Hand-rolled JSON ser/de for FormatV0.Dval.
///
/// The previous implementation used `Json.Vanilla.serialize`/`deserialize<T>`
/// which routes through `JsonSerializer.{De}serialize<T>` and ultimately F#'s
/// reflective union-converter. Under AOT trimming, the union case-name lookup
/// on FormatV0.Dval throws (case metadata is pruned).
///
/// This module walks `Utf8JsonReader`/`Utf8JsonWriter` directly. The wire
/// format is intentionally simpler than ExternalTag: each union value is a
/// JSON array `[tag, ...args]`, and nullary cases are just `"tag"` strings.
/// Format chosen for readability and reader simplicity (no tag-payload
/// parsing); not wire-compatible with the prior FSharp.SystemTextJson output,
/// so any cached trace data on disk under the prior format won't deserialize
/// after this change. Trace data is per-machine local; lose-and-regrow is
/// the migration story.
[<RequireQualifiedAccess>]
module HandJsonV0 =
  open System.Text.Json

  // -------- helpers --------

  let inline private readToken (r : byref<Utf8JsonReader>) : unit =
    if not (r.Read()) then Exception.raiseInternal "Unexpected end of JSON" []

  let private expectStartArray (r : byref<Utf8JsonReader>) : unit =
    if r.TokenType <> JsonTokenType.StartArray then
      Exception.raiseInternal
        "Expected JSON array start"
        [ "tokenType", r.TokenType ]

  let private expectEndArray (r : byref<Utf8JsonReader>) : unit =
    if r.TokenType <> JsonTokenType.EndArray then
      Exception.raiseInternal "Expected JSON array end" [ "tokenType", r.TokenType ]

  let private readTag (r : byref<Utf8JsonReader>) : string * bool =
    // Returns (tag, isNullary). For nullary cases the value is just a JSON
    // string; for compound cases the value is `[tag, ...]`.
    if r.TokenType = JsonTokenType.String then
      r.GetString(), true
    else
      expectStartArray &r
      readToken &r
      if r.TokenType <> JsonTokenType.String then
        Exception.raiseInternal
          "Expected tag string at start of array"
          [ "tokenType", r.TokenType ]
      r.GetString(), false

  let private readString (r : byref<Utf8JsonReader>) : string =
    readToken &r
    if r.TokenType <> JsonTokenType.String then
      Exception.raiseInternal "Expected JSON string" [ "tokenType", r.TokenType ]
    r.GetString()

  let private readBool (r : byref<Utf8JsonReader>) : bool =
    readToken &r
    match r.TokenType with
    | JsonTokenType.True -> true
    | JsonTokenType.False -> false
    | t -> Exception.raiseInternal "Expected JSON bool" [ "tokenType", t ]

  let private readDouble (r : byref<Utf8JsonReader>) : double =
    readToken &r
    if r.TokenType <> JsonTokenType.Number then
      Exception.raiseInternal "Expected JSON number" [ "tokenType", r.TokenType ]
    r.GetDouble()


  // -------- FQTypeName / Hash --------

  let private writeHash (w : Utf8JsonWriter) (h : RT.Hash) : unit =
    let (RT.Hash s) = h
    w.WriteStringValue(s)

  let private readHash (r : byref<Utf8JsonReader>) : RT.Hash =
    readString &r |> RT.Hash

  let private writeFQTypeName
    (w : Utf8JsonWriter)
    (n : FormatV0.FQTypeName.FQTypeName)
    : unit =
    match n with
    | FormatV0.FQTypeName.Package h ->
      w.WriteStartArray()
      w.WriteStringValue("Package")
      writeHash w h
      w.WriteEndArray()

  let private readFQTypeName
    (r : byref<Utf8JsonReader>)
    : FormatV0.FQTypeName.FQTypeName =
    let tag, _ = readTag &r
    match tag with
    | "Package" ->
      let h = readHash &r
      readToken &r // EndArray
      expectEndArray &r
      FormatV0.FQTypeName.Package h
    | t -> Exception.raiseInternal "Unknown FQTypeName tag" [ "tag", t ]


  // -------- ValueType + KnownType (mutually recursive) --------

  let rec private writeKnownType
    (w : Utf8JsonWriter)
    (kt : FormatV0.ValueType.KnownType.KnownType)
    : unit =
    match kt with
    | FormatV0.ValueType.KnownType.KTUnit -> w.WriteStringValue("KTUnit")
    | FormatV0.ValueType.KnownType.KTBool -> w.WriteStringValue("KTBool")
    | FormatV0.ValueType.KnownType.KTInt8 -> w.WriteStringValue("KTInt8")
    | FormatV0.ValueType.KnownType.KTUInt8 -> w.WriteStringValue("KTUInt8")
    | FormatV0.ValueType.KnownType.KTInt16 -> w.WriteStringValue("KTInt16")
    | FormatV0.ValueType.KnownType.KTUInt16 -> w.WriteStringValue("KTUInt16")
    | FormatV0.ValueType.KnownType.KTInt32 -> w.WriteStringValue("KTInt32")
    | FormatV0.ValueType.KnownType.KTUInt32 -> w.WriteStringValue("KTUInt32")
    | FormatV0.ValueType.KnownType.KTInt64 -> w.WriteStringValue("KTInt64")
    | FormatV0.ValueType.KnownType.KTUInt64 -> w.WriteStringValue("KTUInt64")
    | FormatV0.ValueType.KnownType.KTInt128 -> w.WriteStringValue("KTInt128")
    | FormatV0.ValueType.KnownType.KTUInt128 -> w.WriteStringValue("KTUInt128")
    | FormatV0.ValueType.KnownType.KTFloat -> w.WriteStringValue("KTFloat")
    | FormatV0.ValueType.KnownType.KTChar -> w.WriteStringValue("KTChar")
    | FormatV0.ValueType.KnownType.KTString -> w.WriteStringValue("KTString")
    | FormatV0.ValueType.KnownType.KTUuid -> w.WriteStringValue("KTUuid")
    | FormatV0.ValueType.KnownType.KTDateTime -> w.WriteStringValue("KTDateTime")
    | FormatV0.ValueType.KnownType.KTBlob -> w.WriteStringValue("KTBlob")
    | FormatV0.ValueType.KnownType.KTStream vt ->
      w.WriteStartArray()
      w.WriteStringValue("KTStream")
      writeValueType w vt
      w.WriteEndArray()
    | FormatV0.ValueType.KnownType.KTList vt ->
      w.WriteStartArray()
      w.WriteStringValue("KTList")
      writeValueType w vt
      w.WriteEndArray()
    | FormatV0.ValueType.KnownType.KTTuple(a, b, rest) ->
      w.WriteStartArray()
      w.WriteStringValue("KTTuple")
      writeValueType w a
      writeValueType w b
      w.WriteStartArray()
      for vt in rest do
        writeValueType w vt
      w.WriteEndArray()
      w.WriteEndArray()
    | FormatV0.ValueType.KnownType.KTDict vt ->
      w.WriteStartArray()
      w.WriteStringValue("KTDict")
      writeValueType w vt
      w.WriteEndArray()
    | FormatV0.ValueType.KnownType.KTCustomType(typeName, typeArgs) ->
      w.WriteStartArray()
      w.WriteStringValue("KTCustomType")
      writeFQTypeName w typeName
      w.WriteStartArray()
      for vt in typeArgs do
        writeValueType w vt
      w.WriteEndArray()
      w.WriteEndArray()
    | FormatV0.ValueType.KnownType.KTFn(argTypes, retType) ->
      w.WriteStartArray()
      w.WriteStringValue("KTFn")
      // NEList → [head, ...tail]
      w.WriteStartArray()
      writeValueType w argTypes.head
      for vt in argTypes.tail do
        writeValueType w vt
      w.WriteEndArray()
      writeValueType w retType
      w.WriteEndArray()
    | FormatV0.ValueType.KnownType.KTDB vt ->
      w.WriteStartArray()
      w.WriteStringValue("KTDB")
      writeValueType w vt
      w.WriteEndArray()

  and private writeValueType
    (w : Utf8JsonWriter)
    (vt : FormatV0.ValueType.ValueType)
    : unit =
    match vt with
    | FormatV0.ValueType.ValueType.Unknown -> w.WriteStringValue("Unknown")
    | FormatV0.ValueType.ValueType.Known kt ->
      w.WriteStartArray()
      w.WriteStringValue("Known")
      writeKnownType w kt
      w.WriteEndArray()

  let rec private readKnownType
    (r : byref<Utf8JsonReader>)
    : FormatV0.ValueType.KnownType.KnownType =
    let tag, isNullary = readTag &r
    let kt =
      match tag with
      | "KTUnit" -> FormatV0.ValueType.KnownType.KTUnit
      | "KTBool" -> FormatV0.ValueType.KnownType.KTBool
      | "KTInt8" -> FormatV0.ValueType.KnownType.KTInt8
      | "KTUInt8" -> FormatV0.ValueType.KnownType.KTUInt8
      | "KTInt16" -> FormatV0.ValueType.KnownType.KTInt16
      | "KTUInt16" -> FormatV0.ValueType.KnownType.KTUInt16
      | "KTInt32" -> FormatV0.ValueType.KnownType.KTInt32
      | "KTUInt32" -> FormatV0.ValueType.KnownType.KTUInt32
      | "KTInt64" -> FormatV0.ValueType.KnownType.KTInt64
      | "KTUInt64" -> FormatV0.ValueType.KnownType.KTUInt64
      | "KTInt128" -> FormatV0.ValueType.KnownType.KTInt128
      | "KTUInt128" -> FormatV0.ValueType.KnownType.KTUInt128
      | "KTFloat" -> FormatV0.ValueType.KnownType.KTFloat
      | "KTChar" -> FormatV0.ValueType.KnownType.KTChar
      | "KTString" -> FormatV0.ValueType.KnownType.KTString
      | "KTUuid" -> FormatV0.ValueType.KnownType.KTUuid
      | "KTDateTime" -> FormatV0.ValueType.KnownType.KTDateTime
      | "KTBlob" -> FormatV0.ValueType.KnownType.KTBlob
      | "KTStream" ->
        let vt = readValueType &r
        readToken &r
        expectEndArray &r
        FormatV0.ValueType.KnownType.KTStream vt
      | "KTList" ->
        let vt = readValueType &r
        readToken &r
        expectEndArray &r
        FormatV0.ValueType.KnownType.KTList vt
      | "KTTuple" ->
        let a = readValueType &r
        let b = readValueType &r
        readToken &r
        expectStartArray &r
        let rest = ResizeArray<FormatV0.ValueType.ValueType>()
        readToken &r
        while r.TokenType <> JsonTokenType.EndArray do
          rest.Add(readValueTypeFromCurrent &r)
          readToken &r
        readToken &r
        expectEndArray &r
        FormatV0.ValueType.KnownType.KTTuple(a, b, List.ofSeq rest)
      | "KTDict" ->
        let vt = readValueType &r
        readToken &r
        expectEndArray &r
        FormatV0.ValueType.KnownType.KTDict vt
      | "KTCustomType" ->
        readToken &r
        let typeName = readFQTypeNameFromCurrent &r
        readToken &r
        expectStartArray &r
        let typeArgs = ResizeArray<FormatV0.ValueType.ValueType>()
        readToken &r
        while r.TokenType <> JsonTokenType.EndArray do
          typeArgs.Add(readValueTypeFromCurrent &r)
          readToken &r
        readToken &r
        expectEndArray &r
        FormatV0.ValueType.KnownType.KTCustomType(typeName, List.ofSeq typeArgs)
      | "KTFn" ->
        readToken &r
        expectStartArray &r
        let argTypes = ResizeArray<FormatV0.ValueType.ValueType>()
        readToken &r
        while r.TokenType <> JsonTokenType.EndArray do
          argTypes.Add(readValueTypeFromCurrent &r)
          readToken &r
        let retType = readValueType &r
        readToken &r
        expectEndArray &r
        let argList = List.ofSeq argTypes
        match argList with
        | [] -> Exception.raiseInternal "KTFn: empty arg NEList" []
        | head :: tail ->
          FormatV0.ValueType.KnownType.KTFn(NEList.ofList head tail, retType)
      | "KTDB" ->
        let vt = readValueType &r
        readToken &r
        expectEndArray &r
        FormatV0.ValueType.KnownType.KTDB vt
      | t -> Exception.raiseInternal "Unknown KnownType tag" [ "tag", t ]
    if isNullary then () // nothing to consume
    kt

  and private readValueType
    (r : byref<Utf8JsonReader>)
    : FormatV0.ValueType.ValueType =
    readToken &r
    readValueTypeFromCurrent &r

  and private readValueTypeFromCurrent
    (r : byref<Utf8JsonReader>)
    : FormatV0.ValueType.ValueType =
    let tag, isNullary = readTag &r
    match tag with
    | "Unknown" ->
      if not isNullary then
        Exception.raiseInternal "Expected nullary 'Unknown' tag" []
      FormatV0.ValueType.ValueType.Unknown
    | "Known" ->
      let kt = readKnownType &r
      readToken &r
      expectEndArray &r
      FormatV0.ValueType.ValueType.Known kt
    | t -> Exception.raiseInternal "Unknown ValueType tag" [ "tag", t ]

  and private readFQTypeNameFromCurrent
    (r : byref<Utf8JsonReader>)
    : FormatV0.FQTypeName.FQTypeName =
    let tag, _ = readTag &r
    match tag with
    | "Package" ->
      let h = readHash &r
      readToken &r
      expectEndArray &r
      FormatV0.FQTypeName.Package h
    | t -> Exception.raiseInternal "Unknown FQTypeName tag" [ "tag", t ]


  // -------- Dval (recursive) --------

  let rec private writeDval (w : Utf8JsonWriter) (dv : FormatV0.Dval) : unit =
    match dv with
    | FormatV0.DUnit -> w.WriteStringValue("DUnit")
    | FormatV0.DBool b ->
      w.WriteStartArray()
      w.WriteStringValue("DBool")
      w.WriteBooleanValue(b)
      w.WriteEndArray()
    | FormatV0.DInt8 i ->
      w.WriteStartArray()
      w.WriteStringValue("DInt8")
      w.WriteNumberValue(int i)
      w.WriteEndArray()
    | FormatV0.DUInt8 i ->
      w.WriteStartArray()
      w.WriteStringValue("DUInt8")
      w.WriteNumberValue(int i)
      w.WriteEndArray()
    | FormatV0.DInt16 i ->
      w.WriteStartArray()
      w.WriteStringValue("DInt16")
      w.WriteNumberValue(int i)
      w.WriteEndArray()
    | FormatV0.DUInt16 i ->
      w.WriteStartArray()
      w.WriteStringValue("DUInt16")
      w.WriteNumberValue(int i)
      w.WriteEndArray()
    | FormatV0.DInt32 i ->
      w.WriteStartArray()
      w.WriteStringValue("DInt32")
      w.WriteNumberValue(i)
      w.WriteEndArray()
    | FormatV0.DUInt32 i ->
      w.WriteStartArray()
      w.WriteStringValue("DUInt32")
      w.WriteNumberValue(i)
      w.WriteEndArray()
    | FormatV0.DInt64 i ->
      w.WriteStartArray()
      w.WriteStringValue("DInt64")
      // Encode 64-bit ints as strings to dodge JSON number precision (53-bit safe)
      w.WriteStringValue(string i)
      w.WriteEndArray()
    | FormatV0.DUInt64 i ->
      w.WriteStartArray()
      w.WriteStringValue("DUInt64")
      w.WriteStringValue(string i)
      w.WriteEndArray()
    | FormatV0.DInt128 i ->
      w.WriteStartArray()
      w.WriteStringValue("DInt128")
      w.WriteStringValue(string i)
      w.WriteEndArray()
    | FormatV0.DUInt128 i ->
      w.WriteStartArray()
      w.WriteStringValue("DUInt128")
      w.WriteStringValue(string i)
      w.WriteEndArray()
    | FormatV0.DFloat f ->
      w.WriteStartArray()
      w.WriteStringValue("DFloat")
      // JSON doesn't natively encode NaN / Infinity; use string form
      if System.Double.IsNaN(f) then w.WriteStringValue("NaN")
      elif System.Double.IsPositiveInfinity(f) then w.WriteStringValue("+Infinity")
      elif System.Double.IsNegativeInfinity(f) then w.WriteStringValue("-Infinity")
      else w.WriteNumberValue(f)
      w.WriteEndArray()
    | FormatV0.DChar c ->
      w.WriteStartArray()
      w.WriteStringValue("DChar")
      w.WriteStringValue(c)
      w.WriteEndArray()
    | FormatV0.DString s ->
      w.WriteStartArray()
      w.WriteStringValue("DString")
      w.WriteStringValue(s)
      w.WriteEndArray()
    | FormatV0.DDateTime d ->
      w.WriteStartArray()
      w.WriteStringValue("DDateTime")
      let zoned =
        NodaTime.ZonedDateTime(d, NodaTime.DateTimeZone.Utc, NodaTime.Offset.Zero)
      w.WriteStringValue(zoned.ToInstant().toIsoString ())
      w.WriteEndArray()
    | FormatV0.DUuid g ->
      w.WriteStartArray()
      w.WriteStringValue("DUuid")
      w.WriteStringValue(g.ToString())
      w.WriteEndArray()
    | FormatV0.DTuple(a, b, rest) ->
      w.WriteStartArray()
      w.WriteStringValue("DTuple")
      writeDval w a
      writeDval w b
      w.WriteStartArray()
      for d in rest do
        writeDval w d
      w.WriteEndArray()
      w.WriteEndArray()
    | FormatV0.DList(typ, items) ->
      w.WriteStartArray()
      w.WriteStringValue("DList")
      writeValueType w typ
      w.WriteStartArray()
      for d in items do
        writeDval w d
      w.WriteEndArray()
      w.WriteEndArray()
    | FormatV0.DDict(typ, entries) ->
      w.WriteStartArray()
      w.WriteStringValue("DDict")
      writeValueType w typ
      w.WriteStartObject()
      for KeyValue(k, v) in entries do
        w.WritePropertyName(k)
        writeDval w v
      w.WriteEndObject()
      w.WriteEndArray()
    | FormatV0.DRecord(rtt, st, ta, fields) ->
      w.WriteStartArray()
      w.WriteStringValue("DRecord")
      writeFQTypeName w rtt
      writeFQTypeName w st
      w.WriteStartArray()
      for vt in ta do
        writeValueType w vt
      w.WriteEndArray()
      w.WriteStartObject()
      for KeyValue(k, v) in fields do
        w.WritePropertyName(k)
        writeDval w v
      w.WriteEndObject()
      w.WriteEndArray()
    | FormatV0.DEnum(rtt, st, ta, caseName, fields) ->
      w.WriteStartArray()
      w.WriteStringValue("DEnum")
      writeFQTypeName w rtt
      writeFQTypeName w st
      w.WriteStartArray()
      for vt in ta do
        writeValueType w vt
      w.WriteEndArray()
      w.WriteStringValue(caseName)
      w.WriteStartArray()
      for d in fields do
        writeDval w d
      w.WriteEndArray()
      w.WriteEndArray()
    | FormatV0.DLambda -> w.WriteStringValue("DLambda")
    | FormatV0.DDB name ->
      w.WriteStartArray()
      w.WriteStringValue("DDB")
      w.WriteStringValue(name)
      w.WriteEndArray()
    | FormatV0.DBlobPersistent(hash, length) ->
      w.WriteStartArray()
      w.WriteStringValue("DBlobPersistent")
      w.WriteStringValue(hash)
      w.WriteStringValue(string length)
      w.WriteEndArray()
    | FormatV0.DBlobEphemeral g ->
      w.WriteStartArray()
      w.WriteStringValue("DBlobEphemeral")
      w.WriteStringValue(g.ToString())
      w.WriteEndArray()
    | FormatV0.DStreamStub -> w.WriteStringValue("DStreamStub")


  let rec private readDvalMap (r : byref<Utf8JsonReader>) : FormatV0.DvalMap =
    readToken &r
    if r.TokenType <> JsonTokenType.StartObject then
      Exception.raiseInternal
        "Expected JSON object for DvalMap"
        [ "tokenType", r.TokenType ]
    let entries = ResizeArray<string * FormatV0.Dval>()
    readToken &r
    while r.TokenType <> JsonTokenType.EndObject do
      if r.TokenType <> JsonTokenType.PropertyName then
        Exception.raiseInternal
          "Expected property name in DvalMap"
          [ "tokenType", r.TokenType ]
      let key = r.GetString()
      let value = readDval &r
      entries.Add(key, value)
      readToken &r
    Map.ofSeq entries

  and private readDval (r : byref<Utf8JsonReader>) : FormatV0.Dval =
    readToken &r
    readDvalFromCurrent &r

  and private readDvalFromCurrent (r : byref<Utf8JsonReader>) : FormatV0.Dval =
    let tag, isNullary = readTag &r
    match tag with
    | "DUnit" -> FormatV0.DUnit
    | "DLambda" -> FormatV0.DLambda
    | "DStreamStub" -> FormatV0.DStreamStub
    | "DBool" ->
      let b = readBool &r
      readToken &r
      expectEndArray &r
      FormatV0.DBool b
    | "DInt8" ->
      readToken &r
      let i = r.GetSByte()
      readToken &r
      expectEndArray &r
      FormatV0.DInt8 i
    | "DUInt8" ->
      readToken &r
      let i = r.GetByte()
      readToken &r
      expectEndArray &r
      FormatV0.DUInt8 i
    | "DInt16" ->
      readToken &r
      let i = r.GetInt16()
      readToken &r
      expectEndArray &r
      FormatV0.DInt16 i
    | "DUInt16" ->
      readToken &r
      let i = r.GetUInt16()
      readToken &r
      expectEndArray &r
      FormatV0.DUInt16 i
    | "DInt32" ->
      readToken &r
      let i = r.GetInt32()
      readToken &r
      expectEndArray &r
      FormatV0.DInt32 i
    | "DUInt32" ->
      readToken &r
      let i = r.GetUInt32()
      readToken &r
      expectEndArray &r
      FormatV0.DUInt32 i
    | "DInt64" ->
      let s = readString &r
      readToken &r
      expectEndArray &r
      FormatV0.DInt64(System.Int64.Parse s)
    | "DUInt64" ->
      let s = readString &r
      readToken &r
      expectEndArray &r
      FormatV0.DUInt64(System.UInt64.Parse s)
    | "DInt128" ->
      let s = readString &r
      readToken &r
      expectEndArray &r
      FormatV0.DInt128(System.Int128.Parse s)
    | "DUInt128" ->
      let s = readString &r
      readToken &r
      expectEndArray &r
      FormatV0.DUInt128(System.UInt128.Parse s)
    | "DFloat" ->
      readToken &r
      let f =
        match r.TokenType with
        | JsonTokenType.Number -> r.GetDouble()
        | JsonTokenType.String ->
          match r.GetString() with
          | "NaN" -> System.Double.NaN
          | "+Infinity" -> System.Double.PositiveInfinity
          | "-Infinity" -> System.Double.NegativeInfinity
          | s -> System.Double.Parse s
        | t ->
          Exception.raiseInternal "Expected number for DFloat" [ "tokenType", t ]
      readToken &r
      expectEndArray &r
      FormatV0.DFloat f
    | "DChar" ->
      let c = readString &r
      readToken &r
      expectEndArray &r
      FormatV0.DChar c
    | "DString" ->
      let s = readString &r
      readToken &r
      expectEndArray &r
      FormatV0.DString s
    | "DDateTime" ->
      let s = readString &r
      readToken &r
      expectEndArray &r
      let dt = (NodaTime.Instant.ofIsoString s).toUtcLocalTimeZone ()
      FormatV0.DDateTime dt
    | "DUuid" ->
      let s = readString &r
      readToken &r
      expectEndArray &r
      FormatV0.DUuid(System.Guid.Parse s)
    | "DTuple" ->
      let a = readDval &r
      let b = readDval &r
      readToken &r
      expectStartArray &r
      let rest = ResizeArray<FormatV0.Dval>()
      readToken &r
      while r.TokenType <> JsonTokenType.EndArray do
        rest.Add(readDvalFromCurrent &r)
        readToken &r
      readToken &r
      expectEndArray &r
      FormatV0.DTuple(a, b, List.ofSeq rest)
    | "DList" ->
      let typ = readValueType &r
      readToken &r
      expectStartArray &r
      let items = ResizeArray<FormatV0.Dval>()
      readToken &r
      while r.TokenType <> JsonTokenType.EndArray do
        items.Add(readDvalFromCurrent &r)
        readToken &r
      readToken &r
      expectEndArray &r
      FormatV0.DList(typ, List.ofSeq items)
    | "DDict" ->
      let typ = readValueType &r
      let entries = readDvalMap &r
      readToken &r
      expectEndArray &r
      FormatV0.DDict(typ, entries)
    | "DRecord" ->
      readToken &r
      let rtt = readFQTypeNameFromCurrent &r
      readToken &r
      let st = readFQTypeNameFromCurrent &r
      readToken &r
      expectStartArray &r
      let typeArgs = ResizeArray<FormatV0.ValueType.ValueType>()
      readToken &r
      while r.TokenType <> JsonTokenType.EndArray do
        typeArgs.Add(readValueTypeFromCurrent &r)
        readToken &r
      let fields = readDvalMap &r
      readToken &r
      expectEndArray &r
      FormatV0.DRecord(rtt, st, List.ofSeq typeArgs, fields)
    | "DEnum" ->
      readToken &r
      let rtt = readFQTypeNameFromCurrent &r
      readToken &r
      let st = readFQTypeNameFromCurrent &r
      readToken &r
      expectStartArray &r
      let typeArgs = ResizeArray<FormatV0.ValueType.ValueType>()
      readToken &r
      while r.TokenType <> JsonTokenType.EndArray do
        typeArgs.Add(readValueTypeFromCurrent &r)
        readToken &r
      let caseName = readString &r
      readToken &r
      expectStartArray &r
      let fields = ResizeArray<FormatV0.Dval>()
      readToken &r
      while r.TokenType <> JsonTokenType.EndArray do
        fields.Add(readDvalFromCurrent &r)
        readToken &r
      readToken &r
      expectEndArray &r
      FormatV0.DEnum(rtt, st, List.ofSeq typeArgs, caseName, List.ofSeq fields)
    | "DDB" ->
      let name = readString &r
      readToken &r
      expectEndArray &r
      FormatV0.DDB name
    | "DBlobPersistent" ->
      let hash = readString &r
      let lengthStr = readString &r
      readToken &r
      expectEndArray &r
      FormatV0.DBlobPersistent(hash, System.Int64.Parse lengthStr)
    | "DBlobEphemeral" ->
      let s = readString &r
      readToken &r
      expectEndArray &r
      FormatV0.DBlobEphemeral(System.Guid.Parse s)
    | t ->
      if isNullary then
        Exception.raiseInternal "Unknown nullary Dval tag" [ "tag", t ]
      else
        Exception.raiseInternal "Unknown Dval tag" [ "tag", t ]

  // -------- public entry points --------

  let serialize (dv : FormatV0.Dval) : string =
    use stream = new System.IO.MemoryStream()
    use writer = new Utf8JsonWriter(stream)
    writeDval writer dv
    writer.Flush()
    System.Text.Encoding.UTF8.GetString(stream.ToArray())

  let deserialize (json : string) : FormatV0.Dval =
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let mutable reader = Utf8JsonReader(System.ReadOnlySpan(bytes))
    readDval &reader

  let hashList (items : List<FormatV0.Dval>) : byte[] =
    use stream = new System.IO.MemoryStream()
    use writer = new Utf8JsonWriter(stream)
    writer.WriteStartArray()
    for d in items do
      writeDval writer d
    writer.WriteEndArray()
    writer.Flush()
    stream.ToArray()


let toJsonV0 (dv : RT.Dval) : string = dv |> FormatV0.fromRT |> HandJsonV0.serialize

let parseJsonV0 (json : string) : RT.Dval =
  json |> HandJsonV0.deserialize |> FormatV0.toRT

let toHashV2 (dvals : list<RT.Dval>) : string =
  dvals
  |> List.map FormatV0.fromRT
  |> HandJsonV0.hashList
  |> System.IO.Hashing.XxHash64.Hash // fastest in .NET, does not need to be secure
  |> Base64.urlEncodeToString



module Test =
  let rec isRoundtrippableDval (dval : RT.Dval) : bool =
    match dval with
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
    | RT.DUuid _
    | RT.DDateTime _
    | RT.DBlob _ -> true

    | RT.DStream _ -> false

    | RT.DTuple(v1, v2, rest) -> List.all isRoundtrippableDval (v1 :: v2 :: rest)

    | RT.DList(_, dvals) -> List.all isRoundtrippableDval dvals

    | RT.DDict(_, map) -> map |> Map.values |> List.all isRoundtrippableDval

    | RT.DRecord(_, _, _, map) -> map |> Map.values |> List.all isRoundtrippableDval

    | RT.DEnum(_typeName, _, _typeArgsDEnumTODO, _caseName, fields) ->
      List.all isRoundtrippableDval fields

    | RT.DApplicable _ -> false // not supported

    | RT.DDB _ -> true
