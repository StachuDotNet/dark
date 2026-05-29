# Remote Access & Control

> Loop T22 (2026-05-20). Focused on the *wire* — addressing,
> auth, endpoints, exec. Agent-runtime concerns (process model,
> spawning, observing, cross-instance agent identity) split out;
> not in this doc.

The goal:

- **Sync**: a function I write on the laptop is on `major` and
  the reMarkable within seconds.
- **Control**: `dark on major run BatchProcess.run` invokes a fn
  on the GPU box from the reMarkable.
- **Discovery**: `dark devices` shows all my peers with last-sync
  + current activity.
- **Offline resilience**: `dark on offline-box ...` queues the
  call and replays when it reconnects.

## What exists on main today

### Networking — basically nothing

Per Tailscale.md vault note:

> Dark today has essentially no networking — one HTTP client
> builtin, one HTTP server builtin (single port, blocks the CLI),
> no sockets, no auth, no multi-process, no sync, no remote
> anything.

Tailscale becomes the shortcut.

## Stance: lean on Tailscale, don't build a network stack

Per `~/vaults/Darklang Dev/05.Implementation/Networking and
Internet/Tailscale.md`. Tailscale gives us, for free:

- Stable per-device addressing (MagicDNS `<machine>.<tailnet>.ts.net`)
- TLS for free (`tailscale serve --https=443`)
- Identity via `Tailscale-User-Login` HTTP header injection
- E2E encrypted transport between peers
- NAT traversal + DERP relay fallback
- ACLs via tags+grants in HuJSON policy
- Public exposure via `tailscale funnel` (rate-limited, deliberate)

**What Tailscale doesn't do** (per the vault):
- No L2 (no peer discovery via mDNS; must use API/gossip)
- No `tsnet` F# SDK (F# speaks via HTTP + the `tailscale` CLI)
- Free tier user limits (3-6); fine for personal/small-team
- DERP fallback latency occasionally

The substrate **doesn't require** Tailscale — over open internet
the same protocol works with normal TLS + a bearer-token header.
Tailscale is the convenient default for tailnet members.

## Remote access — the wire

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

Implementation: shells out to `tailscale status --json`, parses
the result, augments with per-device `last-sync` from local
sync state + `/sync/whoami`.

### `dark on <peer> <cmd>` — remote execution

```
$ dark on major run BatchProcess.run --gpu
```

What happens:
1. CLI resolves `major` → MagicDNS `major.<tailnet>.ts.net`
2. HTTPS POST to `https://major.<tailnet>.ts.net/exec`
3. Tailscale terminates TLS; injects `Tailscale-User-Login: stachu@stachu.net`
4. Receiver maps login → account_id; verifies the user has
   `peer-control` cap (a new cap) for this device
5. Receiver runs the cmd as that account
6. Result streamed back to caller's terminal

Same auth flow as sync. No additional moving parts.

### Endpoints on each peer

In addition to the sync endpoints (per STABILITY-AND-SHARING):

```
POST /exec
  Body: { cmd: string; args: List<String> }
  Auth: Tailscale-User-Login → account_id
  Response: streamed stdout/stderr + exit code
  Cap-gated: requires CapRemoteExec for the calling account on
    this device.

GET /devices
  Reflects /tailscale status to peers, augmented with sync
  state.
  Auth: any tailnet member.
```

Endpoints are Dark HTTP handlers (the existing
`Builtins.Http.Server`). Each one is a cap-checked Dark fn.

### Offline resilience

```
$ dark on offline-box run Foo.bar
[queued: offline-box not currently reachable. Will execute
 when it reconnects.]
```

Implementation:
1. Caller's CLI notices the peer is offline (Tailscale knows
   this).
2. Enqueue the call as an op in the local `package_ops` table
   with a special `target_peer = "offline-box"` field (new
   column? or use `propagation_id` overload?).
3. When sync next succeeds with offline-box, that op replays on
   the target.
4. Result is published back via the sync stream.

This dovetails with the sync infra — `dark on` is "produce an
op targeting a peer," not its own subsystem.

## Capability surface for remote access

New caps (added to the existing CAPABILITIES list):

- `CapRemoteExec` — let a remote peer invoke `dark run` /
  `eval` / etc. on me. Defaults to: granted for *my own*
  accounts (Stachu's laptop trusts Stachu's other devices);
  denied for others.
- `CapPeerSync` — let a remote peer push/pull sync events.
  This is the foundational one; granted to known tailnet
  members.

These caps follow the same model as the existing capabilities
(per T15). Grant flow is the same. Denials route through the
conflict dispatch.

## What's deliberately out of scope (for v1)

- **F# `tsnet` binding** — no .NET SDK for Tailscale's go
  library. We work via `tailscale` CLI + HTTPS. (Workable; not
  optimal.)
- **Public funnel exposure** — deferred to STABILITY-AND-SHARING
  Phase 4.
- **Multi-tenant isolation between unrelated users on
  matter.darklang.com** — present-day deployment is small-team-
  tailnet-only. Cross-org sharing waits for a real auth/billing
  story.

## Sequencing — work-units in this area

| Chunk | What | Phase |
|---|---|---|
| **RA-1** Tailscale CLI binding builtins | Wrap `tailscale status --json` / `tailscale serve` in `Builtins.Tailscale` (new). Dark calls these. | Phase 3 |
| **RA-2** `/devices` endpoint + `dark devices` cmd | First peer-aware Dark code. Read-only. | Phase 3 |
| **RA-3** `/exec` endpoint + `dark on <peer> ...` cmd | Cap-gated remote execution. Streaming response. | Phase 3 |
| **RA-4** Offline-queue support | New op variant or `propagation_id` overload to encode peer-target; sync replays on reconnect. | Phase 3-4 |

## Open decisions

- **(Q-ra-2) `dark on` syntax.** `dark on major <cmd>` vs
  `dark major: <cmd>` vs `dark --device=major <cmd>`. Pick
  one before RA-3.
- **(Q-ra-3) Stream encoding.** stdout/stderr from `/exec`
  needs to stream back. WebSocket? Chunked HTTP? SSE? Probably
  chunked HTTP (simple, works through Tailscale).
- **(Q-ra-4) Pair-share device control.** If Feriel joins my
  tailnet, can she `dark on major ...`? Defaults: no
  (cap denied); explicit grant required. UX for "trust this
  peer to run X on me" is its own design.
- **(Q-ra-6) Tailnet-vs-internet boundary.** When does
  matter.darklang.com expose endpoints to non-tailnet members?
  Phase 4 with the funnel decision.
