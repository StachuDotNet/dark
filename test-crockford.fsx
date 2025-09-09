#!/usr/bin/env -S dotnet fsi

// Test the Crockford base32 implementation

type Hash = Hash of string

module Hash =
  /// Crockford Base32 alphabet (case-insensitive, excludes ambiguous chars)
  let private crockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"
  
  /// Convert hash to Crockford Base32 short ID
  let toShortId (Hash hashStr) (length : int) : string =
    // Take first N*5/8 bytes (since base32 uses 5 bits per char)
    let bytesToTake = (length * 5 + 7) / 8 // ceiling division
    let maxBytes = min bytesToTake (hashStr.Length / 2)
    
    // Convert hex string to bytes
    let bytes = 
      [| for i in 0 .. 2 .. min (hashStr.Length - 1) (maxBytes * 2 - 1) ->
          System.Convert.ToByte(hashStr.Substring(i, 2), 16) |]
    
    let mutable result = ""
    let mutable bits = 0
    let mutable bitsCount = 0
    
    for b in bytes do
      bits <- (bits <<< 8) ||| int b
      bitsCount <- bitsCount + 8
      
      while bitsCount >= 5 && result.Length < length do
        let idx = (bits >>> (bitsCount - 5)) &&& 0x1F
        result <- result + string crockfordAlphabet.[idx]
        bitsCount <- bitsCount - 5
    
    // Pad with remaining bits if needed
    while result.Length < length && bitsCount > 0 do
      let idx = (bits <<< (5 - bitsCount)) &&& 0x1F
      result <- result + string crockfordAlphabet.[idx]
      bitsCount <- 0
    
    result.PadRight(length, '0') // Pad with zeros if still short
  
  /// Default 12-character short ID  
  let toShortId12 (hash : Hash) : string = toShortId hash 12
  
  /// 8-character short ID
  let toShortId8 (hash : Hash) : string = toShortId hash 8

// Test with some known hashes
printfn "=== Crockford Base32 Short ID Testing ==="

let testHashes = [
  "955af4bf730d8ad40df4e5f44aeacdc642a3c0b5b27fa6a2ab32dcf973335528"
  "594fb3496f67541e2d537cccc42b4a454cb5fbd6e0c3ab253ca1ce71d5fcbbcc" 
  "9f49fa7e37caf33f8c624233d7f77ce25cf2850b6209d6867ca2a3f0d3ef6fd0"
  "f54a4e7510768beb0bfd1ae00dd6c8c5d126410d001fb0cee96e762624622b8e"
  "b7a7d174b723fe601e166f9768d1b8bd5f4938efca70175fdd9be1d8502e28b2"
]

printfn "\n%-64s | %-12s | %-8s" "Full Hash" "12-char ID" "8-char ID"
printfn "%s" (String.replicate 90 "-")

for hashStr in testHashes do
  let hash = Hash hashStr
  let shortId12 = Hash.toShortId12 hash
  let shortId8 = Hash.toShortId8 hash
  printfn "%-64s | %-12s | %-8s" hashStr.[0..15] shortId12 shortId8

// Test uniqueness with current data set
printfn "\n=== Uniqueness Test ==="
let shortIds12 = testHashes |> List.map (Hash >> Hash.toShortId12)
let shortIds8 = testHashes |> List.map (Hash >> Hash.toShortId8)

let unique12 = shortIds12 |> List.distinct |> List.length
let unique8 = shortIds8 |> List.distinct |> List.length

printfn "12-char IDs: %d unique out of %d total" unique12 testHashes.Length
printfn "8-char IDs:  %d unique out of %d total" unique8 testHashes.Length

if unique12 = testHashes.Length then
  printfn "✓ No collisions with 12-char IDs"
else
  printfn "✗ Collisions detected with 12-char IDs"

if unique8 = testHashes.Length then
  printfn "✓ No collisions with 8-char IDs"
else
  printfn "✗ Collisions detected with 8-char IDs"

printfn "\n=== Test Complete ==="