/// Core binary format definitions and constants
module LibSerialization.Binary.BaseFormat

open System

/// The op format's version. v1 is THIS format, the one the op-log substrate ships with; nothing
/// before it is readable and nothing needs to be, because no store predating it survives (the reload
/// rebuilt every store from source, so "an old blob" has never existed in the wild).
///
/// That is why this did not move when the layout changed under it during the rewrite: v1 is the first
/// version that means anything. From here it moves on every wire-layout change, with a `readV1` kept
/// beside the new writer, because from here there are stores that cannot be rebuilt from text.
[<Literal>]
let CurrentVersion = 1u

/// Binary file header structure (8 bytes)
type BinaryHeader =
  {
    // The blob's format version. Passed to version-dispatched readers (makeDeserializerV) so a new
    // binary can decode an OLD layout by branching on it: the keystone of any future format
    // migration. Bump `CurrentVersion` on the next wire-layout change and add the matching readVN.
    Version : uint32 // 4 bytes - format version
    DataLength : uint32 } // 4 bytes - payload size

/// Validation errors for binary format
type BinaryFormatError =
  | UnsupportedVersion of version : uint32
  | CorruptedData of message : string
  | UnexpectedEndOfStream
  | DataLengthMismatch of expected : uint32 * actual : uint32

exception BinaryFormatException of BinaryFormatError


/// Constants for varint encoding
///
/// "varint encoding" ~=
///   "when serializing integers that could be of a large size,
///     try to save some space if it's a small #"
///   e.g. storing `7` for a uint64 sholdn't take up a whole uint64's worth of bits...
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
