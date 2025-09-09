#!/usr/bin/env -S dotnet fsi

open System
open System.IO
open System.Data
open Microsoft.Data.Sqlite
open System.Collections.Generic
open System.Security.Cryptography

// Connect to database
let connectionString = "Data Source=rundir/data.db;Mode=ReadOnly"

// Crockford Base32 encoding
let crockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"

let toCrockfordBase32 (hexHash: string) (charLength: int) =
    // Convert hex to bytes
    let bytes = 
        [| for i in 0 .. 2 .. min (hexHash.Length - 1) 31 ->
            Convert.ToByte(hexHash.Substring(i, 2), 16) |]
    
    let mutable result = ""
    let mutable bits = 0
    let mutable bitsCount = 0
    
    for b in bytes do
        bits <- (bits <<< 8) ||| int b
        bitsCount <- bitsCount + 8
        
        while bitsCount >= 5 && result.Length < charLength do
            let idx = (bits >>> (bitsCount - 5)) &&& 0x1F
            result <- result + string crockfordAlphabet.[idx]
            bitsCount <- bitsCount - 5
    
    while result.Length < charLength && bitsCount > 0 do
        let idx = (bits <<< (5 - bitsCount)) &&& 0x1F
        result <- result + string crockfordAlphabet.[idx]
        bitsCount <- 0
    
    result

// Analyze real hashes from database
let analyzeRealHashes () =
    use connection = new SqliteConnection(connectionString)
    connection.Open()
    
    printfn "\n=== Analyzing Real Hashes from Database ==="
    
    // Get all hashes
    let allHashes = List<string>()
    let hashesByType = Dictionary<string, List<string>>()
    hashesByType.["types"] <- List<string>()
    hashesByType.["values"] <- List<string>()
    hashesByType.["functions"] <- List<string>()
    
    // Collect types
    use cmd1 = new SqliteCommand("SELECT hash FROM package_types_v0 WHERE hash IS NOT NULL", connection)
    use reader1 = cmd1.ExecuteReader()
    while reader1.Read() do
        let hash = reader1.GetString(0)
        allHashes.Add(hash)
        hashesByType.["types"].Add(hash)
    
    // Collect values
    use cmd2 = new SqliteCommand("SELECT hash FROM package_values_v0 WHERE hash IS NOT NULL", connection)
    use reader2 = cmd2.ExecuteReader()
    while reader2.Read() do
        let hash = reader2.GetString(0)
        allHashes.Add(hash)
        hashesByType.["values"].Add(hash)
    
    // Collect functions  
    use cmd3 = new SqliteCommand("SELECT hash FROM package_functions_v0 WHERE hash IS NOT NULL", connection)
    use reader3 = cmd3.ExecuteReader()
    while reader3.Read() do
        let hash = reader3.GetString(0)
        allHashes.Add(hash)
        hashesByType.["functions"].Add(hash)
    
    printfn "\nHash counts by type:"
    printfn "  Types:     %d" hashesByType.["types"].Count
    printfn "  Values:    %d" hashesByType.["values"].Count
    printfn "  Functions: %d" hashesByType.["functions"].Count
    printfn "  Total:     %d" allHashes.Count
    
    // Check for full hash collisions
    let uniqueHashes = HashSet<string>(allHashes)
    let fullCollisions = allHashes.Count - uniqueHashes.Count
    printfn "\nFull hash collisions: %d" fullCollisions
    
    if fullCollisions > 0 then
        // Find duplicates
        let hashCounts = Dictionary<string, int>()
        for hash in allHashes do
            if hashCounts.ContainsKey(hash) then
                hashCounts.[hash] <- hashCounts.[hash] + 1
            else
                hashCounts.[hash] <- 1
        
        printfn "\nDuplicate hashes:"
        for kvp in hashCounts do
            if kvp.Value > 1 then
                printfn "  %s: %d occurrences" kvp.Key kvp.Value
    
    // Test short ID collisions
    printfn "\n=== Short ID Collision Analysis ==="
    let shortIdLengths = [6; 8; 10; 12]
    
    printfn "\n%-15s | %-10s | %-15s" "Length" "Collisions" "Collision Rate"
    printfn "%s" (String.replicate 50 "-")
    
    for length in shortIdLengths do
        let shortIds = List<string>()
        let uniqueShortIds = HashSet<string>()
        
        for hash in allHashes do
            let shortId = toCrockfordBase32 hash length
            shortIds.Add(shortId)
            uniqueShortIds.Add(shortId) |> ignore
        
        let collisions = shortIds.Count - uniqueShortIds.Count
        let rate = float collisions / float shortIds.Count * 100.0
        printfn "%-15s | %-10d | %.4f%%" (sprintf "%d chars" length) collisions rate
    
    // Find minimum unique prefix length
    printfn "\n=== Minimum Unique Prefix Analysis ==="
    let mutable minLength = 1
    let mutable foundUnique = false
    
    while not foundUnique && minLength <= 64 do
        let prefixes = HashSet<string>()
        let mutable hasCollision = false
        
        for hash in allHashes do
            let prefix = hash.Substring(0, min minLength hash.Length)
            if not (prefixes.Add(prefix)) then
                hasCollision <- true
        
        if not hasCollision then
            foundUnique <- true
            printfn "Minimum hex prefix for uniqueness: %d characters" minLength
        else
            minLength <- minLength + 1
    
    // Analyze hash distribution
    printfn "\n=== Hash Distribution Analysis ==="
    let firstByteCounts = Array.create 256 0
    
    for hash in allHashes do
        if hash.Length >= 2 then
            let firstByte = Convert.ToByte(hash.Substring(0, 2), 16)
            firstByteCounts.[int firstByte] <- firstByteCounts.[int firstByte] + 1
    
    let nonZeroBuckets = firstByteCounts |> Array.filter (fun c -> c > 0) |> Array.length
    let maxCount = firstByteCounts |> Array.max
    let minCount = firstByteCounts |> Array.filter (fun c -> c > 0) |> Array.min
    let avgCount = float (Array.sum firstByteCounts) / float nonZeroBuckets
    
    printfn "First byte distribution (0x00-0xFF):"
    printfn "  Buckets used: %d/256" nonZeroBuckets
    printfn "  Max count:    %d" maxCount
    printfn "  Min count:    %d" minCount
    printfn "  Average:      %.2f" avgCount
    printfn "  Std dev:      %.2f" (
        let variance = 
            firstByteCounts 
            |> Array.filter (fun c -> c > 0)
            |> Array.map (fun c -> (float c - avgCount) ** 2.0)
            |> Array.average
        sqrt variance
    )

// Get sample artifacts for inspection
let getSampleArtifacts () =
    use connection = new SqliteConnection(connectionString)
    connection.Open()
    
    printfn "\n=== Sample Artifacts ==="
    
    // Get a few samples of each type
    let sql = """
        SELECT 'type' as kind, owner, modules, name, hash 
        FROM package_types_v0 
        WHERE hash IS NOT NULL 
        LIMIT 3
        UNION ALL
        SELECT 'function', owner, modules, name, hash 
        FROM package_functions_v0 
        WHERE hash IS NOT NULL 
        LIMIT 3
        UNION ALL
        SELECT 'value', owner, modules, name, hash 
        FROM package_values_v0 
        WHERE hash IS NOT NULL 
        LIMIT 3
    """
    
    use cmd = new SqliteCommand(sql, connection)
    use reader = cmd.ExecuteReader()
    
    printfn "\n%-8s | %-40s | %-12s | %s" "Type" "Full Name" "Short ID" "Full Hash"
    printfn "%s" (String.replicate 120 "-")
    
    while reader.Read() do
        let kind = reader.GetString(0)
        let owner = reader.GetString(1)
        let modules = reader.GetString(2)
        let name = reader.GetString(3)
        let hash = reader.GetString(4)
        let shortId = toCrockfordBase32 hash 12
        let fullName = sprintf "%s.%s.%s" owner modules name
        
        printfn "%-8s | %-40s | %-12s | %.64s" kind fullName shortId hash

// Main execution
printfn "=== Real Hash Analysis Tool ==="
printfn "Date: %s" (DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))

try
    analyzeRealHashes()
    getSampleArtifacts()
    printfn "\n=== Analysis Complete ==="
with ex ->
    printfn "Error: %s" ex.Message
    printfn "Stack: %s" ex.StackTrace