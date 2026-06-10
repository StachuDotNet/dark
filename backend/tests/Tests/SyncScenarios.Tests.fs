/// Scenario coverage for the sync conflict-dispatch seam (`Sync.routeDivergences`) — the wire that
/// turns a surfaced `name → two hashes` divergence into a first-class `Conflict.CSyncDivergence` the
/// runtime resolution policy (`ExecutionState.conflictDispatch`) decides. Complements
/// `SyncIdempotency.Tests` (the transport's idempotence + LWW); here we exercise the POLICY layer:
///   - the DEFAULT policy is byte-identical to pre-seam sync (surface-as-data, LWW stands),
///   - a keep-local policy re-binds + records the override,
///   - keep-incoming / unknown-substitute are safe no-ops,
///   - multi-divergence batches, non-fn kinds, and the empty case behave.
module Tests.SyncScenarios

open Expecto

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude

open Fumble
open LibDB.Sqlite

module Inserts = LibDB.Inserts
module Conflicts = LibDB.Conflicts
module Sync = LibDB.Sync
module PT = LibExecution.ProgramTypes
module RT = LibExecution.RuntimeTypes
module RTE = LibExecution.RuntimeTypes.RuntimeError

// ── helpers ──────────────────────────────────────────────────────────────────────────────────

/// An authoring stamp relative to now (positive = future/newer, negative = past/older) — same
/// format the schema/sync use; no baked-in year so it runs in any calendar year.
let private relTs (minutesFromNow : float) : string =
  System.DateTime.UtcNow.AddMinutes(minutesFromNow).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")

let private callCtx : RT.CallContext =
  { branchId = PT.mainBranchId; threadID = System.Guid.NewGuid() }

/// The runtime default (mirrors `Execution.createState`): every divergence fails loudly — meaning,
/// to the sync receiver, "I pick no winner", so `routeDivergences` leaves the LWW outcome standing.
let private defaultDispatch : RT.ConflictDispatch =
  fun conflict _ctx ->
    uply {
      match conflict with
      | RT.CSyncDivergence(loc, e, i) ->
        return RT.RFailLoudly(RTE.UncaughtException($"divergence {loc}: {e} vs {i}", []))
      | RT.CRuntimeError rte -> return RT.RFailLoudly rte
      | RT.CFnNotFound name -> return RT.RFailLoudly(RTE.FnNotFound name)
    }

/// A keep-local policy: always substitute the EXISTING (local) hash — "my version wins".
let private keepLocalDispatch : RT.ConflictDispatch =
  fun conflict _ctx ->
    uply {
      match conflict with
      | RT.CSyncDivergence(_loc, existing, _incoming) ->
        return RT.RSubstitute(RT.DString existing)
      | RT.CRuntimeError rte -> return RT.RFailLoudly rte
      | RT.CFnNotFound name -> return RT.RFailLoudly(RTE.FnNotFound name)
    }

/// A keep-incoming policy: substitute the INCOMING hash (= what already applied) — a no-op rebind.
let private keepIncomingDispatch : RT.ConflictDispatch =
  fun conflict _ctx ->
    uply {
      match conflict with
      | RT.CSyncDivergence(_loc, _existing, incoming) ->
        return RT.RSubstitute(RT.DString incoming)
      | RT.CRuntimeError rte -> return RT.RFailLoudly rte
      | RT.CFnNotFound name -> return RT.RFailLoudly(RTE.FnNotFound name)
    }

let private liveHash (loc : PT.PackageLocation) : Task<Option<string>> =
  Sql.query
    "SELECT item_hash FROM locations WHERE owner=@o AND modules=@m AND name=@n AND unlisted_at IS NULL LIMIT 1"
  |> Sql.parameters
    [ "o", Sql.string loc.owner
      "m", Sql.string (String.concat "." loc.modules)
      "n", Sql.string loc.name ]
  |> Sql.executeAsync (fun read -> read.string "item_hash")
  |> fun t -> task { let! rows = t in return List.tryHead rows }

let private fqOf (loc : PT.PackageLocation) : string =
  let mods = String.concat "." loc.modules
  if mods = "" then $"{loc.owner}.{loc.name}" else $"{loc.owner}.{mods}.{loc.name}"

let private uniqueName (prefix : string) : string =
  prefix + System.Guid.NewGuid().ToString().Replace("-", "")

let private hashChar (c : char) = System.String(c, 64)

/// Establish a local binding (name → hash) with an authoring stamp, then PULL a divergent incoming
/// (name → other hash) through the real receiver. Returns (loc, divergences) so the scenario can
/// route them through a chosen dispatch policy. `remote` keys the conflict record.
let private setupDivergentPull
  (loc : PT.PackageLocation)
  (kind : PT.ItemKind)
  (localHash : string)
  (localTs : float)
  (incomingHash : string)
  (incomingTs : float)
  (remote : string)
  : Task<List<string * string * string>> =
  task {
    let localRef = PT.Reference.fromHashAndKind (PT.Hash localHash, kind)
    let incomingRef = PT.Reference.fromHashAndKind (PT.Hash incomingHash, kind)
    let localOp = PT.PackageOp.SetName(loc, localRef)
    let! _ =
      Inserts.insertAndApplyOpsWithOrigin
        PT.mainBranchId
        None
        [ localOp ]
        (Map.ofList [ (Inserts.computeOpHash localOp, relTs localTs) ])
    let! (_cursor, divs) =
      Sync.applyRemoteOps
        remote
        PT.mainBranchId
        None
        [ (1L, relTs incomingTs, PT.PackageOp.SetName(loc, incomingRef)) ]
    return divs
  }

// ── scenarios ────────────────────────────────────────────────────────────────────────────────

let tests =
  testSequenced
  <| testList
    "SyncScenarios"
    [ testTask
        "default dispatch: a divergence is surfaced but routing is a no-op (LWW stands)" {
        let loc : PT.PackageLocation =
          { owner = "Scenario"; modules = [ "Def" ]; name = uniqueName "d" }
        let local, incoming = hashChar 'a', hashChar 'b'
        let remote = uniqueName "rdef"
        // incoming is NEWER → LWW makes it win the pull
        let! divs =
          setupDivergentPull loc PT.ItemKind.Fn local -120.0 incoming -60.0 remote
        Expect.equal (List.length divs) 1 "exactly one divergence surfaced"
        let! reconciled =
          Sync.routeDivergences defaultDispatch callCtx remote PT.mainBranchId divs
        Expect.equal reconciled 0 "default policy reconciles nothing (byte-identical to pre-seam)"
        let! winner = liveHash loc
        Expect.equal winner (Some incoming) "binding stays on the LWW winner (incoming)"
        // the conflict is recorded and NOT overridden (no policy override happened)
        let! all = Conflicts.list ()
        match all |> List.filter (fun (c : Conflicts.Conflict) -> c.location = fqOf loc) with
        | [ c ] -> Expect.isFalse c.overridden "conflict recorded, not overridden"
        | other -> failtest $"expected 1 conflict, got {List.length other}"
      }

      testTask
        "keep-local dispatch: routing re-binds to OUR hash + marks the conflict overridden" {
        let loc : PT.PackageLocation =
          { owner = "Scenario"; modules = [ "Keep" ]; name = uniqueName "k" }
        let local, incoming = hashChar 'a', hashChar 'b'
        let remote = uniqueName "rkeep"
        // incoming newer → it won the pull; the keep-local policy must override that
        let! divs =
          setupDivergentPull loc PT.ItemKind.Fn local -120.0 incoming -60.0 remote
        let! beforeOps =
          Sql.query "SELECT COUNT(*) AS n FROM package_ops"
          |> Sql.executeRowAsync (fun read -> read.int64 "n")
        let! reconciled =
          Sync.routeDivergences keepLocalDispatch callCtx remote PT.mainBranchId divs
        Expect.equal reconciled 1 "policy reconciled the one divergence (keep-local)"
        let! winner = liveHash loc
        Expect.equal winner (Some local) "location re-bound to our (local) hash"
        // keep-local re-stamps + re-folds the EXISTING local op (content-identical, already in the
        // log) — it adds no NEW op (a fresh insert would dedup); the op count is unchanged.
        let! afterOps =
          Sql.query "SELECT COUNT(*) AS n FROM package_ops"
          |> Sql.executeRowAsync (fun read -> read.int64 "n")
        Expect.equal afterOps beforeOps "no new op appended — the existing local op is re-stamped"
        let! all = Conflicts.list ()
        match all |> List.filter (fun (c : Conflicts.Conflict) -> c.location = fqOf loc) with
        | [ c ] -> Expect.isTrue c.overridden "the conflict is marked overridden by the policy"
        | other -> failtest $"expected 1 conflict, got {List.length other}"
      }

      testTask "keep-incoming dispatch: substituting the incoming hash is a safe no-op" {
        let loc : PT.PackageLocation =
          { owner = "Scenario"; modules = [ "Inc" ]; name = uniqueName "i" }
        let local, incoming = hashChar 'a', hashChar 'b'
        let remote = uniqueName "rinc"
        let! divs =
          setupDivergentPull loc PT.ItemKind.Fn local -120.0 incoming -60.0 remote
        let! reconciled =
          Sync.routeDivergences keepIncomingDispatch callCtx remote PT.mainBranchId divs
        Expect.equal reconciled 0 "keeping the already-applied incoming bind adds no op"
        let! winner = liveHash loc
        Expect.equal winner (Some incoming) "binding stays on the incoming hash"
      }

      testTask "unknown-substitute dispatch: an unrelated hash is ignored (LWW stands)" {
        let loc : PT.PackageLocation =
          { owner = "Scenario"; modules = [ "Unk" ]; name = uniqueName "u" }
        let local, incoming = hashChar 'a', hashChar 'b'
        let remote = uniqueName "runk"
        let! divs =
          setupDivergentPull loc PT.ItemKind.Fn local -120.0 incoming -60.0 remote
        // a policy that returns a hash bound to NEITHER side — must not rebind
        let weirdDispatch : RT.ConflictDispatch =
          fun _ _ -> uply { return RT.RSubstitute(RT.DString(hashChar 'z')) }
        let! reconciled =
          Sync.routeDivergences weirdDispatch callCtx remote PT.mainBranchId divs
        Expect.equal reconciled 0 "an unknown substitute hash reconciles nothing"
        let! winner = liveHash loc
        Expect.equal winner (Some incoming) "binding untouched (still the LWW winner)"
      }

      testTask "keep-local works for a TYPE binding (kindOfHash isn't fn-only)" {
        let loc : PT.PackageLocation =
          { owner = "Scenario"; modules = [ "Typ" ]; name = uniqueName "t" }
        let local, incoming = hashChar 'c', hashChar 'd'
        let remote = uniqueName "rtyp"
        let! divs =
          setupDivergentPull loc PT.ItemKind.Type local -120.0 incoming -60.0 remote
        let! reconciled =
          Sync.routeDivergences keepLocalDispatch callCtx remote PT.mainBranchId divs
        Expect.equal reconciled 1 "a type divergence reconciles too"
        let! winner = liveHash loc
        Expect.equal winner (Some local) "type location re-bound to local"
      }

      testTask "empty divergence list routes to a clean zero (the converged steady state)" {
        let remote = uniqueName "rempty"
        let! reconciled =
          Sync.routeDivergences keepLocalDispatch callCtx remote PT.mainBranchId []
        Expect.equal reconciled 0 "no divergences → nothing reconciled, no ops"
      }

      // The same-millisecond cross-instance tie: two DIFFERENT ops bind one name with the EXACT same
      // origin_ts (two machines authored in the same ms). Resolution must be DETERMINISTIC — the higher
      // item hash wins — so both machines converge regardless of which side they hold. We assert that by
      // running it both ways (local=low/incoming=high AND local=high/incoming=low) → same winner.
      testTask "same-millisecond tie resolves deterministically by hash (higher wins, both ways)" {
        let mk suffix : PT.PackageLocation =
          { owner = "Scenario"; modules = [ "Tie" ]; name = uniqueName suffix }
        let lowH, highH = hashChar 'a', hashChar 'b' // 'b' > 'a' → highH wins
        let tie = "2025-01-01T00:00:00.000Z" // one exact stamp both sides share
        let refOf h = PT.Reference.fromHashAndKind (PT.Hash h, PT.ItemKind.Fn)

        let runTie (localH : string) (incomingH : string) : Task<Option<string>> =
          task {
            let loc = mk "tie"
            let localOp = PT.PackageOp.SetName(loc, refOf localH)
            let! _ =
              Inserts.insertAndApplyOpsWithOrigin
                PT.mainBranchId
                None
                [ localOp ]
                (Map.ofList [ (Inserts.computeOpHash localOp, tie) ])
            let! _ =
              Sync.applyRemoteOps
                (uniqueName "rtie")
                PT.mainBranchId
                None
                [ (1L, tie, PT.PackageOp.SetName(loc, refOf incomingH)) ]
            return! liveHash loc
          }

        // incoming holds the higher hash → incoming wins
        let! a = runTie lowH highH
        Expect.equal a (Some highH) "higher hash wins (incoming was higher)"
        // local holds the higher hash → incoming is stale, local stays — SAME winner
        let! b = runTie highH lowH
        Expect.equal b (Some highH) "higher hash wins (incoming was lower → stale)"
      }

      testTask
        "multi-divergence batch (default policy): every location surfaces + LWW converges" {
        // The SHIPPED path: a single pull carrying TWO divergent bindings. The default policy
        // reconciles nothing (surface-as-data); each location converges to its own LWW winner.
        let mk suffix : PT.PackageLocation =
          { owner = "Scenario"; modules = [ "Multi" ]; name = uniqueName suffix }
        let loc1, loc2 = mk "m1", mk "m2"
        let local1, incoming1 = hashChar 'a', hashChar 'b'
        let local2, incoming2 = hashChar 'c', hashChar 'd'
        let remote = uniqueName "rmulti"
        let! divs1 =
          setupDivergentPull loc1 PT.ItemKind.Fn local1 -120.0 incoming1 -60.0 remote
        let! divs2 =
          setupDivergentPull loc2 PT.ItemKind.Fn local2 -120.0 incoming2 -60.0 remote
        let divs = divs1 @ divs2
        Expect.equal (List.length divs) 2 "two divergences collected from the batch"
        let! reconciled =
          Sync.routeDivergences defaultDispatch callCtx remote PT.mainBranchId divs
        Expect.equal reconciled 0 "default policy reconciles nothing (surface-as-data)"
        let! w1 = liveHash loc1
        let! w2 = liveHash loc2
        // both incoming are newer-by-creation → each location converges to the incoming hash
        Expect.equal w1 (Some incoming1) "first location converged to its LWW winner"
        Expect.equal w2 (Some incoming2) "second location converged to its LWW winner"
      }

      testTask
        "keep-local re-stamp makes our op the newest-by-creation (rides sync to peers)" {
        let loc : PT.PackageLocation =
          { owner = "Scenario"; modules = [ "Prop" ]; name = uniqueName "p" }
        let local, incoming = hashChar 'a', hashChar 'b'
        let remote = uniqueName "rprop"
        // local -120, incoming -60: incoming is the newer-by-creation that won the pull
        let! divs =
          setupDivergentPull loc PT.ItemKind.Fn local -120.0 incoming -60.0 remote
        let localRef = PT.Reference.fromHashAndKind (PT.Hash local, PT.ItemKind.Fn)
        let localOpId = Inserts.computeOpHash (PT.PackageOp.SetName(loc, localRef))
        let incomingRef = PT.Reference.fromHashAndKind (PT.Hash incoming, PT.ItemKind.Fn)
        let incomingOpId = Inserts.computeOpHash (PT.PackageOp.SetName(loc, incomingRef))
        let originTs (id : System.Guid) : Task<string> =
          Sql.query "SELECT origin_ts FROM package_ops WHERE id = @id LIMIT 1"
          |> Sql.parameters [ "id", Sql.uuid id ]
          |> Sql.executeRowAsync (fun read -> read.string "origin_ts")
        let! _ = Sync.routeDivergences keepLocalDispatch callCtx remote PT.mainBranchId divs
        // after keep-local, OUR op's origin_ts is re-stamped to now — strictly newer than the
        // incoming's stamp. A peer re-pulling reads our op's adjacent (newer) origin_ts and, by the
        // same timestamp-LWW, re-adopts our hash. Convergence, not divergence forever.
        let! localStamp = originTs localOpId
        let! incomingStamp = originTs incomingOpId
        Expect.isGreaterThan
          localStamp
          incomingStamp
          "our op is now the newest-by-creation (the re-stamp rides sync so peers re-adopt local)"
      }

      // An incoming binding OLDER (by creation) than ours is stale — it's recorded as a divergence
      // (surfaced as data) but does NOT rebind. The opposite of the default scenarios (incoming newer).
      testTask "incoming older than local → local stays, no rebind (still recorded)" {
        let loc : PT.PackageLocation =
          { owner = "Scenario"; modules = [ "Older" ]; name = uniqueName "o" }
        let localH, incomingH = hashChar 'a', hashChar 'b'
        let remote = uniqueName "rold"
        // local newer (-30), incoming older (-90) → incoming loses by timestamp-LWW
        let! divs =
          setupDivergentPull loc PT.ItemKind.Fn localH -30.0 incomingH -90.0 remote
        Expect.equal (List.length divs) 1 "the divergence is still surfaced as data (recorded)"
        let! winner = liveHash loc
        Expect.equal winner (Some localH) "local stays — the older incoming op did not rebind"
      }

      // Order-independence: whichever side holds the newer-by-creation op, both machines converge to
      // it — proven by running the SAME race with the newer hash on opposite sides.
      testTask "order-independent: both machines converge to the newer op regardless of arrival side" {
        let mk suffix : PT.PackageLocation =
          { owner = "Scenario"; modules = [ "Order" ]; name = uniqueName suffix }
        let a, b = hashChar 'a', hashChar 'b'
        // machine-1: local=a (older), incoming=b (newer) → b wins as the incoming
        let loc1 = mk "ord1"
        let! _ = setupDivergentPull loc1 PT.ItemKind.Fn a -120.0 b -60.0 (uniqueName "ro1")
        let! w1 = liveHash loc1
        // machine-2: local=b (newer), incoming=a (older) → b stays as the local
        let loc2 = mk "ord2"
        let! _ = setupDivergentPull loc2 PT.ItemKind.Fn b -60.0 a -120.0 (uniqueName "ro2")
        let! w2 = liveHash loc2
        Expect.equal w1 (Some b) "machine where b arrived incoming: b (newer) won"
        Expect.equal w2 (Some b) "machine where b was local: b (newer) stayed — same winner"
      }

      // After a divergent pull converges, re-pulling the SAME op is a clean no-op: no new divergence
      // (the binding already equals the incoming), no flip (INSERT OR IGNORE dedups the op).
      testTask "re-pulling an already-applied divergent op is a no-op (no new conflict, no flip)" {
        let loc : PT.PackageLocation =
          { owner = "Scenario"; modules = [ "NoOp" ]; name = uniqueName "np" }
        let localH, incomingH = hashChar 'a', hashChar 'b'
        let remote = uniqueName "rnoop"
        let! _ =
          setupDivergentPull loc PT.ItemKind.Fn localH -120.0 incomingH -60.0 remote
        let! winner1 = liveHash loc
        Expect.equal winner1 (Some incomingH) "incoming (newer) won the first pull"
        // re-deliver the same incoming op
        let incomingRef = PT.Reference.fromHashAndKind (PT.Hash incomingH, PT.ItemKind.Fn)
        let! (_cursor, divs2) =
          Sync.applyRemoteOps
            remote
            PT.mainBranchId
            None
            [ (1L, relTs -60.0, PT.PackageOp.SetName(loc, incomingRef)) ]
        Expect.isEmpty divs2 "re-pulling the same op surfaces no new divergence"
        let! winner2 = liveHash loc
        Expect.equal winner2 (Some incomingH) "binding unchanged after the idempotent re-pull"
      }
    ]
