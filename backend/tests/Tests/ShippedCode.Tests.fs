/// What keeps a store's copy of the CLI's own code from running when it is not the copy the build
/// shipped (`LibDB.ShippedCode`), and the settings that live beside the policy rather than in the
/// store (`LibDB.Config.personOnly`). An agent in `dark agent run` can write the store, so neither
/// may be something it can change for the person.
///
/// And what dispatch picks when the store holds an impl for a type the build already ships one
/// for (`LibExecution.Traits.select`): the shipped one, so code written in the store cannot change
/// what the CLI's own calls and the stdlib mean.
///
/// The whole boundary, with a release binary and bwrap, is `scripts/testing/check-agent-sandbox`.
module Tests.ShippedCode

open Expecto

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open LibDB.Sqlite
open TestUtils.TestUtils
open TestUtils.PTShortcuts

module ShippedCode = LibDB.ShippedCode
module Config = LibDB.Config
module RT = LibExecution.RuntimeTypes
module PT = LibExecution.ProgramTypes
module PT2RT = LibExecution.ProgramTypesToRuntimeTypes
module Traits = LibExecution.Traits

/// Dispatch with a shipped impl and a rival the store gained later, for one trait and one type.
/// The shipped one is the stdlib's `ToString for Int64`, and the rival names
/// `Stdlib.English.toOrdinal` as its `toString`, so which one ran shows in the answer: `5` or
/// `5th`. (Not an operator trait: those answer builtin numbers without dispatching at all.)
module private Rivals =
  /// (the shipped impl, the newer rival, the fn the rival runs)
  let make () : Task<RT.ImplCandidate * RT.ImplCandidate * RT.FQFnName.Package> =
    task {
      let! cs = pmRT.implCandidatesByMethod PT.BranchId.Main "toString" |> Ply.toTask
      let shipped =
        cs
        |> List.find (fun c -> c.self = RT.TInt64)
        |> Option.defaultWith (fun () ->
          failtest "the stdlib has no ToString for Int64")
      let! toOrdinal =
        Sql.query
          "SELECT item_hash FROM locations WHERE owner = 'Darklang'
             AND modules = 'Stdlib.English' AND name = 'toOrdinal'
             AND item_type = 'fn' AND live = 1 LIMIT 1"
        |> Sql.executeRowAsync (fun read -> read.string "item_hash")
      let rivalFn = RT.Hash toOrdinal
      let rival =
        { shipped with
            methods = Map.ofList [ "toString", rivalFn ]
            source = RT.Hash(String.replicate 64 "e")
            stamp = "9999-12-31T23:59:59.999Z-9999"
            shipped = false }
      return ({ shipped with shipped = true }, rival, rivalFn)
    }

  /// `ToString.toString 5L` as saved: <param choice> is what the save recorded.
  let call
    (trait_ : RT.FQTraitName.Package)
    (choice : PT.FQFnName.ImplChoice)
    : PT.Expr =
    let (RT.Hash traitHash) = trait_
    eApply
      (PT.EFnName(
        gid (),
        PT.NameResolution.ok (
          PT.FQFnName.TraitMethod
            { trait_ = PT.Hash traitHash; method_ = "toString"; implFn = choice }
        ),
        [],
        []
      ))
      []
      [ eInt64 5L ]

  /// The text a run answered, if it answered one.
  let answer (result : RT.ExecutionResult) : Option<string> =
    match result with
    | Ok(RT.DString s) -> Some s
    | _ -> None

  /// Run <param expr> where the store answers <param candidates> for every trait.
  let run
    (candidates : List<RT.ImplCandidate>)
    (expr : PT.Expr)
    : Task<RT.ExecutionResult> =
    task {
      let! state = executionStateFor pmPT false
      let state =
        { state with
            fns =
              { state.fns with
                  implCandidates = fun _ _ -> Ply candidates
                  implSelectionMemo =
                    System.Collections.Concurrent.ConcurrentDictionary() } }
      return!
        LibExecution.Execution.executeExpr
          state
          (PT2RT.Expr.toRT Map.empty 0 None expr)
    }

/// Every function's instructions in the test store, by hash.
let private functionRows () : Task<List<string * byte[]>> =
  Sql.query "SELECT hash, rt_instrs FROM package_functions"
  |> Sql.executeAsync (fun read -> read.string "hash", read.bytes "rt_instrs")

/// The digests of the test store, as `export-seed` writes them for a release build to embed.
let private digestsOfTheStore () : ShippedCode.Digests =
  match ShippedCode.parse (ShippedCode.ofStore currentDbPath) with
  | Some digests -> digests
  | None -> failtest "the digests of the store do not parse"

let private dispatch =
  testList
    "dispatch"
    [ testTask "a shipped impl runs, not a newer one the store holds for its type" {
        let! (shipped, rival, _) = Rivals.make ()
        match Traits.select [ rival; shipped ] RT.KTInt64 with
        | Traits.Selected c -> Expect.equal c.source shipped.source "the shipped one"
        | other -> failtest $"expected the shipped impl, got {other}"
        let! ran =
          Rivals.run
            [ rival; shipped ]
            (Rivals.call shipped.trait_ PT.FQFnName.Unknown)
        Expect.equal (Rivals.answer ran) (Some "5") "a call nothing recorded runs it"
      }

      testTask "a call whose save recorded the rival still runs the rival" {
        let! (shipped, rival, rivalFn) = Rivals.make ()
        let (RT.Hash h) = rivalFn
        let recorded = PT.FQFnName.Chosen { name = PT.Hash h; location = None }
        let! ran =
          Rivals.run [ rival; shipped ] (Rivals.call shipped.trait_ recorded)
        Expect.equal (Rivals.answer ran) (Some "5th") "the recorded choice"
      }

      testTask "without a shipped impl for the type, the newer one runs as before" {
        let! (shipped, rival, _) = Rivals.make ()
        let older =
          { (shipped : RT.ImplCandidate) with
              shipped = false
              stamp = "2000-01-01T00:00:00.000Z-0001" }
        match Traits.select [ older; rival ] RT.KTInt64 with
        | Traits.Selected c -> Expect.equal c.source rival.source "the newer one"
        | other -> failtest $"expected the newer impl, got {other}"
        let! ran =
          Rivals.run
            [ older; rival ]
            (Rivals.call shipped.trait_ PT.FQFnName.Unknown)
        Expect.equal (Rivals.answer ran) (Some "5th") "it runs"
        // A shipped impl for another type changes nothing for this one.
        let forString =
          { (shipped : RT.ImplCandidate) with
              self = RT.TString
              source = RT.Hash(String.replicate 64 "d") }
        match Traits.select [ forString; rival ] RT.KTInt64 with
        | Traits.Selected c ->
          Expect.equal c.source rival.source "the only one for Int64"
        | other -> failtest $"expected the store's impl, got {other}"
      }

      test "which impls compete for one type" {
        let option args =
          RT.TCustomType(
            RT.NameResolution.ok (RT.FQTypeName.fqPackage "option"),
            args
          )
        Expect.isTrue (Traits.competes RT.TInt64 RT.TInt64) "one type"
        Expect.isFalse (Traits.competes RT.TInt64 RT.TString) "two types"
        Expect.isTrue
          (Traits.competes (RT.TList(RT.TVariable "a")) (RT.TList RT.TInt64))
          "List<'a> and List<Int64>"
        Expect.isFalse
          (Traits.competes (option [ RT.TInt64 ]) (option [ RT.TString ]))
          "Option<Int64> and Option<String>"
        Expect.isFalse
          (Traits.competes (RT.TVariable "a") RT.TInt64)
          "a blanket impl loses to any specific one anyway"
      }

      testTask "the store's impls are looked up as shipped or not" {
        let digests = digestsOfTheStore ()
        let! impls =
          Sql.query "SELECT hash FROM package_trait_impls"
          |> Sql.executeAsync (fun read -> read.string "hash")
        Expect.isTrue
          (impls |> List.exists (ShippedCode.shipsIn digests ShippedCode.Impl))
          "the test store ships impls"
        Expect.isFalse
          (ShippedCode.shipsIn digests ShippedCode.Impl (String.replicate 64 "0"))
          "one it has no digest for"
      } ]

let tests =
  testList
    "ShippedCode"
    [ dispatch

      testTask "the store's own code matches the digests made from it" {
        let digests = digestsOfTheStore ()
        let! rows = functionRows ()
        let checks =
          rows
          |> List.map (fun (hash, bytes) ->
            ShippedCode.check digests ShippedCode.Fn hash bytes)
        Expect.isNonEmpty rows "the test store ships functions"
        Expect.contains checks ShippedCode.Matches "a shipped function matches"
        Expect.isFalse
          (List.contains ShippedCode.Differs checks)
          "and no row differs from itself"
      }

      testTask "a changed row is told apart from the shipped one" {
        let digests = digestsOfTheStore ()
        let! rows = functionRows ()
        let hash, bytes =
          rows
          |> List.find (fun (hash, bytes) ->
            ShippedCode.check digests ShippedCode.Fn hash bytes = ShippedCode.Matches)
          |> Option.defaultWith (fun () ->
            failtest "no shipped function in the store")
        let changed =
          bytes
          |> Array.mapi (fun i b -> if i = bytes.Length / 2 then b ^^^ 1uy else b)
        Expect.equal
          (ShippedCode.check digests ShippedCode.Fn hash changed)
          ShippedCode.Differs
          "one flipped bit"
        Expect.equal
          (ShippedCode.check
            digests
            ShippedCode.Fn
            hash
            (Array.append bytes [| 0uy |]))
          ShippedCode.Differs
          "one more byte"
        Expect.equal
          (ShippedCode.check digests ShippedCode.Type hash bytes)
          ShippedCode.NotShipped
          "a digest is for one kind of row"
      }

      test "what the build does not ship is not checked" {
        let digests = digestsOfTheStore ()
        let unknown = String.replicate 64 "0"
        Expect.equal
          (ShippedCode.check digests ShippedCode.Fn unknown [| 1uy |])
          ShippedCode.NotShipped
          "a hash the build has no digest for"
        Expect.equal
          (ShippedCode.check digests ShippedCode.Fn "not-a-hash" [| 1uy |])
          ShippedCode.NotShipped
          "a malformed hash"
        Expect.isNone
          (ShippedCode.parse [| 1uy; 2uy |])
          "torn digests are not digests"
      }

      test "the refusal says what to do without dark" {
        let said = ShippedCode.refusal ShippedCode.Fn "Darklang.Cli.Core.run"
        Expect.stringContains said "Darklang.Cli.Core.run" "names the item"
        Expect.stringContains said "backups" "points at the backups"
      }

      testTask "the person's settings are not read from the store" {
        // What an agent writing the store directly would leave behind.
        let write (sql : string) = Sql.query sql |> Sql.executeStatementAsync
        do!
          write
            "INSERT OR REPLACE INTO config_v0 (key, value) VALUES ('http.private', 'on')"
        do!
          write
            "INSERT OR REPLACE INTO config_v0 (key, value) VALUES ('exec.policy', 'Agent.Owned.fn')"
        let! privateNet = Config.get "http.private"
        let! many = Config.getMany [ "exec.policy"; "http.private"; "exec.workers" ]
        do!
          write "DELETE FROM config_v0 WHERE key IN ('http.private', 'exec.policy')"
        Expect.notEqual privateNet (Some "on") "http.private is not the store's"
        Expect.notEqual
          (Map.tryFind "exec.policy" many)
          (Some "Agent.Owned.fn")
          "nor is exec.policy"
      }

      testTask "the person's settings are kept beside the policy" {
        // `off` is the default, so a test running beside this one sees no change.
        do! Config.set "http.private" "off"
        let! v = Config.get "http.private"
        let file =
          match LibDB.LocalFile.read "settings" with
          | LibDB.LocalFile.Read bytes -> System.Text.Encoding.UTF8.GetString bytes
          | _ -> ""
        let! stored =
          Sql.query "SELECT value FROM config_v0 WHERE key = 'http.private'"
          |> Sql.executeRowOptionAsync (fun read -> read.string "value")
        do! Config.unset "http.private"
        Expect.equal v (Some "off") "it reads back"
        Expect.stringContains file "http.private=off" "from the policy directory"
        Expect.isNone stored "and not from the store"
      } ]
