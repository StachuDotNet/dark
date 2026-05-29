# Iter 17 — Synthesis

Sixteen iterations later, time to step back. What's the
through-line? What's load-bearing vs decorative? What surprised
me? What's the recommended order of attack?

The aim of this iter: make the prior 16 actionable. If you read
only this one, you should know what to build first and why.

## The architectural through-line

Five forces show up across nearly every iter:

### 1. Content-addressing is a force multiplier

Iter 01 makes ops content-addressed. From there:

- **Iter 13:** trace replay works because the same code-hash
  produces identical execution given identical inputs. No
  separate "code recording" needed; the hash IS the recording.
- **Iter 15:** package versioning collapses. `import X @ v1.4`
  pins to a content hash; multiple versions coexist; lockfiles
  are unnecessary; dependency hell evaporates.
- **Iter 16:** encrypted ops still have content-addressable
  metadata; sync routing works without decryption.

This single design choice eliminates problems other systems
spend years on. The compounding interest is enormous.

### 2. Daemon is the only client target

Iter 02 establishes "less F#, more Dark" as a discipline.
Concretely: every UI in the system becomes a JSON-RPC client
of the daemon, speaking the same protocol.

- **Iter 09:** LSP server runs in the daemon; editors connect
  via a 50-line shim.
- **Iter 10:** REPL is the same JSON-RPC, plus a line-editor.
- **Iter 11:** projection fns are Dark in the daemon.
- **Iter 12:** dark.run hosts daemons; same protocol everywhere.
- **Iter 14:** tests are Dark fns the daemon executes.
- **Iter 15:** hub features (search, publish) are Dark.

The architectural payoff: **one source of truth, many UIs**.
Bug fixes apply everywhere; multi-window coherence is free;
hot-swap works uniformly.

### 3. Sync as the data model

Iter 04 establishes conflicts as first-class. The mental model:
ops are written; ops sync; conflicts surface; resolutions are
themselves ops. This shows up everywhere:

- **Iter 11:** projections are branch-aware; sync brings
  ops; projections rebuild.
- **Iter 12:** dark.run is a peer, not a server.
- **Iter 13:** traces sync from production to laptop for replay.
- **Iter 14:** multi-peer tests are first-class.
- **Iter 15:** package forks sync upstream changes; PRs are
  sync ops.
- **Iter 16:** encryption protects op bodies; sync still works.

This makes distributed-by-default the norm, not a feature you
opt into. Single-machine use is just "all peers happen to be
the same machine."

### 4. Hot-swap as the dominant interaction

Edit, see, ship. No restart. This shows up explicitly in:

- **Iter 09:** Edit a fn → diagnostics refresh. Edit your own
  diagnoser → it applies on next keystroke.
- **Iter 10:** Edit a REPL command → next invocation uses new
  code.
- **Iter 11:** Edit a projection → daemon picks it up and
  rebuilds.
- **Iter 13:** Edit code → trace pane re-runs against original
  inputs. See the new behavior live.
- **Iter 15:** Author publishes deprecation with migration →
  importers auto-migrate on next pull.

Hot-swap requires: persistent state, content-addressing,
observable subscriptions. We have all three. The compounding
effect: dev loops shorter than anything else on the market.

### 5. Cost is for compute, not custody

The dark.run business model thread runs through:

- **Iter 12:** dark.run is a peer; user's data lives on user's
  laptop; we charge for compute.
- **Iter 15:** free tier publishing because storage is cheap +
  dedup is content-addressed.
- **Iter 16:** end-to-end encryption means dark.run literally
  cannot custody plaintext; reduces compliance surface.

This is a structurally differentiated business model. Most
SaaS sells custody (your data lives on their servers; you pay
for the privilege of getting it back). We sell compute (your
data lives on your machine; you pay for an always-on copy).

The lock-in profile is much weaker — and honest. That's
attractive to security-minded customers.

## Load-bearing vs nice-to-have

### Load-bearing (v1 ship blockers)

- **Iter 01.** Op binary format — fundamental data layer.
- **Iter 03.** Per-stream ACL — multi-user can't ship without it.
- **Iter 04.** Conflict resolution — distributed sync needs it.
- **Iter 05.** Bootstrap from zero — onboarding flow.
- **Iter 07.** Failure modes — operational readiness.
- **Iter 08.** Migration plan — concrete delivery vehicle.
- **Iter 14.** Test infrastructure — every other PR depends on it.

These need to land in v1. They're not optional and not
deferable.

### High-leverage, not blocking

- **Iter 02.** Less-F#-more-Dark — gradual; ongoing.
- **Iter 09.** LSP rewrite — current LSP works; rewrite improves UX.
- **Iter 10.** REPL — net-new feature; can ship later.
- **Iter 11.** Projections as Dark — starts with F# bootstrap;
  port stream-by-stream.
- **Iter 13.** Time-travel debug — UX surface; data layer
  already supports it.
- **Iter 16 tier 1.** At-rest encryption — straightforward addition.

Each adds significant value but doesn't block v1.

### Strategic, future state

- **Iter 06.** Performance budget — guides v1 design but
  optimizations come later.
- **Iter 12.** Multi-tenant hosting — needs v1 daemon stable
  first; then dark.run launch.
- **Iter 15.** Package ecosystem — needs critical mass of
  users; phase 2.
- **Iter 16 tier 2/3.** Advanced encryption — niche but
  important customer segment; v2.

## Recommended order beyond iter 08

The migration plan in iter 08 lays out 8 PR slices to deliver
the new daemon. After that:

| Phase | Iter | Effort | Payoff |
|-------|------|--------|--------|
| 1 | 14 — Test infra | 3K LOC, 6 wks | Unblocks all subsequent work |
| 2 | 09 — LSP daemonization | 500 LOC, 2 wks | Better dev UX everywhere |
| 3 | 11 — Projections-as-Dark | 2K LOC, 6 wks | Completes "less F#" thesis |
| 4 | 16 tier 1 — At-rest encryption | 1.5K LOC, 4 wks | Security baseline |
| 5 | 13 — Trace replay UI | 1K LOC, 3 wks | Edit-and-replay live |
| 6 | 10 — REPL | 2.2K LOC, 5 wks | Power user feature |
| 7 | 12 — dark.run launch | 5K LOC, 2 mo | Hosted commercial |
| 8 | 15 — Hub ecosystem | 5K LOC, 2 mo | Community / discovery |
| 9 | 16 tier 2 — End-to-end | 1.5K LOC, 4 wks | Compliance niche |

Total: ~22K LOC, ~12 months for 2-4 engineers. Reasonable
project size.

Phases 1-3 are foundational — must happen before phases 4+.
Phases 4-9 can be parallelized somewhat.

## The seven surprises

Things I wasn't expecting before starting these iters:

### 1. Content-addressing eliminates lockfiles

I've watched npm, cargo, pip wrestle with version pinning for
20 years. Iter 15 made me realize: when every fn version is a
unique hash and multiple versions coexist, lockfiles are
unnecessary. The version pin in source IS the lock. Two
versions of the same lib coexist without conflict. Generational
improvement.

### 2. Trace replay falls out for free

Iter 13 isn't a separate "feature" — it's a consequence of:
content-addressed code + recorded inputs + deterministic
builtins. Each is an independent design choice; their
combination gives you `rr` plus Replay.io plus Smalltalk halo
plus more. We don't have to build that — it's emergent.

### 3. dark.run-as-peer simplifies compliance

Iter 16: tier 2 (E2E) streams mean dark.run literally cannot
read certain data. For HIPAA / GDPR / PCI: dramatically smaller
audit surface. "We can't see this, so we can't violate
regulations on it." Most low-code platforms can't make this
claim.

### 4. Tests as ops give observability for free

Iter 14: when test results are ops in a stream, "flake
detection," "trend dashboard," "coverage projection" all become
trivial Dark projection fns. Tools that other ecosystems sell
as separate products (Datadog test analytics, etc.) — we get
them as queries.

### 5. The LSP can be user-extensible

Iter 09: when the LSP is Dark code in the daemon, users register
their own diagnosers, completions, hover providers. Project-
specific lints become Dark fns. This is "Emacs for syntax
tools" — extensibility no other LSP has.

### 6. Edit-and-replay-live changes debugging culture

Iter 13: with the editor's trace pane open and a paused trace,
edit a fn → trace re-runs → see new output. End-to-end "find
bug → write fix → verify fix" in seconds, no test rerun, no
deploy. This is genuinely new debugging UX.

### 7. The hub itself is a Dark app

Iter 12: dark.run runs on the same daemon binary the user runs.
Search, billing, audit, package storage — all Dark. Eat the
dogfood. We feel every operational pain customers feel.

## What's the pitch?

Reading through everything, the unified pitch:

> **Dark is the language with the best devloop on Earth.**
>
> Edit code → see effect → ship in seconds, not minutes.
>
> Production bugs come with full execution traces. Replay
> them. Edit the fix in your editor. Watch the trace re-run
> live. Commit when green.
>
> Two versions of any library coexist; no lockfiles, no
> dependency hell. Forks are first-class with typed upstream
> merges. Deprecations propagate with auto-migrations.
>
> Your data lives on your machine. dark.run is a peer that
> runs while your laptop is closed. End-to-end encryption is
> a checkbox; dark.run literally cannot read encrypted
> streams.
>
> Multi-peer tests are tractable. Time-travel debugging is
> built in. The LSP is hot-swappable, customizable in your
> own code. The REPL persists across sessions, attaches to
> running apps, exports as notebooks.

Every individual claim is a feature competitors have. The
combination — and the architectural unity — is what no one
else has. That's the moat.

## Risks

The plan isn't risk-free. Honest accounting:

### Technical risks

- **Bootstrap chicken-and-egg.** Iter 05 / 11 / 14 all touch
  this. The F# bootstrap layer is permanent surface area we
  carry forever. Bugs there are infectious.
- **Op-replay performance at scale.** Iter 06 / 11 — projections
  on 100M-op streams. Incremental rebuild needed; correctness
  bugs in incrementality eat lunches.
- **Sync correctness under partition.** Iter 04 / 07 — CRDT-
  adjacent territory. Easy to get subtle bugs that emerge only
  after long divergence.
- **Determinism leaks in builtins.** Iter 13 needs every Stdlib
  fn to be replayable. New non-deterministic builtins quietly
  break trace replay; needs CI-level guardrails.

### Product risks

- **Dark is unfamiliar.** Most engineers know JS / Python /
  Go. "Learn a new lang" is a tax.
- **The hub is a single point of failure.** Even with peer-
  based sync, dark.run going down breaks discovery, publish,
  billing.
- **Compliance certs take time.** Tier-2 helps but SOC 2 + 12
  months of audit logs is unavoidable for some customers.
- **Ecosystem cold start.** Empty package registry → no users.
  Need anchor packages + community seed.

### Business risks

- **22K LOC, 12 months** is a long road for a small team.
  Funding / runway risk.
- **Pricing must beat free** (a user's own laptop is free).
  Compute pricing has to land at a sweet spot.
- **AWS won't compete on platform; they'll compete on price.**
  When cloud-side compute is too cheap, the unique value of
  hosting evaporates. Pivot toward self-hosting as primary
  story?

## What I didn't iterate on (gaps)

A few important topics I touched but didn't fully expand:

- **Backups & disaster recovery.** What happens when a user's
  daemon's data dir gets corrupted? Recovery from a peer.
  Worth its own iter.
- **Mobile delivery.** Dark on iOS / Android. Daemon as a
  mobile process? Mobile peers in the sync mesh? Not
  obviously well-served by current design.
- **Deployment / release pipeline.** "Commit → live" UX.
  Branch deploy. Canary. Rollback. Touched in iter 08 but
  user-facing UX undefined.
- **Observability dashboard.** Touched in iter 12. The "what's
  happening on dark.run right now" UI for operators.
- **Money/billing internals.** Pricing, metering, refunds,
  invoices. Touched in iter 12 / 15 but no design.
- **Internationalization.** All examples assume English.
  Unicode is fine; localization of error messages, docs,
  community surface — open.

## A 5-minute exec summary

If you read only this section:

The data architecture rewrite is sound. The "ops + projections
+ daemon + hub" model is foundationally right and has been
validated against 16 angles of interrogation. The work splits
into ~8 PRs (iter 08) for the core delivery, then ~9 follow-on
phases (iter 14, 09, 11, 16, 13, 10, 12, 15, 16-tier-2) for
the full vision. Total ~22K LOC over 12 months for 2-4 engineers.

The architectural through-lines:
1. Content-addressing eliminates classes of problems (lockfiles,
   trace replay, version conflicts).
2. Daemon-as-only-client unifies all UIs around one protocol.
3. Sync-as-data-model makes distributed default, single-machine
   the special case.
4. Hot-swap is the dominant interaction (edit-and-see-it).
5. Cost is for compute, not custody — structurally different
   business model.

The pitch: best-devloop-on-Earth. Edit → trace → live-replay-
fix → ship in seconds. No lockfiles. End-to-end encryption.
Multi-peer tests trivial. LSP/REPL hot-swappable.

Risks are real but tractable. Two-three engineer-years gets
you there. Each phase ships independent value.

Recommended next step: land iter 08's PR slices 0-3 (the
foundation: ops.db, projections framework, daemon, ACL).
Validate. Then proceed to iter 14 (test infra) before anything
else.

## Closing

The 16 prior iters aren't a plan. They're the *feasibility
study* of a plan. They show that the rewrite-doc's vision
holds up under interrogation from many angles, and identify
the load-bearing pieces, the surprises, the risks, the order.

The plan itself — concrete tickets, sprint plans, resource
allocation — is downstream. But the architecture is sound.
The story is coherent. The pitch is unique. The work is
finite.

Time to build.
