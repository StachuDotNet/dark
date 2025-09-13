/// Integration tests for developer collaboration system
module Tests.DevCollab

open System.Threading.Tasks
open FSharp.Control.Tasks
open Expecto

open Prelude
open LibPackageManager.DevCollab
open LibPackageManager.DevCollabDb
open LibPackageManager.DevCollabConflicts

module PT = LibExecution.ProgramTypes

let testConfig = 
  { Expecto.Tests.defaultConfig with 
      parallelWorkers = 1
      printer = Expecto.Impl.TestPrinters.silent }

// Helper to create a test patch
let createTestPatch (author: string) (intent: string) : Patch =
  let patch = Patch.create author intent
  let testOp = AddFunction(
    System.Guid.NewGuid(),
    System.Guid.NewGuid(), // Mock package function name
    PT.EUnit(gid()), // Mock expression
    { typeParams = []; parameters = []; returnType = PT.TUnit; description = "Test function" } // Mock signature
  )
  Patch.addOp patch testOp

let databaseTests = 
  testList "Database Operations" [
    testCaseAsync "Can initialize database schema" <| async {
      do! initSchema () |> Async.AwaitTask
      // Test passes if no exception is thrown
    }
    
    testCaseAsync "Can save and load patches" <| async {
      // Initialize database
      do! initSchema () |> Async.AwaitTask
      
      // Create and save a patch
      let patch = createTestPatch "testuser" "Test patch creation"
      do! savePatch patch |> Async.AwaitTask
      
      // Load patches and verify
      let! patches = loadPatches () |> Async.AwaitTask
      Expect.isNonEmpty patches "Should have at least one patch"
      
      let foundPatch = patches |> List.tryFind (fun p -> p.id = patch.id)
      Expect.isSome foundPatch "Should find the saved patch"
      
      match foundPatch with
      | Some p ->
        Expect.equal p.author patch.author "Author should match"
        Expect.equal p.intent patch.intent "Intent should match"
        Expect.equal p.status patch.status "Status should match"
      | None -> failwith "Patch not found"
    }
    
    testCaseAsync "Can load patch by ID" <| async {
      // Initialize database
      do! initSchema () |> Async.AwaitTask
      
      // Create and save a patch
      let patch = createTestPatch "testuser" "Test patch loading"
      do! savePatch patch |> Async.AwaitTask
      
      // Load patch by ID
      let! foundPatch = loadPatchById patch.id |> Async.AwaitTask
      Expect.isSome foundPatch "Should find patch by ID"
      
      match foundPatch with
      | Some p ->
        Expect.equal p.id patch.id "ID should match"
        Expect.equal p.author patch.author "Author should match"
        Expect.equal p.intent patch.intent "Intent should match"
      | None -> failwith "Patch not found by ID"
    }
    
    testCaseAsync "Can save and load sessions" <| async {
      // Initialize database
      do! initSchema () |> Async.AwaitTask
      
      // Create and save a session
      let session = Session.create "testuser" "test-session" "Test session"
      do! saveSession session |> Async.AwaitTask
      
      // Load current session
      let! foundSession = loadCurrentSession "testuser" |> Async.AwaitTask
      Expect.isSome foundSession "Should find current session"
      
      match foundSession with
      | Some s ->
        Expect.equal s.id session.id "Session ID should match"
        Expect.equal s.name session.name "Session name should match"
        Expect.equal s.intent session.intent "Session intent should match"
        Expect.equal s.owner session.owner "Session owner should match"
      | None -> failwith "Session not found"
    }
  ]

let conflictTests =
  testList "Conflict Detection" [
    testCase "Detects no conflicts for non-overlapping patches" <| fun () ->
      let patch1 = createTestPatch "user1" "Add function A"
      let patch2 = createTestPatch "user2" "Add function B"
      
      let conflicts = detectConflicts patch1 patch2
      Expect.isEmpty conflicts "Should have no conflicts for different functions"
    
    testCase "Detects conflicts for same function modifications" <| fun () ->
      let functionId = System.Guid.NewGuid()
      
      // Create two patches that modify the same function
      let patch1 = Patch.create "user1" "Modify function X"
      let patch1 = Patch.addOp patch1 (UpdateFunction(functionId, PT.EUnit(gid()), 1))
      
      let patch2 = Patch.create "user2" "Also modify function X"  
      let patch2 = Patch.addOp patch2 (UpdateFunction(functionId, PT.EUnit(gid()), 1))
      
      let conflicts = detectConflicts patch1 patch2
      Expect.isNonEmpty conflicts "Should detect conflict for same function modification"
      
      let conflict = conflicts |> List.head
      match conflict.type_ with
      | SameFunctionDifferentImpl(id, p1, p2) ->
        Expect.equal id functionId "Conflict should reference the correct function ID"
        Expect.equal p1 patch1.id "Should reference first patch"
        Expect.equal p2 patch2.id "Should reference second patch"
      | _ -> failwith "Wrong conflict type detected"
    
    testCase "Analyzes multiple patches for conflicts" <| fun () ->
      let patch1 = createTestPatch "user1" "Patch 1"
      let patch2 = createTestPatch "user2" "Patch 2"
      let patch3 = createTestPatch "user3" "Patch 3"
      
      let result = analyzeConflicts [patch1; patch2; patch3]
      
      Expect.isNotNull result "Should return conflict analysis result"
      Expect.isNotNull result.conflicts "Should have conflicts list"
      Expect.isNotNull result.autoResolved "Should have auto-resolved list"
      Expect.isNotNull result.requiresManual "Should have manual resolution list"
  ]

let validationTests =
  testList "Patch Validation" [
    testCase "Validates patch with operations" <| fun () ->
      let patch = createTestPatch "user1" "Valid patch"
      let result = validatePatch patch
      
      match result with
      | Valid -> () // Test passes
      | Invalid errors -> failwithf "Valid patch should not have errors: %A" errors
    
    testCase "Rejects patch without operations" <| fun () ->
      let patch = Patch.create "user1" "Empty patch"
      let result = validatePatch patch
      
      match result with
      | Invalid errors -> 
        Expect.contains errors "Patch must contain at least one operation" "Should require operations"
      | Valid -> failwith "Empty patch should be invalid"
    
    testCase "Rejects patch without intent" <| fun () ->
      let patch = Patch.create "user1" ""
      let result = validatePatch patch
      
      match result with
      | Invalid errors ->
        Expect.contains errors "Patch must have a non-empty intent description" "Should require intent"
      | Valid -> failwith "Patch without intent should be invalid"
  ]

let integrationTests =
  testList "End-to-End Integration" [
    testCaseAsync "Complete collaboration workflow" <| async {
      // Initialize database
      do! initSchema () |> Async.AwaitTask
      
      // User 1 creates a patch
      let patch1 = createTestPatch "stachu" "Add List.filterMap"
      let patch1 = Patch.markReady patch1
      do! savePatch patch1 |> Async.AwaitTask
      
      // User 2 creates a session
      let session = Session.create "ocean" "list-work" "Working on List functions"
      do! saveSession session |> Async.AwaitTask
      
      // User 2 loads patches
      let! patches = loadPatches () |> Async.AwaitTask
      Expect.isNonEmpty patches "Should have patches available"
      
      // User 2 finds the specific patch
      let! foundPatch = loadPatchById patch1.id |> Async.AwaitTask
      Expect.isSome foundPatch "Should find the specific patch"
      
      // Validate the found patch
      match foundPatch with
      | Some p ->
        Expect.equal p.status Ready "Patch should be ready"
        let validation = validatePatch p
        match validation with
        | Valid -> () // Good
        | Invalid errors -> failwithf "Patch should be valid: %A" errors
      | None -> failwith "Patch should be found"
    }
  ]

let allTests =
  testList "DevCollab System Tests" [
    databaseTests
    conflictTests  
    validationTests
    integrationTests
  ]

[<Tests>]
let tests = allTests