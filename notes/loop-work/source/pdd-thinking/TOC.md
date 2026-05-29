# pdd-thinking — Reading Order

PDD spike + substrate-roadmap design loop (2026-05-13 → 2026-05-20). **28 docs · ~141 pages.** Read in this order, or jump where curiosity pulls.

**30 min · the spine**
- **README** (3p) — what's live in code.
- **ROADMAP** (14p) — *the deliverable.* Chunks · order · phases · critical path · bootstrap-when · sharing-when · MVP demo · risks · ~45 open decisions.
- **COHABITATION** (8p) — unifying frame: agents + humans as opt-in inhabitants of a shared substrate.
- **CLAIMS** (2p) — five reframed claims.

**The core designs**
- **MIGRATION** (5p) — 45 shippable chunks across 5 phases.
- **CONFLICTS-AND-RESOLUTIONS** (9p) — load-bearing substrate; v0 design.
- **CAPABILITIES** (9p) — must-precede-PDD; per-assembly retrofit; AI-opt-in gates.
- **EVENT-STREAMS-AND-PARKING** (10p) — EventBus + parking; classic-dark precedent.
- **IDENTITY** (6p) — humans + agents + delegations.
- **BOOTSTRAP** (4p) — remove .dark files; `LibDB.Seed` already half-built.
- **STABILITY-AND-SHARING** (6p) — wire protocol; matter.darklang.com; share-10 = MVP.
- **HOT-RELOAD** (6p) — `package_dependencies` already exists; just wire to events.
- **COMPOSABLE-MVU** (8p) — `SubApp` already exists; 6-step evolution.
- **REMOTE-ACCESS** (6p) — agent runtime + Tailscale-based peer reach.
- **ALGORITHM** (2p) — incomplete sketch.

**Cross-cutting reference**
- **SCHEMA** (3p) — 18 existing + 7 new tables · **F-SHARP-VS-DARK** (5p) — per-subsystem v1/v2 split · **VIEW-SKETCHES** (5p) — viewer visual brief · **FRONTIER** (4p) — speculative · **GRAPH-PROJECTION** (5p) — graph as derived view · **WRAP-UP** (6p) — pre-substrate spike retro · **SYNC-AND-STABILITY** (4p) — predecessor to STABILITY-AND-SHARING.

**Process artifacts** (read if curious how this came together)
- **loop.md** — substrate-roadmap loop driver, 30 TODOs, status DONE · **feedback.md** + **feedback-original.md** — spike-end consolidation feedback · **archive/substrate-sketches-loop.md** (8p) — prior loop driver · **archive/20-elevator-pitches.md** (1p) — pitches · **archive/README.md** (1p) — archive index.

**What to do after reading.** Per ROADMAP §"What comes after this loop": run a **3-day spike series** (Tailscale-served sync · cap-check microbenchmark · EventBus+parking prototype) before serious investment. If green, **Phase 0 + Phase 1** lands the user's first itch (bootstrap, ~2-3 weeks).
