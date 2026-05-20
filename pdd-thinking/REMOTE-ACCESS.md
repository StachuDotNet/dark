# Remote Access & Control + Agent Runtime

> Loop T22 + T22b (2026-05-20). Combined doc — agent runtime *is*
> the long-running per-device daemon that makes remote access
> work. Two faces of the same primitive.

The goal:

- **Sync**: a function I write on the laptop is on `major` and
  the reMarkable within seconds.
- **Control**: `dark on major run BatchProcess.run` invokes a fn
  on the GPU box from the reMarkable.
- **Discovery**: `dark devices` shows all my peers with last-sync
  + current activity.
- **Offline resilience**: `dark on offline-box ...` queues the
  call and replays when it reconnects.

Plus the agent runtime story: when a user spawns an LLM agent, it
becomes a *peer-like inhabitant* that other devices can also see
and (with permission) control.

## What exists on main today

### LLM Agent framework — already in Dark

Confirmed via `git show main:packages/darklang/llm/agent.dark`.
Architecture is provider-agnostic:

```
Darklang.Cli.Commands.Agent      -- CLI entry point
  → Darklang.LLM.Agent           -- config builder + run/chat
  → Darklang.LLM.Internal.AgentLoop  -- tool-call loop, retries
  → Darklang.LLM.Provider        -- provider-agnostic types
  → Darklang.LLM.Providers.*     -- Anthropic, OpenAI, Ollama
```

Usage today:
```darklang
Agent.create ()
|> Agent.withModel Models.Anthropic.opus46
|> Agent.withSystemPrompt "You are helpful."
|> Agent.run "Solve this problem..."
```

What's there:
- Provider-agnostic Agent / Tool / ToolCall / AgentResult types
- Per-provider adapters
- Tool-call loop
- Thinking / web-search / JSON-mode hooks (per provider)

What's NOT there:
- Long-running daemon (agents are run synchronously per call)
- Substrate integration (agents don't produce ops; they speak
  through tools)
- Per-agent identity / delegation (agents act as the CLI user)
- Cross-instance addressability (no "send my agent to that peer")
- Trace/audit beyond the CLI session

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

## Agent runtime

### Process model

An agent runs as a **long-running thread** inside a `dark`
process. It's *not* a separate OS process by default; that's
heavier than we need (per IDENTITY's "agent is just an
inhabitant" framing).

```fsharp
type AgentProcess = {
  account : AccountId          // the agent's identity (per IDENTITY)
  delegation : DelegationId    // under what authority
  goal : Option<Goal>          // current intent
  plan : Option<Plan>          // decomposition
  status : AgentStatus         // Running | Paused | Done | Failed | Revoked
  spawnedAt : DateTime
  parentAgent : Option<AccountId>  // for sub-agents
  // Runtime ref to the actual Ply continuation + cancellation token
  thread : AgentThreadHandle
}

type AgentThreadHandle = {
  cancel : unit -> unit
  ply : Ply.Ply<AgentResult>
  events : EventBus<AgentEvent>  // local bus for observers
}
```

### Spawning

```darklang
let agent =
  Agent.spawn
    { name = "csv-helper"
      goal = "process the inputs/*.csv files into a summary table"
      caps = [ CapReadFile; CapInvokeLLM ]
      scope = "User.Stachu.CSV.*"
      expires = Stdlib.DateTime.now + Duration.hours 1L }
```

This:
1. Creates an entry in `accounts_v0` with `kind=Agent`,
   `ownerId=<calling user>`
2. Inserts a `delegations` row with the requested caps/scope/expiry
3. Spawns a thread inside the host `dark` process. The thread
   runs the agent's planner + tool-call loop (per the existing
   `LLM.Agent.run` shape but generalized).
4. Returns an agent handle that can be observed / cancelled.

### Observing

```darklang
// Pull recent activity:
let recent = Agent.recentEvents agentHandle

// Or subscribe (events flow into the user's MVU app):
Agent.subscribe agentHandle (fun ev -> AppMsg.AgentEvent ev)
```

The agent publishes to its local EventBus (per T16). The viewer
subscribes; the user sees what's happening live.

### Cancelling / revoking

```darklang
Agent.cancel agentHandle    // soft-stop on next call boundary
Agent.revoke agentHandle    // owner removes the delegation
```

Per IDENTITY: revocation flips `delegations.revoked_at`; in-flight
calls finish; no new calls authorized. Agent.status becomes
Revoked.

### Sub-agents

An agent can spawn sub-agents (per IDENTITY sub-delegation). The
sub's delegation must be a strict subset of the parent's; revoked
transitively.

## Remote access — the wire

### Discovery

```darklang
let peers = Tailscale.devices ()  // calls `tailscale status --json`
peers |> List.iter (fun p -> Stdlib.printLine $"{p.name}: {p.status}")
```

Or:
```
$ dark devices
laptop     online   last-sync: 2s ago   running: csv-helper
major      online   last-sync: 1s ago   running: -
remarkable online   last-sync: 3m ago   running: -
```

Implementation: shells out to `tailscale status --json`, parses
the result, augments with per-device `last-sync` from local
sync state + `running` from each device's `/sync/whoami` +
`/agent/status`.

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

GET /agents
  Returns: list of agents running on this device + status.
  Auth: same; filtered by what the caller can see.
  Cap-gated: CapRemoteObserveAgents.

POST /agent/<id>/cancel
  Cap-gated: CapRemoteControlAgents + the caller must be the
    agent's owner (or a delegated controller).

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
- `CapRemoteObserveAgents` — let a remote peer see what
  agents are running here. Defaults: same-owner = granted;
  other-owners = denied; pair-collaborators = ask.
- `CapRemoteControlAgents` — let a remote peer cancel /
  pause / resume my agents. Defaults: same-owner = granted;
  others = denied (always).
- `CapPeerSync` — let a remote peer push/pull sync events.
  This is the foundational one; granted to known tailnet
  members.

These caps follow the same model as the existing capabilities
(per T15). Grant flow is the same. Denials route through the
conflict dispatch.

## Cross-instance agent identity

The IDENTITY model says a delegation is content-addressable
and syncs like any other op. So:

- Stachu spawns `csv-helper` on his laptop with a delegation
- The delegation op syncs to `major` + `matter.darklang.com`
- `major` now knows about `csv-helper` (its account_id, caps,
  scope, expiry) — but doesn't run it
- If Stachu does `dark on major run csv-helper.process ...`,
  major recognizes the agent's identity and runs the call
  *as that agent* (with its scoped caps), not as Stachu
  directly
- Audit log on major shows `account_id = csv-helper`, with
  the delegation chain intact

This is what makes "send my agent to that peer" coherent —
agents are first-class identities that exist in the substrate,
not just process-local objects.

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
- **Agent migration** — sending an agent's *state* (mid-execution)
  from one peer to another. Future research.

## Sequencing — work-units in this area

| Chunk | What | Phase |
|---|---|---|
| **RA-1** Tailscale CLI binding builtins | Wrap `tailscale status --json` / `tailscale serve` in `Builtins.Tailscale` (new). Dark calls these. | Phase 3 |
| **RA-2** `/devices` + `/agents` endpoints + `dark devices` cmd | First peer-aware Dark code. Read-only. | Phase 3 |
| **RA-3** `/exec` endpoint + `dark on <peer> ...` cmd | Cap-gated remote execution. Streaming response. | Phase 3 |
| **RA-4** Offline-queue support | New op variant or `propagation_id` overload to encode peer-target; sync replays on reconnect. | Phase 3-4 |
| **RA-5** Agent runtime as long-running thread | Refactor `LLM.Agent.run` synchronous → `Agent.spawn` async with handle + EventBus. | Phase 2b-3 |
| **RA-6** Cross-instance agent identity (sync delegations) | Already lands with IDENTITY; verify cross-instance recognition works. | Phase 2-3 |
| **RA-7** `dark on <peer> agent ...` to control remote agents | Builds on RA-3 + RA-6. | Phase 3 |

## Open decisions

- **(Q-ra-1) Process model: thread vs subprocess.** v1: thread
  inside `dark`. Subprocess (heavyweight) only if a specific
  case demands isolation (e.g., long-running compute on a
  GPU box).
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
- **(Q-ra-5) Agent state migration.** When/whether to support
  moving an agent across peers mid-execution. Probably never
  for v1; killable + relaunch is good enough.
- **(Q-ra-6) Tailnet-vs-internet boundary.** When does
  matter.darklang.com expose endpoints to non-tailnet members?
  Phase 4 with the funnel decision.
