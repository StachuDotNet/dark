---
title: mcp-fs
tier: M
class: mcp-server
modules: [Darklang.ModelContextProtocol, Stdlib.Cli.File, Stdlib.Cli.Dir, Stdlib.Cli.Path, Stdlib.Json]
languages: [dark, ts, py, go, rust]
expected_outcome: pass
known_blockers: []
framework_hint: null
core: false
---

# Description

An MCP (Model Context Protocol) server exposing a rooted directory as a set of tools an LLM agent can call: `read`, `write`, `list`, and `grep`. Each tool operates on paths *relative to a fixed root directory*; absolute paths or `..` traversal attempts are rejected.

The server uses MCP's JSON-RPC-over-stdio transport (the standard MCP setup for local tools). It declares its tools at startup via `tools/list`, accepts `tools/call` requests, and emits well-formed JSON-RPC responses.

The point of this project is twofold: **(1)** verify Dark can build an MCP server (the language already ships `Darklang.ModelContextProtocol.serverBuilder` per project-survey §1, so this should work); **(2)** verify the agent gets the path-guard right — a security concern that's easy to flub.

For TS, the natural implementation is `@modelcontextprotocol/sdk`. For Py, `mcp` package (or hand-rolled JSON-RPC). For Go, a hand-rolled JSON-RPC server (no canonical SDK yet). For Rust, the `rmcp` or `mcp_rust_sdk` crate. For Dark, `Darklang.ModelContextProtocol.serverBuilder` exposes the framework. **All languages have viable library/SDK paths** — this spec is `expected_outcome: pass` everywhere.

# Behaviours

- `mcp-fs --root /tmp/sandbox` starts the server, listens on stdin/stdout for JSON-RPC.
- On `tools/list` request: returns 4 tools (`read`, `write`, `list`, `grep`) with valid schemas (each with name, description, inputSchema).
- `tools/call` for `read` with `{"path": "test.txt"}` reads `<root>/test.txt`, returns content as JSON-RPC result.
- `tools/call` for `read` with `{"path": "/etc/passwd"}` (absolute) returns an error: "absolute paths not allowed."
- `tools/call` for `read` with `{"path": "../../etc/passwd"}` (traversal) returns an error: "path escapes root."
- `tools/call` for `write` with `{"path": "out.txt", "content": "..."}` writes `<root>/out.txt`. Returns `{"success": true}` or equivalent.
- `tools/call` for `list` with `{"path": "."}` returns an array of names in `<root>`.
- `tools/call` for `grep` with `{"pattern": "TODO", "path": "."}` returns matches as `{file, line, content}` objects.
- Malformed JSON-RPC (missing `jsonrpc: "2.0"`, missing `id`, etc.) returns the proper JSON-RPC error code.
- Two clients sending requests interleaved on the same server (test via background pipes) get correct responses for their respective IDs (no cross-talk).
- `mcp-fs --help` exits 0.
- `mcp-fs` (no `--root` flag) exits non-zero with usage.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. **External-verifier check**: install `@modelcontextprotocol/inspector` (`npm install -g @modelcontextprotocol/inspector`) and run it pointed at the artifact:
   ```
   mcp-inspector --command "mcp-fs --root /tmp/sandbox"
   ```
   The inspector should connect, show all 4 tools, allow you to call them. **Connection failure or missing tools = spec failed.**
2. Connect a real Claude Code instance to the server (via the local-MCP config in `~/.claude/mcp.json` — Dark's CLAUDE.md template should have an example). Ask Claude to "list files in the sandbox." It should call `tools/call` for `list` and get a sensible response.
3. **Path-guard test**: ask the inspector to call `read` with path `/etc/passwd`. Must return an error, not the file contents. *Critical security check; spec fails if this leaks.*
4. **Path-guard test 2**: `read` with path `../../etc/passwd`. Must reject. **A regex-based path-guard is *insufficient*** — the agent needs to resolve the canonical path and verify it's still inside `<root>`. Mutation test: mentally substitute a regex-only check; confirm the resolution-based test catches it.
5. Test concurrent clients: open two `mcp-inspector` instances against the same `mcp-fs`. Both should work; responses correlate to the right request IDs.
6. **For Dark specifically**: examine the source. Did the agent reach for `Darklang.ModelContextProtocol.serverBuilder`? *They should* — it's the idiomatic answer, exists in stdlib (per project-survey §1), and is *much* easier than hand-rolling JSON-RPC. **Failure to use serverBuilder is a Discovery failure (§3.1).** Note in `SUMMARY.md`.
7. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- mcp-fs --root /tmp/sandbox &  # background; will block on stdio
- echo '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' | mcp-fs --root /tmp/sandbox
- mcp-fs --help
- mcp-fs   # no --root, should error

---

**Why this spec belongs in the bench despite the §2.1 / §7-Phase-3 "MCP is Phase 4+" boundary**: that boundary is about *Dark shipping its own MCP server* (the ecosystem-reach product). This spec is about *agents building MCP servers in Dark* — different question, different metric. We want the bench to measure "can Dark build common-shape AI tools?" and MCP servers are a real category for 2026. **This spec doesn't conflict with the §7 Phase 3 protocol; it complements it.**

**Path-guard subtlety**: this is a *security* spec disguised as a build-an-MCP-server spec. The path-guard test (self-check step 4) is what most agents will get wrong on the first try (regex is the easy answer; canonical-path resolution is the correct answer). Maps to §3 — agents who get path-guards right are demonstrating real understanding, not pattern-matching. **Watch the rubric here**: a spec that "passes" but fails the path-guard test is dangerous, not pleasing.
