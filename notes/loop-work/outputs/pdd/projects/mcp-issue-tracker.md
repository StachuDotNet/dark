# mcp-issue-tracker

**Goal:** Provide a JSON-file-backed issue tracker as MCP tools for a solo dev.

**Kind:** greenfield

## Acceptance criteria
- [ ] Declares issue create/list/update tools persisted to a JSON file.
- [ ] Passes MCP inspector conformance — every declared tool is discoverable and invocable.
- [ ] Malformed JSON-RPC input produces the correct error code.
- [ ] Path traversal / out-of-scope access is rejected.
- [ ] Two concurrent clients do not interfere.
- [ ] `--help` prints usage and exits 0.
