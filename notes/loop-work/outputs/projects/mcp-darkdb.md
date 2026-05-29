# mcp-darkdb

**Goal:** Expose the local key/value store to an MCP client as tools.

**Kind:** greenfield

## Acceptance criteria
- [ ] Declares read/write tools backed by the local store.
- [ ] Passes MCP inspector conformance — every declared tool is discoverable and invocable.
- [ ] Malformed JSON-RPC input produces the correct error code.
- [ ] Path traversal / out-of-scope access is rejected.
- [ ] Two concurrent clients do not interfere.
- [ ] `--help` prints usage and exits 0.
