# Remote Access and Control

Remote-control of a peer is not a new subsystem — it is a **mode that reuses the
sync wire and the identity stack**. The transport, addressing, and auth are the
ones [sync.md](../stable-and-syncing/sync.md) already specifies; the actor invoking a command is the
same `Identity` that authors ops in [identity.md](identity.md). This doc covers
the wire: addressing, auth, endpoints, and exec. Agent-runtime concerns (process
model, spawning, observing, cross-instance agent identity) are out of scope here.

The goals:

- **Sync** — a function written on the laptop is on `major` and the reMarkable
  within seconds. (This is just [sync.md](../stable-and-syncing/sync.md); listed for contrast.)
- **Control** — `dark on major run BatchProcess.run` invokes a fn on the GPU box
  from the reMarkable.
- **Discovery** — `dark devices` shows all my peers with last-sync and current
  activity.
- **Offline resilience** — `dark on offline-box ...` queues the call and replays
  when the peer reconnects.

## Stance: lean on Tailscale, don't build a network stack

The same stance as [sync.md](../stable-and-syncing/sync.md). Tailscale gives us, for free:

- Stable per-device addressing (MagicDNS `<machine>.<tailnet>.ts.net`).
- TLS (`tailscale serve --https=443`).
- Identity via the `Tailscale-User-Login` HTTP header.
- E2E-encrypted transport between peers.
- NAT traversal with DERP relay fallback.
- ACLs via tags + grants in HuJSON policy.
- Public exposure via `tailscale funnel` (rate-limited, deliberate).

What Tailscale does *not* give us: no L2 peer discovery (use the API/gossip); no
`tsnet` F# SDK (F# speaks via HTTP plus the `tailscale` CLI); a small free-tier
user limit (fine for personal/small-team); occasional DERP-fallback latency.

The substrate **doesn't require** Tailscale — over the open internet the same
protocol works with normal TLS plus a bearer-token header. Tailscale is the
convenient default for tailnet members.

## The wire

### Discovery

```darklang
let peers = Tailscale.devices ()  // calls `tailscale status --json`
peers |> List.iter (fun p -> Stdlib.printLine $"{p.name}: {p.status}")
```

Or:

```
$ dark devices
laptop     online   last-sync: 2s ago
major      online   last-sync: 1s ago
remarkable online   last-sync: 3m ago
```

Implementation: shell out to `tailscale status --json`, parse it, and augment
with per-device `last-sync` from local sync state plus `/sync/whoami` (see
[sync.md](../stable-and-syncing/sync.md)). The device list rendered here is a **projection** over
Tailscale status and local sync state, not a stored table.

### `dark on <peer> <cmd>` — remote execution

```
$ dark on major run BatchProcess.run --gpu
```

What happens:

1. The CLI resolves `major` → MagicDNS `major.<tailnet>.ts.net`.
2. HTTPS POST to `https://major.<tailnet>.ts.net/exec`.
3. Tailscale terminates TLS and injects
   `Tailscale-User-Login: stachu@stachu.net`.
4. The receiver maps the login → `account_id` (the same mapping
   [sync.md](../stable-and-syncing/sync.md) uses) and resolves it to an `Identity`
   ([identity.md](identity.md)).
5. The receiver verifies that identity holds `CapRemoteExec` for this device.
6. The receiver runs the cmd as that account; the resulting ops are authored
   under that identity's `Intent`.
7. The result streams back to the caller's terminal.

Same auth flow as sync. No additional moving parts.

### Endpoints on each peer

In addition to the sync endpoints in [sync.md](../stable-and-syncing/sync.md):

```
POST /exec
  Body: { cmd: String; args: List<String> }
  Auth: Tailscale-User-Login → account_id → Identity
  Response: streamed stdout/stderr + exit code
  Cap-gated: requires CapRemoteExec for the calling identity on this device.

GET /devices
  Reflects `tailscale status` to peers, augmented with sync state.
  Auth: any tailnet member.
```

Endpoints are ordinary Dark HTTP handlers on the existing
`Builtins.Http.Server`. Each is a cap-checked Dark fn.

### Offline resilience

```
$ dark on offline-box run Foo.bar
[queued: offline-box not currently reachable; will execute when it reconnects.]
```

The key reframe: **an offline `dark on` call is an op that targets a peer**, not
a bespoke queue.

1. The caller's CLI notices the peer is offline (Tailscale knows this).
2. The call is enqueued as an op in the local op tables, tagged with its target
   peer.
3. When sync next succeeds with `offline-box`, that op replays on the target via
   the normal op-playback path.
4. The result is published back over the sync stream and surfaces on the bus
   (see [event-bus.md](../pre-s-and-s/event-bus.md)).

This dovetails with sync: `dark on` is "produce an op targeting a peer," and the
durable record is that op. The "[queued: ...]" line and any later "[done]" line
are projections of the op's state, not separate bookkeeping.

## Capability surface

Two caps extend the [capabilities.md](../pre-s-and-s/capabilities.md) set:

- `CapRemoteExec` — let a remote peer invoke `dark run` / `eval` / etc. on me.
  Default: granted for *my own* accounts (Stachu's laptop trusts Stachu's other
  devices); denied for others.
- `CapPeerSync` — let a remote peer push/pull sync events. The foundational one;
  granted to known tailnet members.

Both follow the existing capability model: same grant flow, and denials route
through the conflict dispatch (see [conflicts.md](../stable-and-syncing/conflicts-and-resolutions.md)).

## Deliberately out of scope

- **F# `tsnet` binding** — no .NET SDK for Tailscale's Go library; we work via
  the `tailscale` CLI plus HTTPS. Workable, not optimal.
- **Public funnel exposure** — deferred with the public-share decision in
  [sync.md](../stable-and-syncing/sync.md).
- **Multi-tenant isolation between unrelated users on `matter.darklang.com`** —
  present deployments are small-team-tailnet-only. Cross-org sharing waits for a
  real auth/billing story.

## Open decisions

- **`dark on` syntax.** `dark on major <cmd>` vs `dark major: <cmd>` vs
  `dark --device=major <cmd>`. Pick one before shipping exec.
- **Stream encoding.** stdout/stderr from `/exec` must stream back. WebSocket,
  chunked HTTP, or SSE? Probably chunked HTTP — simple, and works through
  Tailscale.
- **Pair-share device control.** If a collaborator joins my tailnet, can they
  `dark on major ...`? Default: no (cap denied); explicit grant required. The UX
  for "trust this peer to run X on me" is its own design.
- **Tailnet-vs-internet boundary.** When does `matter.darklang.com` expose
  endpoints to non-tailnet members? Tied to the funnel decision in
  [sync.md](../stable-and-syncing/sync.md).
