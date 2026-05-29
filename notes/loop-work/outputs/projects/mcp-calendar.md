# mcp-calendar

**Goal:** Wrap an iCal/tasks file as MCP tools.

**Kind:** greenfield

## Acceptance criteria
- [ ] Declares calendar query/update tools backed by an iCal/tasks file.
- [ ] Passes MCP inspector conformance — every declared tool is discoverable and invocable.
- [ ] Malformed JSON-RPC input produces the correct error code.
- [ ] Path traversal / out-of-scope access is rejected.
- [ ] Two concurrent clients do not interfere.
- [ ] `--help` prints usage and exits 0.
