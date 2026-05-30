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

Walk the *minimal* path to "print-md edits sync between two machines":

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
