# Networking — sync, auth, and instance-to-instance, without Tailscale

Replaces section 10 of `data-architecture-rewrite.md`. Tailscale was the wrong starting point: it requires every user to be on a tailnet, which is a hard sell for "I just installed Dark, signed up, want my code to follow me to my phone." We need something that works for one person on one machine, scales seamlessly to many users, and doesn't require any network setup. The Tailscale flow becomes an optional power-user layer (§ 7), not the default.

The shape: **a Dark-hosted hub that every Instance maintains a long-lived outbound connection to.** Identity, discovery, sync-relay, public HTTP fronting, P2P rendezvous — all the things people don't want to build — happen at the hub. The default user never thinks about networking at all.

This is the same shape as Slack, Discord, GitHub Actions runners, Cloudflare Tunnel, ngrok, Vercel deploys, and basically every modern SaaS-meets-on-prem product. It's well-trodden because it works.

---

## 1. The actors

| Term | What it is | Who owns it |
|---|---|---|
| **User** | A person with a Dark account (email-verified, optionally OAuth-linked) | the person |
| **Instance** | A running `darkd` somewhere — laptop, server, phone, browser | a user |
| **Hub** | Our hosted Dark service at `hub.dark.run` (name TBD) | Dark Inc |
| **Peer** | An Instance reachable from another, via the hub or directly | varies |
| **App** | A long-running Dark program inside an Instance, optionally publicly addressable | hosting user |

A User has 1..N Instances. An Instance is owned by exactly one User. Sharing is always User → User: a User can grant another User access to a branch / app / namespace. Instances inherit access from their owning user.

---

## 2. The login flow (the thing the user said had to be fast)

The whole flow:

```
$ dark login
Opening https://dark.run/cli-auth?code=4f8b
Press Enter once you've authorized.
[user authenticates in browser; clicks "approve laptop"]
✓ Logged in as stachu@dark.run on instance "stachu-laptop"
```

What's happening:
1. `dark login` generates a one-time `code` (8 chars, urlsafe), opens a browser to `https://dark.run/cli-auth?code=4f8b`, and starts polling `https://hub.dark.run/api/cli-auth/poll?code=4f8b`.
2. Browser shows the user a "Authorize <hostname> as a new instance?" page with a name field (default: hostname). User logs in (if not already) and clicks Authorize.
3. Browser POSTs `{code, instance_name, instance_id_proposed}` to the hub.
4. Hub mints an `instance_token` (long-lived JWT, instance-scoped) and stores it against the code.
5. CLI's poll returns the token. CLI writes it to `~/.darklang/config.toml`.
6. CLI immediately opens its long-lived WSS to the hub using the new token. From this moment, the instance is online and visible to peers.

This is the GitHub CLI / Vercel CLI / Cloudflare Wrangler / Linear CLI flow. ~30 seconds for a new user, including the email signup. Tested-to-death pattern.

`dark logout` revokes locally; `dark.run/account/devices` revokes server-side. Token rotation is automatic.

### What's in `~/.darklang/config.toml`

```toml
[account]
user_id = "01H8Z..."
handle = "stachu"
email = "stachu@dark.run"

[instance]
id = "01H9C..."
name = "stachu-laptop"
token = "drk_inst_eyJhbGciOi..."           # JWT, instance-scoped, refreshed nightly
hub_url = "wss://hub.dark.run/instance"

[device_id]
# Same as before; written by `dark init` even before login. Pre-auth instance ID.
local_id = "1d4e..."
```

Pre-login, an Instance has a local id and can do everything offline. Login attaches the local id to a Dark account.

---

## 3. The hub — what it actually is

`hub.dark.run` is a service we run. Conceptually small. Concretely:

- A WebSocket terminator (millions of long-lived connections; this is what NATS / Pusher / Phoenix Channels are for; we'll use one of those or a thin custom thing on top of one).
- A Postgres for accounts, instances, share grants, app subdomains.
- A blob store for hosting `ops-snapshot-v0.db` (initial-clone payload), big shared blobs.
- A reverse proxy fronting `*.dark.run` subdomains, terminating TLS, forwarding to the right instance's WS.
- (Optional later) A TURN server for P2P upgrades when STUN-only hole-punching fails.

**The hub itself is built in Dark, eating our own dogfood.** Its router is a Dark fn served via `dark app start --inline Darklang.Hub.router`. We host it on whatever PaaS makes sense (Fly, Render, plain VPS) — the hub is just an instance running an app, talking to Postgres for the account/instance bookkeeping. This forces us to use everything we ship.

### Hub data model (Postgres)

```sql
CREATE TABLE users (
  id            UUID PRIMARY KEY,
  email         TEXT UNIQUE NOT NULL,
  handle        TEXT UNIQUE NOT NULL,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  oauth_github  TEXT,                       -- optional links
  oauth_google  TEXT,
  email_verified_at TIMESTAMPTZ
);

CREATE TABLE instances (
  id              UUID PRIMARY KEY,
  user_id         UUID NOT NULL REFERENCES users(id),
  name            TEXT NOT NULL,
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  last_seen       TIMESTAMPTZ,
  public_key      BYTEA NOT NULL,           -- instance signs handshakes; hub verifies
  revoked_at      TIMESTAMPTZ
);
CREATE INDEX ON instances(user_id);

CREATE TABLE instance_tokens (
  id              UUID PRIMARY KEY,
  instance_id     UUID NOT NULL REFERENCES instances(id),
  token_hash      BYTEA NOT NULL,
  issued_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  expires_at      TIMESTAMPTZ NOT NULL,
  revoked_at      TIMESTAMPTZ
);

-- Sharing: who can read/write what.
CREATE TABLE share_grants (
  id             UUID PRIMARY KEY,
  granter_user_id UUID NOT NULL REFERENCES users(id),
  grantee_user_id UUID NOT NULL REFERENCES users(id),
  scope           TEXT NOT NULL,           -- 'branch:<id>' | 'namespace:<owner.module>' | 'app:<id>'
  permission      TEXT NOT NULL,           -- 'read' | 'write' | 'admin'
  expires_at      TIMESTAMPTZ,             -- NULL = no expiry
  revoked_at      TIMESTAMPTZ
);
CREATE INDEX ON share_grants(grantee_user_id, scope);

-- Apps that have requested a public subdomain.
CREATE TABLE public_apps (
  app_id          UUID PRIMARY KEY,
  instance_id     UUID NOT NULL REFERENCES instances(id),
  subdomain       TEXT UNIQUE NOT NULL,    -- "stachu-blog" → stachu-blog.dark.run
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  disabled_at     TIMESTAMPTZ
);
CREATE INDEX ON public_apps(instance_id);

-- Online presence (in-memory in production; Postgres is the durable copy).
CREATE TABLE instance_online (
  instance_id  UUID PRIMARY KEY REFERENCES instances(id),
  hub_node     TEXT NOT NULL,              -- which hub shard holds the WS
  connected_at TIMESTAMPTZ NOT NULL,
  rtt_ms       INTEGER
);
```

Five real tables plus one cache. That's the entire hub-side model for the bootstrap. Sync ops, branch ops, blobs — none of those live at the hub. **The hub does not store user code.** It only stores account/identity bookkeeping and a presence registry.

This matters: it caps our liability, our backup footprint, our compliance surface. The hub can lose its database and the world keeps working — Instances re-authenticate (annoying but not data-loss).

### Hub WS protocol (sketch)

```
# Frame format: length-prefixed CBOR (or JSON; CBOR for size).

# Client → Hub (after WSS connect with bearer token)
HELLO { protocol_version, instance_id, capabilities: [...] }

# Hub → Client (always first response)
WELCOME { hub_node, server_time, peers_online: [<instance_id>...] }

# Bidirectional after handshake:
TO    { to_instance: <id>, request_id: <id>, payload: <bytes> }
FROM  { from_instance: <id>, request_id: <id>, payload: <bytes> }
EVENT { kind: "peer_online" | "peer_offline" | "share_granted", ... }
PING  { } / PONG { }

# Public HTTP forwarding (hub → instance):
HTTP_REQUEST  { request_id, method, path, headers, body, app_id }
HTTP_RESPONSE { request_id, status, headers, body }
```

That's the whole protocol surface. Frame multiplexing, presence pushes, request/response routing, HTTP forwarding for public apps. Two-page spec.

---

## 4. Discovery and "connect to another instance" — fast

The user said: **"users will need to be able to connect one Instance of Dark to another really quickly."** Here's how.

After login, the daemon's WS to the hub is open. The daemon already knows:
- All my instances (hub pushed them in WELCOME).
- All instances that share a grant with me (hub also pushes these; updates as new grants land).
- Which of those are online right now (presence stream).

So `dark devices` is local — instant — already cached:

```
$ dark devices
ID         NAME             OWNER     ONLINE   LAST_SEEN
01H9C..    stachu-laptop    me        ✓ now    -
01H9D..    stachu-major     me        ✓ now    -
01H9E..    stachu-pixel     me        ✗        2h ago
01H9F..    feriel-air       feriel    ✓ now    -        (shared: branch:fix-bug)
```

`dark on stachu-major run @MyFn 5L` is a single hub round-trip:

```
1. CLI → daemon: "run @MyFn on stachu-major"
2. daemon → hub: TO { to: stachu-major, payload: ExecRequest{...} }
3. hub → stachu-major's daemon: FROM { from: stachu-laptop, payload: ExecRequest{...} }
4. stachu-major executes, replies: TO { to: stachu-laptop, payload: ExecResponse{...} }
5. hub → laptop: FROM { ... }
6. daemon → CLI: result, render, exit
```

Latency: **~2 × (instance ↔ hub) RTT + remote exec time.** With hub on the same continent, that's 60-150ms of network overhead. Indistinguishable from local for any non-trivial command.

If both peers are online and frequently talking, the daemon can opportunistically upgrade to direct P2P (§ 6) and shave the hub round-trip to one (~20-50ms direct).

**Crucially, no setup.** No tailnet to join, no firewall to configure, no IP to remember. The user logs in once and `dark on <peer>` works.

---

## 5. Sync, redone for this transport

Sync over the hub is identical in *semantics* to the v1 in the original doc — push/pull ops keyed by content hash, idempotent — but the *transport* changes.

### Outbound-only WS as the transport

Every Instance has the WS to the hub. Sync rides that connection:

```
# To push my new ops to peer B:
TO { to: B, payload: SyncPushOps { branch_id, ops: [<op_blob>...] } }

# To pull from peer B:
TO { to: B, payload: SyncPullOps { branch_id, since_hash } }
FROM { from: B, payload: SyncPullOpsResponse { ops: [...] } }
```

The hub is a dumb router. It does not interpret the payload. (We sign the payload with our instance key for end-to-end integrity. The hub can't read it if we encrypt it; we probably don't bother, since the user's instances are all owned by the same user and inter-user shares already pass through the hub by design. But signing is cheap.)

### What about mass clone (initial sync)?

A new instance might need to pull 500 MB of ops. Streaming through the hub is fine but slow and bandwidth-on-us. Better:

- Hub stores the latest **snapshot** of `ops.db` from your "primary" instance (uploaded periodically, opt-in).
- New instance: `dark login` → daemon offers "Bootstrap from snapshot? (450 MB, cached on hub)". User says yes. Direct download from hub blob storage (S3 / R2). Then catch-up on the gap via WS.

Snapshot upload is a per-user feature; we cap free-tier snapshot size and frequency.

For users who don't want snapshots-via-hub: `dark sync clone-from <my-other-instance>` directly initiates a P2P transfer (§ 6). Hub helps the rendezvous; bytes flow direct.

### The hub never holds your code in plaintext

Even snapshots are encrypted client-side with a key derived from the user's account password (PBKDF2-stretched, never leaves the device after login). Hub stores ciphertext. We can lose the snapshot store and all that's lost is the convenience of fast bootstrap. This is the iCloud Keychain / Standard Notes / 1Password threat model.

(For shared-with-other-users branches, we use a separate per-share key wrapped with the recipient's public key. Public-key cryptography on Dark accounts is a one-time setup at signup; pre-quantum X25519 for now.)

### Multi-master conflicts

Same as the v1: two devices both editing main → rebase-before-push, last-writer-wins on names, definitions still exist at their hashes. Nothing new from the transport.

---

## 6. P2P upgrade — when it matters

Most ops are tiny; relay through the hub is fine forever. But three workloads want direct:

1. **Initial clone** (hundreds of MB of ops).
2. **Large blobs** (`package_blobs` rows that hold big embedded data).
3. **`dark on <peer> serve` / live tracing** — bidirectional streams that don't want to round-trip the hub for every frame.

Direct connections work like this:

```
1. A wants to talk to B directly. A asks the hub for B's NAT info.
2. Hub asks B over its WS: "send me your STUN candidates."
3. B runs through its STUN server (we host one), gets its public IP:port candidates.
4. Hub relays candidates to A. A also gathers its own.
5. A and B both open a UDP socket and start sending packets at each other's
   candidate addresses simultaneously (ICE-like hole punching).
6. Once one packet gets through, they have a direct UDP path. They start
   speaking QUIC over it.
7. If hole-punching fails (symmetric NAT), they fall back to a TURN relay
   we host. We pay for that bandwidth but it's the unlucky 10-15%.
```

**.NET 8 has `System.Net.Quic`.** We don't need WebRTC if we don't need browser interop (and for native daemons, we don't). QUIC gives us:
- Multiplexed streams (sync + control + traces all share one connection)
- Built-in TLS 1.3 (we provision certs from a Dark-internal CA at signup)
- Connection migration (laptop changes networks → connection survives)
- 0-RTT resume

The signaling layer (steps 1-4 above) is a small Dark-side protocol over the hub WS. Maybe 100 lines of F# / Dark.

**Heuristic for when to upgrade:** if A and B exchange more than 1 MB total, or 100 messages, in a 60s window, daemon attempts an upgrade. Successful upgrades survive across reconnects (cached signaling info).

---

## 7. The "managed overlay" layer (your "or something like that")

For users/orgs who want to bypass the hub — for compliance, latency, or air-gap reasons — we offer overlay options as a Pro feature. The default user never touches this.

### Tier 1: Bring Your Own Tailscale

If you already have a tailnet, your instances can join it as well as connecting to the hub. A `dark sync set-transport tailscale` setting tells the daemon to prefer tailnet routing for known-on-tailnet peers. No hub bandwidth used between those instances. Hub still does identity, discovery, public subdomain.

### Tier 2: Managed Dark Network (later)

For orgs that want a dedicated overlay without running their own tailnet:
- We provision a Headscale-or-equivalent coordination server scoped to the org.
- Instances auto-join at login (the org's hub config tells them to).
- Sync rides the overlay; hub is only used for identity and out-of-network peers.

This is what the user hinted at with "maybe we manage tailscale networks for them." It works because Dark already controls auth (the hub) — provisioning an overlay is just "spin up another Headscale, hand the org a join key, embed the join key in instance configs." Premium tier.

### Tier 3: Self-hosted hub (Enterprise)

Some orgs will want to run the entire hub themselves. The hub is a Dark app — they can. Source-available; some enterprise license attached. Charges for support.

These tiers exist to grow into. **None of them are necessary for the default user.**

---

## 8. Public HTTP serving (subdomain fronting)

Today, `serve <router>` blocks the CLI. Tomorrow, `dark app start --port 9001` runs locally. To make it public:

```
$ dark app expose my-blog
✓ Now serving at https://stachu-myblog.dark.run
✓ Auto-TLS active. Free until 1 GB/mo egress.
```

What happens:
1. CLI calls hub: `POST /api/apps/expose { app_id, subdomain_proposed }`.
2. Hub allocates `stachu-myblog` if free, records it, returns 200.
3. From now on, hub's reverse proxy: incoming HTTPS to `stachu-myblog.dark.run` → look up which instance hosts it → forward over the WS as `HTTP_REQUEST` frames → instance's daemon runs the router → reply via `HTTP_RESPONSE` frames → proxy returns to client.

This is **Cloudflare Tunnel**, **ngrok**, **Tailscale Funnel**, **Vercel deployments** — same architecture as all of them. Battle-tested. Cheap to run because most apps are low-traffic.

Custom domains (`blog.stachu.dev → my Dark app`) come later; user adds a CNAME, hub provisions a cert via ACME, done. Standard SaaS-hosting feature.

For users who **don't** want public exposure, nothing's exposed. Local-only `dark app start` is its own complete feature.

---

## 9. Code sketches

### F# side: the hub-WS client

```fsharp
module Darkd.Hub

open System.Net.WebSockets
open System.Threading.Channels

type Frame =
  | Hello of HelloPayload
  | Welcome of WelcomePayload
  | To of ToPayload
  | From of FromPayload
  | Event of EventPayload
  | Ping
  | Pong
  | HttpRequest of HttpRequestPayload
  | HttpResponse of HttpResponsePayload

type HubClient = {
  /// outbound channel — anything written here gets sent to the hub
  outbound: Channel<Frame>
  /// inbound channels per request_id — hub responses get routed here
  pending: ConcurrentDictionary<RequestId, TaskCompletionSource<Frame>>
  /// presence updates: peer online/offline, share granted, etc.
  presenceEvents: Channel<PresenceEvent>
  /// incoming TO frames from peers (sync requests, exec requests, etc)
  incomingFromPeers: Channel<FromPayload>
}

let connect (config: Config) : Task<HubClient> = task {
  let ws = new ClientWebSocket()
  ws.Options.SetRequestHeader("Authorization", $"Bearer {config.instanceToken}")
  do! ws.ConnectAsync(Uri config.hubUrl, CancellationToken.None)

  let client = makeHubClient ws
  do! sendHello client config
  let! welcome = expectWelcome client
  do! seedPresence client welcome.peersOnline

  // Spawn read loop and write loop. Both run for the lifetime of the conn.
  let _ = readLoop client ws
  let _ = writeLoop client ws
  let _ = pingLoop client

  return client
}

/// Send a request and await response. Used for sync push, exec, anything
/// that has a single reply.
let request
    (client: HubClient)
    (toInstance: InstanceId)
    (payload: byte[])
    : Task<byte[]> = task {
  let reqId = Guid.NewGuid()
  let tcs = TaskCompletionSource<Frame>()
  client.pending.[reqId] <- tcs
  do! client.outbound.Writer.WriteAsync(To { to_=toInstance; request_id=reqId; payload=payload })
  let! reply = tcs.Task
  match reply with
  | From f -> return f.payload
  | _ -> return failwith "unexpected reply"
}

/// Reconnect with exponential backoff. The hub's last-seen tracking
/// gives us a few minutes' grace before peers think we're offline.
let runReconnectLoop (config: Config) (onConnect: HubClient -> Task<unit>) : Task<unit> =
  task {
    let mutable backoff = 1000  // ms
    while true do
      try
        let! client = connect config
        backoff <- 1000
        do! onConnect client
      with ex ->
        log.Warning $"hub disconnected: {ex.Message}; retrying in {backoff}ms"
        do! Task.Delay backoff
        backoff <- min (backoff * 2) 60_000
  }
```

That's the bulk of the client side. Maybe 400 lines total once you add framing, presence handling, multiplexing per request_id, and the Channels around incoming peer messages.

### Dark side: the hub itself

The hub is a Dark app. Its router handles incoming WS upgrades and HTTP requests:

```dark
module Darklang.Hub.Router

let router (request: Http.Request) : Http.Response =
  match request.path with
  | "/api/cli-auth/init" -> Auth.handleCliAuthInit request
  | "/api/cli-auth/poll" -> Auth.handleCliAuthPoll request
  | "/instance" ->
    if Http.isWebSocketUpgrade request then
      WebSocket.upgrade request InstanceConn.handle
    else
      Http.Response.badRequest "websocket only"
  | "/api/apps/expose" -> Apps.handleExpose request
  | path when Stdlib.String.startsWith path "/proxy/" ->
    // Subdomain proxy: hub's TLS terminator routes <x>.dark.run → /proxy/<x>/...
    Proxy.forwardToInstance request
  | _ -> Http.Response.notFound

module Darklang.Hub.InstanceConn

let handle (conn: WebSocket.Conn) : Unit =
  let token = Auth.extractToken conn
  match Auth.verifyInstanceToken token with
  | Error err -> WebSocket.close conn 1008L err
  | Ok instance ->
    Presence.markOnline instance.id conn.id
    Hub.registerConnection instance.id conn

    while WebSocket.isOpen conn do
      match WebSocket.receive conn with
      | Frame.Hello h -> handleHello conn instance h
      | Frame.To t -> Hub.routeFrame instance.id t
      | Frame.HttpResponse r -> Proxy.deliverResponse r
      | Frame.Ping -> WebSocket.send conn Frame.Pong
      | _ -> ()

    Presence.markOffline instance.id
    Hub.unregisterConnection instance.id
```

This is *very* roughly what the hub looks like. Maybe 2000 lines of Dark for the v1 surface. Importantly: **the hub is a real Dark app**, exercising the multi-instance / sync / public-app layers we built. Eating our own dogfood.

### Hub deployment

Single VPS or Fly.io app to start. Postgres on RDS or Neon. Snapshot blobs on R2 or S3. Annual cost at 0 users: ~$50/mo. At 1k users: ~$200/mo (still tiny). At 100k users: shard hubs by user-id, run a load balancer in front, ~$5k/mo. None of these are a barrier.

Hub source-available so paranoid users can audit it. We don't open-source the orchestration (CD, monitoring, billing) yet, but the Dark router code is just package code, visible.

---

## 10. Auth and access control, in Dark code

The hub passes identity to handlers as headers when proxying public requests:

```
X-Dark-User-Id: 01H8Z...
X-Dark-User-Handle: stachu
X-Dark-Instance-Id: 01H9C...
X-Dark-Auth-Source: hub
```

User code reads those like any other request header. If you serve a private endpoint, you check `X-Dark-User-Id` against your own ACL. Hub guarantees the headers are honest — they can only be set by the hub itself, after instance-token verification.

For instance-to-instance traffic over the hub (not public requests), the daemon side has rich identity:

```dark
match Stdlib.Http.requestSource request with
| Hub.PeerInstance peer -> 
  // peer.userId, peer.instanceId, peer.handle are all known
  if Stdlib.SCM.canRead peer.userId branchId then
    SyncProtocol.handlePush peer payload
  else
    Http.Response.forbidden
| Hub.PublicHttp _ ->
  // anonymous; or X-Dark-User-Id if logged in via web flow
  ...
```

Access checks live in Dark code. Hub enforces transport identity; permission semantics are ours to write in Dark.

---

## 11. Comparison: this vs Tailscale-default

| Concern | Tailscale-default (old) | Hub-default (new) |
|---|---|---|
| New user signup time | "Sign up for tailscale, install client, join tailnet, then login" — 10+ minutes, requires platform install | `dark login` → browser → done. ~30 seconds. |
| Cross-user sharing (Stachu → Feriel) | "Add her to your tailnet" — privileged op, weird permission model | `dark share branch fix-bug --with feriel@dark.run` |
| Mobile / browser access | Tailscale on phone, kind of works; browser doesn't | WSS works from any browser; mobile is just another Instance |
| Public HTTP exposure | `tailscale funnel` (3 ports, TLS-only, weird limits) | `dark app expose` (any subdomain, normal TLS) |
| Bandwidth cost | Free (mostly direct P2P) | We pay; capped per tier; unlimited on Pro |
| Compliance / privacy | Tailscale sees your metadata | We see metadata; ciphertext only; opt-out via overlay tier |
| Air-gap / on-prem | "Use a self-hosted Headscale" | Tier-3 self-hosted hub (also a Dark app) |
| Engineering scope | "Use tailscale" — small | Real service to build, run, operate |

The new model is more engineering. It's also the only model that gets a stranger from "I heard about Dark" to "my code is syncing across my devices" without any setup outside Dark itself. That's the bet.

---

## 12. Phasing — building the hub without slowing the rest down

The original doc's slice 8 (sync) was "build sync over Tailscale." Replace with these slices:

### Slice 8a: Account + login
- `dark.run/signup`, `dark.run/login`, OAuth bridges (GitHub, Google).
- `dark login` CLI flow.
- Hub stores account + instances + tokens. No WS yet.
- Outcome: users can sign up, log in, see their instance list at `dark.run/account`.

### Slice 8b: Hub WS + presence
- Hub WS endpoint + protocol framing (HELLO/WELCOME/PING).
- Daemon's reconnect loop.
- Presence: instances online/offline, broadcast to peers (for now: just self).
- Outcome: `dark devices` shows your own instances with online status.

### Slice 8c: Hub-relayed sync
- `TO`/`FROM` frame routing.
- `SyncPushOps` / `SyncPullOps` payloads, idempotent.
- Daemon's background sync loop pushes new ops, pulls peer ops via hub.
- Outcome: two-instance same-user sync. The "code follows you between devices" demo.

### Slice 8d: Cross-user sharing
- `share_grants` table, `dark share` CLI.
- ACL enforcement in daemon's sync handler.
- Outcome: Stachu and Feriel collaborate without being on the same tailnet.

### Slice 8e: Snapshot bootstrap
- Periodic encrypted snapshot upload from "primary" instance.
- Hub blob storage (R2 / S3).
- New-instance fast clone.
- Outcome: log in on a new device, full code tree appears in seconds.

### Slice 8f: Public HTTP fronting
- Subdomain allocation, hub reverse proxy.
- `HTTP_REQUEST` / `HTTP_RESPONSE` frame routing.
- Auto-TLS via wildcard cert.
- Outcome: `dark app expose` works.

### Slice 8g: P2P upgrade (optional, later)
- STUN server, signaling protocol over WS.
- QUIC peer connection in daemon.
- Heuristic upgrade for high-volume flows.
- Outcome: less hub bandwidth; lower latency between active peers.

### Slice 8h: Overlay tiers (optional, much later)
- Bring-your-own-tailscale config.
- Managed Dark Network (Headscale-equivalent under our control).
- Self-hosted hub recipe.

Each slice is independently shippable. Each gives users something. 8a-8c is the "I can use this" minimum.

---

## 13. Why this scales

Per-user resource cost on the hub:
- One persistent WS connection (~10 KB of state).
- Some Postgres rows (account, ~5 instances, ~10 share grants, ~3 public apps): ~5 KB.
- Sync bandwidth: avg hundreds of bytes/day; bursts on edits (still small).
- Snapshot storage (opt-in): ~100 MB-1 GB per active user.

100k active users:
- 100k concurrent WS — one beefy node holds 100k easily, two for HA.
- Postgres: ~500 MB hot data; RDS small instance is fine.
- Sync bandwidth: maybe 10 GB/day; trivial.
- Snapshots: ~50 TB on R2 if everyone opts in; ~$1k/mo.

This is well within the price envelope of a normal SaaS. We can offer a generous free tier and still be profitable on power users + orgs. Crucially, **we never become a bottleneck for the actual code execution** — that all happens on the user's instances, not on us. The hub is a phonebook + a courier.

---

## 14. What this means for the rest of the architecture

The original doc's data layer (`ops.db` master, per-branch projection DBs, per-app data DBs, the daemon) doesn't change. Sync transport was the only Tailscale-shaped assumption. With it replaced:

- The daemon's sync agent now talks to `Darkd.Hub`, not to a tailnet peer's IP.
- Auth in Dark code reads `X-Dark-*` headers from the hub, not `Tailscale-User-Login`.
- `dark on <peer>` routes through `Hub.request`, not direct HTTPS to a tailnet name.
- Sessions still live in `sessions.db`; sessions can now be shared across devices via the hub trivially (every instance subscribes to the user's session updates).

The only cost compared to the Tailscale-default plan is: **we have to actually build and run the hub.** That's a real engineering investment. But it's the only path to "millions of users who never think about networking."

---

## 15. Open questions

1. **WS framing format:** CBOR (compact, schema-friendly) vs JSON (debuggable) vs custom binary (we already have one in LibSerialization). Probably CBOR for v1.
2. **Hub source availability:** open-source from day one, source-available with a commercial-use clause, or proprietary? Leaning source-available.
3. **Account recovery:** if a user loses their password and their account-encryption-key derives from it, snapshots are unrecoverable. Need a recovery key flow. (Standard Notes does this; we copy that.)
4. **Subdomain squatting:** first-come-first-served on `<x>.dark.run` is going to be annoying. Reserve handles per-user, namespace public subdomains under the user (`<app>.<user>.dark.run`)? Probably the latter, with `<x>.dark.run` for org accounts.
5. **Rate limiting:** at what tier do we cap WS messages, push throughput, snapshot frequency? Punt; revisit when we have real users.
6. **Encryption key management:** "key derived from password" is fine for personal data; for inter-user shares it's per-share keys wrapped with recipient public keys. Need a clear UX for "I just changed my password; my old shares still work" — solved by re-wrapping under the new key.
7. **Hub multi-region:** Postgres becomes the bottleneck before WS does. Read replicas + a "home region" per user, with cross-region sync only when needed. Standard.
8. **What does "logged out" mean?** The Instance is still alive, still has its local data, still works offline. It just can't sync until login. Need to think about local-only operation as a first-class state, not just a degraded one.

---

## Closing

Tailscale was easy to write about because someone else built the hard part. But the hard part is exactly what we need to own to ship a product: identity, discovery, public exposure, frictionless onboarding. We were going to need an account system, a hosted thing, a way to expose apps publicly anyway. May as well build them as the foundation, with a transport layer thatcan grow from solo-laptop to many-user-org without architectural rework.

The hub is a Dark app. The product runs on the product. Login is one command. Sync is automatic. Sharing is one command. Public HTTP is one command. Tailscale becomes the optional power-user / org / compliance layer.

That's the shape.
