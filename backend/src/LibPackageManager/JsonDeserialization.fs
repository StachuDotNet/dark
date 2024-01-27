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



let dotnetParsingOptions =
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
    // type Error = { errorType : ErrorType; nameType : NameType; names : List<string> }
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
  // //   type NameResolution<'a> = Result<'a, NameResolutionError.Error>

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


// //   module FQFnName =
// //     type Builtin = { name : string; version : int }
// //     type Package =
// //       { owner : string; modules : List<string>; name : string; version : int }

// //     type FQFnName =
// //       | Builtin of Builtin
// //       | Package of Package


// //   module FQConstantName =
// //     type Builtin = { name : string; version : int }
// //     type Package =
// //       { owner : string; modules : List<string>; name : string; version : int }

// //     type FQConstantName =
// //       | Builtin of Builtin
// //       | Package of Package


// //   type TypeReference =
// //     | TVariable of string
// //     | TUnit
// //     | TBool
// //     | TInt64
// //     | TUInt64
// //     | TInt8
// //     | TUInt8
// //     | TInt16
// //     | TUInt16
// //     | TInt32
// //     | TUInt32
// //     | TInt128
// //     | TUInt128
// //     | TFloat
// //     | TChar
// //     | TString
// //     | TDateTime
// //     | TUuid
// //     | TList of TypeReference
// //     | TTuple of TypeReference * TypeReference * List<TypeReference>
// //     | TDict of TypeReference
// //     | TCustomType of
// //       NameResolution<FQTypeName.FQTypeName> *
// //       typeArgs : List<TypeReference>
// //     | TDB of TypeReference
// //     | TFn of NEList<TypeReference> * TypeReference

// //   type LetPattern =
// //     | LPVariable of ID * name : string
// //     | LPTuple of ID * LetPattern * LetPattern * List<LetPattern>

// //   type MatchPattern =
// //     | MPVariable of ID * string
// //     | MPUnit of ID
// //     | MPBool of ID * bool
// //     | MPInt64 of ID * int64
// //     | MPUInt64 of ID * uint64
// //     | MPInt8 of ID * int8
// //     | MPUInt8 of ID * uint8
// //     | MPInt16 of ID * int16
// //     | MPUInt16 of ID * uint16
// //     | MPInt32 of ID * int32
// //     | MPUInt32 of ID * uint32
// //     | MPInt128 of ID * System.Int128
// //     | MPUInt128 of ID * System.UInt128
// //     | MPFloat of ID * Sign * string * string
// //     | MPChar of ID * string
// //     | MPString of ID * string
// //     | MPList of ID * List<MatchPattern>
// //     | MPListCons of ID * head : MatchPattern * tail : MatchPattern
// //     | MPTuple of ID * MatchPattern * MatchPattern * List<MatchPattern>
// //     | MPEnum of ID * caseName : string * fieldPats : List<MatchPattern>

// //   type BinaryOperation =
// //     | BinOpAnd
// //     | BinOpOr

// //   type InfixFnName =
// //     | ArithmeticPlus
// //     | ArithmeticMinus
// //     | ArithmeticMultiply
// //     | ArithmeticDivide
// //     | ArithmeticModulo
// //     | ArithmeticPower
// //     | ComparisonGreaterThan
// //     | ComparisonGreaterThanOrEqual
// //     | ComparisonLessThan
// //     | ComparisonLessThanOrEqual
// //     | ComparisonEquals
// //     | ComparisonNotEquals
// //     | StringConcat

// //   type Infix =
// //     | InfixFnCall of InfixFnName
// //     | BinOp of BinaryOperation

// //   type StringSegment =
// //     | StringText of string
// //     | StringInterpolation of Expr

// //   and PipeExpr =
// //     | EPipeVariable of ID * string * List<Expr>
// //     | EPipeLambda of ID * pats : NEList<LetPattern> * body : Expr
// //     | EPipeInfix of ID * Infix * Expr
// //     | EPipeFnCall of
// //       ID *
// //       NameResolution<FQFnName.FQFnName> *
// //       typeArgs : List<TypeReference> *
// //       args : List<Expr>
// //     | EPipeEnum of
// //       ID *
// //       typeName : NameResolution<FQTypeName.FQTypeName> *
// //       caseName : string *
// //       fields : List<Expr>


// //   and Expr =
// //     | EUnit of ID

// //     | EBool of ID * bool
// //     | EInt64 of ID * int64
// //     | EUInt64 of ID * uint64
// //     | EInt8 of ID * int8
// //     | EUInt8 of ID * uint8
// //     | EInt16 of ID * int16
// //     | EUInt16 of ID * uint16
// //     | EInt32 of ID * int32
// //     | EUInt32 of ID * uint32
// //     | EInt128 of ID * System.Int128
// //     | EUInt128 of ID * System.UInt128
// //     | EFloat of ID * Sign * string * string
// //     | EChar of ID * string
// //     | EString of ID * List<StringSegment>

// //     | EConstant of ID * NameResolution<FQConstantName.FQConstantName>

// //     | EList of ID * List<Expr>
// //     | EDict of ID * List<string * Expr>
// //     | ETuple of ID * Expr * Expr * List<Expr>
// //     | ERecord of ID * NameResolution<FQTypeName.FQTypeName> * List<string * Expr>
// //     | EEnum of
// //       ID *
// //       typeName : NameResolution<FQTypeName.FQTypeName> *
// //       caseName : string *
// //       fields : List<Expr>

// //     | ELet of ID * LetPattern * Expr * Expr
// //     | EFieldAccess of ID * Expr * string
// //     | EVariable of ID * string

// //     | EIf of ID * cond : Expr * thenExpr : Expr * elseExpr : Option<Expr>
// //     | EMatch of ID * arg : Expr * cases : List<MatchCase>
// //     | EPipe of ID * Expr * List<PipeExpr>

// //     | EInfix of ID * Infix * Expr * Expr
// //     | ELambda of ID * pats : NEList<LetPattern> * body : Expr
// //     | EApply of ID * Expr * typeArgs : List<TypeReference> * args : NEList<Expr>
// //     | EFnName of ID * NameResolution<FQFnName.FQFnName>
// //     | ERecordUpdate of ID * record : Expr * updates : NEList<string * Expr>

// //   and MatchCase = { pat : MatchPattern; whenCondition : Option<Expr>; rhs : Expr }


// //   type Deprecation<'name> =
// //     | NotDeprecated
// //     | RenamedTo of 'name
// //     | ReplacedBy of 'name
// //     | DeprecatedBecause of string


// //   module TypeDeclaration =
// //     type RecordField = { name : string; typ : TypeReference; description : string }

// //     type EnumField =
// //       { typ : TypeReference; label : Option<string>; description : string }

// //     type EnumCase = { name : string; fields : List<EnumField>; description : string }

// //     type Definition =
// //       | Alias of TypeReference
// //       | Record of NEList<RecordField>
// //       | Enum of NEList<EnumCase>

// //     type TypeDeclaration = { typeParams : List<string>; definition : Definition }


// //   type PackageType =
// //     { tlid : TLID
// //       id : System.Guid
// //       name : FQTypeName.Package
// //       declaration : TypeDeclaration.TypeDeclaration
// //       description : string
// //       deprecated : Deprecation<FQTypeName.FQTypeName> }


// //   module PackageFn =
// //     type Parameter = { name : string; typ : TypeReference; description : string }

// //     type PackageFn =
// //       { tlid : TLID
// //         id : System.Guid
// //         name : FQFnName.Package
// //         body : Expr
// //         typeParams : List<string>
// //         parameters : NEList<Parameter>
// //         returnType : TypeReference
// //         description : string
// //         deprecated : Deprecation<FQFnName.FQFnName> }

// //   type Const =
// //     | CInt64 of int64
// //     | CUInt64 of uint64
// //     | CInt8 of int8
// //     | CUInt8 of uint8
// //     | CInt16 of int16
// //     | CUInt16 of uint16
// //     | CInt32 of int32
// //     | CUInt32 of uint32
// //     | CInt128 of System.Int128
// //     | CUInt128 of System.UInt128
// //     | CBool of bool
// //     | CString of string
// //     | CChar of string
// //     | CFloat of Sign * string * string
// //     | CUnit
// //     | CTuple of first : Const * second : Const * rest : List<Const>
// //     | CEnum of
// //       NameResolution<FQTypeName.FQTypeName> *
// //       caseName : string *
// //       List<Const>
// //     | CList of List<Const>
// //     | CDict of List<string * Const>


// //   type PackageConstant =
// //     { tlid : TLID
// //       id : System.Guid
// //       name : FQConstantName.Package
// //       description : string
// //       deprecated : Deprecation<FQConstantName.FQConstantName>
// //       body : Const }
