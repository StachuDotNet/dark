# mcp-fs

**Goal:** Provide an MCP server that exposes a rooted directory as `read`/`write`/`list`/`grep` tools over JSON-RPC, rejecting any path that escapes the root.

**Kind:** greenfield

## Acceptance criteria
- [ ] `mcp-fs --root /tmp/sandbox` starts the server and speaks JSON-RPC over stdin/stdout.
- [ ] A `tools/list` request returns 4 tools (`read`, `write`, `list`, `grep`), each with name, description, and inputSchema.
- [ ] `tools/call` `read` with a relative path reads `<root>/<path>` and returns its content.
- [ ] `tools/call` `read` with an absolute path returns an error ("absolute paths not allowed").
- [ ] `tools/call` `read` with a `../` traversal path returns an error ("path escapes root"); the guard resolves the canonical path rather than relying on a regex.
- [ ] `tools/call` `write` writes `<root>/<path>` and returns a success result.
- [ ] `tools/call` `list` returns the names in the given directory under root; `grep` returns `{file, line, content}` matches.
- [ ] Malformed JSON-RPC (missing `jsonrpc`/`id`) returns the proper JSON-RPC error code.
- [ ] Interleaved requests from two clients get responses correlated to their respective IDs (no cross-talk).
- [ ] `mcp-fs` with no `--root` exits non-zero with usage; `mcp-fs --help` exits 0.
