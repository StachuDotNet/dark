# Stability & Sharing

## Goal
Two contexts to solve for:
1. **Production**: Central server (always-on) + client installs on your machines. Push/pull branches, preserve history, backup on server.
2. **Local dev** (building Dark itself): Can wipe freely. Pull old `.db` snapshots from the server as needed.

## Dependency Graph

```
                    ┌─────────────────────┐
                    │  Eliminate packages/ │
                    │  dir from repo       │
                    └──────┬──────────────┘
                           │
              ┌────────────┼────────────┐
              ▼            ▼            ▼
     ┌────────────┐  ┌──────────┐  ┌─────────────────┐
     │ Seed DB in │  │ Push/Pull│  │ Git↔Dark branch  │
     │ CI release │  │ mechanism│  │ correspondence   │
     └─────┬──────┘  └────┬─────┘  └────────┬────────┘
           │               │                 │
           │          ┌────┴─────┐           │
           │          ▼          ▼           │
           │   ┌───────────┐ ┌────────┐     │
           │   │ Server    │ │ Server │     │
           │   │ infra     │ │ backup │     │
           │   └─────┬─────┘ └───┬────┘     │
           │         │           │           │
           ▼         ▼           ▼           ▼
     ┌─────────────────────────────────────────┐
     │ Binary serialization versioning         │
     └─────────────────┬───────────────────────┘
                       │
              ┌────────┴────────┐
              ▼                 ▼
     ┌──────────────┐  ┌──────────────────┐
     │ Stop purging │  │ PT→PT migration  │
     │ GH releases  │  │ (punt for now)   │
     └──────────────┘  └──────────────────┘
```

## Phases

### Phase 0: Foundations
- [ ] **Stop purging old CLI releases from GH** — delete lines 122–172 in `scripts/deployment/publish-github-release`. Small, safe change.
- [ ] **Binary serialization versioning** — add version-dispatching to `makeDeserializer` in `BinarySerialization.fs` (~50 lines); add header to `RT.ValueType`; document tag registry. See [03_binary-serialization.md](03_binary-serialization.md).

### Phase 1: Server + Sync
- [ ] **Server command** — register `server` in CLI's `allCommands`, write Darklang router + handlers. See [02_server.md](02_server.md).
- [ ] **Server backup** — systemd timer + sqlite3 `.backup` command, keep last N snapshots. See [02_server.md](02_server.md).
- [ ] **Push/pull/clone** — Darklang code using `Stdlib.HttpClient` (client) and `Stdlib.HttpServer` (server). JSON payloads. See [01_sync.md](01_sync.md).

### Phase 2: Bootstrap
- [ ] **Seed DB generation** — add `generate-seed-db` command to `LocalExec.fs`, add CI step, upload as release asset. See [06_bootstrapping.md](06_bootstrapping.md).
- [ ] **Git branch ↔ Darklang branch** — env var (`DARK_BRANCH`) or `.darklang-branch` config file. See [01_sync.md](01_sync.md).

### Phase 3: Cleanup
- [ ] Eliminate `packages/` dir as runtime dependency
- [ ] `.db` snapshot browser on central server (additional routes)

## Key Files (reference)

| Area | Key Files |
|------|-----------|
| Binary serialization | `backend/src/LibBinarySerialization/BinaryFormat.fs`, `BinarySerialization.fs`, `Serializers/PT/Expr.fs` |
| Op insertion | `backend/src/LibPackageManager/Inserts.fs` |
| Purge | `backend/src/LibPackageManager/Purge.fs` |
| Package loading | `backend/src/LocalExec/LoadPackagesFromDisk.fs`, `LocalExec.fs` |
| HTTP server | `backend/src/BuiltinHttpServer/Libs/HttpServer.fs`, `packages/darklang/stdlib/httpserver.dark` |
| HTTP client | `packages/darklang/stdlib/httpclient.dark` |
| CLI commands | `packages/darklang/cli/core.dark`, `packages/darklang/cli/commands/` |
| Release script | `scripts/deployment/publish-github-release` |
| CI | `.circleci/config.yml` |
| Migrations | `backend/migrations/` (5 files) |
| SCM | `packages/darklang/cli/scm/` |

## Files in this directory (reading order)
- [01_sync.md](01_sync.md) — push/pull wire format, ID model, SCM invariants, git↔dark branch mapping
- [02_server.md](02_server.md) — server architecture, HTTP server details, backup
- [03_binary-serialization.md](03_binary-serialization.md) — versioning strategy for binary blobs, tag registry
- [04_serialization-rename.md](04_serialization-rename.md) — LibBinarySerialization → LibSerialization rename, JSON wire format
- [05_new-builtins.md](05_new-builtins.md) — exactly which new F# builtins sync needs (~90 lines total)
- [06_bootstrapping.md](06_bootstrapping.md) — seed DB generation pipeline, eliminating .dark files
- [07_db-size.md](07_db-size.md) — PT vs RT breakdown, ops-only shipping, lazy RT derivation
- [08_propagation.md](08_propagation.md) — purity analysis of propagation ops, sync safety, improvement options
- [09_error-paths-and-testing.md](09_error-paths-and-testing.md) — error handling for push/pull/clone, testing strategy
- [10_implementation-plan.md](10_implementation-plan.md) — concrete steps with effort estimates, ordered by dependency
- [11_questions.md](11_questions.md) — open questions (some answered from code review)
