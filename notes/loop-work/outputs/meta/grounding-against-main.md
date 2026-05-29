# Grounding ledger — design claims vs. the live codebase

Every factual claim the design docs make about the real repo, checked read-only.
Separates **what exists on `main` (reality)** from **what is a `pdd`-spike artifact**,
from **what is proposed/assumed** — so a builder never mistakes a candidate, or a
spike experiment, for a shipped strength.

> **Important — re-grounded against `main`.** This branch (`pdd`) is a research/thinking
> **spike**: ~368 files differ from `main` (incl. `RuntimeTypes.fs`, `Execution.fs`,
> `Tracing.fs`, `LibParser/NameResolver.fs`, plus new `PDDMaterializer.fs`/`PddCommand.fs`).
> An earlier loop pass checked claims against the **spike working tree** by mistake. This
> ledger has been corrected to check `main` specifically (`git show main:…`), and a few
> "facts" turned out to be spike-only — moved to their own section below.

Legend: **[main]** verified on `main` · **[spike]** exists only on the `pdd` spike, not
`main` · **[fixed]** was wrong, corrected in the doc · **[flag]** could not confirm.

## Verified on `main` (safe to depend on)

| Claim | Doc | Evidence (on `main`) |
|---|---|---|
| `LibExecution/Stream.fs` ~292 LoC, pull-based `DStream`/`StreamImpl` | event-bus | file **unchanged by the spike**; exactly 292 LoC |
| classic-dark `EventQueueV2.fs` ~465 LoC + `QueueWorker.fs` ~381 LoC | event-bus | separate repo; exactly 465 and 381 LoC |
| 9-assembly Builtin split `Builtins.{Pure, Http.Client, Http.Server, Random, Time, Cli, CliHost, Language, Matter}` | capabilities | 9 `.fsproj`s, names exact (dir unchanged by spike) |
| `Previewable = Pure \| ImpurePreviewable \| Impure` | async/capabilities | present in `main:RuntimeTypes.fs` |
| `Execution.execute` returns `Task<…>`; builtins run through `Ply<Dval>`/`DvalTask` (`BuiltInFnSig`) | async / event-bus | present on `main` — the kill-Task/Ply premise is accurate |
| `RuntimeError` variants `DivideByZeroError`/`PatternDoesNotMatch`/`NonStringInInterpolation` | conflicts | present on `main` |
| `OnMissing` exists for unresolved names | conflicts | on `main`: `ThrowError \| Allow` (see corrected row) |
| `cli` ships `SubApp`(`onKey`/`onDisplay`/`onSave`)/`AppState`/`Page`; apps under `cli/apps/` | composable-mvu | **`packages/darklang/cli` untouched by the spike** → all valid on `main` |
| real `traces` subcommands `list/view/tail/follow/stats/find/hotspots/replay/delete` | ai-coding-target | `cli/commands/traces.dark` (untouched by spike) |
| `find-values`, `agent`, `review`, `docs for-ai`/`for-ai-internal` | ai-coding-target | cli untouched → valid |
| `merge --dry-run`, `rebase --status`, `branch rename` | editing-and-refactor | `scm/merge.dark` + `docs/scm.dark` (untouched) |
| deprecation kinds `Obsolete`/`Harmful`/`SupersededBy` | ai-coding-target | packages untouched → valid |
| `schema.sql` + `LoadPackagesFromDisk` + `LocalExec/Migrations.fs`; `package_ops`/`branch_ops`; `accounts_v0` seeds; `Seed.export`/`growIfNeeded`/`pmSeedExport`; `Rebase.getConflicts` | bootstrap/sync/identity/conflicts | those files not in the spike diff → valid on `main` |
| telemetry `cli.total`, `commandExec`, `httpserver.*`, `seed.*` | ai-coding-target | emitted on `main` |
| `package-ref-hashes.txt` two-pass build | ai-coding-target | file + `PackageRefs(Generator).fs` present |
| `DARK_ACCOUNT` env var gone | ai-coding-target | 0 occurrences on `main` — confirmed (not just spike) |

## Spike-only — NOT on `main` (the `pdd` branch is a research spike)

These are real on the spike but **do not exist on `main`**. The PDD docs that mention
them are describing the spike (which is correct), now labeled so they aren't mistaken
for shipped reality:

| Artifact | Where referenced | Note |
|---|---|---|
| `EventSink` / `currentSink` (the spike's PDD event sink) | algorithm (labeled "spike's"), event-bus ("compared to the spike's EventSink") | spike artifact; the real design target is `event-bus.md` |
| `EmptyBody` (PDD empty-body materialization result) | algorithm, claims | spike materialization concept |
| `defaultFor` (substitute-a-default helper) | algorithm, claims | spike helper; not on `main` |
| `OnMissing.AllowPending` | conflicts (now labeled) | the spike *added* this third case; `main` has only `ThrowError \| Allow` |
| `Pending` body (deferred/not-yet-materialized body) | conflicts, event-bus, algorithm (labeled) | spike concept; `Pending` on `main` exists only in unrelated SCM propagation |
| `MaterializeResult`, the materializer (`PDDMaterializer.fs`) | algorithm (labeled) | new spike file; absent on `main` |
| `PDDEvent`, the spike event sink | algorithm/event-bus (labeled) | spike-only |
| PDD storage `promoted.jsonl` / `promoted_hashes.jsonl` / `rundir/pdd-cache/*.jsonl` | sync (now labeled), conflicts | spike-only JSONL sidecars; not on `main` |
| `PddCommand.fs`, `PDDHTMLView.fs`, `RTQueryCompiler.fs` | (spike implementation files) | new on the spike; absent on `main` |

## Corrected (claim was wrong; fixed in the doc)

| Was claimed | Reality | Doc(s) fixed |
|---|---|---|
| `OnMissing.Strict` | `main` has `ThrowError \| Allow`; spike adds `AllowPending`. No `Strict`. | conflicts |
| `traces gen-test` exists | only a code comment in `Tracing.fs`; not a command | traces-and-debugging, ai-coding-target |
| `traces replay --diff` (regression-testing built in) | `replay` exists, **no `--diff`** | ai-coding-target, traces-and-debugging |
| `traces inspect`, `traces values` | not present | ai-coding-target |
| "existing `traces export`/`import`" | none; real reuse target is **seed** export (`pmSeedExport`) | publishing-and-sharing |
| "16-plus traces subcommands" | ~10 real | traces-and-debugging |

## Unconfirmed (flagged as candidate/assumed)

| Claim | Status | Doc |
|---|---|---|
| `view --with-trace` overlay flag | not found in cli | ai-coding-target (flagged) |
| `test.suite.*` telemetry | not found (other four event families confirmed) | ai-coding-target (flagged) |
| `dark uncommit` / `dark revert` | **confirmed absent** on `main` (`cli/scm/` has no such files); proposing them as new is correct | editing-and-refactor |

## How to use this

**[main]** rows are safe to build on. **[spike]** rows describe this branch's experiment,
not shippable infrastructure — treat them as prior art, not a foundation. **[fixed]** and
**[flag]** rows are auditable corrections. The deeper caveat the spike exposes: the design
docs are a *target* layered on `main`; where they cite the spike's internals, that's
illustrative of the experiment, not a description of what ships today.
