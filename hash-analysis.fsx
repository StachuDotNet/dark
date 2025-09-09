#!/usr/bin/env -S dotnet fsi

open System
open System.IO
open System.Security.Cryptography
open System.Collections.Generic

// Birthday paradox probability calculation
let collisionProbability (n: float) (space: float) =
    // Using approximation: p ≈ 1 - e^(-n^2 / (2 * space))
    1.0 - Math.Exp(-(n * n) / (2.0 * space))

// Calculate probabilities for different hash sizes
let analyzeCollisionProbabilities () =
    printfn "\n=== Collision Probability Analysis ==="
    printfn "\nFor different hash sizes and artifact counts:"
    
    let hashSizes = [
        ("32-bit", 2.0 ** 32.0)
        ("64-bit", 2.0 ** 64.0)
        ("128-bit", 2.0 ** 128.0)
        ("256-bit", 2.0 ** 256.0)
    ]
    
    let artifactCounts = [
        1_000.0
        10_000.0
        100_000.0
        1_000_000.0
        10_000_000.0
    ]
    
    printfn "\n%-12s" "Artifacts" 
    for (name, _) in hashSizes do
        printf " | %-20s" name
    printfn ""
    printfn "%s" (String.replicate 100 "-")
    
    for count in artifactCounts do
        printf "%-12.0f" count
        for (_, space) in hashSizes do
            let prob = collisionProbability count space
            if prob < 0.000001 then
                printf " | %-20s" (sprintf "%.2e" prob)
            else
                printf " | %-20.6f%%" (prob * 100.0)
        printfn ""

// Crockford Base32 encoding
let crockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"

let toCrockfordBase32 (bytes: byte[]) (length: int) =
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
            
        if result.Length >= length then
            ()
    
    while result.Length < length && bitsCount > 0 do
        let idx = (bits <<< (5 - bitsCount)) &&& 0x1F
        result <- result + string crockfordAlphabet.[idx]
        bitsCount <- 0
    
    result

// Test short ID collision rates
let testShortIDCollisions () =
    printfn "\n=== Short ID Collision Testing ==="
    
    let testSizes = [1000; 10000; 100000]
    let shortIdLengths = [6; 8; 10; 12]
    
    printfn "\n%-10s" "IDs"
    for length in shortIdLengths do
        printf " | %-15s" (sprintf "%d chars" length)
    printfn ""
    printfn "%s" (String.replicate 80 "-")
    
    for size in testSizes do
        printf "%-10d" size
        
        for idLength in shortIdLengths do
            let ids = HashSet<string>()
            let mutable collisions = 0
            
            for i in 0 .. size - 1 do
                use sha256 = SHA256.Create()
                let input = sprintf "artifact_%d" i
                let hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input))
                let shortId = toCrockfordBase32 hash idLength
                
                if not (ids.Add(shortId)) then
                    collisions <- collisions + 1
            
            let rate = float collisions / float size * 100.0
            printf " | %-15s" (sprintf "%d (%.3f%%)" collisions rate)
        
        printfn ""

// Analyze .dark files for artifact counts
let analyzeDarkFiles () =
    printfn "\n=== Analyzing .dark Files ==="
    
    let packageDir = "/home/dark/app/packages"
    if Directory.Exists(packageDir) then
        let darkFiles = Directory.GetFiles(packageDir, "*.dark", SearchOption.AllDirectories)
        printfn "Found %d .dark files" darkFiles.Length
        
        // Count different artifact types (rough estimate based on file content)
        let mutable typeCount = 0
        let mutable fnCount = 0
        let mutable valueCount = 0
        
        for file in darkFiles do
            let content = File.ReadAllText(file)
            // Simple heuristic counting
            typeCount <- typeCount + (content.Split("type ").Length - 1)
            fnCount <- fnCount + (content.Split("let ").Length - 1)
            valueCount <- valueCount + (content.Split("const ").Length - 1)
        
        printfn "\nEstimated artifact counts:"
        printfn "  Types:     %d" typeCount
        printfn "  Functions: %d" fnCount  
        printfn "  Values:    %d" valueCount
        printfn "  Total:     %d" (typeCount + fnCount + valueCount)
        
        typeCount + fnCount + valueCount
    else
        printfn "Package directory not found"
        0

// Benchmark hashing performance
let benchmarkHashing () =
    printfn "\n=== Hashing Performance Benchmark ==="
    
    let sizes = [
        ("Small (100 bytes)", 100)
        ("Medium (1 KB)", 1024)
        ("Large (10 KB)", 10240)
        ("XLarge (100 KB)", 102400)
    ]
    
    let iterations = 10000
    
    printfn "\n%-20s | %-15s | %-15s" "Size" "SHA-256" "Operations/sec"
    printfn "%s" (String.replicate 60 "-")
    
    for (name, size) in sizes do
        let data = Array.create size (byte 0)
        Random().NextBytes(data)
        
        use sha256 = SHA256.Create()
        let sw = System.Diagnostics.Stopwatch()
        
        sw.Start()
        for _ in 0 .. iterations - 1 do
            sha256.ComputeHash(data) |> ignore
        sw.Stop()
        
        let elapsed = sw.Elapsed.TotalMilliseconds
        let opsPerSec = float iterations / (elapsed / 1000.0)
        
        printfn "%-20s | %8.3f ms    | %10.0f" name elapsed opsPerSec

// Calculate space requirements
let calculateSpaceRequirements (artifactCount: int) =
    printfn "\n=== Storage Space Requirements ==="
    printfn "\nFor %d artifacts:" artifactCount
    
    let hashSizes = [
        ("SHA-256", 32)
        ("BLAKE3-256", 32)
        ("XXH3-128", 16)
        ("SHA-512", 64)
    ]
    
    printfn "\n%-15s | %-15s | %-15s" "Algorithm" "Per Artifact" "Total"
    printfn "%s" (String.replicate 50 "-")
    
    for (name, bytes) in hashSizes do
        let totalMB = float (bytes * artifactCount) / 1_048_576.0
        printfn "%-15s | %3d bytes      | %.2f MB" name bytes totalMB

// Main analysis
printfn "=== Darklang Hash Analysis Tool ==="
printfn "Date: %s" (DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))

// Run all analyses
analyzeCollisionProbabilities()
testShortIDCollisions()
let artifactCount = analyzeDarkFiles()
benchmarkHashing()
if artifactCount > 0 then
    calculateSpaceRequirements artifactCount

printfn "\n=== Analysis Complete ==="