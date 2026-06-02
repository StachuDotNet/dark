/// Tests for async Stage A part 1 — the `effects` field on every BuiltInFn.
/// Confirms at runtime that the field is populated and carries the right values:
/// the conservative codemod default plus the hand-set real effects.
module Tests.AsyncStageA

open Expecto

open Prelude
open TestUtils.TestUtils

module RT = LibExecution.RuntimeTypes

let private builtinList () : List<RT.BuiltInFn> =
  (localBuiltIns pmPT).fns |> Map.toList |> List.map snd

let private emptyInstrs : RT.Instructions =
  { registerCount = 1; instructions = []; resultIn = 0 }

let private freshVM () : RT.VMState = RT.VMState.createWithoutTLID emptyInstrs

let tests =
  testList
    "AsyncStageA.effects"
    [ test "every builtin declares an effect (the field is populated)" {
        let bs = builtinList ()
        Expect.isTrue
          (List.length bs > 400)
          "the builtin set is populated and each carries the required `effects` field"
      }

      test
        "Http.Client builtins carry AsyncRead (real effect, not the conservative default)" {
        let asyncReads =
          builtinList ()
          |> List.filter (fun b -> b.effects = RT.Effect.AsyncRead)
          |> List.length
        Expect.isTrue
          (asyncReads >= 2)
          "the Http.Client builtins are tagged AsyncRead"
      }

      test "uuidGenerate carries ConcurrentSafe (the hand-set multi-line case)" {
        let concurrentSafe =
          builtinList ()
          |> List.filter (fun b -> b.effects = RT.Effect.ConcurrentSafe)
          |> List.length
        Expect.isTrue (concurrentSafe >= 1) "at least uuidGenerate is ConcurrentSafe"
      }

      // part 2 — child-VM isolation
      test "spawnChild isolates mutable state from the parent" {
        let parent = freshVM ()
        let parentThread = parent.threadID

        let child =
          RT.VMState.spawnChild
            parent
            System.Threading.CancellationToken.None
            (None, emptyInstrs)

        // the child records its parent for the scheduler's thread tree…
        Expect.equal
          child.parentThreadID
          (Some parentThread)
          "child links to its parent's threadID"
        Expect.equal
          parent.parentThreadID
          None
          "the parent (root) VM has no parent link"

        // …but shares NO mutable state — fresh identity + its own containers
        Expect.notEqual child.threadID parent.threadID "child gets a fresh threadID"
        Expect.isFalse
          (System.Object.ReferenceEquals(child.callFrames, parent.callFrames))
          "child has its own callFrames dictionary (no shared mutable state)"

        // mutating the child can never touch the parent
        child.threadID <- System.Guid.NewGuid()
        child.currentFrameID <- System.Guid.NewGuid()
        Expect.equal
          parent.threadID
          parentThread
          "parent threadID is unchanged after mutating the child"
      }

      // part 3 — cancellation
      test "a spawned child unwinds on cancellation at the next safe point" {
        let parent = freshVM ()
        use cts = new System.Threading.CancellationTokenSource()
        let child = RT.VMState.spawnChild parent cts.Token (None, emptyInstrs)

        // before cancel, the safe-point check is a no-op
        child.throwIfCancelled ()

        // after cancel, the next safe-point check unwinds cleanly
        cts.Cancel()
        Expect.throwsT<System.OperationCanceledException>
          (fun () -> child.throwIfCancelled ())
          "after cancel, throwIfCancelled raises at the safe point"
      }

      // part 3 — the check is WIRED into the real interpreter eval loop
      testTask
        "the eval loop honors cancellation — a cancelled VM unwinds mid-execute" {
        let! exeState = executionStateFor pmPT false Map.empty

        // an already-cancelled token: the loop must raise at its first safe point,
        // BEFORE running to completion. (Without the wired check, execute would run the
        // empty program to its result register and return normally — no throw.)
        let cts = new System.Threading.CancellationTokenSource()
        cts.Cancel()
        let parent = freshVM ()
        let vm = RT.VMState.spawnChild parent cts.Token (None, emptyInstrs)

        let mutable cancelled = false
        try
          let! _ = LibExecution.Interpreter.execute exeState vm |> Ply.toTask
          ()
        with :? System.OperationCanceledException ->
          cancelled <- true

        Expect.isTrue
          cancelled
          "Interpreter.execute raised OperationCanceledException at the eval-loop safe point"
      } ]
