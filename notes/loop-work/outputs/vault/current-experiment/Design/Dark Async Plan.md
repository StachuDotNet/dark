
Goal
----

Support scalable async/concurrency without exposing async/await, Task, Ply, or
other host-runtime concepts in Dark source code.

Dark stays direct-style:

  let a = HttpClient.get url1 []
  let b = HttpClient.get url2 []
  combine a b

Function calls still return values. Internally, the runtime can suspend,
schedule, overlap, cancel, and trace work when it is safe to do so.


What We Have
------------

-Host-level async plumbing already exists. Execution.execute returns Task, and
  the interpreter/builtins run through Ply<Dval>.
- Builtin calls can already suspend internally because BuiltInFn.fn returns
  DvalTask/Ply<Dval>.
- Prelude.Task has mapInParallel, mapWithConcurrency, iterWithConcurrency, and
  semaphore helpers.
- The HTTP server already accepts concurrent requests via GetContextAsync and
  Task.Run per request.
- The HTTP client already uses nonblocking .NET calls such as SendAsync,
  ReadAsStreamAsync, CopyToAsync, and streaming response bodies.
- DStream exists for lazy stream processing, including chunked byte reads.
- Tracing exists for function/frame call recording and SQLite storage.
- Coarse purity metadata exists via Previewable = Pure | ImpurePreviewable |
  Impure, and SQL queryability metadata exists via SqlSpec.


What's Missing
--------------

- No effect system. Builtins/package functions do not say "async read",
  "ordered IO", "concurrent-safe", "DB write", "network", "blocking", etc.
  Previewable is not enough.

- No package-function effect metadata. Package functions only carry hash,
  params, return type, and body. No effects are stored or inferred:
  backend/src/LibExecution/RuntimeTypes.fs:1377.

- No dependency DAG scheduler. The interpreter executes linear instructions in
  one mutable VMState. It does not find independent calls and schedule them
  concurrently.

- No child VM/fiber model. VMState is mutable and single-lane: current frame,
  call frames, registers, stats. Parallel branches would need isolated child
  VMs.

- No structured concurrency. There is no parent/child task tree, cancellation
  propagation, sibling cancellation policy, or error aggregation model.

- No cancellation through the whole runtime. HTTP server has a cancellation
  token for listener shutdown, HTTP client creates local timeout tokens, but
  Dark execution itself is not token-threaded end to end.

- Blob lifetime is still a concurrency hazard. Ephemeral blobs use
  ExecutionState.blobStore plus blobScopes : Stack<HashSet<Guid>>:
  backend/src/LibExecution/Blob.fs. The design note already calls out the race
  class.

- Streams are explicitly single-consumer and unenforced. The comments call this
  a latent bug under concurrent consumers:
  backend/src/LibExecution/Stream.fs:197.

- Tracing is not task-aware. It records call events, but not async spans, branch
  IDs, parent/child task relationships, suspension points, cancellation, or
  parallel timing.

- Runtime stats/test state are mutable and not concurrency-ready.
  InterpreterStats and TestContext are mutable per execution; parallel branches
  would need merging or isolation.


What Needs To Change
--------------------

1. Add a real Effect model to BuiltInFn and PackageFn.

   Start conservative:

     Pure
     Deterministic
     AsyncRead
     AsyncWrite
     OrderedIO
     ConcurrentSafe
     Blocking
     Resource(Stream/File/DB/Process)
     Harmful

2. Infer package function effects from their bodies.

   Builtins declare effects manually. Package fns get the union of called
   functions plus local constructs. Unknown means ordered/unsafe.

3. Keep Dark source direct-style.

   No async, await, Task, or Ply exposed. Function calls still return values.

4. Add a dependency planner over lowered code.

   Build data dependencies from registers/instructions, then add effect
   dependencies for ordered/resource-sensitive operations.

5. Add a scheduler.

   Ready nodes whose effects are concurrent-safe can run in parallel, bounded
   by runtime limits. Ordered effects stay serialized.

6. Run branches in isolated child VMs.

   Do not share one mutable VMState. Share only safe ExecutionState components,
   and make everything else branch-local.

7. Replace blob scope/store design.

   Best fix: BlobRef.Ephemeral of byte[] instead of UUID lookup through
   ExecutionState. That removes scope stacks and request/fiber lifetime races.

8. Enforce stream ownership.

   Either make streams affine/single-owner at runtime, or guard
   readNext/readChunk with a Ply-safe async lock and fail on concurrent
   consumption.

9. Make tracing task-aware.

   Add task/span IDs, parent IDs, start/end timestamps, suspension points,
   cancellation/error status, and deterministic display ordering.

10. Thread cancellation through execution and IO.

   Execution.execute, builtin signatures, HTTP client, streams, process IO,
   sleep/retry, DB calls, and HTTP handlers should all see a runtime
   cancellation context.


Best Path
---------

First do effect metadata + conservative inference, then child VM execution,
then explicit internal scheduler for simple independent calls.

Keep auto-parallelization narrow at first: independent ConcurrentSafe AsyncRead
calls like multiple HttpClient.gets. Expand only after blobs, streams, tracing,
and cancellation are made concurrency-safe.


Phased Implementation Plan
--------------------------

Phase 0: Design Constraints and Semantics

- Confirm the product rule: no user-facing async syntax or async return type.
- Define direct-style evaluation semantics:
  - Normal values remain normal values.
  - Sequential ordering is preserved for ordered effects.
  - Independent concurrent-safe async work may overlap.
- Define failure semantics:
  - If one auto-parallel branch fails, decide whether siblings cancel or finish.
  - Prefer structured concurrency: parent owns children, no orphan work.
- Define scheduling limits:
  - Global default max concurrency.
  - Per-resource limits for network, DB, process, file, and streams.

Deliverable:

- Short design note with effect categories, scheduling rules, and error policy.


Phase 1: Effect Metadata

- Add Effect types in RuntimeTypes.
- Extend BuiltInFn with effects.
- Extend PackageFn representations with effects where persisted/runtime package
  metadata needs it.
- Mark builtins manually, conservatively.
- Keep unknown package functions ordered/unsafe until inference lands.

Initial builtin classifications:

- Pure deterministic:
  - string/list/math/json construction, basic transforms, type conversions.
- AsyncRead + ConcurrentSafe:
  - HttpClient.get/request where response is fully buffered.
  - HTTP stream setup only if stream body ownership is handled separately.
- OrderedIO:
  - printLine, stdout/stderr, terminal prompts.
- AsyncWrite or OrderedIO:
  - DB writes, file writes, package manager mutations.
- Blocking:
  - sleep, process wait, polling loops, any sync file/process API.
- Resource(Stream):
  - streamNext, streamToList, streamToBlob, streamClose.
- Resource(Process):
  - process spawn/read/kill/wait.

Deliverable:

- Builtins compile with explicit effect metadata.
- No behavior change yet.


Phase 2: Package Effect Inference

- Walk package function bodies/instructions and collect effects from every
  called function.
- Add local effects for language constructs if needed.
- For unresolved or unknown calls, infer OrderedIO/UnknownUnsafe.
- Store or cache inferred effects in the package manager/runtime lookup path.
- Add tests:
  - Pure wrapper around pure builtin stays pure.
  - Wrapper around HttpClient is AsyncRead + ConcurrentSafe only if all called
    effects allow it.
  - Wrapper around printLine/DB write becomes ordered.
  - Unknown function reference is not auto-parallelizable.

Deliverable:

- Effect lookup works for both builtins and package functions.


Phase 3: Runtime State Safety Foundations

- Replace BlobRef.Ephemeral Guid with BlobRef.Ephemeral byte[].
- Remove or stop depending on ExecutionState.blobStore and blobScopes.
- Update all Blob.newEphemeral/read/promote callers.
- Update serializers/trace preparation/tests.
- Enforce stream ownership:
  - Add stream state that can detect active pull.
  - Fail clearly on concurrent stream consumption.
  - Preserve current single-consumer behavior for normal use.
- Isolate mutable stats/test state:
  - Make branch-local stats and merge after child completion.
  - Make test side-effect counters thread-safe or branch-local with merge.

Deliverable:

- Existing sequential behavior preserved.
- Blob and stream concurrency hazards reduced before scheduling parallel Dark
  work.


Phase 4: Cancellation Context

- Add an execution context carrying CancellationToken and scheduling metadata.
- Thread it through:
  - Execution.execute / executeApplicable / executeFunction.
  - BuiltInFnSig.
  - HTTP client request and stream reads.
  - HTTP server handler execution.
  - streams, process IO, sleep/retry, file IO, DB calls where available.
- Add cancellation tests:
  - HTTP handler shutdown cancels work.
  - Timed-out HTTP request cancels body read.
  - Stream close/dispose runs on cancellation.
  - Process cleanup happens on cancellation.

Deliverable:

- Runtime can cancel an execution tree without leaking streams/processes.


Phase 5: Child VM/Fiber Execution

- Add a child execution runner that evaluates a subexpression/call in an
  isolated VMState.
- Share only safe ExecutionState components:
  - package/lambda instruction caches.
  - package manager lookups.
  - immutable program data.
- Keep registers, call frames, current frame, stats, and pending trace state
  branch-local.
- Define result handoff from child VM back to parent register.
- Add tests for independent child execution and error propagation.

Deliverable:

- Runtime can run two Dark computations concurrently without sharing VMState.


Phase 6: Dependency Planner

- Build a planner for lowered instructions.
- Track register reads/writes to determine data dependencies.
- Add effect dependencies:
  - OrderedIO nodes depend on prior ordered effects.
  - Same-resource unsafe nodes serialize.
  - Unknown effects serialize.
  - ConcurrentSafe AsyncRead nodes can overlap when data-independent.
- Start with simple let-binding/function-call shapes.
- Do not try to optimize every instruction class initially.

Deliverable:

- Planner can identify independent async-safe call groups.


Phase 7: Scheduler MVP

- Add scheduler that runs ready DAG nodes.
- Bound concurrency with runtime limits.
- Preserve result ordering in registers.
- Preserve deterministic error reporting as much as possible.
- Initially enable only narrow cases:
  - Independent ConcurrentSafe AsyncRead calls.
  - No stream-consuming calls.
  - No DB writes.
  - No process/file writes.
- Add feature flag to disable auto-concurrency.

Deliverable:

- Multiple independent HttpClient.get/request calls can overlap automatically.


Phase 8: Task-Aware Tracing

- Add trace task/span IDs.
- Record parent task/span ID.
- Record start/end timestamps and duration per async branch.
- Record status: ok, runtime error, uncaught exception, canceled.
- Record suspension/await points if practical.
- Keep CLI trace display deterministic:
  - display parent-child tree.
  - preserve source order for siblings unless sorted by time explicitly.
- Update trace storage schema if needed.

Deliverable:

- Parallel execution can be debugged from traces.


Phase 9: Expand Effect Coverage

- Audit all stdlib/builtins:
  - HTTP client/server.
  - streams.
  - CLI process.
  - CLI file.
  - POSIX/sleep/flock.
  - DB/package manager/traces.
  - random/time.
  - print/log/terminal.
- Mark resource-specific effects.
- Add resource-aware scheduling:
  - DB reads can overlap if SQLite pool/transaction rules allow.
  - DB writes serialize or run transactionally.
  - file writes to same path serialize.
  - process handle operations serialize per handle.
  - stream reads serialize per stream.

Deliverable:

- Scheduler handles more than HTTP without changing observable ordered effects.


Phase 10: Stress, Compatibility, and Rollout

- Add stress tests:
  - parallel HTTP requests.
  - blob creation/promotion under concurrency.
  - trace storage under parallel branches.
  - cancellation during HTTP/stream/process work.
  - DB write contention.
  - stream concurrent consumption failure.
- Add benchmarks:
  - sequential vs auto-parallel HTTP calls.
  - scheduler overhead on pure/sequential code.
  - trace overhead.
- Add rollout controls:
  - feature flag.
  - telemetry for scheduler decisions.
  - fallback to sequential path on unsupported shapes.

Deliverable:

- Production-ready opt-in, then gradual default-on rollout.


Estimated Timeline
------------------

MVP: 2-4 weeks

- Effect metadata for builtins.
- Conservative package effect inference.
- Basic child VM runner.
- Simple planner/scheduler for independent ConcurrentSafe AsyncRead calls.
- Basic parallel HttpClient tests.

Usable safe version: 6-10 weeks

- Blob redesign.
- Stream ownership enforcement.
- Structured concurrency and cancellation.
- Task-aware tracing.
- Bounded concurrency limits.
- Expanded effect coverage.
- Stress tests.

Production-grade: 3-5 months

- Robust effect inference.
- Resource-aware scheduling.
- Full cancellation/resource cleanup.
- Trace UI/CLI support for parallel trees.
- Performance tuning.
- Backward compatibility validation.
- Gradual rollout controls.


Open Decisions
--------------

- Should auto-parallelization be default-on after MVP, or opt-in behind a flag
  until traces/resource semantics mature?
- On one branch failure, should siblings cancel immediately or continue and
  aggregate errors?
- Should HTTP POST be ConcurrentSafe by default, or OrderedIO unless explicitly
  marked idempotent/reorderable?
- How much effect metadata should be persisted vs inferred/cached at load time?
- Should streams be affine at the language/runtime level, or just guarded by a
  runtime lock/error?
- Do DB reads get ConcurrentSafe initially, or stay ordered until transaction
  semantics are clearer?


Immediate Next Steps
--------------------

1. Land Effect type definitions and BuiltInFn.effects.
2. Mark a small builtin subset:
   - pure basics.
   - HttpClient request/get as AsyncRead + ConcurrentSafe.
   - printLine as OrderedIO.
   - stream ops as Resource(Stream).
3. Implement package effect inference for direct function calls.
4. Redesign BlobRef.Ephemeral to hold byte[].
5. Add child VM execution primitive.
6. Build planner/scheduler for independent HTTP calls behind a feature flag.
