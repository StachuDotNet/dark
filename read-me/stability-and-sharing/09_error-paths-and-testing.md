# Error Paths & Testing Strategy

## Error Paths

### Push Errors

| Scenario | Detection | User-Facing Message | Recovery |
|----------|-----------|-------------------|----------|
| Server unreachable | HTTP connection failure | `Error: Cannot connect to server at <url>` | Check server is running, check network |
| Branch doesn't exist on server (first push) | 404 from server | Auto-create branch on server, then push | N/A (handled automatically) |
| Server is ahead (someone else pushed) | Server returns newer commit ID than client's last-known | `Error: Server has newer commits. Run 'dark pull' first.` | `dark pull`, resolve any conflicts, then retry push |
| WIP ops exist (uncommitted changes) | Local check before push | `Error: Uncommitted changes on branch. Run 'dark commit' first.` | `dark commit` then retry |
| Op fails to apply on server | Server returns error from `insertAndApplyOps` | `Error: Server rejected ops: <reason>` | Investigate — likely a bug or corruption |
| Auth failure (future) | 401/403 from server | `Error: Authentication failed` | Re-authenticate |

### Pull Errors

| Scenario | Detection | User-Facing Message | Recovery |
|----------|-----------|-------------------|----------|
| Server unreachable | HTTP connection failure | `Error: Cannot connect to server at <url>` | Check server/network |
| Branch doesn't exist on server | 404 from server | `Error: Branch '<name>' not found on server` | Check branch name, or push it first |
| Client has diverged (local commits server doesn't have) | Client has commits after last common ancestor | `Error: Local branch has diverged from server. Push your changes first.` | `dark push` first |
| Op fails to apply locally | Error from `insertAndApplyOps` | `Error: Failed to apply ops: <reason>` | Clone fresh DB as escape hatch |
| Value evaluation fails after pull | `evaluateAllValues` fails for some values | `Warning: <N> values could not be evaluated. They may depend on packages not yet available.` | Re-pull after dependencies are available, or evaluate manually |

### Clone Errors

| Scenario | Detection | User-Facing Message | Recovery |
|----------|-----------|-------------------|----------|
| Server unreachable | HTTP connection failure | `Error: Cannot connect to server at <url>` | Check server/network |
| Local `data.db` already exists | File check before download | `Error: data.db already exists. Use --force to overwrite.` | Pass `--force` flag |
| Download interrupted | Partial file / HTTP error | `Error: Download failed. Partial file removed.` | Retry clone |
| Downloaded DB is corrupted | SQLite integrity check | `Error: Downloaded DB failed integrity check` | Retry clone |

### General Design Principles

1. **Never leave the DB in a partially-synced state.** If applying ops fails midway, roll back the entire batch. Use SQLite transactions.
2. **Clone is the escape hatch.** If anything goes wrong with push/pull, `dark clone` gives you a fresh copy from the server. This should always work.
3. **Errors should suggest the next action.** Don't just say "failed" — say what the user should do about it.
4. **Server errors should be logged server-side** for debugging, but only return sanitized messages to the client.

## Testing Strategy

### Approach: Single DB, Branch Isolation

The existing test infrastructure uses a single shared SQLite DB with isolation via unique canvas IDs. For sync testing, we use **branch isolation** — each test creates its own branch and operates on it.

No need for multiple DB instances. The sync builtins operate on the same DB (they read/write ops, commits, and branches). Testing "push" and "pull" means testing the builtins that would be called by the HTTP handlers.

### Test Layers

#### Layer 1: Builtin Unit Tests (F#)

Test each new builtin in isolation. Follow the pattern in `Propagation.Tests.fs`.

```fsharp
// Example: test scmInsertCommittedOps
let testInsertCommittedOps =
  testTask "insert committed ops creates commit and applies ops" {
    let! branchId = setupBranch "test-insert-committed"
    let fn = makeFn (eVar "x")
    let ops = [ PT.PackageOp.AddFn fn; PT.PackageOp.SetFnName(fn.id, loc "test") ]
    let commitId = System.Guid.NewGuid()

    // Act
    let! result = SyncBuiltins.insertCommittedOps branchId commitId "test commit" ops

    // Assert: commit exists
    let! commits = Queries.getCommits branchId 10
    Expect.hasLength commits 1 "should have 1 commit"

    // Assert: ops were applied (function exists in DB)
    let! fnOpt = ProgramTypes.Fn.get fn.id
    Expect.isSome fnOpt "function should exist"

    // Assert: location exists
    let! locOpt = ProgramTypes.Fn.getLocation [branchId] fn.id
    Expect.isSome locOpt "location should exist"

    // Cleanup
    do! discardAndDeleteBranch branchId
  }
```

**Tests needed for each builtin:**
- `scmInsertCommittedOps`: happy path, duplicate op handling (INSERT OR IGNORE), op playback runs
- `scmGetOpsSinceCommit`: no new ops, some new ops, multiple commits
- `scmGetBranchLatestCommit`: branch with commits, branch with no commits
- `scmCreateBranchFromRemote`: happy path, duplicate branch ID
- Value re-evaluation after sync (new values get evaluated)

#### Layer 2: Push/Pull Integration Tests (Darklang or F#)

Test the full push/pull flow by simulating client and server operations on the same DB using different branches.

```
Setup:
  1. Create branch A (simulates "server state")
  2. Create branch B as child of A (simulates "client branch")
  3. Add ops to B, commit
  4. Call "push" logic: get ops from B, insert as committed on A
  5. Verify A has the ops and content
```

This doesn't require an actual HTTP server — just testing the builtin functions that push/pull handlers call.

#### Layer 3: HTTP Integration Tests (if needed)

Follow the `BwdServer.Tests.fs` pattern — start the sync server in-process as a background Task, then send HTTP requests against it.

```fsharp
let init (token : CancellationToken) : Task =
  let port = TestConfig.syncServerPort
  (SyncServer.webserver port).RunAsync(token)

let testPushViaHttp =
  testTask "push via HTTP" {
    // Setup: create branch with ops locally
    let! branchId = setupBranch "test-http-push"
    // ... add ops, commit ...

    // Act: HTTP POST to push endpoint
    let! response = httpPost $"http://localhost:{port}/branches/{branchId}/ops" body

    // Assert
    Expect.equal response.StatusCode 200 "should succeed"
  }
```

This layer is lower priority — if the builtins work correctly, the HTTP layer is just routing.

#### Layer 4: Conflict / Error Path Tests

- Push when server is ahead → rejected
- Pull when client has diverged → flagged
- Duplicate op insertion → silently ignored (INSERT OR IGNORE)
- Value evaluation failure after pull → partial success with warning
- Concurrent pushes to same branch → one succeeds, one must retry

### SQLite Concurrency for Tests

The existing setup handles this well:
- **WAL mode** enabled (`PRAGMA journal_mode=WAL` in `LibDB/Db.fs`)
- **5s busy_timeout** prevents immediate failures on lock contention
- **Connection pooling** enabled
- Tests run sequentially within a test list (Expecto default)

For concurrent push tests, use `Task.WhenAll` to simulate two simultaneous pushes. SQLite's write serialization + busy_timeout ensures one completes and the other waits (or fails after 5s, which would be a real bug to find).

### What We Don't Need to Test

- Binary serialization roundtrip (already tested separately)
- Op playback correctness (already tested in `Propagation.Tests.fs`)
- HTTP server framework (ASP.NET Core — not our code)
- JSON serialization of Darklang values (already tested in `DvalRepr.Tests.fs`)
