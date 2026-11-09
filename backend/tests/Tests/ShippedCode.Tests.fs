/// What keeps a store's copy of the CLI's own code from running when it is not the copy the build
/// shipped (`LibDB.ShippedCode`), and the settings that live beside the policy rather than in the
/// store (`LibDB.Config.personOnly`). An agent in `dark agent run` can write the store, so neither
/// may be something it can change for the person.
///
/// The whole boundary, with a release binary and bwrap, is `scripts/testing/check-agent-sandbox`.
module Tests.ShippedCode

open Expecto

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open LibDB.Sqlite

module ShippedCode = LibDB.ShippedCode
module Config = LibDB.Config

/// Every function's instructions in the test store, by hash.
let private functionRows () : Task<List<string * byte[]>> =
  Sql.query "SELECT hash, rt_instrs FROM package_functions"
  |> Sql.executeAsync (fun read -> read.string "hash", read.bytes "rt_instrs")

/// The digests of the test store, as `export-seed` writes them for a release build to embed.
let private digestsOfTheStore () : ShippedCode.Digests =
  match ShippedCode.parse (ShippedCode.ofStore currentDbPath) with
  | Some digests -> digests
  | None -> failtest "the digests of the store do not parse"

let tests =
  testList
    "ShippedCode"
    [ testTask "the store's own code matches the digests made from it" {
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
