/// Runtime helpers for the `Stream<'a>` Dval type.
///
/// Streams are lazy, single-consumer, non-persistable. This module
/// owns the construction, drain, and disposal mechanics; the
/// transform-tree shape (`StreamImpl`'s `Mapped` / `Filtered` /
/// `Take` / `Concat`) is defined alongside the types in
/// `RuntimeTypes.fs`.
module LibExecution.Stream

open System.Threading.Tasks

open Prelude

open LibExecution.RuntimeTypes


/// Walk a StreamImpl tree invoking any IO-source disposers. Wrapped
/// in try/with so a misbehaving disposer doesn't take the whole
/// runtime down — best-effort cleanup. Safe to call multiple times:
/// the disposers themselves must be idempotent (e.g. `response.Dispose()`
/// on .NET HttpResponseMessage is a no-op if already disposed).
let rec disposeImpl (impl : StreamImpl) : unit =
  match impl with
  | FromIO(_, _, Some d, _) ->
    try
      d ()
    with _ ->
      ()
  | FromIO(_, _, None, _) -> ()
  | Mapped(src, _, _) -> disposeImpl src
  | Filtered(src, _) -> disposeImpl src
  | Take(src, _, _) -> disposeImpl src
  | Concat streams -> streams.Value |> List.iter disposeImpl


/// GC-triggered cleanup for DStreams that callers never explicitly
/// drain or close. Doubles as the DStream's lockObj so the lifetime
/// tracks the DStream itself — the GC can only finalize this object
/// when the DStream holding it is unreachable. On finalize, runs the
/// full [disposeImpl] chain once (guarded by the shared `disposed`
/// ref, so no double-fire if streamClose/drain-to-EOF already ran).
///
/// Carries a permit-1 SemaphoreSlim that the consumer paths
/// (`readNext`, `readChunk`) try to claim immediately. A second
/// concurrent consumer hits the contended path and raises a clean
/// `concurrent consumer` error rather than racing silently. Reachable
/// via the `lockObj : obj` slot on `DStream`; consumers cast back
/// through `Finalizer.consumerLock`. Disposed via `Dispose` from
/// the finalizer/streamClose chain so the OS handle is released.
///
/// Swallows disposer exceptions — finalizers that throw crash the
/// process, and we'd rather leak on the pathological case than take
/// down everything.
type Finalizer(impl : StreamImpl, disposed : bool ref) =
  let consumerLock = new System.Threading.SemaphoreSlim(1, 1)
  member _.ConsumerLock = consumerLock
  override this.Finalize() =
    try
      if not disposed.Value then
        disposed.Value <- true
        disposeImpl impl
        consumerLock.Dispose()
    with _ ->
      ()


/// Wrap a [StreamImpl] in a fresh DStream with its own disposed flag
/// and a GC-backed finalizer. When the DStream becomes unreachable,
/// the GC finalizes the Finalizer (which is also the lockObj) and
/// the disposer chain runs once.
///
/// Used by `newFromIO` and by the Stream transform builtins
/// (streamMap/streamFilter/streamTake/streamConcat) when they wrap a
/// source's impl into a new DStream.
let wrapImpl (impl : StreamImpl) : Dval =
  let disposed = ref false
  DStream(impl, disposed, Finalizer(impl, disposed) :> obj)


/// Try to claim the per-stream consumer lock without blocking. Used
/// by `readNext` and `readChunk` to detect concurrent consumers —
/// the second consumer hits the contended path and we raise a clean
/// error rather than racing on shared `disposed`/`carry` state.
///
/// Cast back through the `Finalizer` because `DStream`'s `lockObj`
/// slot is `obj` (kept opaque so the lifecycle is always managed
/// through `wrapImpl`/`Finalizer`).
let private tryAcquireConsumerLock (lockObj : obj) : bool =
  match lockObj with
  | :? Finalizer as f -> f.ConsumerLock.Wait(0)
  | _ -> true // unknown lock object — be permissive rather than crash

let private releaseConsumerLock (lockObj : obj) : unit =
  match lockObj with
  | :? Finalizer as f -> f.ConsumerLock.Release() |> ignore<int>
  | _ -> ()


/// Mint a fresh DStream from a pull function. Convenience wrapper
/// over [wrapImpl] for the common FromIO case. [disposer], when
/// `Some`, is called once when the stream is drained to completion,
/// `streamClose`d, or finalized by the GC — used by IO-backed
/// producers to release the underlying source (HttpResponseMessage,
/// FileStream, etc.). Use [newChunked] for byte streams that can
/// efficiently yield a whole chunk per pull.
let newFromIO
  (elemType : ValueType)
  (next : unit -> Task<Option<Dval>>)
  (disposer : (unit -> unit) option)
  : Dval =
  wrapImpl (FromIO(next, elemType, disposer, None))


/// Mint a DStream<UInt8> that can be drained bulk-wise via
/// [readChunk]. The `nextChunk` callback fills up to `maxBytes` into
/// a fresh byte[] and returns it (or None on exhaustion). Consumers
/// that want full-chunk bytes — `streamToBlob`, SSE byte
/// accumulators — bypass per-byte Ply/Dval boxing by calling
/// `readChunk` instead of `readNext`. The `next` path stays
/// available so `streamNext` returns one DUInt8 at a time as before
/// — the implementation synthesises single-byte pulls from the
/// chunk buffer.
let newChunked
  (elemType : ValueType)
  (nextChunk : int -> Task<Option<byte[]>>)
  (disposer : (unit -> unit) option)
  : Dval =
  // Maintain a small carry buffer so single-byte `next` pulls can
  // be served from the chunks that `nextChunk` returned.
  let carry = ref [||]
  let carryPos = ref 0
  let next () : Task<Option<Dval>> =
    task {
      if carryPos.Value >= carry.Value.Length then
        // Refill from the underlying chunked producer. 8 KB mirrors
        // the socket-read buffer size we use across the codebase.
        let! chunk = nextChunk 8192
        match chunk with
        | None -> return None
        | Some buf ->
          carry.Value <- buf
          carryPos.Value <- 0
          if buf.Length = 0 then
            return None
          else
            let b = buf[0]
            carryPos.Value <- 1
            return Some(DUInt8 b)
      else
        let b = carry.Value[carryPos.Value]
        carryPos.Value <- carryPos.Value + 1
        return Some(DUInt8 b)
    }
  wrapImpl (FromIO(next, elemType, disposer, Some nextChunk))


/// Pull one element through a [StreamImpl]. Separate from [readNext]
/// so the recursion can walk transform nodes
/// (Mapped/Filtered/Take/Concat) without re-entering the root's
/// disposed flag — nested transforms share the wrapping DStream's
/// lifecycle.
let rec private pullImpl (impl : StreamImpl) : Task<Option<Dval>> =
  task {
    match impl with
    | FromIO(next, _elemType, _disposer, _nextChunk) -> return! next ()

    | Mapped(src, fn, _elemType) ->
      let! upstream = pullImpl src
      match upstream with
      | None -> return None
      | Some v ->
        let! mapped = fn v
        return Some mapped

    | Filtered(src, pred) ->
      // Pull from source until the predicate accepts or the source
      // runs dry. Written as a mutable loop rather than tail recursion
      // so a long rejection run doesn't blow the state-machine chain.
      let mutable result : Option<Dval> = None
      let mutable keepGoing = true
      while keepGoing do
        let! upstream = pullImpl src
        match upstream with
        | None -> keepGoing <- false
        | Some v ->
          let! matches = pred v
          if matches then
            result <- Some v
            keepGoing <- false
      return result

    | Take(src, _n, remaining) ->
      if remaining.Value <= 0L then
        return None
      else
        let! upstream = pullImpl src
        match upstream with
        | Some _ ->
          remaining.Value <- remaining.Value - 1L
          return upstream
        | None ->
          // Source dried up before the limit; clamp so future pulls
          // stay at zero and short-circuit without touching source.
          remaining.Value <- 0L
          return None

    | Concat streams ->
      // Pull from the head stream; when it's exhausted, drop it and
      // try the next. Mutating the ref means future pulls don't
      // re-enter a drained stream.
      let mutable result : Option<Dval> = None
      let mutable keepGoing = true
      while keepGoing do
        match streams.Value with
        | [] -> keepGoing <- false
        | head :: tail ->
          let! pulled = pullImpl head
          match pulled with
          | Some _ ->
            result <- pulled
            keepGoing <- false
          | None -> streams.Value <- tail
      return result
  }


/// Pull the next element from a stream. Returns [None] when the
/// stream is exhausted; subsequent calls after exhaustion return
/// [None] (single-consumer — once drained, stays drained).
///
/// Single-consumer enforcement: each DStream carries a permit-1
/// SemaphoreSlim on its `lockObj` (the `Finalizer`). `readNext`
/// claims the permit non-blockingly at entry; a second concurrent
/// consumer hits `Wait(0) = false` and we raise an internal error
/// rather than racing on shared `disposed`/transform-node state.
/// The permit is held across the inner `pullImpl` await — it's
/// released in the `finally`, so even a raised lambda inside `fn v`
/// / `pred v` can't strand the lock.
///
/// TODO per-element state-machine cost: every `next` allocates a
/// state machine. A 1000-element pipeline with three transforms is
/// ~3000 allocations. The chunked `nextChunk` fast path covers byte
/// streams; element-wise streams pay full cost. A future iteration
/// could replace `Task<'a>` on this hot path with a custom
/// `Future<'a>` struct or a CPS interpreter with a fiber scheduler.
let readNext (dv : Dval) : Task<Option<Dval>> =
  task {
    match dv with
    | DStream(impl, disposed, lockObj) ->
      if disposed.Value then
        return None
      else if not (tryAcquireConsumerLock lockObj) then
        return
          Exception.raiseInternal
            "concurrent consumer on a single-consumer DStream"
            []
      else
        try
          let! result = pullImpl impl
          match result with
          | Some _ -> return result
          | None ->
            disposed.Value <- true
            disposeImpl impl
            return None
        finally
          releaseConsumerLock lockObj
    | _ -> return Exception.raiseInternal "readNext: expected DStream" []
  }


/// Pull up to `maxBytes` bytes from a byte stream as one chunk.
/// Returns [None] when exhausted; subsequent calls stay [None].
/// Prefers a FromIO's own `nextChunk` when present; falls back to
/// byte-wise pulls for streams that were built via [newFromIO] or
/// that walk transform nodes (Mapped/Filtered/Take/Concat) where a
/// chunk semantic isn't well-defined.
///
/// Used by `streamToBlob` and SSE byte accumulators to amortise the
/// Ply-continuation cost across whole chunks rather than paying it
/// per byte.
let readChunk (maxBytes : int) (dv : Dval) : Task<Option<byte[]>> =
  task {
    match dv with
    | DStream(impl, disposed, lockObj) ->
      if disposed.Value then
        return None
      else if not (tryAcquireConsumerLock lockObj) then
        return
          Exception.raiseInternal
            "concurrent consumer on a single-consumer DStream"
            []
      else
        try
          match impl with
          | FromIO(_, _, _, Some nextChunk) ->
            let! chunk = nextChunk maxBytes
            match chunk with
            | Some buf when buf.Length > 0 -> return Some buf
            | _ ->
              disposed.Value <- true
              disposeImpl impl
              return None
          | _ ->
            // Fallback: pull byte-by-byte. Only pays off vs per-byte
            // `readNext` if the caller really wants bulk bytes —
            // transform chains lose the chunk optimisation but still
            // drain correctly.
            use collected = new System.IO.MemoryStream()
            let mutable keepGoing = true
            let mutable bytesSoFar = 0
            while keepGoing && bytesSoFar < maxBytes do
              let! pulled = pullImpl impl
              match pulled with
              | Some(DUInt8 b) ->
                collected.WriteByte b
                bytesSoFar <- bytesSoFar + 1
              | Some _ ->
                Exception.raiseInternal
                  "readChunk: expected Stream<UInt8> element"
                  []
              | None -> keepGoing <- false
            if bytesSoFar = 0 then
              disposed.Value <- true
              disposeImpl impl
              return None
            else
              return Some(collected.ToArray())
        finally
          releaseConsumerLock lockObj
    | _ -> return Exception.raiseInternal "readChunk: expected DStream" []
  }
