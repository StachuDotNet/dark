# Open Questions

## Binary Serialization
- [ ] How often do PT changes happen right now? (Determines urgency of versioning work)
- [x] ~~The current format version is `1` in the header. Is there any code that checks this, or is it ignored?~~ **Answered:** Yes, `Validation.validateVersion` raises `UnsupportedVersion` if incoming version > `CurrentVersion`. The version IS checked, but since everything writes `1` and checks `<= 1`, it's effectively a no-op today. The infrastructure is already there — just needs version-dispatching in the reader.

## Server
- [ ] Same CLI binary in server mode (`dark server start`), or a separate binary? **Leaning strongly toward same binary** — the HTTP server builtin already exists (`Builtin.httpServerServe`), is ASP.NET Core based, supports binary bodies, and is already registered in CLI builtins. A `http-server` CLI command already demonstrates this working.
- [ ] Tailscale on your big always-on PC — is that the plan? Any concerns about availability/reliability?
- [ ] Process management: systemd service? Docker container? Just a tmux session? (Systemd recommended for auto-restart and logging)
- [ ] Does the server need auth? For two people on Tailscale, maybe not. But if it's ever more exposed, it would. (Could start without auth if Tailscale provides network-level isolation)

## Sync
- [x] ~~Wire format for push/pull: binary-serialized ops (same as on-disk)? Or something else?~~ **Answered:** JSON. Ops convert to Darklang values via `PT2DT.PackageOp.toDT`/`fromDT`, then serialize to JSON via `DvalReprInternalRoundtrippable`. All 8 op types roundtrip cleanly (PT expression trees are structural DEnum values, not runtime closures). Data volumes are tiny (~50-80KB per push). Binary transport is possible later if needed but JSON is simpler to start with.
- [x] ~~When pushing a branch, should the content items (package_types/values/functions rows) be pushed too, or should the server re-derive them by replaying ops?~~ **Answered:** Re-derive by replaying ops. `insertAndApplyOps` already calls `PackageOpPlayback.applyOps` which updates all projection tables automatically. Only ops need to travel over the wire. This is much simpler.
- [ ] Should `dark pull` auto-rebase the current branch if the server has new commits on the parent? Or keep it manual?

## Git ↔ Darklang Branches
- [ ] The `--branch` flag already exists. Is an env var (`DARK_BRANCH`) or a local config file (`.darklang-branch`) the right UX for "pin my local dev to this Darklang branch"?
- [ ] Should there be a naming convention? e.g., git branch `feature-x` corresponds to Darklang branch `feature-x` or `pr/1234`?
- [ ] When building Dark itself on a git branch that changes PT: the local dev DB will have old-format blobs. Is "wipe and re-parse from `.dark` files" still the acceptable answer here, at least until PT→PT migration is built?

## Bootstrapping
- [x] ~~The seed DB is generated in CI by parsing `.dark` files. Who decides when to regenerate it?~~ **Answered:** Every release. CI already builds CLI binaries in `.circleci/config.yml` (build-parser → build-backend → build-cli → publish-github-release). Add seed DB generation after build-cli, upload as additional release asset.
- [ ] Should the seed DB include any branches besides `main`?
- [x] ~~What's the minimum the CLI needs to function (enough to `dark clone`)?~~ **Answered:** Everything in `packages/`. The CLI binary invokes a Darklang function (`executeCliCommand` in `packages/darklang/cli/core.dark`). The CLI command code, SCM code, stdlib, and all dependencies must be in the DB. The `dark clone` command itself would be Darklang code that calls `Stdlib.HttpClient.get(serverUrl ++ "/snapshot", [])` and writes the response to disk. So even `dark clone` needs a working DB with at least the clone command's code loaded. This is why the seed DB must include all packages, not just stdlib.

## Scope / Priority
- [ ] Is there a concrete timeline or milestone for "I want to be able to push/pull with my coworker"?
- [ ] For Phase 0 (stop purging releases, binary serialization versioning): should I start on the implementation, or is this still in design/approval phase?

## New Questions (from deep code review)

### Value Evaluation on Server
- [x] ~~When ops are pushed to the server, should values be re-evaluated server-side?~~ **Answered:** Yes, the server should evaluate values, and it CAN — the server IS a Darklang app running on the same engine. `PackageOpPlayback.applyOps` handles content table updates when ops are inserted. Value evaluation (`evaluateAllValues`) would need to run after op insertion for values to be usable. But for sync purposes, the ops are the source of truth — evaluation can happen lazily.
- [x] ~~Can evaluation happen lazily / not at all?~~ **Answered: No — it's required.** `applyAddValue` always stores NULL for `rt_dval`. The CLI runtime (Interpreter.fs line 441) directly reads `rt_dval` from the DB with no fallback — if NULL, it throws `ValueNotFound`. There's no lazy evaluation path. After pulling ops that create values, `evaluateAllValues` (or a scoped version) MUST run. Values are evaluated once and stay evaluated unless propagation creates a new version (which is a new value item that also needs evaluation). See implementation plan step 1h.

### RT.ValueType Special Case
- [ ] `RT.ValueType.serialize/deserialize` does NOT use the binary header — it's written raw. Should it get a header for consistency, or is this intentional? (It's used for the `value_type` column in `package_values`, which is a small ancillary blob.)

### Propagation System and Sync
- [x] ~~Do propagation ops sync correctly?~~ **Answered:** Yes. Propagation ops (`PropagateUpdate`/`RevertPropagation`) contain the exact UUIDs and repoints. When played back via `insertAndApplyOps`, they're applied as-is — the system does NOT re-generate propagation. The rule: never call `pmPropagate()` locally for changes that were already propagated on the remote. Just sync the ops and let playback handle it.
- [x] ~~Does `propagation_id` affect sync?~~ **Answered:** No. It's just another column on `package_ops` that gets stored alongside the op. It's used for batch undo (`pmAtomicUndo`), not for sync.

### Merge Location (from SCM code review)
- [ ] Should merge only be allowed on the server (`dark merge --remote` → `POST /branches/:id/merge`)? Local merge + push has a risk: if the server's parent branch has different state than local, merge will deprecate wrong locations. Server-side merge is safer because the server's state is always canonical. **Leaning toward server-side merge.**

### Item ID Determinism for Sync
- [x] ~~Would content-addressed IDs cause problems for sync?~~ **Answered:** Op IDs ARE content-addressed (SHA256 of binary blob → GUID). Package item IDs are NOT content-addressed — they're location-addressed (name-based lookup in `PackageIDs.fs`). This is the right design: content-addressed ops ensure no duplicates on sync; location-addressed items ensure stability across renames. The existing two-pass parsing with `stabilizeOpsAgainstPM` handles the determinism.

### Rebase Safety
- [x] ~~Is rebase safe across machines?~~ **Answered:** Yes. Rebase only updates `base_commit_id` — it does NOT rewrite ops. The branch's ops are untouched. Push/pull ensures both machines agree on parent's latest commit, so rebase is deterministic.

### Propagation Purity (from deep analysis)
- [ ] **Propagation ops are less pure than core ops** — see [08_propagation.md](08_propagation.md) for full writeup. Key issues: `PropagateUpdate` is a no-op during playback (metadata only), `RevertPropagation` uses direct SQL location manipulation instead of generating real ops, `Guid.NewGuid()` makes propagation non-deterministic.
- [ ] **Should propagation UUIDs be deterministic (hash-based)?** Deriving new UUIDs via hashing (`hash(sourceUUID + dependentItemId + ...)`) instead of `Guid.NewGuid()` would make propagation idempotent across machines — highest-value improvement for sync safety. **Note:** Darklang has tried hash-based / content-addressed IDs before and moved away — the reasons for abandoning that path need to be recalled/investigated before committing to this approach again. May have been scoped to all item IDs (too broad) vs. just propagation-generated UUIDs (narrower, possibly fine).
- [ ] **Should branch operations be ops?** Currently branch create/delete/merge/rebase are direct SQL mutations, not in the op log. Making them ops (either new `PackageOp` variants or a separate `BranchOp` type) would give audit trail and make sync more natural. Leaning toward a separate `BranchOp` type since branch ops are meta-level and don't belong to any specific branch. **Stronger case now:** If we want ops-only DB shipping (see [07_db-size.md](07_db-size.md)), branches and commits MUST be derivable from ops. Currently they're the only tables that aren't projections of the op log. For MVP, ship ops + branches + commits; convert to branch ops later.
