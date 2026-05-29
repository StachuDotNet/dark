# mcp-git

**Goal:** Expose git `status`/`diff`/`log`/`blame` as MCP tools.

**Kind:** greenfield

## Acceptance criteria
- [ ] Declares git tools that return correct results for the current repo.
- [ ] Passes MCP inspector conformance — every declared tool is discoverable and invocable.
- [ ] Malformed JSON-RPC input produces the correct error code.
- [ ] Path traversal / out-of-scope access is rejected.
- [ ] Two concurrent clients do not interfere.
- [ ] `--help` prints usage and exits 0.
