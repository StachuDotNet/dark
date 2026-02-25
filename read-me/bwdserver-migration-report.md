# BwdServer Migration Report
## What to Reimplement When Dropping F# BwdServer

*Generated: January 31, 2026*

---

## Executive Summary

The F# BwdServer is ~420 lines but depends on ~29 files of supporting infrastructure that would become obsolete after migration to Darklang. The good news: much of this is already partially addressed by the new `http-server` command and `Stdlib.HttpServer` module.

---

## 1. BwdServer Core (Server.fs)

**What it does:**
- HTTP server on port 11001 via ASP.NET Core/Kestrel
- Domain → Canvas routing via SQLite lookup
- Loads HTTP handlers for path/method matching
- Route matching with specificity (concrete > wildcard > empty)
- Executes Darklang handlers, returns HTTP responses

**Request Flow:**
```
HTTP Request → Kestrel
  → Canvas.canvasIDForDomain()
  → Canvas.loadHttpHandlers()
  → Routing.filterMatchingHandlers()
  → Routing.routeInputVars()
  → CloudExecution.executeHandler()
  → Http.Response → Client
```

**Migration Status:** Partially done with `Stdlib.HttpServer` module

**Still Needed:**
- Multi-canvas/domain routing (currently single-router)
- Dynamic handler discovery from DB
- Hot reload without restart

---

## 2. LibCloud - Infrastructure Layer

### Canvas.fs (~433 lines)
- Canvas CRUD (create, load, save, delete)
- SQLite persistence (`canvases_v0`, `domains_v0`)
- Domain-to-Canvas mapping
- Kubernetes health checks

**Reimplement:**
- Canvas loading/saving as Darklang types
- Domain routing table management

### Routing.fs (~195 lines)
- URL pattern matching (`/user/:id`)
- Route variable extraction
- Specificity scoring

**Migration Status:** Done in `Stdlib.HttpServer.matchRoute`

### UserDB.fs (~200+ lines)
- Datastore CRUD operations
- Type checking for stored values
- SQL query compilation from Darklang lambdas

**Reimplement:**
- `DB.set`, `DB.get`, `DB.delete`, `DB.query` builtins
- Lambda → SQL compilation

### Secret.fs (~55 lines)
- Canvas-level secret storage
- CRUD operations

**Reimplement:** Secret management builtins

### Tracing.fs (~100+ lines)
- Request tracing for debugging
- Trace sampling rules
- Execution recording

**Consider:** May not need in simplified Darklang server

### Additional Files
| File | Purpose | Action |
|------|---------|--------|
| Account.fs | User management | Keep minimal |
| Serialize.fs | Canvas serialization | Port to Darklang |
| Config.fs | Cloud config | Use Darklang config |
| File.fs | Static file serving | Reimplement |
| Password.fs | Password hashing | Keep as builtin |
| Stats.fs | Usage stats | Reimplement |

---

## 3. LibCloudExecution

### CloudExecution.fs (~178 lines)
- Handler execution orchestration
- ExecutionState creation
- Tracing integration
- Error handling

**Migration Status:** Partially absorbed by LocalExec

### HttpClient.fs (~102 lines)
- HTTP client security sandboxing
- Blocks private IPs, metadata services, localhost

**Keep:** Critical security - should remain as F# builtin enforcement

### Init.fs
- Library initialization

**Drop:** Not needed

---

## 4. LibHttpMiddleware (Http.fs ~105 lines)

- Request → Darklang type conversion
- Response → HTTP bytes conversion
- Header normalization

**Migration Status:** Done in `Stdlib.HttpServer` and F# `HttpServer.fs` builtin

---

## 5. LibService - Ops Infrastructure

| Component | Purpose | Action |
|-----------|---------|--------|
| Kubernetes.fs | Health probes, graceful shutdown | Reimplement for prod |
| Kestrel.fs | Server config | Keep in F# builtin |
| Logging.fs | Request logging | Reimplement in Darklang |
| HSTS.fs | Security headers | Add to response helpers |

---

## 6. BuiltinCloudExecution

### DB.fs (~200+ lines)
Database access builtins:
- `DB.set(table, key, value)`
- `DB.get(table, key)`
- `DB.delete(table, key)`
- `DB.query(table, filterLambda)`

**Critical:** Query compilation from Darklang lambdas to SQL

**Reimplement:** Need these builtins for datastores

---

## What Can Be Dropped Entirely

| Project | Files | Reason |
|---------|-------|--------|
| LibCloud | 15 | Cloud concerns → Darklang |
| LibCloudExecution | 3 | Execution setup → LocalExec |
| BuiltinCloudExecution | 2 | Builtins → Darklang fns |
| BuiltinDarkInternal | 7 | Admin ops → Darklang |
| BwdServer | 1 | Replaced by Darklang |
| LibHttpMiddleware | 1 | HTTP → Darklang |
| **Total** | **~29** | |

---

## What Stays (Core F# Infrastructure)

- **LibExecution** - Execution engine, types
- **LibPackageManager** - Package loading
- **LibParser** - Source parsing
- **LibBinarySerialization** - Storage format
- **BuiltinExecution** - Core builtins
- **Prelude** - F# utilities

---

## Priority Reimplementation List

### High Priority (Blocking Production Use)
1. **Database builtins** - DB.set/get/delete/query
2. **Multi-domain routing** - Canvas lookup by domain
3. **Secret management** - Canvas-scoped secrets
4. **Static file serving** - Webroot access

### Medium Priority (Nice to Have)
5. **Kubernetes health checks** - Liveness/readiness probes
6. **Request logging** - Structured logging
7. **Graceful shutdown** - Request draining
8. **HSTS headers** - Security headers helper

### Low Priority (Can Defer)
9. **Tracing** - Execution debugging
10. **Usage stats** - Analytics
11. **Password hashing** - Already have builtin

---

## Minimal New Builtins Needed

Based on analysis, these F# builtins would enable full Darklang server:

```fsharp
// Already exists
httpServerServe : Int64 -> (Request -> Response) -> Unit

// Needed for dynamic handler discovery
packageValuesByType : TypeName -> List<PackageValue>

// Needed for DB
dbSet : String -> String -> Dval -> Unit
dbGet : String -> String -> Option<Dval>
dbDelete : String -> String -> Bool
dbQuery : String -> (Dval -> Bool) -> List<Dval>

// Needed for secrets
secretGet : String -> Option<String>
secretSet : String -> String -> Unit

// Needed for static files
fileRead : String -> Option<Bytes>
```

---

## Recommended Migration Path

### Phase 1: Current (Done)
- Basic HTTP server in Darklang
- Single-router pattern
- Package manager API working

### Phase 2: Database
- Add DB builtins
- Port UserDB operations
- Test with datastores

### Phase 3: Multi-Canvas
- Domain → Canvas routing
- Dynamic handler loading
- Handler hot reload

### Phase 4: Production Ready
- Kubernetes probes
- Graceful shutdown
- Logging/tracing
- Security headers

### Phase 5: Cleanup
- Remove BwdServer
- Remove LibCloud
- Remove LibCloudExecution
- Consolidate remaining F#

---

## Files Reference

**Current F# to Study:**
- `backend/src/BwdServer/Server.fs`
- `backend/src/LibCloud/Routing.fs`
- `backend/src/LibCloud/Canvas.fs`
- `backend/src/LibCloud/UserDB.fs`
- `backend/src/LibCloudExecution/CloudExecution.fs`

**Darklang Implementation (Done):**
- `packages/darklang/stdlib/httpserver.dark`
- `packages/darklang/cli/commands/http-server.dark`
- `packages/darklang/canvas/dark-packages/main.dark`

---

*End of Report*
