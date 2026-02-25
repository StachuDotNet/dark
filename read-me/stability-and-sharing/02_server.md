# Central Server

## Role
Always-on canonical store for the package tree. Clients (your machine, coworker's machine) push/pull branches. Server also hosts old `.db` snapshots for local dev.

## Infrastructure

### Where it runs
A dedicated always-on machine (your big PC, exposed via Tailscale or similar). Not ephemeral — needs to be reachable across the internet whenever either of you wants to sync.

### What it runs

**Recommendation: Same CLI binary, server mode** (`dark server start --port 8080`)

Based on code review, this is clearly viable:

**HTTP server builtin already exists.** `Builtin.httpServerServe` (`backend/src/BuiltinHttpServer/Libs/HttpServer.fs`) is an ASP.NET Core server that:
- Binds to any port, catches all routes via `/{**path}`
- Supports **binary request/response bodies** as `List<UInt8>` — perfect for serialized ops
- Delegates all routing/handling to a Darklang handler function
- Has pattern-based routing (`/user/:id`, wildcard `*`) via `Stdlib.HttpServer.routeRequest`
- Already registered in CLI builtins (`BuiltinCliHost/Libs/Cli.fs`)

**HTTP client builtin also exists.** `Stdlib.HttpClient` (`packages/darklang/stdlib/httpclient.dark`) provides `get`, `post`, `put`, `delete` with binary body support — clients can push/pull using this.

**Existing CLI command demonstrates this.** `http-server <port>` command already launches an HTTP server from the CLI binary. The sync server would be the same pattern with different routes.

**Concrete implementation sketch:**
```
dark server start --port 8080
```
Registers a Darklang router with sync endpoints (see [01_sync.md](01_sync.md)). The handler function reads/writes binary op data from request/response bodies, interacts with the local `data.db` using the same `insertAndApplyOps` / SQL queries the CLI already uses.

**What would need to be built:**
1. A `server` CLI command (register in `allCommands`)
2. A Darklang router with sync endpoints
3. Handler functions that bridge HTTP ↔ DB operations
4. Process management (systemd unit file or similar)

**Why not a separate binary:**
- All the DB code, serialization, and op playback logic already lives in the CLI's builtins
- The HTTP server is already wired in
- Dogfooding: the server is itself a Darklang app

### Server's data.db
Same schema as local. Contains the canonical state of all branches. Server runs the same migrations as clients. SQLite in WAL mode, single file.

## Backup

Backups happen on the server only (not on local dev machines — local dev can wipe and re-pull).

### What to backup
The server's `data.db` (the whole thing — it's SQLite, single file). SQLite's `.backup` API or a simple file copy (with WAL checkpoint first) works.

### When
- Before any upgrade of the server's CLI binary
- On a schedule (daily? hourly? depends on how often you push)
- Before destructive operations (if any)

### Where
- Local backup dir on the server machine (rotated, keep last N)
- Optionally: offsite (S3, another machine, etc.) for disaster recovery

### Restore
Stop the server process, replace `data.db` with backup, pin to the CLI version that created the backup, restart.

### Automation sketch
A simple script or systemd timer:
```bash
# Pre-upgrade backup
sqlite3 /path/to/data.db ".backup /backups/data-$(date +%Y%m%d-%H%M%S).db"
# Rotate: keep last 30
ls -t /backups/data-*.db | tail -n +31 | xargs rm -f
```

## Small site for .db browsing
A Darklang app on the server that lets you browse old `.db` snapshots and download them. Useful for local dev: "give me last Tuesday's DB so I can work on this git branch."

This is just another route on the sync server — e.g., `GET /snapshots` lists available backups, `GET /snapshots/:name` downloads one.

## TODOs
- [ ] Register `server` command in CLI's `allCommands`
- [ ] Write Darklang router with sync endpoints (see [01_sync.md](01_sync.md))
- [ ] Write handler functions for push/pull/clone/snapshot
- [ ] Set up always-on machine (Tailscale or similar)
- [ ] Create systemd unit file for the server process
- [ ] Implement backup system (pre-upgrade + scheduled via systemd timer)
- [ ] Build `.db` snapshot browser (additional routes on the sync server)
