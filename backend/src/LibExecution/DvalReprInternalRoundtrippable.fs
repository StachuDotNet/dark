/// Ways of converting Dvals to/from strings, to be used exclusively internally.
///
/// That is, they should not be used in libraries, in the BwdServer, in HttpClient,
/// etc.
module LibExecution.DvalReprInternalRoundtrippable

open Prelude
open VendoredTablecloth

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

  /// A UserType is a type written by a Developer in their canvas
  type UserTypeName = { type_ : string; version : int }

  type DvalMap = Map<string, Dval>
  and DvalSource =
    | SourceNone
    | SourceID of tlid * id

  and Dval =
    | DInt of int64
    | DFloat of double
    | DBool of bool
    | DUnit
    | DStr of string
    | DChar of string
    | DList of List<Dval>
    | DTuple of Dval * Dval * List<Dval>
    | DLambda // See docs/dblock-serialization.md
    | DObj of DvalMap
    | DError of DvalSource * string
    | DIncomplete of DvalSource
    | DHttpResponse of int64 * List<string * string> * Dval
    | DDB of string
    | DDateTime of NodaTime.LocalDateTime
    | DPassword of byte array // We are allowed serialize this here, so don't use the Password type which doesn't deserialize
    | DUuid of System.Guid
    | DOption of Option<Dval>
    | DResult of Result<Dval, Dval>
    | DBytes of byte array

  let rec toRT (dv : Dval) : RT.Dval =
    match dv with
    | DStr s -> RT.DStr s
    | DChar c -> RT.DChar c
    | DInt i -> RT.DInt i
    | DBool b -> RT.DBool b
    | DFloat f -> RT.DFloat f
    | DUnit -> RT.DUnit
    | DLambda ->
      RT.DFnVal(
        RT.Lambda { parameters = []; symtable = Map []; body = RT.Expr.EUnit 0UL }
      )
    | DIncomplete SourceNone -> RT.DIncomplete RT.SourceNone
    | DIncomplete (SourceID (tlid, id)) -> RT.DIncomplete(RT.SourceID(tlid, id))
    | DError (SourceNone, msg) -> RT.DError(RT.SourceNone, msg)
    | DError (SourceID (tlid, id), msg) -> RT.DError(RT.SourceID(tlid, id), msg)
    | DDateTime d -> RT.DDateTime d
    | DDB name -> RT.DDB name
    | DUuid uuid -> RT.DUuid uuid
    | DPassword pw -> RT.DPassword(Password pw)
    | DHttpResponse (code, headers, hdv) -> RT.DHttpResponse(code, headers, toRT hdv)
    | DList l -> RT.DList(List.map toRT l)
    | DTuple (first, second, theRest) ->
      RT.DTuple(toRT first, toRT second, List.map toRT theRest)
    | DObj o -> RT.DObj(Map.map toRT o)
    | DOption None -> RT.DOption None
    | DOption (Some dv) -> RT.DOption(Some(toRT dv))
    | DResult (Ok dv) -> RT.DResult(Ok(toRT dv))
    | DResult (Error dv) -> RT.DResult(Error(toRT dv))
    | DBytes bytes -> RT.DBytes bytes


  let rec fromRT (dv : RT.Dval) : Dval =
    match dv with
    | RT.DStr s -> DStr s
    | RT.DChar c -> DChar c
    | RT.DInt i -> DInt i
    | RT.DBool b -> DBool b
    | RT.DFloat f -> DFloat f
    | RT.DUnit -> DUnit
    | RT.DFnVal _ -> DLambda
    | RT.DIncomplete RT.SourceNone -> DIncomplete SourceNone
    | RT.DIncomplete (RT.SourceID (tlid, id)) -> DIncomplete(SourceID(tlid, id))
    | RT.DError (RT.SourceNone, msg) -> DError(SourceNone, msg)
    | RT.DError (RT.SourceID (tlid, id), msg) -> DError(SourceID(tlid, id), msg)
    | RT.DDateTime d -> DDateTime d
    | RT.DDB name -> DDB name
    | RT.DUuid uuid -> DUuid uuid
    | RT.DPassword (Password pw) -> DPassword pw
    | RT.DHttpResponse (code, headers, hdv) ->
      DHttpResponse(code, headers, fromRT hdv)
    | RT.DList l -> DList(List.map fromRT l)
    | RT.DTuple (first, second, theRest) ->
      DTuple(fromRT first, fromRT second, List.map fromRT theRest)
    | RT.DObj o -> DObj(Map.map fromRT o)
    | RT.DOption None -> DOption None
    | RT.DOption (Some dv) -> DOption(Some(fromRT dv))
    | RT.DResult (Ok dv) -> DResult(Ok(fromRT dv))
    | RT.DResult (Error dv) -> DResult(Error(fromRT dv))
    | RT.DBytes bytes -> DBytes bytes
    | RT.DUserEnum _ -> DUnit // TODO why?


let toJsonV0 (dv : RT.Dval) : string =
  dv |> FormatV0.fromRT |> Json.Vanilla.serialize

let parseJsonV0 (json : string) : RT.Dval =
  json |> Json.Vanilla.deserialize<FormatV0.Dval> |> FormatV0.toRT

let toHashV2 (dvals : list<RT.Dval>) : string =
  dvals
  |> List.map FormatV0.fromRT
  |> FormatV0.DList
  |> Json.Vanilla.serialize
  |> UTF8.toBytes
  |> System.IO.Hashing.XxHash64.Hash // fastest in .NET, does not need to be secure
  |> Base64.urlEncodeToString



module Test =
  let rec isRoundtrippableDval (dval : RT.Dval) : bool =
    match dval with
    | RT.DFnVal _ -> false // not supported
    | RT.DStr _
    | RT.DInt _
    | RT.DFloat _
    | RT.DUnit _
    | RT.DBool _
    | RT.DChar _
    | RT.DBytes _
    | RT.DDateTime _
    | RT.DUserEnum _
    | RT.DOption None
    | RT.DPassword _ -> true
    | RT.DList dvals -> List.all isRoundtrippableDval dvals
    | RT.DObj map -> map |> Map.values |> List.all isRoundtrippableDval
    | RT.DUuid _ -> true
    | RT.DTuple (v1, v2, rest) -> List.all isRoundtrippableDval (v1 :: v2 :: rest)
    | RT.DOption (Some v)
    | RT.DHttpResponse (_, _, v)
    | RT.DResult (Error v)
    | RT.DResult (Ok v) -> isRoundtrippableDval v
    | RT.DDB _
    | RT.DError _

    | RT.DIncomplete _ -> true
