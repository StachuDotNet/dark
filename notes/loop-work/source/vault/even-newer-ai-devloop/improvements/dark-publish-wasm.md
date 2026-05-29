---
title: dark publish --target wasm
section: 3.5 Sharing
priority: P4+ stretch
harness_signal: deferred
---

# `dark publish --target wasm` — browser distribution

**Problem**: "share with a friend on their phone" easiest path is a URL. A browser-runnable artifact unlocks zero-setup sharing.

**Proposed fix**: WASM target compiles the project + a slim runtime to a single `.wasm` + HTML harness. Friend opens a URL, Dark code runs in their browser sandbox.

**Status**: Out-of-scope for Phase 1–3; a Phase 4+ stretch goal. Listed for completeness so the [`dark-publish.md`](dark-publish.md) feature flagging knows where to add the `--target` flag stub today.

**Harness signal**: deferred until shipped.
