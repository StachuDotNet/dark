/// A tight, general-purpose harness for spinning up FRESH, ISOLATED Darklang instances in-process — each
/// instance is its own store (schema applied; `main` comes from schema.sql). Switch the active store with
/// `activate`, sync the wire between instances via the real receive path, and assert they converge. This is
/// the "a fresh instance in a few lines" setup the sync work needs for true multi-instance scenarios:
///   let! a = freshInstance "a"
///   let! b = freshInstance "b"
///
/// `testSequenced` + a `finally` that always restores the default store, so swapping the process-global
/// connection can't disturb the parallel store tests (they've completed by the sequenced phase; and we hand
/// the default store back no matter what). The global swap lives behind `Sql.useStoreForTesting` (test-only).
module Tests.MultiInstance

open Expecto

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Fumble
open LibDB.Sqlite

module Seed = LibDB.Seed
module PT = LibExecution.ProgramTypes
module BS = LibSerialization.Binary.Serialization
module Hashing = LibSerialization.Hashing.Hashing
module File = LibCloud.File
module Config = LibCloud.Config

// ── the fresh-instance harness ──────────────────────────────────────────────────────────────────────

type Instance = { name : string; path : string }

let private schemaSql = lazy (File.readfile Config.Migrations "schema.sql")

let private deleteStore (path : string) : unit =
  for suffix in [ ""; "-wal"; "-shm" ] do
    try
      System.IO.File.Delete(path + suffix)
    with _ ->
      ()

/// Spin up a fresh, isolated instance (its own store file, schema applied — `main` included) and make it the
/// active store. The whole setup is one line at the call site.
let freshInstance (name : string) : Task<Instance> =
  task {
    let path = $"/tmp/dark-test-instance-{name}-{System.Guid.NewGuid()}.db"
    deleteStore path
    Sql.useStoreForTesting path
    Sql.query (schemaSql.Force()) |> Sql.executeStatementSync
    return { name = name; path = path }
  }

/// Make `inst` the active store (all subsequent LibDB ops hit it).
let activate (inst : Instance) : unit = Sql.useStoreForTesting inst.path

/// Restore the default store + delete the temp instance files. Always safe to call.
let teardown (insts : List<Instance>) : unit =
  Sql.resetStoreForTesting ()
  insts |> List.iter (fun i -> deleteStore i.path)

// ── helpers ─────────────────────────────────────────────────────────────────────────────────────────

/// A BranchOp wire event (id, blob) as it arrives at `receiveBranchOps`.
let private branchEvent (op : PT.BranchOp) : string * byte[] =
  let (PT.Hash h) = Hashing.computeBranchOpHash op
  (h, BS.PT.BranchOp.serialize h op)

/// The branch names on the currently-active instance.
let private branchNames () : Task<List<string>> =
  Sql.query "SELECT name FROM branches ORDER BY name"
  |> Sql.executeAsync (fun read -> read.string "name")

// ── tests ─────────────────────────────────────────────────────────────────────────────────────────────

let tests =
  testSequenced
  <| testList
    "MultiInstance"
    [ testTask "fresh instances are isolated stores, and branch structure syncs across the wire" {
        let mutable insts : List<Instance> = []

        try
          let! a = freshInstance "a"
          insts <- [ a ]
          let! b = freshInstance "b"
          insts <- [ a; b ]

          // A authors a new branch off main.
          activate a
          let create =
            PT.BranchOp.CreateBranch(
              System.Guid.NewGuid(),
              "feature",
              Some PT.mainBranchId,
              None
            )
          let! _ = Seed.receiveBranchOps [ branchEvent create ]
          let! aBranches = branchNames ()

          // B, before syncing, has only `main` — proving the two instances are genuinely separate stores.
          activate b
          let! bBefore = branchNames ()

          // Sync A's branch-op wire (hex over the "wire") into B via the real receive path.
          activate a
          let! (wire, _cursor) = Seed.branchOpsSince 0L 1000L

          activate b
          let! _ =
            Seed.receiveBranchOps (
              wire
              |> List.map (fun ((id, hex) : string * string) ->
                (id, System.Convert.FromHexString hex))
            )
          let! bAfter = branchNames ()

          Expect.equal aBranches [ "feature"; "main" ] "A has both branches"
          Expect.equal bBefore [ "main" ] "B is isolated: only main before sync"
          Expect.equal bAfter [ "feature"; "main" ] "B converged: feature arrived over the wire"
        finally
          teardown insts
      } ]
