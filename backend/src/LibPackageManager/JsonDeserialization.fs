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



type JsonDecodeContext =
  {
    json : Json

    /// Where are we in traversing the JSON?
    /// i.e. `root[1].name`?
    path : JsonPath
  }

type JsonDecodeError = JsonDecodeContext * string

type JsonDecoder<'T> = JsonDecodeContext -> Result<'T, JsonDecodeError>

exception JsonDecodeException of JsonDecodeError

module Decoder =
  let uint64 : JsonDecoder<uint64> =
    fun (ctx : JsonDecodeContext) ->
      match ctx.json with
      | Number n -> Ok(uint64 n)
      | _ -> Error(ctx, "not a uint64")

  let string : JsonDecoder<string> =
    fun (ctx : JsonDecodeContext) ->
      match ctx.json with
      | String s -> Ok s
      | _ -> Error(ctx, "not a string")


  let list (innerDecoder : JsonDecoder<'T>) : JsonDecoder<List<'T>> =
    fun (ctx : JsonDecodeContext) ->
      match ctx.json with
      | Array items ->
        let items =
          items
          |> List.mapi (fun i item ->
            { json = item; path = (JsonPathPart.Index i) :: ctx.path })

        (List.fold
          (fun acc encodedItem ->
            match acc with
            | Error err -> Error err
            | Ok decodedItems ->
              match innerDecoder encodedItem with
              | Ok decodedItem -> Ok(decodedItem :: decodedItems)
              | Error err -> Error err)
          (Ok [])
          items)
        |> fun r ->
          match r with
          | Error err -> Error err
          | Ok items -> Ok(List.reverse items)

      | _ -> Error(ctx, "not a list")


// let field (fieldName: string) (inner: JsonDecoder<'T>) : JsonDecoder<'T> =
//   fun (ctx : JsonDecodeContext) ->
//     match ctx.json with
//     | Object fields ->
//       match fields |> List.find(fun (k, v) -> k = fieldName)  with
//       | None -> Error (ctx, "missing field: " + fieldName)
//       | Some (_, found) ->
//         let fieldCtx = {path = (JsonPathPart.Field fieldName) :: ctx.path; json = found}
//         match inner fieldCtx with
//         | Ok decoded -> Ok decoded
//         | Error _ -> Error (fieldCtx, "field of wrong type")
//     | _ -> Error (ctx, "not an object")

/// Returns the first successful result of a few decoders, if any
///
/// Useful for decoding DUs and Enums
let anyOf<'T> (decoders : List<JsonDecoder<'T>>) : JsonDecoder<'T> =
  fun (ctx : JsonDecodeContext) ->
    List.fold
      (fun acc decoder ->
        match acc with
        | Ok acc -> Ok acc
        | Error err ->
          match decoder ctx with
          | Ok decoded -> Ok decoded
          | Error _newErr -> Error err)
      (Error (ctx, "no rules pass"))
      decoders



module ID =
  let decoder : JsonDecoder<ID> = Decoder.uint64

module TLID =
  let decoder : JsonDecoder<TLID> = Decoder.uint64



module Sign =
  let decoder : JsonDecoder<Sign> =
    fun (ctx : JsonDecodeContext) ->
      match ctx.json with
      | Object [ "Positive", Array [] ] -> Ok Sign.Positive
      | Object [ "Negative", Array [] ] -> Ok Sign.Negative
      | _ -> Error (ctx, "No matching case rule found")

  let decoderAlt : JsonDecoder<Sign> =
    Decoder.anyOf
      [
        Decoder.enumCaseNoFields
          "Positive"
          (fun ctx fields ->
            match fields with
            | [] -> Ok Sign.Positive
            | _ -> Error )

        Decoder.enumCaseNoFields
          "Negative"
          (fun ctx fields -> Ok Sign.Negative)
      ]

// match ctx.json with
// | Object [ "Positive", Array [] ] -> Ok Sign.Positive
// | Object [ "Negative", Array [] ] -> Ok Sign.Negative
// | _ -> Error ctx

// Decoder.anyOf
//   [ Decode.enumCasefun (ctx : JsonDecodeContext) ->
//     match ctx.json with
//     | Obj  ctx]


// |> Decode.map (function
//     | "Positive" -> Ok Sign.Positive
//     | "Negative" -> Ok Sign.Negative
//     | other -> failwithf "Unexpected value for 'Sign': %s" other)
//   )


module NameResolutionError =
  module ErrorType =
    let decoder : JsonDecoder<NameResolutionError.ErrorType> =
      fun (ctx : JsonDecodeContext) ->
        match ctx.json with
        | Object [ "NotFound", Array [] ] ->
          Ok NameResolutionError.ErrorType.NotFound

        | Object [ "ExpectedEnumButNot", Array [] ] ->
          Ok NameResolutionError.ErrorType.ExpectedEnumButNot

        | Object [ "ExpectedRecordButNot", Array [] ] ->
          Ok NameResolutionError.ErrorType.ExpectedRecordButNot

        | Object [ "MissingEnumModuleName", Array [ String caseName ] ] ->
          Ok(NameResolutionError.ErrorType.MissingEnumModuleName caseName)

        | Object [ "InvalidPackageName", Array [] ] ->
          Ok NameResolutionError.ErrorType.InvalidPackageName

        | _ -> Error (ctx, "No matching case")


  module NameType =
    let decoder : JsonDecoder<NameResolutionError.NameType> =
      fun (ctx : JsonDecodeContext) ->
        match ctx.json with
        | Object [ "Type", Array [] ] ->
          Ok NameResolutionError.NameType.Type
        | Object [ "Constant", Array [] ] ->
          Ok NameResolutionError.NameType.Constant
        | Object [ "Function", Array [] ] ->
          Ok NameResolutionError.NameType.Function
        | _ -> Error (ctx, "No matching case")


//   //   type Error = { errorType : ErrorType; nameType : NameType; names : List<string> }
//   module Error =
//     let decoder: JsonDecoder<NameResolutionError.Error> =
//       fun (ctx: JsonDecodeContext) ->
//         let errorType = Decoder.field "errorType" ErrorType.decoder ctx
//         let nameType =  Decoder.field "nameType"  NameType.decoder ctx
//         let names = Decoder.field "names" (Decoder.list Decoder.string) ctx

//         match errorType, nameType, names with
//         | Ok errorType, Ok nameType, Ok names ->
//           Ok { errorType = errorType; nameType = nameType; names = names}

//         | _ -> Error (ctx, "")



// module ProgramTypes =
//   type NameResolution<'a> = Result<'a, NameResolutionError.Error>

//   module FQTypeName =
//     type Package =
//       { owner : string; modules : List<string>; name : string; version : int }

//     type FQTypeName = Package of Package


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
//     | TVariable of string
//     | TUnit
//     | TBool
//     | TInt64
//     | TUInt64
//     | TInt8
//     | TUInt8
//     | TInt16
//     | TUInt16
//     | TInt32
//     | TUInt32
//     | TInt128
//     | TUInt128
//     | TFloat
//     | TChar
//     | TString
//     | TDateTime
//     | TUuid
//     | TList of TypeReference
//     | TTuple of TypeReference * TypeReference * List<TypeReference>
//     | TDict of TypeReference
//     | TCustomType of
//       NameResolution<FQTypeName.FQTypeName> *
//       typeArgs : List<TypeReference>
//     | TDB of TypeReference
//     | TFn of NEList<TypeReference> * TypeReference

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


//   type Deprecation<'name> =
//     | NotDeprecated
//     | RenamedTo of 'name
//     | ReplacedBy of 'name
//     | DeprecatedBecause of string


//   module TypeDeclaration =
//     type RecordField = { name : string; typ : TypeReference; description : string }

//     type EnumField =
//       { typ : TypeReference; label : Option<string>; description : string }

//     type EnumCase = { name : string; fields : List<EnumField>; description : string }

//     type Definition =
//       | Alias of TypeReference
//       | Record of NEList<RecordField>
//       | Enum of NEList<EnumCase>

//     type TypeDeclaration = { typeParams : List<string>; definition : Definition }


//   type PackageType =
//     { tlid : TLID
//       id : System.Guid
//       name : FQTypeName.Package
//       declaration : TypeDeclaration.TypeDeclaration
//       description : string
//       deprecated : Deprecation<FQTypeName.FQTypeName> }


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
