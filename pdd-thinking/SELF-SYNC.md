# Self-Sync — The Focused Plan

Goal: get to the point where you can edit Dark on one of your
laptops and have it show up on your other laptop within ~30s, via
Tailscale, with `.dark` files removed from the repo and the
database seeded from a real `seed.db`.

**Three things, nothing more:**

1. Remove `.dark` files from the repo
2. Bootstrap from a real DB snapshot (no LibParser at runtime)
3. Cheap sync between your own laptops via Tailscale

This doc is **the narrow plan** — extracted from the full
ROADMAP/MIGRATION but stripped of everything that's not strictly
necessary for self-sync between Stachu's own devices.

---

## What this is NOT (deliberately skipped)

| Skipped | Why |
|---|---|
| **Conflict dispatch** (the whole T14/CONFLICTS substrate) | Your laptops won't conflict in interesting ways. Last-writer-wins by timestamp is fine. Add real dispatch when N>1 humans show up. |
| **Capabilities system** (T15) | Your laptops fully trust each other. Add caps when external peers join. |
| **Agents / `CapInvokeLLM` / PDD** | Orthogonal to sync. Per AI-opt-in: skip unless explicitly wanted. |
| **`matter.darklang.com`** central instance | Your laptops are direct peers via Tailscale. No central host until N>1 user. |
| **`ApprovalRequest` flow** | Same user owns everything. Cross-namespace doesn't happen. |
| **Public funnel** | Tailscale-only is fine; tailnet members = you. |
| **PDD viewer / VIEW-SKETCHES** | Not on sync's critical path. |
| **Hot-reload** (per HOT-RELOAD.md) | Restart works. Add when actual live-development demands it. |
| **Full identity model** (delegations, kind, etc.) | Single user (you). One account. Multiple devices. |

Everything in this doc assumes single-user, multi-device, fully-trusted Tailscale tailnet.

---

## Spike series — 3 days, BEFORE any real work

Each spike has a hard time-box + concrete success criterion. If
any spike fails, the plan pivots before serious investment.

### S1: "Two darks talk over Tailscale" — 1 day

**What:** One Dark instance on laptop A runs `Builtins.Http.Server`,
listening on a port. `tailscale serve --https=443 http://localhost:PORT`
exposes it. From laptop B, `dark` runs an HTTP client call to
`https://A.tailnet/hello`. The handler returns hardcoded bytes.

**Why:** Proves Tailscale + Dark's existing HTTP builtins integrate
without surprises. No new infra; just two existing pieces
talking through Tailscale.

**Success criterion:** Calling B → A returns `200 hi` with the
`Tailscale-User-Login` header populated. **One-shot pass/fail.**

**Risk if red:** Tailscale's `tailscale serve` doesn't play nice
with how Dark binds ports, or Dark's Http.Server has a
limitation that blocks. The plan pivots to "build a thin HTTP
server outside Dark" — adds ~1 week.

### S2: "Round-trip a snapshot" — 1 day

**What:** On A, `dark` runs the existing `LibDB.Seed.export` to
produce `/tmp/seed.db`. Copy via `tailscale file cp` to B. On B,
fresh data dir → place `/tmp/seed.db` → start `dark`. Verify
`dark search Stdlib.Int64.add` returns the fn.

**Why:** Proves the seed extract+grow flow round-trips across
machines (not just on one machine). `LibDB.Seed` already exists
on main, but it's never been tested in this cross-machine
config.

**Success criterion:** Stdlib fns accessible on B after install.
Specifically: `Stdlib.List.map` resolves; `dark` runs a simple
expression using it.

**Risk if red:** Schema mismatch / version drift / blob-orphan
sweep issue. Fix is small (~1-3 days) but flags an unknown.

### S3: "Round-trip an op" — 1 day

**What:** A: edit a fn via the existing `dark` CLI (e.g. `dark
commit` with a small edit). Get the new commit's hash. B (still
running): manually `curl A.tailnet/sync/events?since=...` (we'll
add this endpoint as a 5-line Dark HTTP handler in the spike),
get the op-blob, write a 5-line script to feed it into
`PackageOpPlayback`. Verify B now has A's new fn.

**Why:** Proves idempotent op replay across instances. The
existing `PackageOpPlayback.fs` is the lift — we're confirming
it's portable across two DBs that have the same baseline seed.

**Success criterion:** A creates `User.Stachu.Test.foo`; B can
call it after sync.

**Risk if red:** Op-replay isn't idempotent enough across
machines (hash mismatch, dependency resolver disagrees, etc.).
This is the most likely-to-surface-issues spike — would shape
real design work.

### After the spike series

- All 3 green → ship as planned, ~4-6 weeks.
- 1 red → fix path identified; +1-2 weeks.
- 2+ red → pause; re-design.

---

## Extracted PR sequence (post-spike)

12 PRs, ordered for shipping. Each is independently mergeable
and reversible. Each links back to ROADMAP / MIGRATION / BOOTSTRAP
/ STABILITY-AND-SHARING.

### Track A — Bootstrap (3 weeks, ~6 PRs)

| PR | What | Reversibility |
|---|---|---|
| **PR1** *bootstrap-audit* | Run `LibDB.Seed.export` + `growIfNeeded` round-trip in a new CI test. Document any drift. | Test-only addition |
| **PR2** *build-seed-cli* | Add `dark build-seed --output seed.db` CLI mode. Wraps `LoadPackagesFromDisk` + `PackageOpPlayback` + `Seed.export`. End-user runtime no longer touches LoadPackagesFromDisk at this PR's merge. | New code path; existing path unchanged |
| **PR3** *ci-builds-seed* | CI now builds `seed.db` as a release artifact alongside the binary. | CI workflow change |
| **PR4** *first-run-install* | At startup, if data dir empty: copy bundled `seed.db` into place. Otherwise existing flow. | Pure addition |
| **PR5** *relocate-loadpm* | Move `LoadPackagesFromDisk` from `LocalExec` to new `LibBuildTools` project. Only linked into the `build-seed` mode (PR2). | Module reorg; reversible |
| **PR6** *delete-packages-dot-dark* | **The big one.** Tag a release first. Delete `packages/*.dark` (400 files). Archive in a separate branch or repo. Test files in `backend/testfiles/` stay. | Tagged release before merge; revert = re-add from tag |

**Outcome after Track A:** `.dark` files gone from the repo
top-level. CI faster. Install snapshot-based.

### Track B — Sync (2-3 weeks, ~6 PRs)

| PR | What | Reversibility |
|---|---|---|
| **PR7** *sync-snapshot-endpoint* | Dark HTTP handler: `GET /sync/snapshot` returns the current `seed.db` bytes; `GET /sync/snapshot/hash` returns SHA256. Localhost only initially. | New endpoint |
| **PR8** *sync-events-endpoint* | `GET /sync/events?since=<seq>&branch=<id>` returns `package_ops` + `branch_ops` filtered. Localhost only. | New endpoint |
| **PR9** *sync-events-post* | `POST /sync/events` accepts inbound events, runs through `PackageOpPlayback`. Idempotent (existing applied flag handles dedup). | New endpoint; reversible |
| **PR10** *tailscale-identity-min* | Read `Tailscale-User-Login` from request headers. For single-user case: map to your hardcoded `account_id` from `accounts_v0` (no `account_identities` table needed yet — just config). | Hardcoded config; later swap to table |
| **PR11** *autosync-cron* | Dark-side background loop: poll configured peers every N seconds; pull events; push local-since-last-push events. Peers in `cli-config.json`. | Toggle in config |
| **PR12** *cli-sync-status* | `dark sync status` shows last-pull, last-push, pending-out, errors. Manual `dark sync pull` / `push` for forcing. | Pure addition |

**Outcome after Track B:** Edit on laptop A → visible on laptop
B within autosync interval. Tailscale handles addressing + auth.
No central server.

---

## Timeline

```
Day 0-3       Spike series (S1, S2, S3) — pause if any red
              ↓
Week 1        PR1 audit + PR2 build-seed CLI + PR3 CI builds seed
Week 2        PR4 first-run install + PR5 relocate loadpm
Week 3        PR6 delete packages/*.dark (tagged release first)
              -- Bootstrap done; .dark files gone from repo
Week 4        PR7 GET /sync/snapshot + PR8 GET /sync/events
Week 5        PR9 POST /sync/events + PR10 Tailscale identity
Week 6        PR11 autosync cron + PR12 CLI status; iterate
              -- Self-sync working end-to-end
```

- **Optimistic:** 4-5 weeks focused
- **Realistic with one mid-PR debugging session:** 6 weeks
- **Conservative w/ a red spike:** 7-8 weeks

**Aggressive compression to ~3 weeks** is possible if:

- Skip PR5 (relocate loadpm) — leave it where it is; just don't
  invoke at runtime. Less clean but faster.
- Skip PR12 (sync status CLI) — autosync just works silently.
- Use existing schema/identity machinery without polishing (e.g.,
  hardcode the binding in F# rather than adding `account_identities`
  table).

This gives you the *capability* in 3 weeks; the *polished
shape* in 4-6.

---

## Open decisions (narrowed to this scope)

| Q | Recommendation |
|---|---|
| Snapshot file name | `seed.db` for export, `data.db` for runtime. Keep both names. |
| Sync polling interval | 30s default; 5s when actively editing (window of "recent activity"). |
| Conflict semantics | Self-sync: last-writer-wins by timestamp. Same account = no human conflict. If it ever bites, revisit. |
| Where snapshot lives at first-fetch | Hardcode primary device's MagicDNS name in `cli-config.json` for v0. Real discovery later. |
| Identity binding | Hardcode one `account_id` for all your devices for v0 (you're the single user). Add `account_identities` table when adding a 2nd user. |
| Branch sync | Sync all branches (you've got few). Per-branch filter when N>1 users. |
| Failures + retries | Log + skip; user manually re-pushes. Real retry policy when needed. |

---

## What's done at the end

After PR12:

- `.dark` files gone from `packages/`. Repo simpler. CI faster.
- New `dark` install: download binary + bundled `seed.db`. First-run
  copies the seed into place. `Stdlib` + `Darklang.*` all there.
- Edit a fn on laptop A via the existing SCM CLI (`dark commit`).
  Within ~30s (or `dark sync push` for immediate), the new commit
  appears on laptop B.
- `dark sync status` on either device shows the relationship.
- No central server. Tailscale does addressing + TLS + identity.
- You can take laptop B offline; reconnect; sync catches up.

That's the goal. Total: **4-6 weeks of focused work, after a
3-day spike series.**

---

## What this does NOT unblock

When you want it later, you'll know — and you'll be doing it on
top of working sync, not as part of getting sync working.

- **A second user joining** — add the `account_identities` table,
  per-account `capability_grants_v0`, the `ApprovalRequest` op
  variant. ~2-3 weeks of work on top of this plan.
- **Real agents (PDD-style)** — needs `kind=Agent`,
  `delegations`, `CapInvokeLLM` gating. ~2 weeks on top.
- **Public Darklang** (matter.darklang.com for external users) —
  needs auth/billing, public funnel, multi-tenant isolation.
  Substantial.
- **Live collaborative editing** (real-time multi-cursor) — needs
  WebSocket live-push, finer-grained ops, sub-second sync.
  Substantial.
- **Hot-reload of in-flight code** — full T14 dispatch + T16
  EventBus + T17 hot-reload work. ~3-4 weeks on top.

For *your* current use case (two of your own laptops, you edit
on whichever's at hand), the 4-6-week plan above is enough.

---

## Cross-links

- **`ROADMAP.md`** — the full substrate plan; this doc is a
  scope-narrowed subset
- **`BOOTSTRAP.md`** — design for Track A (the 6 bootstrap PRs)
- **`STABILITY-AND-SHARING.md`** — design for Track B (sharing
  wire protocol); this doc cuts to single-user
- **`MIGRATION.md` Phase 1 + Phase 3 subset** — corresponding
  full-substrate chunks
- **`~/vaults/Darklang Dev/05.Implementation/Networking and Internet/Tailscale.md`**
  — the network-stack-shortcut framing that this plan relies on
