# CLI structural Dark ProgramTypes editor

A structural / projectional editor for Dark: you edit the **ProgramTypes AST
directly**, never text. There is no parse step between you and the program —
the thing on screen *is* a projection of the AST, and every keystroke is an op
against it. This is the editor in the spirit of **dark-classic** (the old
fluid/structural editor) and **Hazel** (typed holes, edit-the-tree-not-the-string).

Read alongside the vault note **"05.Implementation/Editing/structured and
projectional editing.md"** (the conceptual background) and
[distributed-event-sourcing.md](../pre-s-and-s/distributed-event-sourcing.md) (the substrate
this rides on). The one-line framing borrowed from that doc:

> **An edit is an op. The rendered view is a projection.** The editor never
> mutates a buffer of characters; it appends ops to a stream, and re-folds them
> into whatever the cursor is currently looking at.

## Why structural, in the ops-vs-projections frame

Text editing conflates two things the rest of Dark keeps apart: the **model**
(the AST, mutated only by ops) and the **projection** (what you see). A text
buffer *is* the model in a normal editor — you mutate characters and a parser
guesses the tree afterward. Here the AST is canonical and the text-looking
layout is a derived view, one of possibly many:

- **Model:** `ProgramTypes.Expr` (and the surrounding `PT.PackageFn`,
  `PT.PackageType`, …). The only way it changes is by applying an **EditOp**.
- **Projection:** a `View` tree (see below) folded from the current AST plus
  cursor/selection state. The same AST projects to a terminal layout today, an
  HTML layout later, a graph view, a type-signature-only view, etc.

This buys the things text editors fight for and lose:

- **No parse errors mid-edit.** You cannot type an unbalanced paren because
  there are no parens to balance — there are nodes and **holes**. An incomplete
  program is a tree with holes, not a syntactically broken string.
- **Renames, moves, wraps are ops, not regex.** "Wrap this expression in a
  `match`" is one EditOp that replaces node N with `Match(N, holes)`. It
  conflicts cleanly with concurrent edits ([conflicts](../stable-and-syncing/conflicts-and-resolutions.md)) and
  replays deterministically.
- **Projections are free and plural.** Because the view is derived, a second
  person (or a screen reader, or a web pane) can fold the same op stream into a
  *different* projection without the editor knowing or caring.

### EditOps are just the App's ops

The editor is one `App<EditorState, EditOp>` (per
[distributed-event-sourcing.md](../pre-s-and-s/distributed-event-sourcing.md)). Sketch:

```fsharp
type EditOp =
  | ReplaceNode of NodeId * Expr      // fill a hole, swap a subtree
  | InsertInto  of NodeId * slot: Int64 * Expr
  | DeleteNode  of NodeId             // collapses back to a hole
  | Rename      of NodeId * String    // binding / field / fn name
  | WrapIn      of NodeId * ctor: Ctor // wrap N in match/if/let/lambda…
  | MoveCursor  of NodeId * Caret
  | SetView     of ViewSpec           // change the *projection*, not the AST
```

The split that has to stay clean: `ReplaceNode`/`Wrap`/`Rename`/… mutate the
**model** (they sync, they replay, they conflict). `MoveCursor`/`SetView`
mutate only **local projection state** — they never leave the instance and never
conflict. Cursor position is session-specific projection, exactly like the file
view or dependency graph in the keystone doc.

## Powered by a tiny LLM loop

The hard part of a structural editor is the **action mapping**: a flat keyboard
emits hundreds of intents, and "what does `Ctrl-W` mean *right here*" depends on
what node the cursor is on, what type is expected, and which projection is
active. dark-classic hand-wrote this table and it was enormous and brittle.

Instead, model the mapping as a **tiny LLM loop**, deliberately small and local:

```
(keyboard shortcut)  +  (current state / view / cursor node + expected type)
        │
        ▼
   tiny model  ──►  a list of EditOps (how the projection should change)
        │
        ▼
   apply ops  ──►  re-fold  ──►  new View
```

The model's job is narrow: **given this shortcut and this context, decide *how*
the rendering should change** — usually by emitting one EditOp, sometimes a few.
It is not writing your program; it is resolving "what did that keypress mean
here." Keep it small enough to run on every keystroke.

### Caching makes it fast

A per-keystroke model call is only viable if it almost never actually calls the
model:

- **Key the cache on `(shortcut, node-kind, expected-type, view-spec)`** — *not*
  on the literal AST. The vast majority of keystrokes are repeats of a situation
  already seen (`Tab` on a hole expecting `Int`, `Ctrl-W` on a function-call
  node). Those resolve from cache with no model call at all.
- **Cold path only on novel shapes.** A genuinely new `(shortcut, context)`
  tuple is the only thing that hits the model; the result is cached and the
  cache *is itself a projection* — losing it costs only CPU, never correctness.
- **Promote hot entries to a static table.** Once an entry is hit enough, freeze
  it into a plain keymap. Over time the editor compiles its own dark-classic-style
  action table out of the loop, instead of a human writing one. The loop becomes
  the fallback for the long tail, not the per-keystroke critical path.

### Self-hosting: the editor's model is editable in the editor

The mapping model is **a Dark value** (a fn body that is "forever lazy" — a
delegated LLM call, per [distributed-event-sourcing.md](../pre-s-and-s/distributed-event-sourcing.md)
and the CLAIMS framing). It lives in the package corpus like any other value,
which means:

- You can open the model's prompt/weights-ref/keymap **in this editor** and edit
  it structurally, the same way you edit any fn.
- Changing it is an op on the corpus; it syncs and forks like everything else.
- A user who dislikes a binding doesn't file a bug — they edit the projection
  rule and (optionally) fork it. The editor editing its own behavior is the same
  mechanism as the editor editing your CSV pipeline.

This is the self-hosting claim: **the editor is an App whose own
keyboard-to-op mapping is data the editor can edit.**

## UI design

The view is a `View` tree (the same algebra as
[composable-mvu.md](../pre-s-and-s/composable-mvu.md), shared across the codebase):

```fsharp
type View =
  | Text of String * Style
  | Row of List<View>
  | Column of List<View>
  | Bordered of View
  | KeyHints of List<(String * String)>
  | ScrollableList of List<View> * focused: Int64
  | Input of String * placeholder: String
  | Empty
```

Layout *ideas* are stolen from a generic immediate-mode component library like
**Clay**: the editor builds a fresh view tree every frame (immediate-mode, no
retained widget objects), nodes declare **sizing intent** (`grow`, `fit`, fixed)
and **flow** (row/column) rather than absolute coordinates, and the renderer
solves the layout. That declarative sizing/flow is exactly what lets the **same
tree render to a terminal today and to HTML later** — a `Row` with `grow`
children is `<div style="display:flex">` in the browser and a column-balanced
ANSI strip in the terminal. The view is target-agnostic; only the final renderer
differs (terminal → ANSI, web → HTML/flexbox, later → svg/voice).

### Components, top to bottom

- **Breadcrumb / zoom strip** — where you are in the tree (`Package ▸ User.Stachu
  ▸ analyzeCsv ▸ body ▸ pipe[2]`). Clicking a crumb zooms the projection out.
- **Signature line** — the focused item's type, always shown, rendered from the
  AST (not retyped). Holes show as typed blanks.
- **Edit canvas (the focus panel)** — the AST projected as an editable tree. This
  is the main region; the cursor sits *on a node*, not between characters.
- **Hole / completion popover** — when the cursor is on a hole, the candidates the
  expected type admits (and the tiny-loop's suggested op).
- **Dive-in panel (optional, right)** — detail for the selected node: its sig,
  body, dependents, trace — mirrors the [view-sketches.md](../pdd/view-sketches.md) dive-in mechanic.
- **Key hints bar** — context-sensitive shortcuts for the current node kind,
  populated from the cache/keymap (so it shows *real* current bindings, including
  user-forked ones).
- **Op/event ribbon** — the recent EditOps as a timeline (this is the op stream,
  surfaced as a projection); scrubbable, since replay is just re-folding.

### Mockup 1 — cursor on a typed hole

```
┌ Package ▸ User.Stachu ▸ analyzeCsv ▸ body ──────────────────────────┐
│ analyzeCsv : String -> List<String>                                 │
├─────────────────────────────────────────────────────────────────────┤
│  analyzeCsv csv =                                                    │
│    csv                                                               │
│    |> parseRows                                                      │
│    |> List.map ⟦ ▮ : List<String> -> Float ⟧   ◄ cursor on hole     │
│    |> sortByVariance                                                 │
│                                                                       │
│  ┌ fill hole : List<String> -> Float ──────────────────────────┐    │
│  │ ▸ calcVar        (fn in scope, exact type)                   │    │
│  │ ▸ \row -> …      (new lambda, body = hole)                   │    │
│  │ ▸ ⌕ search by type…                                          │    │
│  └──────────────────────────────────────────────────────────────┘    │
├─────────────────────────────────────────────────────────────────────┤
│ Tab fill · Ctrl-W wrap · Ctrl-↑ zoom out · / search                  │
├─────────────────────────────────────────────────────────────────────┤
│ ops:  Rename pipe[1].arg → "csv"   ·   ReplaceNode #h4 → parseRows    │
└─────────────────────────────────────────────────────────────────────┘
```

The hole `⟦ ▮ : … ⟧` is a first-class node, not missing text. The popover lists
only type-admissible fills; the key-hints bar is the current (cached) keymap for
a hole node.

### Mockup 2 — a "wrap" op resolved by the tiny loop

```
┌ analyzeCsv ▸ body ▸ pipe[2] ────────────────────────────────────────┐
│ pressed Ctrl-W on  `List.map calcVar`                                │
├─────────────────────────────────────────────────────────────────────┤
│ tiny-loop:  (Ctrl-W, node=Apply, type=List<Float>)                   │
│   → cache MISS → model → WrapIn(#n2, ctor=Match)                      │
│   ✓ cached for next time                                             │
│                                                                       │
│  ... |> match (List.map calcVar) with                                │
│        | [] -> ⟦ ▮ : List<String> ⟧                                  │
│        | vars -> ⟦ ▮ : List<String> ⟧                                │
│                                                                       │
├─────────────────────────────────────────────────────────────────────┤
│ Enter accept · Esc undo (pops the op) · Ctrl-W cycle ctor            │
└─────────────────────────────────────────────────────────────────────┘
```

`Ctrl-W` on an `Apply` node was novel, so the loop ran once and emitted a single
`WrapIn` op; the result is now cached, so the next `Ctrl-W`-on-Apply is instant.
Undo is just popping the op off the stream.

### Mockup 3 — editing the editor's own mapping (self-hosting)

```
┌ Package ▸ Dark.Editor ▸ keymap ▸ holeMapping ───────────────────────┐
│ holeMapping : Shortcut -> NodeCtx -> List<EditOp>     (lazy / LLM)   │
├─────────────────────────────────────────────────────────────────────┤
│  rules =                                                             │
│    [ (Tab,   onHole)  -> [ FillBestCandidate ]                       │
│      (CtrlW, onApply) -> [ WrapIn Match ]      ◄ you are editing this │
│      (CtrlW, onLet)   -> [ WrapIn If ]                                │
│      _                 -> delegate ⟦ tiny-model ⟧ ]                  │
│                                                                       │
│  editing a rule here is an op on the corpus → syncs + forkable        │
├─────────────────────────────────────────────────────────────────────┤
│ Enter edit rule · F fork keymap · ↩ revert to upstream               │
├─────────────────────────────────────────────────────────────────────┤
│ ops:  ReplaceNode #r17 → WrapIn If    (in Dark.Editor.holeMapping)    │
└─────────────────────────────────────────────────────────────────────┘
```

The keymap is just another package value, opened in the same editor; the
`_ -> delegate ⟦ tiny-model ⟧` arm is the fallback loop. Promoting a hot
cache entry is literally adding a rule arm above that fallback.

## Eventually: HTML, not just the terminal

Nothing above is terminal-specific. Because views are projections and layout is
declarative (Clay-style sizing/flow), the editor targets:

- **Terminal** — the `View` tree → ANSI; the default and the proving ground.
- **HTML** — the same tree → flexbox DOM; `Row`/`Column`/`grow` map onto
  `display:flex`. This is the eventual web editor, with zero new view code.
- **Later** — svg/reMarkable, voice (narrate the tree). Each is a renderer, not
  a fork of the editor.

The renderer is a substrate function, not per-editor code — exactly the
[composable-mvu.md](../pre-s-and-s/composable-mvu.md) stance, so the structural editor inherits
multi-target rendering for free.

## Open questions

- **Op granularity vs. typing latency.** One EditOp per keystroke is clean but
  chatty for sync; batching keystrokes into a coarser op risks muddying replay.
  Likely: local cursor ops never sync, structural ops batch on a debounce.
- **Where the tiny model runs.** Local small model vs. a cached remote call. The
  cache design above assumes the model is rarely on the hot path either way.
- **Holes vs. partial text.** Hazel allows text inside a hole pending parse;
  decide whether a hole can hold uninterpreted text as an escape hatch, or stays
  strictly structural.
- **Conflict shape for concurrent structural edits.** Two people wrapping the
  same node — defer to the [conflicts](../stable-and-syncing/conflicts-and-resolutions.md) timing model (a dev-time
  conflict), don't special-case it here.
- **Parser / `compile` dependency.** Editing is parse-free, but two enabling
  primitives from the keystone still matter: a **composable Dark parser** for
  importing/pasting text into the AST, and the **`compile` builtin** for turning
  the edited AST into runnable code. See the parser/`ref`/`compile` thread in
  [distributed-event-sourcing.md](../pre-s-and-s/distributed-event-sourcing.md).
