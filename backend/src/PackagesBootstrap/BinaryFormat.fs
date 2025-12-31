/// Core binary format definitions and constants
module PackagesBootstrap.BinaryFormat

[<Literal>]
let CurrentVersion = 1u

/// Binary file header structure (8 bytes)
type BinaryHeader =
  { Version : uint32
    DataLength : uint32 }

/// Validation errors for binary format
type BinaryFormatError =
  | UnsupportedVersion of version : uint32
  | CorruptedData of message : string
  | UnexpectedEndOfStream
  | DataLengthMismatch of expected : uint32 * actual : uint32

exception BinaryFormatException of BinaryFormatError

module Varint =
  [<Literal>]
  let MaxSingleByteValue = 127

  [<Literal>]
  let ContinuationBit = 0x80uy

  [<Literal>]
  let ValueMask = 0x7Fuy

module Validation =
  let validateVersion (version : uint32) =
    if version > CurrentVersion then
      raise (BinaryFormatException(UnsupportedVersion version))

  let validateDataLength (expected : uint32) (actual : uint32) =
    if expected <> actual then
      raise (BinaryFormatException(DataLengthMismatch(expected, actual)))
