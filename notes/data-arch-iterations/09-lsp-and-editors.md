# Iter 09 — LSP, editors, and "live"

The rewrite doc § 19 q10 raises the LSP question: "the LSP becomes
a daemon client too. This is a separate project but one this
rewrite *enables* — the daemon is the natural LSP backend." Time
to put a sketch on it.

There's a lot of LSP code already in
`packages/darklang/languageTools/lsp-server/`. This iteration
isn't "design from scratch" — it's "what changes about the
existing LSP when the daemon is the source of truth, and what
new affordances does the editor get?"

## Where does the LSP server live?

Two options:

**Option A:** Per-window LSP process. Each VS Code window spawns
its own LSP server (today's model in most editors). The server
talks to the daemon for resolved package data.

**Option B:** LSP runs inside the daemon. VS Code talks directly
to the daemon over JSON-RPC. One LSP, many editors.

I'd take B. Reasons:

- **Single source of truth.** With the daemon owning projections,
  package resolution, branch context, and conflicts, having a
  separate LSP process duplicates state. Bugs become "the LSP
  thinks Foo.bar is at hash X, but the daemon thinks Y" — same
  class as today's branchChainCache invalidation problems we're
  trying to delete.
- **Multi-window coherence.** Two editors open on the same
  project see exactly the same diagnostics, completions, etc.
  Edits made in one window propagate to the other immediately.
- **Resource efficiency.** No per-window 100MB process.
- **The LSP becomes hot-swappable.** Per iter 02, the LSP server
  is Dark code. Edit the autocomplete fn, see your edit land in
  the next keystroke.

The concerns are:

- **LSP message volume can be high.** Cancel-on-keystroke
  patterns, diagnostic refresh, semantic-token updates per save.
  Daemon's main loop has to handle this cleanly. Mitigation:
  per-editor session contexts in the daemon, async dispatch, no
  serialized request queue.
- **Crash isolation.** LSP is Dark code with a 500ms timeout
  budget per request (per iter 07's poison-op handling). A bad
  diagnostic fn doesn't crash the daemon; it just returns a
  fallback empty list. Per-window state is recoverable from the
  ops log.

## The transport

Today's LSP transport is stdio JSON-RPC. With the daemon, the
question is: same JSON-RPC, different stream?

**Yes.** The daemon's unix socket already speaks length-prefixed
JSON for CLI RPC. Add a `Content-Type: application/lsp` content
negotiation flag on the connection, after which the daemon
treats the stream as a vanilla LSP server. VS Code's standard
LSP client connects via a small bootstrap that opens the socket
and forwards stdin/stdout.

```
VS Code ←─ stdio ─→ dark-lsp-bootstrap (~50 LOC binary)
                      ↓ unix socket
                    darkd
                      ↓ Dark fn dispatch
                    Darklang.LspServer.handleMessage
```

The "LSP bootstrap" is a tiny shim that tells VS Code "here's an
LSP" while the actual server is in the daemon. ~50 lines of F#
or C, statically linked, runs in microseconds. Same approach
Roslyn LSP uses.

## What's already in the codebase

`packages/darklang/languageTools/lsp-server/` — Dark code, ~25 files:

```
aaaa-state.dark              # LspState type
docSync.dark / sync.dark     # text doc lifecycle
hover.dark / hoverInformation.dark
completions.dark
diagnostics.dark
semanticTokens.dark
docs.dark
fileSystemProvider.dark
showDocument.dark
treeView.dark
scm.dark                     # SCM commands (commit, etc.)
…
```

This is already most of what the LSP needs. The migration:

1. **Stop spawning a separate LSP process.** The CLI side that
   spawns it dies; the daemon's `handleMessage` is now reachable
   over the unix socket from VS Code.
2. **State migrates from per-process LspState to daemon-side
   per-editor session.** Each editor connection gets a session
   id, daemon tracks `(sessionId → LspState)` in a Map.
3. **Multi-window coordination.** When window A edits a doc,
   window B's LspState is notified via a stream. Implementation:
   each LspState subscribes to ops on the (stream='packages',
   key=*) and refreshes its diagnostics when relevant ops land.

## The user's code drives the LSP

This is the wildest idea, and I think it's right.

A Dark user wants a custom lint rule. Today they'd file an issue
or write an external tool. With Dark code driving the LSP:

```dark
// In the user's package tree:
module Mycorp.LintRules

let noStringConcatInLoops (expr: ProgramTypes.Expr) : List<Lsp.Diagnostic> =
  // Walk the AST, find Stdlib.String.append calls inside Stdlib.List.map,
  // emit a warning.
  ...

// Register with the LSP:
Lsp.registerDiagnoser
  { id = "no-string-concat-in-loops"
    severity = Warning
    fn = Mycorp.LintRules.noStringConcatInLoops
    appliesTo = Lsp.AppliesTo.AllFunctions }
```

The user adds this fn to their tree; the daemon's LSP picks it
up (LSP server reads its own diagnoser registry from the
projection); the editor surfaces the diagnostic on next keystroke.

What this enables:

- **Project-specific lints** without writing a custom LSP server.
- **Code review automation.** A team's review checklist becomes
  Dark fns that diagnose every doc.
- **Refactor-correctness checking.** "When you do X, also do Y" as
  a Dark fn that runs at edit time.
- **Documentation rules.** Style guide as Dark code.
- **Custom completions.** User-defined snippets that suggest
  type-aware defaults.
- **Hot-swap during a debug session.** Add a `Builtin.debug` call
  via `Lsp.registerDiagnoser` that fires whenever a specific fn
  shape appears.

This is the Emacs lesson — make the editor scriptable in the
target language. We get this almost for free since the LSP is
already Dark.

## Editor-as-supervisor

The editor's "daemon-aware" features:

### Live diagnostics (already)

Every keystroke parses through tree-sitter (in-editor); validates
through daemon (on debounce). Diagnostics refresh.

### Live trace overlays

User runs an app from another terminal. Editor shows the trace's
fn calls inline next to source:

```dark
let process (xs: List<Int64>) : List<Int64> =
  xs                                       // received [1; 2; 3] from caller
  |> Stdlib.List.map (fun x -> x * 2)      // returned [2; 4; 6]
  |> Stdlib.List.filter (fun x -> x > 2)   // returned [4; 6]
```

Implementation: editor subscribes to the `traces` stream filtered
to "ops referencing fn-hashes I'm currently displaying." Daemon
pushes new trace ops to the editor; editor renders inline
annotations. Per-trace toggle.

This is the same machine as today's `view --with-trace` but live-
streaming, in the editor. Hot-swappable pretty-printer registry
(per `notes/wrap-up/hot-swappable-pretty-printers.md`) controls
how each value renders.

### Live conflicts (per iter 04)

Code-lens above conflicted fns. Click → opens a diff editor with
both sides + accept-left/right/merge buttons. Resolution writes a
Resolution op via the daemon's API. Other windows' diagnostics
update.

### Live presence

Editor shows a small banner when peer X is editing the same fn:

```
👤 Feriel is editing this function (last keystroke 3s ago)
```

Implementation: Editor's `didChange` notifications get rebroadcast
through the hub as `presence` ops (ephemeral, not stored). Other
windows subscribe and render.

This is non-blocking — both can edit; LWW resolution if they conflict.
But the warning prevents accidental concurrent edits.

### Live test results

If an app or fn has tests, the editor surfaces the test status as
inline gutters. Test runs in the daemon (or its app subprocess);
results push back to editor.

### Live REPL / playground

A right-hand-side panel in VS Code that's a `dark eval` REPL
backed by the daemon's ExecutionState. Variables defined here
persist across REPL inputs (within the editor session). Closing
the editor cleans up.

```
dark> let users = [{name: "alice"}, {name: "bob"}]
dark> users |> Stdlib.List.map (fun u -> u.name)
["alice"; "bob"]
dark> Mycorp.processUsers users
[handler return]
```

Implementation: the panel speaks a small subset of LSP custom
methods (`dark/repl/eval`, `dark/repl/state`).

### Branch switching from editor

UI: a status-bar item showing current branch. Click → branch
picker. Pick a different branch → daemon switches the editor's
session's branch context; doc symbols refresh.

```
[main ▾]  →  feat-cli  feat-auth  bugfix-x  +new
```

`DARK_SESSION` and per-folder config drive the default; explicit
selection overrides.

## What VS Code shows that today's editors can't

The big affordances unique to the daemon-backed model:

1. **All your projects live in one place.** Open VS Code on
   `Mycorp.MyApp` and `Personal.MyBlog` simultaneously; both
   projects' state is in the daemon; switching is instant.
2. **No per-project setup.** No `nuget restore`, no `npm install`,
   no per-project build. The package store is global (per-user);
   all dependencies are content-addressed.
3. **Edit and run a remote app.** `dark on stachu-major start
   my-blog` from VS Code. The editor stays local; the run
   happens remotely. Traces stream back.
4. **Pair-debugging.** Two users on the same project, each
   sees the other's edits and traces in real-time.

These are the "remember when this was hard" affordances. The
daemon makes them straightforward.

## Diagnostic invalidation

When does an editor's diagnostic refresh?

- **Editor edit.** Tree-sitter validates locally, debounce 200ms,
  send `didChange`, daemon re-resolves, returns diagnostics.
- **A peer's edit lands** (sync pull brings ops). Daemon notices
  the (stream, key)s affected; for each editor session whose
  open docs touch those keys, push refreshed diagnostics.
- **A schema bump in the daemon.** All open editors get a "your
  diagnostics may be stale; refreshing" notification, then
  re-validate everything.
- **A user-registered diagnoser fn was edited.** The diagnoser
  registry refreshes; affected diagnostics re-run.

The frequency: tens to hundreds per second on active editing
sessions. Daemon's LSP dispatcher handles each in <50ms typical.
Diagnostics push to editor on completion.

## Interactive code editing — "save" semantics

Today: edit a fn → save the file → daemon parses and emits a
WIP op. The op enters the projection; diagnostics go.

What if save = nothing? Each keystroke could emit a WIP op. But
that's silly — too noisy, too many tiny ops. Compromise:

- **In-editor edits** are not committed to ops.db until either:
  - User saves (Cmd-S), OR
  - 30 seconds idle, OR
  - User explicitly runs `dark save`.
- **Each save** is one WIP op (per fn that changed).
- **`dark commit "msg"`** wraps recent WIPs into a commit.

The editor shows pending save state ("3 unsaved changes") in the
status bar. `dark wip` from terminal lists them.

Per the unified-model doc, this is just the existing op flow
with a debounce on emission. The hot-edit feel is preserved
(no waiting on saves) without filling the op log with garbage.

## What the LSP API looks like

Per iter 02, the LSP itself is Dark. Its public Dark surface
(for users to register diagnosers, completions, hover, etc.):

```dark
module Darklang.Stdlib.Lsp


/// A piece of code (an expression, a function declaration, a module).
type LspNode = ...

/// Diagnostic surfaced in the editor.
type Diagnostic =
  { id: String
    severity: DiagnosticSeverity
    message: String
    range: Range
    relatedInfo: List<RelatedInfo> }

type DiagnosticSeverity = Error | Warning | Info | Hint


/// Register a diagnoser fn. Called for every parsed node matching
/// `appliesTo`.
let registerDiagnoser (rule: DiagnosticRule) : Unit = ...


/// Register a completion provider. Called when the user triggers
/// completion in a context the provider claims.
let registerCompletionProvider (provider: CompletionProvider) : Unit = ...


/// Register a hover provider. Called when the cursor rests on a node.
let registerHoverProvider (provider: HoverProvider) : Unit = ...


/// Register a code-action provider. "Quick fix" entries.
let registerCodeActionProvider (provider: CodeActionProvider) : Unit = ...


/// Schedule a notification (for warnings that aren't tied to a node).
let notify (msg: String) (severity: DiagnosticSeverity) : Unit = ...
```

A whole lint suite is a small Dark module. A whole language
extension is a small Dark module + maybe a custom syntax-
highlight rule.

## What the daemon needs to expose

The daemon's RPC surface for editors:

```
# Standard LSP messages
initialize / initialized / shutdown / exit
textDocument/didOpen / didChange / didClose
textDocument/hover
textDocument/definition / typeDefinition / references
textDocument/completion
textDocument/codeAction / rename
textDocument/semanticTokens
textDocument/diagnostic (push notification)
window/showMessage
workspace/workspaceFolders

# Dark-specific extensions
dark/branch/list / set / show
dark/conflicts/list / resolve
dark/traces/subscribe (stream filtered to displayed fn-hashes)
dark/repl/eval / state / clear
dark/presence/subscribe
dark/sync/status / pull / push
```

The Dark-specific extensions are the affordances iter 04 +
above produces. They're cheap to add — daemon already has the
data; the LSP-side handler is a few lines per method.

## Editor-side: VS Code extension

The Dark VS Code extension would do:

1. On activate: find or spawn `darkd`. Resolve the socket path
   (`~/.darklang/daemon.sock`).
2. Run the LSP bootstrap shim that bridges stdio ↔ socket.
3. Configure the standard LSP client to use that bootstrap.
4. Add VS Code-specific UIs: status bar items, tree views,
   command palette commands that send `dark/*` requests.

~500 LOC of TypeScript total. The heavy lifting is on the daemon
side.

## Open questions

1. **What about non-VS-Code editors?** Same LSP server works for
   anything that speaks LSP — Cursor, Helix, Neovim with
   nvim-lspconfig, etc. The bootstrap shim is the only
   editor-specific piece (and it's <50 LOC; can be replicated).

2. **Web-based editor (Codespaces, dark.run/edit)?** Same. The
   web editor connects to a daemon in a sandbox; LSP messages
   ride a WebSocket to that daemon.

3. **What if a user has 20 windows on the same project?** Daemon
   handles 20 LSP sessions with shared underlying state. Each
   session has its own per-window context (open docs, cursor
   positions); the package store / projections / etc. are shared.

4. **Crash recovery for LSP state.** If the daemon restarts mid-
   session, the editor's LSP client reconnects. Document state
   is reopened (`textDocument/didOpen` from the editor's
   working buffer). Cursor positions, scroll positions —
   those are editor state, not LSP state, so no loss.

5. **Schema migrations of LSP types.** Dark fns evolve. If
   `Stdlib.Lsp.Diagnostic` gains a field, registered diagnosers
   that produce the old shape need a migration story. Same as
   any Dark API: deprecate the old type, ship the new, run
   both for a release cycle.

6. **Latency tolerance for completion.** VS Code completion
   wants sub-200ms response. Daemon's LSP dispatcher needs to
   prioritize completion requests over slower stuff (diagnostic
   recompute). Worth a request-priority queue.

## TL;DR

LSP server runs in the daemon, not as a separate process.
VS Code (and any LSP client) talks to the daemon via a tiny
~50-line bootstrap shim. The LSP itself is Dark code, hot-
swappable; users register their own lint/completion/hover
providers as Dark fns. Live trace overlays, conflict UI, peer
presence, REPL panels are daemon-side affordances surfaced
through a few `dark/*` LSP extensions.

The big idea: **the editor is just one client of the daemon.**
The daemon is what runs, persists, syncs, and reasons; the
editor is a presentation layer. VS Code, terminal, hub-served
web editor, future GUI — they're all peers, all looking at the
same state.

Free affordances: multi-window coherence, hot-swap LSP, live
traces, peer presence. Things only this architecture makes
trivial.
