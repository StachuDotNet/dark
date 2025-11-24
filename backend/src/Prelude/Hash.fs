/// Content-addressed hash type
///
/// A SHA256 hash encoded as a hex string.
/// This is used throughout the codebase for content-addressed storage.
module Hash

open System

/// A content-addressed hash
///
/// This is a SHA256 hash encoded as a lowercase hex string (64 chars).
/// Hashes should only be created through proper hashing functions.
type Hash = Hash of string

/// Get the hex string representation of a hash
let toString (Hash s) : string = s

/// Convert hash bytes to a Hash
let ofBytes (bytes : byte[]) : Hash =
  let hex = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant()
  Hash hex

/// Create a Hash from a hex string (for deserialization only)
/// Does not validate the format - use with caution
let unsafeOfString (s : string) : Hash = Hash s

/// An empty/zero hash (all zeros)
/// Useful for normalization when computing content hashes
let empty : Hash =
  Hash "0000000000000000000000000000000000000000000000000000000000000000"

/// Convert first 16 bytes of hash to a Guid (for backwards compatibility)
/// TODO: Eventually migrate away from Guid-based IDs
let toGuid (Hash hexString) : Guid =
  if hexString.Length < 32 then
    Exception.raiseInternal
      "Hash too short for Guid conversion"
      [ "length", hexString.Length ]

  // Take first 32 hex chars (16 bytes)
  let guidHex = hexString.Substring(0, 32)
  let bytes = [| for i in 0..2..30 -> Convert.ToByte(guidHex.Substring(i, 2), 16) |]
  Guid(bytes)
