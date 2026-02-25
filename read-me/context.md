# Darklang IDE Context

Background for building CLI + VS Code tooling.

## Darklang Basics

**Live programming environment** (like Smalltalk). No files - code lives in a persistent package tree of entities (functions, types, values).

## Current CLI

Already exists with these commands:

| Category | Commands |
|----------|----------|
| Create | `fn`, `type`, `val` |
| Execute | `run`, `eval` |
| Navigate | `nav`, `ls`, `view`, `tree`, `search` |
| SCM | `status`, `commit`, `discard`, `log` |

Workflow: `fn` → `run` → `status` → `commit`

## Current VS Code Extension

Uses **virtual URI schemes** (no real files):
- `darkfs://Darklang.Stdlib.List.dark` - editable module
- `dark://package/Darklang.Stdlib.List` - read-only browser

**Custom LSP requests** beyond standard protocol:
```
dark/getRootNodes          → package tree
dark/getChildNodes         → expand node
fileSystem/read { uri }    → module content
fileSystem/write { uri, content } → parse + persist, returns { success, ops[], errors[] }
dark/scm/getWipSummary     → uncommitted changes
dark/scm/getCommits        → history
```

**LSP started via CLI:**
```bash
dark run @Darklang.LanguageTools.LspServer.runServerCli ()
```

## What's Missing (for AI agents)

**Diagnostics:**
- `check` - show errors (most important for agents)
- `check <entity>` - check specific thing

**Intelligence:**
- `complete <context>` - what can I type here?
- `typeof <expr>` - type info
- `actions <entity>` - available fixes/refactors
- `fix <entity> --action N` - apply a fix

**Refactoring:**
- `rename <old> <new>`
- `move <entity> <new-path>`

**Output:**
- `--format=json` on all commands (agents parse JSON, not human text)

## The Editor/File Problem

Agents and editors expect files. Darklang has none.

**Options:**
1. **Virtual filesystem (FUSE)** - transparent but complex
2. **Export/import** - `dark export ... > file`, edit, `dark import file ...` - clunky
3. **Inline strings** - `fn "..."` - awkward for multi-line
4. **LSP with package URIs** - `dark://Darklang.Math.add` - purest solution
5. **Heredoc/stdin** - `dark set Entity <<'EOF' ... EOF` - agent-friendly

## Architecture Question

How should CLI access LSP intelligence?

VS Code connects via stdio. How does CLI connect?

**Option A: CLI spawns LSP per-command**
- Simple, slow (startup cost each time)

**Option B: Shared daemon**
- Fast, complex (socket/IPC, state sync)

**Option C: CLI calls LSP functions directly**
- No IPC, but tighter coupling

## LSP Resilience Problem

Current LSP crashes on:
- Malformed JSON
- Invalid requests
- Runtime exceptions

Needs: try/catch everywhere, error responses instead of crashes, logging.

## Agent Workflow (ideal)

```bash
# Create
fn "Darklang.Math.add (a: Int64) (b: Int64): Int64 = a + b"

# Check
check --format=json
# → {"errors": []}

# Run
run Darklang.Math.add 2 3
# → 5

# If errors, see fixes
actions Darklang.Math.add --format=json
# → [{"id": 1, "title": "Add type annotation"}, ...]

# Apply fix
fix Darklang.Math.add --action 1

# Commit
commit "add Math.add"
```

## Key Insight

The LSP already does the hard work (parsing, type checking, code actions). CLI just needs to:
1. Connect to LSP (somehow)
2. Format args → LSP request
3. Format LSP response → terminal output

The intelligence exists. We just need to expose it.
