---
title: dark edit — diff-based function editing
section: 3.2 Authoring
priority: P1
harness_signal: median tokens per pass (§6 #5) + edit-to-first-green (§6 #8)
---

# `dark edit` — diff-based function editing

**Problem**: `dark fn Foo.bar = …` requires resending the full body even for a one-line change. A 50-line function with one bug costs ~50 lines of output every iteration. (`~/vaults/.../Agent Next Steps.md` item #19. Validated by user-known friction: vault TODO "multi-line editing" + "update existing package entities" + "rename package items".)

**Proposed fix**: a new CLI command `dark edit <name> --replace <old> --with <new>` with exact-string-replace semantics (Claude-Code-Edit-style: error if `<old>` not unique, succeed silently otherwise). Falls back to whole-body rewrite if `--replace` is omitted. No language-level change required — it's a CLI surface only.

**Harness signal**: median tokens per pass (§6 #5), edit-to-first-green (§6 #8) drop on the M/L tier. Compare a sweep with/without the wrapper exposing `edit`.

---

**Round-2 reconsideration** *(per feedback-plan P2 "Rethink dark edit --replace for AST-centric model")*: the user pushed back on the simple text-replace framing, noting Dark is **AST-centric and image-based** — no _text_ per se is persisted. A textual `--replace old --with new` doesn't fit cleanly. Reframings to consider:

- `dark edit <fn> --rename-binding <old> <new>` — renames a `let` binding inside a fn body
- `dark edit <fn> --replace-call <old-fn> <new-fn>` — swaps a function-call AST node
- `dark edit <fn> --map-leaves <pattern> <replacement>` — AST-aware pattern match

These respect the image-based / AST-centric model; "old text → new text" doesn't. The original text-replace fix above is *the wrong shape* for Dark; this AST-shape redesign is the actual proposal to ship. The harness signal stays the same — token-cost on the M/L tier — but the API needs to be designed around AST nodes, not strings.
