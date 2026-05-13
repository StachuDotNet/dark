# 09 — Carving the Codebase

> Stachu's directive: "cut all corners as you see fit. any 'packages' or F# that's not needed for this experiment, cut. remove, delete, comment out, I don't care. tighten things so you can work efficiently"

**Status:** Stub. To be deepened with a concrete list.

## Principle

We need: a CLI that can parse Dark code, build a PT, lower to RT, execute. Plus LLM bindings. Plus package store reads/writes.

We **don't** need: BwdServer, ProdExec, QueueWorker, CronChecker, anything cloud, anything K8s, anything web, anything wasm, anything VS Code extension publishing, anything pretty-printing-for-LSP, anything tree-sitter, etc.

## First pass — what to disable in `.sln`

(Verify each in a build sweep before disabling — some may be transitively needed.)

Candidates to disable at the `.sln` level (don't delete, just exclude from build):

- `backend/src/BwdServer/`
- `backend/src/LibCloud/` and `LibCloudExecution/`
- `backend/src/LibClientTypes/` (if still present)
- Anything under `backend/src/CronChecker/`, `QueueWorker/`, `ProdExec/` (per the memory: LibCloud-style, prefer disable at sln/fsproj over delete)
- Most `backend/tests/` — keep only the test project that exercises LibExecution + the new PDD bits
- `vscode-extension/`
- `tree-sitter-darklang/` (compilation only — Darklang code itself stays)
- `wasm/` if it exists

Per the **LibCloud — disable, don't delete** memory: prefer disabling at fsproj/sln level rather than removing source. Quick toggle if we need to bring things back.

## Second pass — what NuGet refs to drop

- `Microsoft.Extensions.*` cloud stuff if not used by anything we keep
- Pubsub, Rollbar, OpenTelemetry, LaunchDarkly, etc. (most already dropped on AOT branch — check)
- Anything related to PostgreSQL, Yugabyte

## Third pass — what to add

- An LLM-provider abstraction (probably already exists from Feriel's work — `LibAI`?). Check `backend/src/LibAI/` or wherever the Anthropic/OpenAI bindings live.
- A `LibPDD` project, parallel to `LibExecution`, for:
  - `materializeFn` implementation
  - find / generate coroutines
  - trace event types
  - capability check hooks

## What stays untouched

- `backend/src/LibExecution/` (we're modifying it but not gutting)
- `backend/src/Cli/` (the CLI, our main surface)
- `backend/src/Tests/` (just the LibExecution-relevant subset)
- The Darklang package source files (the `.dark` files) — but we may add a `pdd_demos/` subdir
- `scripts/` — keep, they're well-curated. Don't break `_assert-in-container` (memory).

## Order of operations

1. Disable in `.sln` first (safest reversal).
2. Confirm build succeeds.
3. Confirm `./scripts/run-cli` still works (CLI hasn't been damaged).
4. Run `./scripts/run-backend-tests` with just the LibExecution-relevant filter (see memory: `--filter` wants exact prefix path; use `--filter-test-list <name>` for substring).
5. Then start adding the PDD bits.

## A note on the memory

The "LibCloud — disable, don't delete" memory says prefer disabling at fsproj/sln level. This applies to the standard codebase, but this is an experimental branch that won't be pushed — the user said "cut all corners as you see fit." So actually deleting is acceptable. But disabling first is faster to do and faster to reverse, so we still default to disabling.
