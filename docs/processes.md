# Processes and the scheduler

A running computation is a value the runtime can step, park, resume and
inspect; one thread runs many of them, and a group of worker threads (one per
core) runs many more. Reads run concurrently on their own and writes keep
their order; `Exec.spawn`/`await` run chosen work in the background. A run is
an execution: kept with the log of what it did to the world, suspended by
Ctrl-C, resumed or forked by replaying that log, moved to another machine as
a file. A lambda a builtin applies is a frame on the process's own stack; a
builtin that needs the host names the operation and the loop performs it.
`dark docs processes` is the short, user-facing version of this document.

In one paragraph: a process is a `VMState` plus the `ExecutionState` it runs
under plus a status. A scheduler steps a process until it finishes, has to
wait for something, or spends its instruction budget. A waiting process is
parked on the task it waits for; when that completes, an event lands on the
scheduler's queue and the scheduler thread resumes the process. A preempted
process goes to the back of the line. Keys, timers and store changes arrive
on the same queue, so `readKey` parks instead of holding the thread. A
scheduler is one thread; a process spawned on a worker scheduler runs on
another core for its whole life, sharing nothing with its neighbours but the
state's concurrent caches.

---

## What a process is

`LibExecution/Scheduler.fs`:

- `Process`: `vm`, `exeState`, `entry` (the function it was spawned on, or
  `EntryExpr`), `parent`, `started`, `status`, `slices` (how many times the
  budget was refilled), the completion F# callers await, and the park state.
- `Status`: `Runnable | Parked of Parked | Done of Dval | Failed of rte * stack`.
- `Parked`: what a parked process waits for, for `ps`. `OnBuiltin name`
  (a `sleep`, an HTTP call, the script an `eval` runs), `OnPackageFn hash`,
  `OnLambda`, `OnRareOpcode` (the interpreter waiting on the store),
  `OnEvent specs` (a `Host.await`).

`VMState` was already self-contained (its own frames, frame pool, caches), so
wrapping it cost nothing. The one field added is `budget`.

## The step

`Interpreter.executeSync exeState vm : StepOutcome` is the scheduler's whole
view of the interpreter:

- `StepDone dv`: the root frame returned.
- `StepBudget`: the budget ran out with work left. Nothing is half done; the
  counter sits on the instruction that has not run.
- `StepAwait (wait, resume)`: something has to be waited for. `wait` is the
  builtin's or package call's task; `resume` writes its result into the frame's
  register (or, for a request made after the wait, pushes the callable's
  frame) and is run on the scheduler thread when the process's turn comes.
  For the rare opcodes and the deferred return-type check, `wait` is a task
  that advances the VM itself as it completes, and `resume` does nothing.

There is one loop. `executeSync` runs frames until something has to be waited
for and answers a `StepOutcome`; `awaitOf` turns the wait into `(wait,
resume)` at the bail site. `executeSync` is that loop; an unscheduled run
(`execute`: tests, the LSP, a host running a function itself) is
`driveToEnd`, fifteen lines that await each `wait` in place and step again.
Its budget is negative, so it never sees `StepBudget`. The task-based second
loop (`executeInnerTask`, with `handleFrameStep` re-deciding every wait) is
gone: a wait is decided once, where it happens, and the scheduler and the
plain run differ only in who waits.

## The budget

`runSyncInstructions` counts `vm.budget` down by one per instruction and stops
at zero; `runFrame` reports that as `FrameBudget`. The default quantum is
10,000 instructions (`Scheduler.defaultQuantum`), refilled before every slice.
A negative budget means unlimited, which is what every VM nobody schedules runs
with. (A callable a builtin applies runs as a frame of the same VM now, so it
is preempted like anything else; the borrowed-VM case is gone.)

Measured with `scripts/perf/bench ab` against the pre-change binary: within
noise on `interp-arith`, `interp-list` and `eval-listheavy` (median paired
difference +0.0%, -0.3%, -0.5%; IQRs 14 to 24 ms). The instrument cannot
resolve a 1% change either way, so the per-instruction check stayed and the
fallback (check on jumps and calls only) was not needed.

## The one rule

Only a process's own scheduler thread steps it. Everything else (the reader
thread, timer callbacks, the store poll, Ply continuations, other schedulers)
only posts to the queue. `Step` checks the thread id and raises if it is ever
wrong.

The one exception is documented at `StepOutcome`: a rare opcode's deferred
completion writes the VM it belongs to, on whatever thread completes it. The
process is parked meanwhile and nothing looks at its VM until the completion
has posted, so it is exclusive, not shared.

## The event queue and its sources

`LibExecution/HostEvents.fs`. One `Queue` per scheduler; one console and one
store per OS process, so the reader thread and the store poll are
process-wide (`HostEvents.Shared`) and deliver to whichever queue asked:

- `Key of KeyRead`: from the stdin reader thread. It starts on the first
  `Key` subscription and reads one key per request, delivered to the queue
  that requested it (requests from several schedulers are served oldest
  first, one read in flight), so a run that never waits on a key never
  touches the console, and nothing eats keys meant for a `readLine` after a
  TUI has quit. Redirected stdin never starts it: `readKey` answers Escape at
  once, as it always did.
- `Timer id`: a one-shot `System.Threading.Timer` armed per `Timer ms` spec,
  disposed when something else satisfies the subscription; a late fire posts
  an id nobody wants and is dropped.
- `StoreChanged`: a poll of `PRAGMA data_version` every `Scheduler.storePollMs`
  (200 ms) on a connection of its own (the pragma answers per connection),
  posted to every queue watching. It carries nothing: which ops landed is
  Dark's question, answered by `Stdlib.Live.poll` inside `Stdlib.Host.await`
  before the loop sees the event. Latched per process: a process that
  subscribes after a change it has not been told about is woken at once, so
  a change during a render is not lost.
- `Completed pid`: internal; the parked task finished.
- `ExecDone pid`: a process finished, well or badly, for Dark subscribers
  (`Exec.await` says how). Posted to the schedulers with a subscriber for it,
  which may not be the one that ran it; a subscription to a process already
  over, or forgotten, is answered at once.
- `Wake`: nothing to route; the loop, blocked with nothing runnable, looks
  again (a spawn from another thread, a `Stop`).

The sources `LibExecution` cannot provide itself (the console, the store) are
installed by the host: `Stdin.fs` installs the key source, `Cli.fs` the store
version.

## Cores: workers

A `Scheduler` is one loop on one thread. `Scheduler.Workers` is a group: the
root plus N more schedulers, each looping on a background thread of its own
(`dark-worker-<i>`), started the first time anything asks for them. N is
`exec.workers` in the store's config (`dark config set exec.workers 4`), or
one per core, which is the default nobody needs to set; `Cli.fs` reads it
into `Scheduler.defaultWorkers` before the root starts. No environment
variable shadows it.

- `root.SpawnOn(...)` spawns on the least loaded worker (fewest runnable or
  parked processes); the process runs there for its whole life. `Spawn`
  keeps it on the calling scheduler.
- `Await` is the process's completion task and works from anywhere. `ps` and
  `kill` from any scheduler in the group see and reach every process in it.
- The Http server's request handlers and `Exec.spawn` are what use the
  workers; the CLI's root and each `eval` expression stay on the root. A CLI
  run that never spawns on a worker never starts the threads.
- Measured on the shared desktop (a Threadripper 3960X), warm: four
  CPU-bound processes on four workers finish in 0.59 to 0.65 of the
  one-thread wall time published, 0.34 in Debug. Not the 1/4 an idle
  machine would give a compute loop, and the scheduler is not why: four
  plain `Interpreter.execute` calls on four threads, with or without a
  shared state, scale the same, and so does a plain F# loop that only
  allocates (546 ms alone, 871 ms four at once), while a loop that only
  computes scales nearly perfectly. The interpreter allocates per value,
  so the allocator's scaling is its ceiling; server GC and a larger gen0
  budget did not move it. The allocation work in `docs/perf/roadmap.md` is
  therefore also the multi-core work. The test bounds the ratio at 0.8.
- A spawn onto a worker costs about 9 us in Debug (10,000 spawns of a
  trivial program in 90 ms, including the placement scan and the `Wake`).

## What a process shares and what it owns

The audit of `ExecutionState`, field by field, for two processes on two
threads under one state. The short answer: the interpreter already ran on
many threads with one state (the Http server's handlers, the parallel test
suite), and the caches were made concurrent for that (`fix-types-cache-race`,
August 2026), so a process copies almost nothing.

Shared by reference, safe as is:

- `lambdaInstrCache`, `packageFnCallCache`: `ConcurrentDictionary`, keyed by
  a lambda's expression id and a package fn's content hash, holding immutable
  compiled data. Shared on purpose: a lambda created in one process is
  callable from another (an `eval` expression's lambda from an HTTP handler,
  say), which a copy-on-spawn would break. The one mutable inside,
  `PackageFnCallData.policy`, is a memo of an immutable record whose owner is
  compared by reference; two writers racing both write a correct answer.
- `Types.find`'s declaration cache: a `ConditionalWeakTable` of
  `ConcurrentDictionary`, per `Types` instance.
- `builtins`, `types`, `fns`, `values`, `blobs`, `program`, `access`,
  `packagePolicy`, `isBundledPackageFn`, `accountID`, `branchId`, the flags,
  `reportException`, `notify`: immutable, or functions over the package
  manager, whose own caches are content-keyed and concurrent.
- The SQLite connection: not on the state at all; every statement takes a
  pooled connection (`Pooling=true`), so there is nothing per thread to keep.

Shared, and written under a lock:

- `deniedRequests`, `permissionWarnings`: the host installs a list, runs the
  script, reads the list. A child's denial has to land in the parent's list,
  so they are shared and the two appends (`PermissionCheck.raiseDenial`,
  `recordPermissionViolation`) lock. Denials are rare; the lock is never hot.
- `test`: the test context's counters. Tests only; left as they are.

One process's own:

- `tracing`: the recorder keeps a call stack to pair frame entries with
  exits, and one stack cannot hold two processes' frames. At spawn the
  scheduler asks the parent's tracer for a per-process view
  (`Tracing.forProcess pid`, `Scheduler.stateForProcess`): the same event
  list, the process's own stack, and every event stamped with the process id
  and a `seq` across the whole trace. `noTracing` answers itself, so an
  untraced spawn copies nothing. See "Traces" below.
- `VMState`: never shared; that was already the rule.

So `stateForProcess` is one record copy when tracing is on and the parent's
state itself when it is off.

## Reads are concurrent

The user-facing rule, in one paragraph: a call whose effects are all reads
(a file, env, db, package or trace read; an HTTP GET or HEAD) that has to
wait does not stop your program. You get its result back at
once, as a read still in flight, and the program runs on; the first thing
that looks at the value waits for it. Every write (`print`, `File.write`, a
POST, a db write) runs when it is reached, in program order. So

```
let pages = List.map urls (fun u -> HttpClient.get u [])  // every GET in flight, at once
Stdlib.printLine "fetching"                              // a write: runs now
let first = List.head pages                              // looks at the list: waits for all
Cli.FileSystem.writeFile out first.body                  // in order
```

`Stdlib.await x` forces a read now rather than at its first use; it is the
identity function, since calling anything with the value is what forces it,
and it works on a list of reads as well as one. (`Exec.await h` is the one
for a handle; the two share the word and not the module, since one function
cannot be typed as both `'a -> 'a` and `Handle<'a> -> 'a`.)

How it works (`Interpreter.Promises`, `RuntimeTypes.Promise`):

- At the builtin call site, when the builtin's `Ply` is not finished and the
  call is deferrable, the register gets a `DPromise` (the task, the builtin's
  name and the frame's execution point) instead of the process parking. A
  call is deferrable when every call of the builtin is a read
  (`Effects.readsOnly`: its effects are all reads, or it is the HTTP
  client's GET and HEAD builtin, `httpClientRead`, which `Effects.fs` names
  since `http` stays one word in the permission language and a policy grants
  a URL, not a method; `HttpClient.get`, `head`, and `request "GET"` go
  through it). `Clock` and `Random` are not read effects either: reading
  them never waits, and `sleep`, the one clock call that does, is a wait the
  program means to take (it was deferred in a first cut, and `let _ = sleep`
  then slept nobody). A read that finishes synchronously (most file and db
  reads in this runtime) is never a promise; only a real wait is.
- A promise is only ever at the top level of a register, a frame's result, or
  a builtin's returned value. Every instruction that inspects, stores or
  passes a value forces it first: `Apply` forces the callee and every
  argument (so no builtin body ever sees one, and `await` is an identity
  function), record, enum, list, tuple, dict and string construction force
  their parts, a closure forces what it closes over, `if`, `||`, `&&`, match
  and let patterns force what they look at, and the end of a run forces its
  result. A bare `let x = ...` copies without looking, which is what keeps a
  read in flight across the statements after it. Returning a promise from a
  function is fine (a wrapper handing back its builtin's result); the return
  type check lets it through, since the builtin's own return type was checked
  when the value was made or is when it lands (`TypeChecker.tryUnifySync`,
  `Dval.toValueType` says `Unknown`).
- Forcing: the instruction does not run; `Promises.settle` replaces a landed
  promise with its value and the instruction runs again at once, or, for one
  still in flight, the frame stops with the counter on the instruction
  (`FrameAwaitForce`) and the process parks on the task, exactly as it parks
  on a builtin. Under a plain `execute` the task loop awaits it.
- A read that failed raises at the force point, with the read's own error and
  the frames it was called from added below the stack (`vm.nestedCallStack`),
  so the report names both sites. A denial is raised at the call, before
  anything is in flight: the ambient effect check runs before the body. The
  corollary: a read that had to wait and whose value nothing ever looks at
  has no force point, so its failure is never raised (`let _ = HttpClient.get
  bad []` followed by code that never reads it succeeds). Reads that finish
  on the calling thread raise at the call as they always have; only a real
  wait is deferred. `Stdlib.await x` is the way to say the failure matters.
  Forcing every leftover promise at a frame's return would make each return
  a barrier; deciding whether the end of a run should is a follow-up.
- `List.map` is promise-aware (`mappedListOrPromise` in `Builtins.Pure`'s
  `List.fs`): a lambda that returns a read in flight hands it back rather
  than being forced at the end of its run, and the map's result is one
  promise for the whole list, landing when every element has. Everything else
  that applies a lambda gets the lambda's result forced. So `List.map urls
  (fun u -> HttpClient.get u [])` is where the reads fan out; the same lambda
  under `List.filter` would run them one by one.
- The bound: at most `Promises.maxInflight` reads in flight per OS process,
  256, fixed rather than a setting (past it the program is saturating
  whatever it reads from). Past it a read is awaited in program order, so a
  map over a hundred thousand urls does not open a hundred thousand sockets.
- Tracing: the builtin's result is recorded when it lands (the recording is
  inside the builtin's own `Ply`); the trace's `seq` is completion order. A
  builtin that combined reads (`List.map`) records its value when the
  combination lands. Under tracing an awaiting builtin's arguments are copied
  before the wait, since the frame's argument buffer is reused once the frame
  runs on.
- Cost when nothing is in flight: one type test per operand on the
  instructions above. The gate is unchanged. A first cut restructured
  `finishBuiltin` around a `match` on the result and cost 200 bytes per
  builtin call (the `uply` arm's closure was built on every call); the check
  moved into `tryUnifySync` instead. Keep it there.

Measured: three reads under `List.map` are all in flight before anything
waits, and the statement after the map runs while they are; two reads in
program order with a write between them: the write runs before either lands;
a failed read raises at `await` with "after the call" already run; the bound
holds (`Scheduler.Tests.fs`, the reads group).

## `Exec.spawn`, `await`, `awaitWithin`, `select`, `cancel`

`Exec.spawn f` starts `f ()` as a process of its own on a worker (the least
loaded), under the access the caller had at the spawn, like a closure, and
hands back a `Handle<'a>`; `Exec.await h` is the value it finished with, or
its error raised again with the child's frames kept below the caller's;
`Exec.awaitWithin ms h` is `None` after `ms` milliseconds and leaves the
process running; `Exec.select hs` is the first to finish with its value.
`Exec.cancel h` asks the process to stop, and everything it spawned that was
not `spawnDetached`; a parent's end does the same to its children, so a
program that spawned and never awaited leaves nothing running (the tree
below, and "`dark ps`" for the hard variant). `spawn` carries the
`Concurrency` effect, ambient and allowed by the default instance policy: a
spawned process can do nothing the spawner could not. An install whose policy
was seeded before this effect existed needs `dark permissions allow
concurrency` once. `List.parallelMap` is `spawn` per element then `await` in
order, for work that computes; reads run concurrently under plain `List.map`
already.

From a run nobody scheduled (a test's `execute`, the LSP, an HTTP handler)
`spawn` uses a process-wide scheduler with workers of its own
(`Scheduler.CurrentOrShared`), started on first use, and `await` blocks that
thread on the completion as any builtin wait would.

## No host re-entry: a builtin asks, the interpreter applies

A builtin that takes a callable used to apply it by running a nested VM on the
host stack (`Execution.executeApplicable`): the lambda's frames were invisible
to `ps`, could not be preempted by the budget, and a read in the lambda held
the .NET stack. The list builtins now ask instead (`Interpreter.requestApply`):

- The builtin's body calls `requestApply vm applicable arg moreArgs next` and
  returns what it returns (a placeholder). The interpreter, at the call site,
  sees the request (`VMState.pendingNext` and the three slots beside it: no
  record, so a chain of a thousand applications allocates nothing for them),
  pushes the callable's frame in the same VM from the calling frame, with
  `next` on it (`CallFrame.continuation`), and stops the drain as it would for
  any pushed frame. A builtin or package function passed as the callable is
  called through the ordinary paths and its result driven straight on; a
  partial application answers the applied lambda, as `Apply` does.
- When that frame returns, its result does not go into the caller's register:
  `returnFromFrame` hands it to `next`, and `drive` looks at what `next`
  answered. A further request (the next element) pushes the next frame at
  once; a value ends the chain, into the register the `Apply` named, with the
  frame's counter moved past it, and `finish` records the builtin's result in
  the trace, since the builtin's own return was the placeholder; a wait (a
  `next` that awaits) parks the process on it (`FrameAwaitContinuation`) and
  drives on when it lands.
- `Interpreter.withValue` is for a continuation that has to look at the
  callable's result (a predicate, a key, a fold's accumulator): it waits for a
  read still in flight first, through the same wait. `List.map` and its kin
  carry the result along unlooked-at, so a read in a mapped lambda stays in
  flight and the list comes back as one promise, as before.
- A request is usually the body's first move, and the call site sees it.
  A body that had to wait first (a stream pulling from the network, then
  applying its transform) may still ask: its wait lands where its result
  would have gone into the register, and that landing (`landBuiltin`, in
  all three loops) pushes the frame instead and picks up what records the
  chain's result from the VM (`pendingFinish`). Not for a read: its wait
  would have been handed back as a promise with the request inside it, so
  that raises `requestApply after the first await of a read`. A continuation
  may await and then request; that is `drive`'s ordinary path.
- The frame runs under the builtin's applying access narrowed by what the
  callable captured, exactly as `Apply` narrows a frame's. Errors inside the
  lambda propagate through the process's own frames, so the stack names the
  lambda without `nestedCallStack`.

Migrated: `List.map`, `indexedMap`, `map2shortest`, `fold`, `filter`,
`filterMap`, `findFirst`, `any`, `sortBy` (`Builtins.Pure/Libs/List.fs`), each
with one continuation over two mutable cells rather than a closure per
element. `Dict`, `Option`, `Result` and `String` have no re-entry on this
branch (they are Dark, or take no callable).

Streams (`Stream.unfold`, `map`, `filter`; `Builtins.Pure/Libs/Stream.fs`):
a transform node holds its callable, not a closure over it (`StreamImpl.
Unfold/Mapped/Filtered`), and a pull is a step machine (`Stream.pull`):
`Pulled` an element, `Apply` this callable to this element and continue, or
`Wait` on native IO and continue. The pulling builtin (`next`, `toList`,
`toBlob`) drives it: an `Apply` is a `requestApply`, so the transform runs
as a frame of the pulling process, its answer forced (`withValue`) and
handed back to the pull; a `Wait` is waited for, and a request after it is
the landing case above. The access re-intersection that used to happen on
every pull happens once, when the transform is built: the builder's active
access is folded into the callable (`narrowedBy`), and the frame push
narrows the puller's access by it, as `Apply` narrows any frame's. So a
narrow producer's transform stays narrow under a wide consumer, and the
deferred-execution matrix in `PermissionsGate` still holds. F# code that
owns a native stream (the HTTP client's body, tests) pulls with
`Stream.readNext`, which drives `Wait` and raises on `Apply`: a stream that
runs Dark code is pulled from a Dark process.

Not migrated, and why:

- `HttpServer.fs` (the per-request handler, `onListening`): the handler runs
  on a pool thread through `executeApplicable`. The plan's leaf makes it a
  spawned process on a worker (`Scheduler.SpawnApply` is there for it); left
  for the live track, whose file it is, since it changes `serve`'s latency
  shape and is measured by `scripts/perf/http`.

Measured, the list family: gate 9.5 MB against 9.4 (exact totals 9,463,000
against 9,428,440 bytes: +0.4%, inside the 0.8% noise band; the first cut,
with a record per request and a closure per element, was 11.1 MB, +19%);
`bench ab` before against after, interp-list -1.7% (13 of 15 pairs faster),
eval-listheavy -0.1%, eval-map1000 -0.4%, interp-arith +1.5% (2 of 15;
arith applies no lambda, so that is the bigger step structs or noise).
Tests (`Scheduler.Tests.fs`): a process parked inside `List.map f` shows the
lambda's frame in `ps` and resumes; a tight loop inside a mapped lambda is
preempted and another process runs between the slices; an error inside a
mapped lambda names the lambda's frame; all 6,734 testfile cases pass over
the migrated builtins.

Measured, the stream family: gate 9.5 MB, unchanged (the reference workload
has no stream); `bench ab` before against after, eval-stream (3,000
elements through a map and a filter) -1.8%, 14 of 15 pairs faster;
interp-arith +0.5% (noise). Tests: a process parked inside a stream
transform shows the lambda's frame in `ps` and resumes; a transform over a
stream whose source waits on the host before every element (a test stream
built like a network one) runs as a frame, scheduled and unscheduled; the
stream testfiles and the SSE parser (an `unfold` whose step pulls bytes)
pass unchanged.

## Traces

`trace_fn_calls` rows carry `process_id`, `seq` and `ord`
(`migrations/schema/08-traces.sql`; existing stores get the columns from
`LibDB/Releases.fs`, with `''`, `0` and `-1` for old rows). `seq` is assigned
as calls complete, under the tracer's lock, across every process writing the
trace: all the rows in `seq` order are the interleaving. `ord` is an effectful
builtin call's ordinal among its process's effectful calls, taken when the
call is made (`Tracing.nextEffect`), so a read that lands late keeps its
place; one process's rows in `ord` order are its log, and what a replay keys
on. `Tracing.FnCall` in Dark carries `processId : Option<Uuid>` and `seq`. A
run nobody scheduled writes `''`.

Trace detail has three levels (`DARK_CONFIG_TRACE_DETAIL`): `off`; `effects`,
the classic rule (only builtin calls with non-empty `callEffects`, with their
ordinals; no frames, no pure calls; the interpreter keeps its fast paths); and
`on`, every call, frame and lambda, the tree `traces view` renders, which
carries the effect log too. The default is `effects`, the level that makes
every run resumable; it is thin enough to leave on because retention keeps
the tables bounded: after a store, the oldest traces past `trace.keep` (200
unset) or `trace.maxMb` (256 unset) of logged args and results go, except one
a suspended execution still needs. What the log holds is what the effects
were given and returned: an `Authorization` header, a key file's bytes, an
env value are in it in the clear; a secret you do not want on disk is one
to keep out of an effect's arguments, or run with `off`.

## Executions

A traced `eval` or `run` is an execution (`LibDB.Executions`, the
`executions` table): its input (the expression or the script's source, the
same the trace row stores), its trace, its status (`running`, `done`,
`failed`, `suspended`), and, for a fork, the execution and the position it
branched from. `dark exec` lists them; `exec show`, `exec resume`, `exec fork
[--at <position>]`. `Stdlib.Exec.Execution` is the Dark side (`list`, `get`,
`fork`, `armResume`).

- Ctrl-C during a traced run: the CLI's handler stores the log as it stands,
  marks the execution suspended, prints the resume command and leaves
  (`Cli.fs`, `installSuspendOnInterrupt`; `Executions.Foreground`). A TUI
  reading keys takes Ctrl-C as input and never gets here. A run the suspend
  took out of the foreground stores nothing more if it goes on (a test's does;
  the CLI's has exited).
- `resume`: `armResume` then the same input through the ordinary `eval` or
  `run` path; the script runner takes the armed resume in place of a fresh
  tracer (`Tracing.createReplayTracer`). Every effectful call whose
  `(process, ordinal)` the log has is answered from it, and not performed: a
  replayed `printLine` is echoed dimmed, so the person resuming sees where
  the run had got to without the world seeing it twice. A logged call the
  log cannot stand in for (an `Exec.spawn`, an OS subprocess, an open HTTP
  stream: a live handle the old process owned) stops the resume at that
  step, naming it, and the run stays suspended. A logged file read whose file has changed
  since the run was recorded warns and continues on what it read then. The
  first ordinal a process asks for that the log lacks ends that process's
  replay for good, so nothing later in the log can be handed to it after a
  live call; from there the run is live, still recording, and the stored
  trace ends up as the replayed prefix plus what ran after. The recorded
  process ids are the recorded run's; a resumed run's processes are matched
  to them in the order they first appear in the log, which is the order a
  script's expressions start in. A run nobody scheduled (a plain `execute`,
  as in the test harness) records and replays under one process id.
- `fork`: a new execution with the same input, a new trace holding the
  parent's rows with `seq` below the position (the whole log with no
  `--at`), suspended; resume it and it diverges where the log ends. Cutting
  by `seq` can leave a process's later ordinals without earlier ones, which
  the rule above turns into "live from the first hole".
- Replay after a package edit: the input is re-parsed, so names resolve to
  the new code, and the effects come from the log: the new pure code runs
  against the old I/O. Live's H9 (live values) can start from this.
- The determinism audit (every pure builtin, run twice on the same inputs,
  must agree): a scan of every builtin declared with no effects for the
  nondeterministic APIs (guids, clocks, random, environment, hash codes,
  unordered enumeration) finds three, all host facts read live and not
  recorded, by design: `cliTerminalColorEnabled` (the terminal's colour
  support), `interpreterStatsEnableDetailedTiming` and `interpreterStatsGet`
  (dev instrumentation). A replay in another terminal renders for that
  terminal. Dark's dict is an ordered map, so enumeration is deterministic.
  `uuidGenerate` declares `Random` and is in the log.

Tested in `CliExec.Tests.fs`: record and resume, fork at a position, suspend
mid-way and resume, replay after an edit, export and import, retention
(the count cap sparing a suspended run, the byte cap sparing the newest),
the echo and the refusal.

## A run on another machine

`dark exec export <id> [<file>]` writes an execution as one text file: its
row, its trace row, and the trace's effect log, blobs base64, versioned by
the first line. `dark exec import <file>` on another machine stores those
rows with their ids kept, status suspended, and `dark exec resume <id>`
takes it up there: the log answers every call it has, then the run goes
live. A bundle carries no code: the other side resolves the log's hashes
from its own store, so it needs the same package code (a synced branch).
Moving the file is the person's: `scp`, a shared folder, an attachment.
Through the relay would be a `/exec` route and storage on the relay; not
built, since the relay carries package ops only and lives on its own deploy.

## Host operations are requests: a builtin names, the loop performs

A builtin that touches the OS used to call `PermissionCheck.performHost` from
inside its `uply` body: the check, the wait and the result were all inside a
builder the loop could only park on as an opaque task. It names the operation
instead (`Interpreter.requestHost vm op next`; in `Builtins.Cli`: `File`,
`Directory`, `Environment`, `Execution`, `Posix`; in the HTTP client: the
guest request and stream open, and the sync transport's GET and POST):

- The body puts the `Host.Operation` and a continuation on the VM
  (`VMState.pendingHostOp`, `pendingHostNext`; no record, same as an apply
  request) and returns a placeholder. Right after the body returns,
  `invokeBuiltin` sees the request and performs it through the one checked
  boundary (`PermissionCheck.performHostWithAccess`, under the body's access),
  then hands the outcome to `next`; a continuation may name another operation,
  or ask for an apply, and is driven the same way (`performRequested`). The
  body itself is a value again: no builder, nothing awaited inside it.
- A synchronous operation (every file, directory, environment and libc call:
  microseconds, and a pool hop would cost more than the wait) completes on the
  spot and nothing parks. One that waits (an HTTP request; a process run or a
  round of process IO, which `Host.blocking` moves to the pool so a `sleep 10`
  in one process does not stall a scheduler's others) parks the process as any
  wait does, and the VM records the operation (`hostInflight`) so `ps` says
  `the host: process-run /bin/bash` rather than the builtin's name.
- A body that had to wait before it could name the operation (`File.write` of
  a persisted blob reads the bytes from the store first) names it from the
  continuation of that wait, and the landing performs it. Rare; the
  ephemeral-blob case, which is nearly every write, names it at once.
- Denials and rejections raise at the call as before: the check runs on the
  loop's thread, before anything is performed, under the same access the body
  ran with.

Two OS-facing calls still perform from inside the body, on purpose.
`httpGetUnsafeBytesStart` starts a sync GET and hands back a handle for
`httpAwaitBytes` to collect: the point is not to wait, so it has no
continuation to give the loop. The HTTP server's bind is performed under the
child guest state's access, not the calling frame's, and `serve` then runs
its listener in the same body; the bind is synchronous, so nothing parks
there anyway.

The host boundary itself (`Host.perform`: resolve, check, execute, audit) did
not move. What moved is who calls it: the loop, from one line, for every
OS-facing builtin, which is the shape the Rust port wants (an operation is a
value the host answers) and what lets `ps` name the wait.

## An HTTP request is a process

`serve` spawns each request's handler as a process on a worker, with the
server's process as its parent: `ps` shows it under the server with its own
frames, the budget can preempt it, a read inside it is a value in flight, and
a slow handler never holds up another (a request that sleeps 800 ms sits
beside three that answer at once; the batch takes one slow request, not four).
The handler's outcome is the response:

- It returns a `Http.Response`: that is the response.
- It raises: 500, the body says `The handler failed: <the error>`.
- It runs past the request timeout: the server cancels it (politely, so
  what it has on the host completes and its children stop with it) and
  answers 504, `The handler ran for more than N ms and was cancelled`. The
  limit is the store's `http.requestTimeoutMs`, read once when `serve`
  starts; 30 s unset; 0 means no limit.
- It is stopped from outside (`dark ps cancel`/`kill` on the request's
  process): 503, `The request was stopped: <reason>`.
- The router has no usable version (a live `serve` whose newest router
  fails its checks and has no last good one): 503, `Service Unavailable`.

A finished leaf process (a request, a spawned read) skips the group-wide
scan for children: `childCounts` says whether it ever had any. That scan
was most of a request's cost as a process.

## `Host.await`, the contract

```
module Darklang.Stdlib.Host
type EventSpec = Key | StoreChanged | Timer of ms: Int64 | ExecDone of id: Uuid
type Change = Unknown
type Event = Key of KeyRead | StoreChanged of Change | Timer | ExecDone of id: Uuid
let await (specs: List<EventSpec>) : Event
```

Under the scheduler the calling process parks on the first spec to fire.
Outside one (a plain `execute`) it blocks the thread polling, which is what the
live track's shim does today and what the rebase deletes. `Builtin.hostAwait`
declares `{Stdin; PackageRead}` statically, the union of what any spec could
need.

`readKey ()` is unchanged for users; under the scheduler its body is "park on
`[Key]`, return the key".

## Entry points

- `Cli.fs` `main`: the entry function is the root process of a fresh scheduler
  that runs on the main thread until it finishes. (`DARK_SCHEDULER=off`, in
  `Cli.fs`, runs the old plain path; a bisect switch for whoever is asking
  whether an oddity is the scheduler's, not a setting.)
- `cliParseAndExecuteScript`: each expression is a child process of the CLI's,
  awaited in order. So a script budget-yields, a `readKey` in it parks, and
  `ps` lists it. Daemons are launched as `eval` and ride the same path.
- `execute` everywhere else, unchanged.

## `dark ps`

`Stdlib.Exec.list/inspect/cancel` over `Builtin.execList/execInspect/execCancel`
(`Builtins.Language/Libs/Exec.fs`), and `Builtin.execKill` called by id from
`cli/ps.dark`, the one place; rendered by `cli/ps.dark` as a tree, a process
under the one that spawned it. Rows are copies;
nothing hands Dark a reference into a running VM. A process on a worker is
snapshotted from another thread: its call stack is read best-effort (a frame
popped under the read comes back as no frames, never a fault). One group per
OS process, so `dark ps` from a shell is the CLI alone; from inside an `eval`
it is the CLI parked on `cliEvaluateExpression` plus the expression's process,
with `ps show` giving both call stacks.

Two ways to stop a process, one asymmetry: `cancel` (`Exec.cancel`, `ps
cancel`) lets what the process is doing on the host complete before it stops
at its next turn, while `kill` (`ps kill`) gives a parked process that turn
at once and abandons what it waited for, which is the escape hatch for a
process stuck in a call that never returns. Both set a reason the process
fails with ("cancelled", "killed from ps"); a wait on events
(`Host.await`, `readKey`) is cut by either, since nothing is in flight
there; a running process finishes its slice first, so a short program that
stops itself completes. Both reach the process's undetached children, now
and again when it finishes (`Scheduler.Stop`, `StopChildrenOf`), as hard or
as politely as the parent's stop was; a child spawned with
`Exec.spawnDetached` is left alone. Tested: cancel lets a gate land, then
stops; children die with the parent unless detached; `awaitWithin` times out
and the child is cancelled after; a kill of a parent reaches a child stuck on
the host at once (`Scheduler.Tests.fs`, the cancellation group).

## Every Dark process on the machine

`dark ps` from any shell starts with the other Dark processes on the box: a
`serve`, a daemon, another terminal's TUI. Each CLI writes one file under
`<rundir>/run/ps/<pid>.json` at startup (pid, the title a system monitor
shows, the command line, the branch, when it started) and removes it at exit;
a reader drops any whose pid is gone, which is what survives a crash. The
registry is the outer ring, one row per OS process; what is inside another
process (its own Dark process tree) is its own, so `ps` shows this instance's
tree under the machine's rows. Reaching into a row is a signal: `ps cancel
<pid>` sends INT, the Ctrl-C path that suspends a traced run; `ps kill <pid>`
sends KILL. `ps --watch` repaints both tables every half second with a cursor
(`c`, `k`, Escape), and the workbench's Processes pane lists the machine's
rows under its own.

What a daemon is, in these terms: one OS process (`dark apps daemon-main
<slug>`, so its title is `dark <slug>`), whose root process is the daemon's
step loop; anything it spawns is a child in its own table. The registry
outlives the CLI that started the daemon, since the daemon writes its own row.

## The scheduling policy, in Dark

Which runnable process a scheduler steps next is round robin: the one that
has waited longest. That is F#, and the default. An expert setting, for
someone studying scheduling rather than using Dark: a store can name a Dark
function instead:

    dark config set exec.policy Darklang.Stdlib.Exec.Policy.youngestFirst

The function takes the runnable
processes of the scheduler that is asking, as `List<Stdlib.Exec.Summary>`,
oldest first, and answers `Option<Uuid>`: the one to step, or `None` for no
preference. `Stdlib.Exec.Policy` ships `roundRobin`, `youngestFirst` and
`leastRunFirst`; a policy of your own is any function of that shape.

What holds it honest:

- It is asked only when there is a choice, two or more runnable on the same
  scheduler, between slices. One process at a time never pays for it, and
  neither does a process alone on its worker.
- It runs on the scheduler's own thread, outside the scheduler's lock, as an
  unscheduled run of its own, untraced. It may call `Exec.list`. It should be
  quick and should not wait: the whole scheduler waits with it.
- An answer that names no runnable process, or a failure, counts as no
  preference for that turn, and the first failure is said once on stderr. A
  name that does not resolve is said at start and ignored.
- The scheduler still owns the budget and the parking: a policy chooses among
  the runnable, it cannot keep a process past its slice or wake a parked one.

Each scheduler asks for itself; with workers, that is per core.

## Edges

What is deliberately not here, and where the seams are:

- `LiveValues.fs` applies a callable through `executeApplicable`, on a VM of
  its own: it runs a function for inspection, not as part of a program.
- A policy chooses which runnable process to step, not where a spawn lands:
  `Exec.spawn` goes to the least loaded worker, in F#.
- A resume matches recorded processes to new ones by start order; a run that
  spawned from Dark may not line up. `resume` is the CLI's, since it runs the
  input through the CLI's own paths; `Exec.fork` from Dark exists.
- `ps show` says how many reads a process has in flight, not which, and shows
  the call stack, not registers.
- A builtin's signature is still `Ply<Dval>`. Store-facing builtins (`DB`,
  the package manager, traces, executions, the CLI host's script runner)
  await SQLite through `LibDB`, which has no asynchronous I/O, so they
  complete on the calling thread and the loop sees finished values rather
  than parking; a request form for them would be a store-operation type over
  some sixty queries that buys nothing while the store is in-process. A pure
  builtin that reads a persisted blob still waits for the store inside
  `uply`; an ephemeral one answers without a builder.
- `sleep` parks on its timer task, not on a `Timer` event; `ps` says `sleep`.
- `Event.ExecDone` carries only the id (a Dark enum cannot hold an untyped
  value); `Exec.await` is how a value comes back.
- Runs moved to another machine are files; a relay route would be its own
  change on the relay's own deploy.
- The stdin reader thread has not been checked on Windows.
