# Task: Plan Darklang CLI + LSP Improvements

## Read First
- `context.md` in this directory - background on current state
- Run CLI: `cd ~/code/dark/main && ./scripts/run-cli` then `docs for-ai` and `docs cli`
- VS Code extension: `~/code/dark/main/vscode-extension/`

## Goal

Create implementation plan for:
1. LSP resilience (doesn't crash)
2. CLI access to LSP features (check, complete, actions, fix)
3. `--format=json` on CLI commands

---

## Phase 1: Research LSP

**Find and understand the LSP server.**

- Locate LSP source in `~/code/dark/main/`
- How is it started? (hint: `dark run @Darklang.LanguageTools.LspServer.runServerCli ()`)
- Trace a request: what happens when VS Code sends `fileSystem/write`?
- Where does it crash? What's not wrapped in error handling?

**Output:** Section in plan.md describing LSP architecture and vulnerabilities.

---

## Phase 2: Research CLI

**Understand CLI architecture.**

- How are commands implemented?
- Where does command dispatch happen?
- How would a new command (`check`) be added?
- How do commands currently produce output? (for `--format=json`)

**Output:** Section in plan.md describing CLI architecture and extension points.

---

## Phase 3: Research VS Code Extension

**See how it uses the LSP.**

- What custom requests does it make? (see `context.md` for list)
- How does `fileSystem/write` return errors to the extension?
- How does the extension display those errors?

**Output:** Section in plan.md on existing patterns to reuse.

---

## Phase 4: Architecture Decision

**Decide how CLI connects to LSP.**

| Option | Pros | Cons |
|--------|------|------|
| Spawn LSP per-command | Simple, isolated | Slow startup |
| Shared daemon (socket) | Fast, shared state | Complex |
| Direct function calls | No IPC overhead | Tight coupling |

**Output:** Section in plan.md with chosen architecture + rationale.

---

## Phase 5: Resilience Plan

**How to make LSP not crash.**

- Identify all request handlers
- Pattern for wrapping in try/catch
- How to return LSP error response instead of crashing
- Logging strategy

**Output:** Section in plan.md with specific changes needed.

---

## Phase 6: CLI Commands Plan

**New commands to add, in priority order.**

**Priority 1 (MVP):**
- `check` - show type errors
- `--format=json` - structured output flag

**Priority 2:**
- `complete <context>` - completion suggestions
- `actions <entity>` - list available fixes
- `fix <entity> --action N` - apply a fix

**Priority 3:**
- `rename <old> <new>` - rename across codebase
- `move <entity> <path>` - move to different module

**Output:** Section in plan.md with command specs and implementation approach.

---

## Phase 7: Implementation Roadmap

**Break into shippable increments.**

1. **MVP:** `check` command + `--format=json` (smallest useful change)
2. **v2:** LSP resilience (wrap handlers, error responses)
3. **v3:** `complete`, `actions`, `fix`
4. **v4:** `rename`, `move`

**Output:** Section in plan.md with ordered steps and dependencies.

---

## Final Output

Create `plan.md` with sections from each phase:
1. LSP Architecture (from Phase 1)
2. CLI Architecture (from Phase 2)
3. Existing Patterns (from Phase 3)
4. Architecture Decision (from Phase 4)
5. Resilience Changes (from Phase 5)
6. New CLI Commands (from Phase 6)
7. Implementation Roadmap (from Phase 7)
8. Open Questions

## Rules
- Don't implement anything
- Don't modify code
- Just research and plan
