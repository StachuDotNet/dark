module LibPackageManager.JsonDeserialization

open System.Text.Json

open Types

type JsonParseError = | NotJson
exception JsonParseException of JsonParseError

type Json =
  | Null
  | Bool of bool
  | Number of double
  | String of string
  | Array of List<Json>
  | Object of List<string * Json>


type JsonPathPart =
  | Index of int
  | Field of string

/// A single 'root' is always implied
type JsonPath = List<JsonPathPart>



let private dotnetParsingOptions =
  new JsonDocumentOptions(
    CommentHandling = JsonCommentHandling.Skip,
    MaxDepth = System.Int32.MaxValue
  )

let parseJson (str : string) : Result<Json, JsonParseError> =
  let rec convert (j : JsonElement) : Json =
    match j.ValueKind with
    | JsonValueKind.Null -> Json.Null

    | JsonValueKind.True -> Json.Bool true
    | JsonValueKind.False -> Json.Bool false

    | JsonValueKind.Number -> j.GetDouble() |> Json.Number

    | JsonValueKind.String -> j.GetString() |> Json.String

    | JsonValueKind.Array ->
      j.EnumerateArray() |> Seq.map convert |> Seq.toList |> Json.Array

    | JsonValueKind.Object ->
      j.EnumerateObject()
      |> Seq.map (fun jp -> (jp.Name, convert jp.Value))
      |> Seq.toList
      |> Json.Object

    | _ -> raise (JsonParseException JsonParseError.NotJson)

  // .net does the hard work of actually parsing the JSON
  let parsedByDotNet =
    try
      Ok(JsonDocument.Parse(str, dotnetParsingOptions).RootElement)
    with _ex ->
      Error JsonParseError.NotJson

  match parsedByDotNet with
  | Error err -> Error err
  | Ok parsed ->
    try
      Ok(convert parsed)
    with JsonParseException ex ->
      Error ex


type DecodingContext =
  { path : JsonPath
    json : Json }

  /// Represents traversing into the json via some index
  ///
  /// i.e. `root[0]`
  member this.Index (index : int) (json : Json) =
    { path = Index index :: this.path; json = json }

  /// Represents traversing into the json via some field
  ///
  /// i.e. `root.fieldName`
  member this.Field (fieldName : string) (json : Json) =
    { path = Field fieldName :: this.path; json = json }


type JsonDecodeError = DecodingContext * string
type JsonDecodeResult<'T> = Result<'T, JsonDecodeError>

type JsonDecoder<'T> = DecodingContext -> JsonDecodeResult<'T>
exception JsonDecodeException of JsonDecodeError


module Decoders =
  let int32 : JsonDecoder<int32> =
    fun ctx ->
      match ctx.json with
      | Number n -> Ok(int32 n)
      | _ -> Error(ctx, "Expected int32")

  let uint64 : JsonDecoder<uint64> =
    fun ctx ->
      match ctx.json with
      | Number n -> Ok(uint64 n)
      | _ -> Error(ctx, "Expected uint64")

  let string : JsonDecoder<string> =
    fun ctx ->
      match ctx.json with
      | String s -> Ok s
      | _ -> Error(ctx, "Expected string")

  let guid : JsonDecoder<System.Guid> =
    fun ctx ->
      match ctx.json with
      | String s ->
        match System.Guid.TryParse(s) with
        | true, guid -> Ok guid
        | _ -> Error(ctx, "Expected guid")
      | _ -> Error(ctx, "Expected guid")


  let enum0Fields (ctor : 'T) (fields : List<Json>) : JsonDecoder<'T> =
    fun ctx ->
      match fields with
      | [] -> Ok ctor
      | _ -> Error(ctx, "Expected one field")

  let enum1Field
    (d1 : JsonDecoder<'T1>)
    (ctor : 'T1 -> 'T)
    (fields : List<Json>)
    : JsonDecoder<'T> =
    fun ctx ->
      match fields with
      | [ f1 ] -> d1 (ctx.Index 0 f1) |> Result.map ctor
      | _ -> Error(ctx, "Expected one field")

  let enum2Fields
    (d1 : JsonDecoder<'T1>)
    (d2 : JsonDecoder<'T2>)
    (ctor : 'T1 -> 'T2 -> 'T)
    (fields : List<Json>)
    : JsonDecoder<'T> =
    fun ctx ->
      match fields with
      | [ f1; f2 ] ->
        match d1 (ctx.Index 1 f1), d2 (ctx.Index 2 f2) with
        | Ok f1, Ok f2 -> Ok(ctor f1 f2)
        | Error err, _
        | _, Error err -> Error err
      | _ -> Error(ctx, "Expected two fields")

  let enum3Fields
    (d1 : JsonDecoder<'T1>)
    (d2 : JsonDecoder<'T2>)
    (d3 : JsonDecoder<'T3>)
    (ctor : 'T1 -> 'T2 -> 'T3 -> 'T)
    (fields : List<Json>)
    : JsonDecoder<'T> =
    fun ctx ->
      match fields with
      | [ f1; f2; f3 ] ->
        match d1 (ctx.Index 1 f1), d2 (ctx.Index 2 f2), d3 (ctx.Index 3 f3) with
        | Ok f1, Ok f2, Ok f3 -> Ok(ctor f1 f2 f3)
        | Error err, _, _
        | _, Error err, _
        | _, _, Error err -> Error err
      | _ -> Error(ctx, "Expected three fields")


  // this is an enum - NOT expecting null, but rather a DU setup
  let option (_innerDecoder : JsonDecoder<'T>) : JsonDecoder<'T option> =
    fun ctx -> Error(ctx, "TODO: option decoder")


  let list (innerDecoder : JsonDecoder<'T>) : JsonDecoder<List<'T>> =
    fun ctx ->
      match ctx.json with
      | Array items ->
        (List.foldWithIndex
          (fun i acc item ->
            match acc with
            | Error err -> Error err
            | Ok decodedItems ->
              innerDecoder (ctx.Index i item)
              |> Result.map (fun d -> d :: decodedItems))
          (Ok [])
          items)
        |> Result.map List.reverse

      | _ -> Error(ctx, "Expected a list")


  let decodeField (fields : List<string * Json>) (d : string * JsonDecoder<'T>) =
    fun (ctx : DecodingContext) ->
      let fieldName, decoder = d

      match fields |> List.find (fun (k, _v) -> k = fieldName) with
      | None -> Error(ctx, "missing field: " + fieldName)
      | Some(_, found) ->
        let fieldCtx = ctx.Field fieldName found
        match decoder fieldCtx with
        | Ok decoded -> Ok decoded
        | Error _ -> Error(fieldCtx, "field of wrong type")


  let obj1Field
    (name : string)
    (d1 : string * JsonDecoder<'T1>)
    (ctor : 'T1 -> 'T)
    : JsonDecoder<'T> =
    fun ctx ->
      match ctx.json with
      | Object fields ->
        match decodeField fields d1 ctx with
        | Ok f1 -> Ok(ctor f1)
        | Error err -> Error err

      | _ -> Error(ctx, sprintf "Expected %s to be an object" name)

  let obj2Fields
    (name : string)
    (d1 : string * JsonDecoder<'T1>)
    (d2 : string * JsonDecoder<'T2>)
    (ctor : 'T1 -> 'T2 -> 'T)
    : JsonDecoder<'T> =
    fun ctx ->
      match ctx.json with
      | Object fields ->
        match decodeField fields d1 ctx, decodeField fields d2 ctx with
        | Ok f1, Ok f2 -> Ok(ctor f1 f2)
        | Error err, _
        | _, Error err -> Error err

      | _ -> Error(ctx, sprintf "Expected %s to be an object" name)

  let obj3Fields
    (name : string)
    (d1 : string * JsonDecoder<'T1>)
    (d2 : string * JsonDecoder<'T2>)
    (d3 : string * JsonDecoder<'T3>)
    (ctor : 'T1 -> 'T2 -> 'T3 -> 'T)
    : JsonDecoder<'T> =
    fun ctx ->
      match ctx.json with
      | Object fields ->
        let f1 = decodeField fields d1 ctx
        let f2 = decodeField fields d2 ctx
        let f3 = decodeField fields d3 ctx

        match f1, f2, f3 with
        | Ok f1, Ok f2, Ok f3 -> Ok(ctor f1 f2 f3)
        | Error err, _, _
        | _, Error err, _
        | _, _, Error err -> Error err

      | _ -> Error(ctx, sprintf "Expected %s to be an object" name)


  let obj4Fields
    (name : string)
    (d1 : string * JsonDecoder<'T1>)
    (d2 : string * JsonDecoder<'T2>)
    (d3 : string * JsonDecoder<'T3>)
    (d4 : string * JsonDecoder<'T4>)
    (ctor : 'T1 -> 'T2 -> 'T3 -> 'T4 -> 'T)
    : JsonDecoder<'T> =
    fun ctx ->
      match ctx.json with
      | Object fields ->
        let f1 = decodeField fields d1 ctx
        let f2 = decodeField fields d2 ctx
        let f3 = decodeField fields d3 ctx
        let f4 = decodeField fields d4 ctx

        match f1, f2, f3, f4 with
        | Ok f1, Ok f2, Ok f3, Ok f4 -> Ok(ctor f1 f2 f3 f4)
        | Error err, _, _, _
        | _, Error err, _, _
        | _, _, Error err, _
        | _, _, _, Error err -> Error err

      | _ -> Error(ctx, sprintf "Expected %s to be an object" name)


  let obj5Fields
    (name : string)
    (d1 : string * JsonDecoder<'T1>)
    (d2 : string * JsonDecoder<'T2>)
    (d3 : string * JsonDecoder<'T3>)
    (d4 : string * JsonDecoder<'T4>)
    (d5 : string * JsonDecoder<'T5>)
    (ctor : 'T1 -> 'T2 -> 'T3 -> 'T4 -> 'T5 -> 'T)
    : JsonDecoder<'T> =
    fun ctx ->
      match ctx.json with
      | Object fields ->
        let f1 = decodeField fields d1 ctx
        let f2 = decodeField fields d2 ctx
        let f3 = decodeField fields d3 ctx
        let f4 = decodeField fields d4 ctx
        let f5 = decodeField fields d5 ctx

        match f1, f2, f3, f4, f5 with
        | Ok f1, Ok f2, Ok f3, Ok f4, Ok f5 -> Ok(ctor f1 f2 f3 f4 f5)
        | Error err, _, _, _, _
        | _, Error err, _, _, _
        | _, _, Error err, _, _
        | _, _, _, Error err, _
        | _, _, _, _, Error err -> Error err

      | _ -> Error(ctx, sprintf "Expected %s to be an object" name)


  let obj6Fields
    (name : string)
    (d1 : string * JsonDecoder<'T1>)
    (d2 : string * JsonDecoder<'T2>)
    (d3 : string * JsonDecoder<'T3>)
    (d4 : string * JsonDecoder<'T4>)
    (d5 : string * JsonDecoder<'T5>)
    (d6 : string * JsonDecoder<'T6>)
    (ctor : 'T1 -> 'T2 -> 'T3 -> 'T4 -> 'T5 -> 'T6 -> 'T)
    : JsonDecoder<'T> =
    fun ctx ->
      match ctx.json with
      | Object fields ->
        let f1 = decodeField fields d1 ctx
        let f2 = decodeField fields d2 ctx
        let f3 = decodeField fields d3 ctx
        let f4 = decodeField fields d4 ctx
        let f5 = decodeField fields d5 ctx
        let f6 = decodeField fields d6 ctx

        match f1, f2, f3, f4, f5, f6 with
        | Ok f1, Ok f2, Ok f3, Ok f4, Ok f5, Ok f6 -> Ok(ctor f1 f2 f3 f4 f5 f6)
        | Error err, _, _, _, _, _
        | _, Error err, _, _, _, _
        | _, _, Error err, _, _, _
        | _, _, _, Error err, _, _
        | _, _, _, _, Error err, _
        | _, _, _, _, _, Error err -> Error err

      | _ -> Error(ctx, sprintf "Expected %s to be an object" name)





module ID =
  let decoder : JsonDecoder<ID> = Decoders.uint64

module TLID =
  let decoder : JsonDecoder<TLID> = Decoders.uint64



module Sign =
  let decoder : JsonDecoder<Sign> =
    fun ctx ->
      match ctx.json with
      | Object [ (caseName, Array fields) ] ->
        match caseName with
        | "Positive" -> Decoders.enum0Fields Sign.Positive fields ctx
        | "Negative" -> Decoders.enum0Fields Sign.Negative fields ctx
        | _ -> Error(ctx, sprintf "Unknown Sign case: %s" caseName)
      | _ -> Error(ctx, "Sign enum should be an object with 1 key")


module NameResolutionError =
  module ErrorType =
    type DU = NameResolutionError.ErrorType
    let decoder : JsonDecoder<DU> =
      fun ctx ->
        match ctx.json with
        | Object [ (caseName, Array fields) ] ->
          match caseName with
          | "NotFound" -> Decoders.enum0Fields DU.NotFound fields ctx
          | "ExpectedEnumButNot" ->
            Decoders.enum0Fields DU.ExpectedEnumButNot fields ctx
          | "ExpectedRecordButNot" ->
            Decoders.enum0Fields DU.ExpectedRecordButNot fields ctx
          | "MissingEnumModuleName" ->
            Decoders.enum1Field
              Decoders.string
              (fun name -> DU.MissingEnumModuleName name)
              fields
              ctx
          | "InvalidPackageName" ->
            Decoders.enum0Fields DU.InvalidPackageName fields ctx
          | _ -> Error(ctx, sprintf "Unknown ErrorType case: %s" caseName)
        | _ -> Error(ctx, "ErrorType enum should be an object with 1 key")

  module NameType =
    type DU = NameResolutionError.NameType

    let decoder : JsonDecoder<DU> =
      fun ctx ->
        match ctx.json with
        | Object [ (caseName, Array fields) ] ->
          match caseName with
          | "Type" -> Decoders.enum0Fields DU.Type fields ctx
          | "Constant" -> Decoders.enum0Fields DU.Constant fields ctx
          | "Function" -> Decoders.enum0Fields DU.Function fields ctx
          | _ -> Error(ctx, sprintf "Unknown NameType case: %s" caseName)
        | _ -> Error(ctx, "NameType enum should be an object with 1 key")


  module Error =
    type Rec = NameResolutionError.Error

    let decoder : JsonDecoder<Rec> =
      Decoders.obj3Fields
        "NameResolution"
        ("errorType", ErrorType.decoder)
        ("nameType", NameType.decoder)
        ("names", Decoders.list Decoders.string)
        (fun errType nameType names ->
          { errorType = errType; nameType = nameType; names = names })


module ProgramTypes =
  // type NameResolution<'a> = Result<'a, NameResolutionError.Error>

  module NameResolution =
    type DU<'a> = ProgramTypes.NameResolution<'a>

    let decoder (inner : JsonDecoder<'TInner>) : JsonDecoder<DU<'TInner>> =
      fun ctx ->
        match ctx.json with
        | Object [ (caseName, Array fields) ] ->
          match caseName with
          | "Ok" -> Decoders.enum1Field inner (fun name -> Ok name) fields ctx
          | "Error" ->
            Decoders.enum1Field
              NameResolutionError.Error.decoder
              (fun err -> Error err)
              fields
              ctx
          | _ -> Error(ctx, sprintf "Unknown NameResolution case: %s" caseName)
        | _ -> Error(ctx, "NameResolution enum should be an object with 1 key")


  module FQTypeName =
    module Package =
      let decoder : JsonDecoder<ProgramTypes.FQTypeName.Package> =
        Decoders.obj4Fields
          "FQTypeName.Package"
          ("owner", Decoders.string)
          ("modules", Decoders.list Decoders.string)
          ("name", Decoders.string)
          ("version", Decoders.int32)
          (fun owner modules name version ->
            { owner = owner; modules = modules; name = name; version = version })

    module FQTypeName =
      type DU = ProgramTypes.FQTypeName.FQTypeName

      let decoder : JsonDecoder<DU> =
        fun ctx ->
          match ctx.json with
          | Object [ (caseName, Array fields) ] ->
            match caseName with
            | "Package" -> Decoders.enum1Field Package.decoder DU.Package fields ctx
            | _ -> Error(ctx, sprintf "Unknown FQTypeName case: %s" caseName)
          | _ -> Error(ctx, "NameType enum should be an object with 1 key")


  //   module FQFnName =
  //     type Builtin = { name : string; version : int }
  //     type Package =
  //       { owner : string; modules : List<string>; name : string; version : int }

  //     type FQFnName =
  //       | Builtin of Builtin
  //       | Package of Package


  //   module FQConstantName =
  //     type Builtin = { name : string; version : int }
  //     type Package =
  //       { owner : string; modules : List<string>; name : string; version : int }

  //     type FQConstantName =
  //       | Builtin of Builtin
  //       | Package of Package


  //   type TypeReference =
  //     | TList of TypeReference
  //     | TTuple of TypeReference * TypeReference * List<TypeReference>
  //     | TDict of TypeReference
  //     | TCustomType of
  //       NameResolution<FQTypeName.FQTypeName> *
  //       typeArgs : List<TypeReference>
  //     | TDB of TypeReference
  //     | TFn of NEList<TypeReference> * TypeReference

  module TypeReference =
    type DU = ProgramTypes.TypeReference

    let rec decoder : JsonDecoder<DU> =
      fun ctx ->
        match ctx.json with
        | Object [ (caseName, Array fields) ] ->
          match caseName with
          | "TVariable" ->
            Decoders.enum1Field
              Decoders.string
              (fun name -> DU.TVariable name)
              fields
              ctx
          | "TUnit" -> Decoders.enum0Fields DU.TUnit fields ctx
          | "TBool" -> Decoders.enum0Fields DU.TBool fields ctx
          | "TInt64" -> Decoders.enum0Fields DU.TInt64 fields ctx
          | "TUInt64" -> Decoders.enum0Fields DU.TUInt64 fields ctx
          | "TInt8" -> Decoders.enum0Fields DU.TInt8 fields ctx
          | "TUInt8" -> Decoders.enum0Fields DU.TUInt8 fields ctx
          | "TInt16" -> Decoders.enum0Fields DU.TInt16 fields ctx
          | "TUInt16" -> Decoders.enum0Fields DU.TUInt16 fields ctx
          | "TInt32" -> Decoders.enum0Fields DU.TInt32 fields ctx
          | "TUInt32" -> Decoders.enum0Fields DU.TUInt32 fields ctx
          | "TInt128" -> Decoders.enum0Fields DU.TInt128 fields ctx
          | "TUInt128" -> Decoders.enum0Fields DU.TUInt128 fields ctx
          | "TFloat" -> Decoders.enum0Fields DU.TFloat fields ctx
          | "TChar" -> Decoders.enum0Fields DU.TChar fields ctx
          | "TString" -> Decoders.enum0Fields DU.TString fields ctx
          | "TDateTime" -> Decoders.enum0Fields DU.TDateTime fields ctx
          | "TUuid" -> Decoders.enum0Fields DU.TUuid fields ctx
          | "TList" ->
            Decoders.enum1Field decoder (fun typ -> DU.TList typ) fields ctx
          | "TTuple" ->
            Decoders.enum3Fields
              decoder
              decoder
              (Decoders.list decoder)
              (fun first second rest -> DU.TTuple(first, second, rest))
              fields
              ctx
          | "TDict" ->
            Decoders.enum1Field decoder (fun typ -> DU.TDict typ) fields ctx
          | "TCustomType" ->
            Decoders.enum2Fields
              (NameResolution.decoder FQTypeName.FQTypeName.decoder)
              (Decoders.list decoder)
              (fun name typeArgs -> DU.TCustomType(name, typeArgs))
              fields
              ctx
          | "TDB" -> Decoders.enum1Field decoder (fun typ -> DU.TDB typ) fields ctx
          | "TFn" ->
            Decoders.enum2Fields
              (Decoders.list decoder)
              decoder
              (fun typeArgs returnType ->
                DU.TFn(
                  NEList.ofListUnsafe
                    "TODO: PT.TypeRef.TFn should have an NEList in Dark"
                    []
                    typeArgs,
                  returnType
                ))
              fields
              ctx
          | _ -> Error(ctx, sprintf "Unknown TypeReference case: %s" caseName)
        | _ -> Error(ctx, "TypeReference enum should be an object with 1 key")

  //   type LetPattern =
  //     | LPVariable of ID * name : string
  //     | LPTuple of ID * LetPattern * LetPattern * List<LetPattern>

  //   type MatchPattern =
  //     | MPVariable of ID * string
  //     | MPUnit of ID
  //     | MPBool of ID * bool
  //     | MPInt64 of ID * int64
  //     | MPUInt64 of ID * uint64
  //     | MPInt8 of ID * int8
  //     | MPUInt8 of ID * uint8
  //     | MPInt16 of ID * int16
  //     | MPUInt16 of ID * uint16
  //     | MPInt32 of ID * int32
  //     | MPUInt32 of ID * uint32
  //     | MPInt128 of ID * System.Int128
  //     | MPUInt128 of ID * System.UInt128
  //     | MPFloat of ID * Sign * string * string
  //     | MPChar of ID * string
  //     | MPString of ID * string
  //     | MPList of ID * List<MatchPattern>
  //     | MPListCons of ID * head : MatchPattern * tail : MatchPattern
  //     | MPTuple of ID * MatchPattern * MatchPattern * List<MatchPattern>
  //     | MPEnum of ID * caseName : string * fieldPats : List<MatchPattern>

  //   type BinaryOperation =
  //     | BinOpAnd
  //     | BinOpOr

  //   type InfixFnName =
  //     | ArithmeticPlus
  //     | ArithmeticMinus
  //     | ArithmeticMultiply
  //     | ArithmeticDivide
  //     | ArithmeticModulo
  //     | ArithmeticPower
  //     | ComparisonGreaterThan
  //     | ComparisonGreaterThanOrEqual
  //     | ComparisonLessThan
  //     | ComparisonLessThanOrEqual
  //     | ComparisonEquals
  //     | ComparisonNotEquals
  //     | StringConcat

  //   type Infix =
  //     | InfixFnCall of InfixFnName
  //     | BinOp of BinaryOperation

  //   type StringSegment =
  //     | StringText of string
  //     | StringInterpolation of Expr

  //   and PipeExpr =
  //     | EPipeVariable of ID * string * List<Expr>
  //     | EPipeLambda of ID * pats : NEList<LetPattern> * body : Expr
  //     | EPipeInfix of ID * Infix * Expr
  //     | EPipeFnCall of
  //       ID *
  //       NameResolution<FQFnName.FQFnName> *
  //       typeArgs : List<TypeReference> *
  //       args : List<Expr>
  //     | EPipeEnum of
  //       ID *
  //       typeName : NameResolution<FQTypeName.FQTypeName> *
  //       caseName : string *
  //       fields : List<Expr>


  //   and Expr =
  //     | EUnit of ID

  //     | EBool of ID * bool
  //     | EInt64 of ID * int64
  //     | EUInt64 of ID * uint64
  //     | EInt8 of ID * int8
  //     | EUInt8 of ID * uint8
  //     | EInt16 of ID * int16
  //     | EUInt16 of ID * uint16
  //     | EInt32 of ID * int32
  //     | EUInt32 of ID * uint32
  //     | EInt128 of ID * System.Int128
  //     | EUInt128 of ID * System.UInt128
  //     | EFloat of ID * Sign * string * string
  //     | EChar of ID * string
  //     | EString of ID * List<StringSegment>

  //     | EConstant of ID * NameResolution<FQConstantName.FQConstantName>

  //     | EList of ID * List<Expr>
  //     | EDict of ID * List<string * Expr>
  //     | ETuple of ID * Expr * Expr * List<Expr>
  //     | ERecord of ID * NameResolution<FQTypeName.FQTypeName> * List<string * Expr>
  //     | EEnum of
  //       ID *
  //       typeName : NameResolution<FQTypeName.FQTypeName> *
  //       caseName : string *
  //       fields : List<Expr>

  //     | ELet of ID * LetPattern * Expr * Expr
  //     | EFieldAccess of ID * Expr * string
  //     | EVariable of ID * string

  //     | EIf of ID * cond : Expr * thenExpr : Expr * elseExpr : Option<Expr>
  //     | EMatch of ID * arg : Expr * cases : List<MatchCase>
  //     | EPipe of ID * Expr * List<PipeExpr>

  //     | EInfix of ID * Infix * Expr * Expr
  //     | ELambda of ID * pats : NEList<LetPattern> * body : Expr
  //     | EApply of ID * Expr * typeArgs : List<TypeReference> * args : NEList<Expr>
  //     | EFnName of ID * NameResolution<FQFnName.FQFnName>
  //     | ERecordUpdate of ID * record : Expr * updates : NEList<string * Expr>

  //   and MatchCase = { pat : MatchPattern; whenCondition : Option<Expr>; rhs : Expr }


  module Deprecation =
    let decoder
      (nameDecoder : JsonDecoder<'name>)
      : JsonDecoder<ProgramTypes.Deprecation<'name>> =
      fun ctx ->
        match ctx.json with
        | Object [ (caseName, Array fields) ] ->
          match caseName with
          | "NotDeprecated" ->
            Decoders.enum0Fields ProgramTypes.Deprecation.NotDeprecated fields ctx
          | "RenamedTo" ->
            Decoders.enum1Field
              nameDecoder
              (fun name -> ProgramTypes.Deprecation.RenamedTo name)
              fields
              ctx
          | "ReplacedBy" ->
            Decoders.enum1Field
              nameDecoder
              (fun name -> ProgramTypes.Deprecation.ReplacedBy name)
              fields
              ctx
          | "DeprecatedBecause" ->
            Decoders.enum1Field
              Decoders.string
              (fun reason -> ProgramTypes.Deprecation.DeprecatedBecause reason)
              fields
              ctx
          | _ -> Error(ctx, sprintf "Unknown Deprecation case: %s" caseName)
        | _ -> Error(ctx, "Deprecation enum should be an object with 1 key")


  module TypeDeclaration =
    module RecordField =
      type Rec = ProgramTypes.TypeDeclaration.RecordField

      let decoder : JsonDecoder<Rec> =
        Decoders.obj3Fields
          "TypeDeclaration.RecordField"
          ("name", Decoders.string)
          ("typ", TypeReference.decoder)
          ("description", Decoders.string)
          (fun name typ description ->
            { name = name; typ = typ; description = description })


    module EnumField =
      type Rec = ProgramTypes.TypeDeclaration.EnumField

      let decoder : JsonDecoder<Rec> =
        Decoders.obj3Fields
          "TypeDeclaration.EnumField"
          ("typ", TypeReference.decoder)
          ("label", Decoders.option Decoders.string)
          ("description", Decoders.string)
          (fun typ label description ->
            { typ = typ; label = label; description = description })

    module EnumCase =
      type Rec = ProgramTypes.TypeDeclaration.EnumCase

      let decoder : JsonDecoder<Rec> =
        Decoders.obj3Fields
          "TypeDeclaration.EnumCase"
          ("name", Decoders.string)
          ("fields", Decoders.list EnumField.decoder)
          ("description", Decoders.string)
          (fun name fields description ->
            { name = name; fields = fields; description = description })


    module Definition =
      type DU = ProgramTypes.TypeDeclaration.Definition

      let decoder : JsonDecoder<DU> =
        fun ctx ->
          match ctx.json with
          | Object [ (caseName, Array fields) ] ->
            match caseName with
            | "Alias" ->
              Decoders.enum1Field
                TypeReference.decoder
                (fun typ -> DU.Alias typ)
                fields
                ctx
            | "Record" ->
              Decoders.enum1Field
                (Decoders.list RecordField.decoder)
                (fun fields ->
                  fields
                  |> NEList.ofListUnsafe
                    "TODO: PT.Definition.Record should have an NEList in Dark"
                    []
                  |> DU.Record)
                fields
                ctx
            | "Enum" ->
              Decoders.enum1Field
                (Decoders.list EnumCase.decoder)
                (fun cases ->
                  cases
                  |> NEList.ofListUnsafe
                    "TODO: PT.Definition.Enum should have an NEList in Dark"
                    []
                  |> DU.Enum)
                fields
                ctx
            | _ -> Error(ctx, sprintf "Unknown Definition case: %s" caseName)
          | _ -> Error(ctx, "Definition enum should be an object with 1 key")


    // type TypeDeclaration = { typeParams : List<string>; definition : Definition }
    module TypeDeclaration =
      type Rec = ProgramTypes.TypeDeclaration.TypeDeclaration

      let decoder : JsonDecoder<Rec> =
        Decoders.obj2Fields
          "TypeDeclaration.TypeDeclaration"
          ("typeParams", Decoders.list Decoders.string)
          ("definition", Definition.decoder)
          (fun typeParams definition ->
            { typeParams = typeParams; definition = definition })


  //   type PackageType =
  //     { tlid : TLID
  //       id : System.Guid
  //       name : FQTypeName.Package
  //       declaration : TypeDeclaration.TypeDeclaration
  //       description : string
  //       deprecated : Deprecation<FQTypeName.FQTypeName> }
  module PackageType =
    type Rec = ProgramTypes.PackageType

    let decoder : JsonDecoder<Rec> =
      Decoders.obj6Fields
        "PackageType"
        ("tlid", TLID.decoder)
        ("id", Decoders.guid)
        ("name", FQTypeName.Package.decoder)
        ("declaration", TypeDeclaration.TypeDeclaration.decoder)
        ("description", Decoders.string)
        ("deprecated", Deprecation.decoder FQTypeName.FQTypeName.decoder)
        (fun tlid id name declaration description deprecated ->
          { tlid = tlid
            id = id
            name = name
            declaration = declaration
            description = description
            deprecated = deprecated })




//   module PackageFn =
//     type Parameter = { name : string; typ : TypeReference; description : string }

//     type PackageFn =
//       { tlid : TLID
//         id : System.Guid
//         name : FQFnName.Package
//         body : Expr
//         typeParams : List<string>
//         parameters : NEList<Parameter>
//         returnType : TypeReference
//         description : string
//         deprecated : Deprecation<FQFnName.FQFnName> }

//   type Const =
//     | CInt64 of int64
//     | CUInt64 of uint64
//     | CInt8 of int8
//     | CUInt8 of uint8
//     | CInt16 of int16
//     | CUInt16 of uint16
//     | CInt32 of int32
//     | CUInt32 of uint32
//     | CInt128 of System.Int128
//     | CUInt128 of System.UInt128
//     | CBool of bool
//     | CString of string
//     | CChar of string
//     | CFloat of Sign * string * string
//     | CUnit
//     | CTuple of first : Const * second : Const * rest : List<Const>
//     | CEnum of
//       NameResolution<FQTypeName.FQTypeName> *
//       caseName : string *
//       List<Const>
//     | CList of List<Const>
//     | CDict of List<string * Const>


//   type PackageConstant =
//     { tlid : TLID
//       id : System.Guid
//       name : FQConstantName.Package
//       description : string
//       deprecated : Deprecation<FQConstantName.FQConstantName>
//       body : Const }

// For the sake of conversation,
// "deserialize" is when you parse and then decode

type JsonDeserializationError =
  | ParseError of JsonParseError
  | DecodeError of JsonDecodeError

let deserialize<'T>
  (decoder : JsonDecoder<'T>)
  (json : string)
  : Result<'T, JsonDeserializationError> =
  match parseJson json with
  | Error err -> Error(ParseError err)
  | Ok parsed ->
    match decoder { path = []; json = parsed } with
    | Ok decoded -> Ok decoded
    | Error err -> Error(DecodeError err)
