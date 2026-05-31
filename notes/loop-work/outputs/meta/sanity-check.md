# Top-to-bottom sanity check

A whole-design read against one question: **will this work as a product, and what keeps it
from being as universal and boring-reliable as `git` and `sqlite`?** Not a per-doc review — a
step-back. Re-run every few passes; findings feed back as todos.

## What makes git/sqlite universal (the bar)

No daemon. Single binary / single file. A format stable for *decades*. Fully local-first,
works offline. Small and boring — a primitive others build a hundred things on. No magic; you
can predict exactly what happens.

## Verdict: the *data* model clears the bar; the *runtime* model is the risk

**Aligned with git/sqlite (this part is strong):**
- The **op stream = git's object store**: append-only, content-addressed. `core.db`/per-repo
  ops DB ≈ a sqlite file. Sync = push/pull of objects. "Each repo is an ops DB" is literally
  git's mental model. This is the most universal-shaped part of the design and it's solid.
- **Projections are disposable caches** — lose them, re-fold. That's the sqlite-index stance.
- **Capabilities as per-instance settings** ≈ git config. **Lean-on-Tailscale** keeps us out
  of the network-stack business. **Daemons-are-Apps** keeps the surface small.

**In tension with the bar (watch these):**

1. **The daemon.** git needs none; sqlite is in-process. Our design leans on a resident core
   daemon (bus, scheduler, sync, warm projections). *A daemon is a thing that runs, crashes,
   and must be managed* — the single biggest deviation from "universal as git/sqlite." The
   docs allow a per-call fallback for stateless reads, but the stance should be **stronger and
   explicit**: the boring local floor must be *complete and daemon-free* (like `sqlite`), with
   the daemon a pure opt-in for sync/apps/live features. Make the no-daemon path the default,
   not the fallback.

2. **The grand runtime substrate may be over-built for the milestone.** Own-async +
   Ply-replacement + parked-frame scheduler + multi-bus event system is a *large, novel*
   amount of machinery. git/sqlite have no scheduler. The async doc already concedes
   build-our-own is justified only by inspectability/playback/caps and says to lean on the
   thin-wrapper stage first — good. But the **spine still lists the scheduler + event bus as
   foundations under sync**, and that's likely wrong (next finding).

3. **Over-unification.** "Capabilities, conflicts, event-streams, MVU are one system seen from
   N angles" is elegant — but git/sqlite are *not* unified theories, they're small boring
   primitives. A beautiful unification can become hard to ship incrementally. The mitigation
   is already in place (everything decomposes into PRs); the discipline to keep is: **each
   primitive must ship and stand alone, never require the whole cathedral.**

## Sharpest finding: the sync milestone probably doesn't need the heavy substrate

Walk the *minimal* path to "print-md edits sync across the tailnet" (two machines is the first
proof; the target is any number of tailnet members against the hub):

```
ops DB (content-addressed)  +  Tailscale HTTP  +  POST /sync/events → fold apply
   +  conflict = FailLoudly (default)  +  install = alias            =  print-md sync
```

None of that inherently needs **parked frames**, an **await scheduler**, or even the **event
bus abstraction** — `POST ops; fold them into the ops DB; rebuild the projection` is a pure
fold. The bus + scheduler are what make *push notification*, *cross-session await*, and
*hot-reload* composable later — the **vision substrate**, not the floor.

So the spine's "Sync… rests on the scheduler" (effort 6 under 7) is **too strong**.
Recommended: explicitly split the spine into **floor** (ops⊥projections, Tailscale, sync
read/write, conflict-as-FailLoudly, apps-alias — ships print-md sync) vs **vision substrate**
(EventBus, async scheduler — makes it composable and live). Autosync can poll first; the bus
upgrades it to push. This keeps the milestone git-small and de-risks it massively.

## Format stability — the real long-pole

sqlite's file format is stable for decades; that's *why* it's universal. Our equivalent is the
**op/PT format**. `bootstrap.md` is honest that F#↔Dark mutual reference under language change
is the gating blocker, and `identity.md` now flags that `Intent` (a PT type on every op) must
stay stable. The universal-as-sqlite goal makes this non-negotiable: **the op + PT
serialization has to reach a frozen, versioned, decades-stable shape.** It isn't there (the
spike still moves it). This is the deepest prerequisite, and it's correctly punted — but it's
the thing that ultimately decides whether this is "universal."

## Not insane — what's genuinely good

The ops/projections split, content addressing, lean-on-Tailscale, capabilities-as-settings,
daemons-are-apps, and the dependency-clean bucket structure are all sound and mutually
reinforcing. The design is *coherent* — the five-facets framing, even if over-claimed, did
produce one small core (`name/init/apply/conflict/resolve/views/invariants`) that everything
else genuinely reduces to. The risk isn't incoherence; it's **scope** — confusing the elegant
full substrate with the boring floor that actually ships print-md sync.

## Gaps fed back as todos

1. Verify the sync milestone's true dependency on EventBus + scheduler; **re-sequence the
   spine** into floor vs vision-substrate; correct "sync rests on the scheduler."
2. **Sharpen the no-daemon-default stance** — the local floor complete without a daemon; daemon
   opt-in for sync/apps (cli-daemon.md + spine).
3. Keep testing the unification against "does each PR stand alone?" — flag any doc that only
   works if the whole system exists.

---

# Second pass — is it review-ready?

Re-run after the floor PR specs, the floor/vision split, tailnet scope, the no-daemon-default
fix, and the meta cleanup. The question now is Sunday-readiness, not viability.

**The first pass's central prediction held — and is now *validated*, not asserted.** It claimed
the floor is git-small and the runtime substrate is the over-build risk. Since then every floor
PR spec was written, and **each one grounds in machinery that already exists on `main`**:
sync = `package_ops` + `applyOps` + idempotent `INSERT OR IGNORE` (already there); ops⊥projections
= `package_ops` + `Seed.growIfNeeded` (already there); conflict-dispatch = wrap `raiseRTE`
(already there); EventBus integration = one construction site (proven in prework). So "the floor
is mostly *expose/reorganize what exists*" is now a demonstrated fact across four specs, not a
hope. The EventBus + scheduler were correctly pulled off the floor's critical path. Both findings
1 and 2 from the first pass are resolved (spine re-sequenced; cli-daemon no-daemon-default).

**Health check (real numbers):** pre-S&S + S&S = ~2360 lines across ~14 tight docs, **zero
up-ref violations**, 0 broken links, every codebase claim grounded against `main`. The spine is
a clean ordered entry point with floor/vision tags and links down to specs. This is review-ready.

**What could make Sunday go badly (honest):**
- **A foundational disagreement Stachu hasn't seen coming.** Mitigated: the genuinely-contested
  calls are *flagged as open*, not hidden — conflict-carrying vs conflict-blind App, build-our-own
  async vs keep Ply, WIP-sync. He adjudicates these; the spec doesn't pretend they're settled.
- **The op/PT format-stability long-pole.** Still the deepest unsolved gate to "universal as
  git/sqlite" (a peer on an older op encoding can't read a newer op). Correctly punted to
  bootstrap, but it's the one thing that ultimately decides universality — worth him knowing it's
  *the* prerequisite, not a detail.
- **Breadth/navigation.** ~14 priority docs + 4 PR specs is a lot to review. The spine is the
  entry point, but a one-paragraph "read in this order" pointer at the top of the spine would
  help a cold reader. *(Low-effort win — candidate next pass.)*

**Verdict: the pre-S&S + S&S deliverable is review-ready.** The floor is a coherent, git-small,
grounded, executable plan whose every step traces to existing `main` code. What's left for
Stachu is to adjudicate the handful of explicitly-flagged open decisions — not to fill gaps.

**Minor housekeeping found:** `later/dark-virtual-files.md` is ~1200 lines (over the <1000 bar) —
a tighten candidate, but it's punted/secondary so low priority.

---

# Third pass — does the BUILT system hold together as a product?

Re-run after the floor was actually *implemented* in `loop-fun` (sync rungs 1–3, builtins, the
CLI command), not just specced. The question is no longer "is the plan viable / review-ready" but
"**does the working thing behave like git/sqlite?**" — now answerable by running it.

**The central prediction held all the way to working code.** Passes 1–2 claimed the floor is
git-small and grounds in existing `main` machinery. The implementation confirms it: `dark sync
pull <peer>` is a thin shell over `Inserts.insertAndApplyOps` (already on `main`) — the new code
is `pull`/`pullFromFile` (read a source's ops above a cursor, fold via the *existing* apply path),
three small builtins, a wire codec, and a CLI command. Full suite **9,534 / 0 / 0**.

**The "it just works" bar is met for the local floor — demonstrated, not asserted.** A value
authored on one instance (`DARK_CONFIG_DB_NAME=peerB.db`, `dark val … = 42L`) was `Not found` on
a second instance, then after `dark sync pull <peerB.db>` it **resolved** there. Idempotent
(re-pull is a no-op via the persisted cursor), content-addressed, no daemon. That *is* the
git/sqlite-shaped property working: a boring one-liner that converges two stores.

**Building it (vs theorizing) surfaced real obstacles a spec wouldn't have:**
- **The SSRF guard blocks the tailnet.** `HttpClient`'s default config blocks loopback *and*
  `100.64.0.0/10` — the Tailscale range — so cross-machine sync over the standard client can't
  reach a peer. Found by *running* it; fixed with a `looseConfig` fetch (`httpClientGetUnsafe`),
  justified because the tailnet IS the trust boundary. A pure-design pass would have missed this.
- **The two-build hash-binding** (`package-ref-hashes.txt`) is the concrete shape of the
  F#↔Dark coupling that gates `.dark`-seeding removal — now grounded, with two exit options.
- **Test-isolation under real load**: global-count assertions race the destructive refold
  (`testSequenced` fix). The kind of thing only a full parallel run exposes.

**Honest gaps to the print-md north star (what's NOT done):**
- **Cross-machine HTTP demo not run live** — the code is built + the SSRF blocker fixed, but the
  headless env can't probe server-readiness; needs a controlled run. (Local cross-instance *is*
  proven, same mechanism.)
- **print-md as an *App* + the `dark apps` surface** — the actual north-star wrapper — isn't built;
  sync moves the *ops*, but "show up under `dark apps`, forkable by Ocean" is the next layer.
- **Divergence auto-resolution isn't wired into the pull path** — `detectDivergences` +
  `CSyncDivergence` + resolution policies all exist and are tested, but `pull` applies straight
  through `insertAndApplyOps` without consulting a policy. Fine for single-author/last-writer; the
  multi-author auto-resolve loop is unbuilt.
- **Format stability** remains the deepest gate (a peer on an older op encoding can't read a newer
  one) — correctly punted to bootstrap, still *the* prerequisite for true git-universality.

**Verdict: the sync *floor* works as a product.** The mechanism — ops + idempotent apply +
per-peer cursors + content-addressed blobs — is as boring-reliable as hoped, and *demonstrated*
moving real content between instances. What stands between here and the north star is not the
sync core (done) but the layers around it: the App/`dark apps` wrapper, the cross-machine
transport's live proof, and the format-stability gate. None of those are "is the design right"
risks anymore — they're build-it-out work.
