# Tailscale transport

The network primitive sync rests on. The whole stance: **lean on Tailscale, don't build a
network stack.** We get peer addressing, TLS, identity, and encryption for free, and the Dark
side reduces to HTTP-over-Tailscale plus a few shell-outs to the `tailscale` CLI. `main` has
nothing here today — this is new, but deliberately thin.

## What Tailscale gives us (and what it doesn't)

| Free from Tailscale | Not provided → our move |
|---|---|
| Stable per-device addressing (MagicDNS `<machine>.<tailnet>.ts.net`) | No L2 peer discovery → read `tailscale status` |
| TLS (`tailscale serve --https=443`) | No `tsnet` F# SDK → speak via the `tailscale` CLI + HTTP |
| Identity via the `Tailscale-User-Login` header | Small free-tier user cap → fine for personal/small-team |
| E2E-encrypted transport + NAT traversal (DERP fallback) | Occasional DERP-fallback latency → acceptable |
| ACLs (tags + grants in HuJSON); public exposure via `funnel` | — |

The substrate is **not bound** to Tailscale: over the open internet the same endpoints work
with ordinary TLS + a bearer-token header. Tailscale is the convenient default for tailnet
members, nothing more.

## No new builtin assembly — a Dark module over shell-outs

`main` ships 9 builtin assemblies (`Pure`, `Http.Client`, `Http.Server`, `Random`, `Time`,
`Cli`, `CliHost`, `Language`, `Matter`). Tailscale needs **none added** — it's a Dark
`Tailscale` module built on `CliHost` (subprocess spawn) + `Http`:

```fsharp
module Darklang.Tailscale

// shells out to `tailscale status --json`, parses the device list (a projection,
// not a stored table). Needs CliHost(spawn) capability — see capabilities.md.
let status () : List<Device> = ...
type Device = { name: String; dnsName: String; online: Bool; self: Bool }

// the MagicDNS URL for a peer: "https://{name}.{tailnet}.ts.net"
let peerUrl (name: String) : String = ...

// expose this instance's HTTP server over TLS: `tailscale serve --https=443 <port>`
let serve (localPort: Int64) : Result<Unit, String> = ...

// read on the Http.Server path: Tailscale injects this after terminating TLS.
let loginHeader (req: HttpRequest) : Option<String> = ...   // "stachu@stachu.net"
```

So the transport is: `serve` to expose the local sync server, `status`/`peerUrl` to address
peers, and `loginHeader` to authenticate inbound requests.

## Auth: the header *is* the identity

Tailscale terminates TLS and injects `Tailscale-User-Login` on every inbound request. The
receiver maps that login → `account_id` (a one-time onboarding binds a tailnet login to a Dark
account), and that `account_id` becomes op authorship. **No bearer tokens, no second identity
source — the tailnet is the trust boundary for now.**

```
caller ──HTTPS──► tailscale (terminates TLS, injects Tailscale-User-Login: stachu@stachu.net)
                     │
                     ▼
              receiver: login → account_id → op authorship
```

`serve` can also inject `Tailscale-User-Name` and **`Tailscale-App-Capabilities`** (grants
declared in the tailnet's HuJSON policy). The app-capabilities header is a useful *input* to
the capability gate ([capabilities.md](capabilities.md)) — a network-level hint about what a
peer may do — but it is **not** authority: grants are per-instance settings, so the receiving
instance still decides. Read it, don't obey it.

## First move: ping/pong

The single most confidence-building first step — it proves the whole stance end-to-end before
any sync logic exists:

```
# on the server (always-on desktop)
$ dark tailscale serve 11011        # tailscale serve --https=443 → :11011
serving https://major.tail-scale.ts.net  →  GET /ping → "pong"

# on the client
$ dark tailscale ping major
→ GET https://major.tail-scale.ts.net/ping
← 200 "pong"  (as stachu@stachu.net, 14ms)
```

Once ping/pong works over the tailnet, the sync endpoints are just more HTTP handlers on the
same `serve`d server, authenticated by the same header.

## Test plan

- `Tailscale.status` parses a captured `tailscale status --json` fixture into `Device`s (.fs
  unit test over a fixture; no live daemon).
- `peerUrl` builds correct MagicDNS URLs (.dark test).
- ping/pong integration: two instances on a tailnet (or a mocked `serve`), assert `pong` +
  the login header round-trips to the right `account_id`.

## What's built on this

The sync wire protocol (S&S) and remote-control (later) both ride this transport — they
reference *down* here for addressing + auth. This doc owns only the transport; the protocol
that moves ops over it is specified one bucket up.
