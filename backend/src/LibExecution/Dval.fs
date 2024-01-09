/// Simple pass-through functions for creating Dvals
module LibExecution.Dval

open Prelude

open LibExecution.RuntimeTypes
module VT = ValueType


let int64 (i : int64) = DInt64(i)

let uint64 (i : uint64) = DUInt64(i)

let int8 (i : int8) = DInt8(i)

let uint8 (i : uint8) = DUInt8(i)

let int16 (i : int16) = DInt16(i)

let uint16 (i : uint16) = DUInt16(i)

let int32 (i : int32) = DInt32(i)

let uint32 (i : uint32) = DUInt32(i)

let int128 (i : System.Int128) = DInt128(i)

let uint128 (i : System.UInt128) = DUInt128(i)

let list (typ : KnownType) (list : List<Dval>) : Dval = DList(VT.known typ, list)

let dict (typ : KnownType) (entries : List<string * Dval>) : Dval =
  DDict(VT.known typ, Map entries)

let dictFromMap (typ : KnownType) (entries : Map<string, Dval>) : Dval =
  DDict(VT.known typ, entries)


/// VTTODO
/// the interpreter "throws away" any valueTypes currently,
/// so while these .option and .result functions are great in that they
/// return the correct typeArgs, they conflict with what the interpreter will do
///
/// So, to make some tests happy, let's ignore these for now.
///
/// (might need better explanation^)
let ignoreAndUseEmpty (_ignoredForNow : List<ValueType>) = []



let optionType = FQTypeName.fqPackage "Darklang" [ "Stdlib"; "Option" ] "Option" 0


let optionSome (innerType : KnownType) (dv : Dval) : Dval =
  DEnum(
    optionType,
    optionType,
    ignoreAndUseEmpty [ VT.known innerType ],
    "Some",
    [ dv ]
  )

let optionNone (innerType : KnownType) : Dval =
  DEnum(optionType, optionType, ignoreAndUseEmpty [ VT.known innerType ], "None", [])

let option (innerType : KnownType) (dv : Option<Dval>) : Dval =
  match dv with
  | Some dv -> optionSome innerType dv
  | None -> optionNone innerType



let resultType = FQTypeName.fqPackage "Darklang" [ "Stdlib"; "Result" ] "Result" 0


let resultOk (okType : KnownType) (errorType : KnownType) (dvOk : Dval) : Dval =
  DEnum(
    resultType,
    resultType,
    ignoreAndUseEmpty [ ValueType.Known okType; ValueType.Known errorType ],
    "Ok",
    [ dvOk ]
  )

let resultError
  (okType : KnownType)
  (errorType : KnownType)
  (dvError : Dval)
  : Dval =

  DEnum(
    resultType,
    resultType,
    ignoreAndUseEmpty [ ValueType.known okType; ValueType.known errorType ],
    "Error",
    [ dvError ]
  )

let result
  (okType : KnownType)
  (errorType : KnownType)
  (dv : Result<Dval, Dval>)
  : Dval =
  match dv with
  | Ok dv -> resultOk okType errorType dv
  | Error dv -> resultError okType errorType dv


let byteArrayToDvalList (bytes : byte[]) : Dval =
  bytes
  |> Array.toList
  |> List.map (fun b -> DUInt8(byte b))
  |> fun dvalList -> DList(VT.uint8, dvalList)

let DlistToByteArray (dvalList : List<Dval>) : byte[] =
  dvalList
  |> List.map (fun dval ->
    match dval with
    | DUInt8 b -> b
    | _ -> (Exception.raiseInternal "Invalid type in byte list") [])
  |> Array.ofList
