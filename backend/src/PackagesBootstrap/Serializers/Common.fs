/// Low-level binary read primitives (deserialization only)
module PackagesBootstrap.Serializers.Common

open System.IO
open System.Text
open Prelude

open PackagesBootstrap.BinaryFormat


module Header =
  let read (reader : BinaryReader) : BinaryHeader =
    let version = reader.ReadUInt32()
    Validation.validateVersion version
    let dataLength = reader.ReadUInt32()
    { Version = version; DataLength = dataLength }


module Varint =
  let read (r : BinaryReader) : int =
    let mutable result = 0u
    let mutable shift = 0
    let mutable continueReading = true

    while continueReading do
      if shift >= 32 then
        raise (BinaryFormatException(CorruptedData "Varint too long"))

      let b = r.ReadByte()
      result <- result ||| ((uint32 (b &&& Varint.ValueMask)) <<< shift)
      shift <- shift + 7
      continueReading <- (b &&& Varint.ContinuationBit) <> 0uy

    int result


module String =
  let read (r : BinaryReader) : string =
    let length = Varint.read r
    if length = 0 then
      ""
    else
      let bytes = r.ReadBytes(length)
      if bytes.Length <> length then
        raise (BinaryFormatException(UnexpectedEndOfStream))
      Encoding.UTF8.GetString bytes


module Bool =
  let read (r : BinaryReader) : bool = r.ReadBoolean()


module Guid =
  let read (r : BinaryReader) : System.Guid =
    let bytes = r.ReadBytes 16
    if bytes.Length <> 16 then raise (BinaryFormatException UnexpectedEndOfStream)
    System.Guid(bytes)


module Option =
  let read (reader : BinaryReader) (readValue : BinaryReader -> 'T) : 'T option =
    match reader.ReadByte() with
    | 0uy -> None
    | 1uy -> Some(readValue reader)
    | b -> raise (BinaryFormatException(CorruptedData $"Invalid option tag: {b}"))


module List =
  let read (reader : BinaryReader) (readItem : BinaryReader -> 'T) : 'T list =
    let count = Varint.read reader
    [| for _ in 1..count -> readItem reader |] |> Array.toList


module NEList =
  let read (readItem : BinaryReader -> 'T) (r : BinaryReader) : NEList<'T> =
    let head = readItem r
    let tail = List.read r readItem
    { head = head; tail = tail }
