# Grounding ledger — design claims vs. the live codebase

Every factual claim the design docs make about the real repo (`backend/`,
`packages/darklang/`, and the `classic-dark` precedent), checked read-only against
the actual code. This separates **what is verified to exist today** from **what is
proposed or assumed** — so a builder never mistakes a candidate for a strength. Built
over loop passes 28-32; the corrections it records are already applied in the docs.

Legend: **[ok]** verified accurate · **[fixed]** was wrong, corrected in the doc ·
**[flag]** could not confirm, now marked candidate/assumed in the doc.

## Verified accurate

| Claim | Doc | Evidence |
|---|---|---|
| `LibExecution/Stream.fs` is ~292 LoC, pull-based `DStream`/`StreamImpl` | event-bus | exactly 292 LoC; both types present |
| classic-dark `EventQueueV2.fs` ~465 LoC + `QueueWorker.fs` ~381 LoC | event-bus | exactly 465 and 381 LoC |
| 9-assembly Builtin split: `Builtins.{Pure, Http.Client, Http.Server, Random, Time, Cli, CliHost, Language, Matter}` | capabilities | 9 separate `.fsproj`s, names exact |
| `Previewable = Pure \| ImpurePreviewable \| Impure` | async/capabilities | present in RuntimeTypes |
| `RuntimeError` variants: `DivideByZeroError`, `PatternDoesNotMatch`, `NonStringInInterpolation` | conflicts | all present |
| `packages/darklang/cli` ships `SubApp` (`onKey`/`onDisplay`/`onSave`), `AppState`, `Page` | composable-mvu | all present in `cli/core.dark` |
| real apps under `cli/apps/` (review, …) | composable-mvu | `apps/review/app.dark` present |
| `schema.sql` + `LoadPackagesFromDisk` + `LocalExec/Migrations.fs` | bootstrap | all present |
| `package_ops` / `branch_ops` tables | sync | both in schema.sql |
| `LibDB.Seed.export` / `growIfNeeded` / `pmSeedExport` builtin | bootstrap | all present |
| `Rebase.getConflicts` | conflicts | `LibDB/Rebase.fs` |
| `accounts_v0` seeds Darklang/Stachu/Paul/Feriel; `account_id` columns | sync/identity | all in schema.sql |
| deprecation kinds `Obsolete` / `Harmful` / `SupersededBy` | ai-coding-target | all present in packages |
| `merge --dry-run`, `rebase --status`, `branch rename` | editing-and-refactor | `scm/merge.dark` + `docs/scm.dark` |
| `find-values` (`findValues.dark`), `agent`, `review`, `docs for-ai`/`for-ai-internal` | ai-coding-target | all present |
| real `traces` subcommands: `list/view/tail/follow/stats/find/hotspots/replay/delete` | ai-coding-target | `cli/commands/traces.dark` |
| telemetry `cli.total`, `commandExec`, `httpserver.*`, `seed.*` | ai-coding-target | all emitted |
| `package-ref-hashes.txt` two-pass build (`PackageRefs.fs`, `PackageRefsGenerator.fs`) | ai-coding-target | file + generators present |
| algorithm internals `EmptyBody`, `defaultFor`, `currentSink`/`EventSink` | algorithm | all present |
| `DARK_ACCOUNT` env var is gone (feedback asked to confirm) | ai-coding-target | 0 occurrences in `backend/src` + `packages/darklang` — confirmed removed, no follow-up todo needed |

## Corrected (claim was wrong; fixed in the doc)

| Was claimed | Reality | Doc(s) fixed |
|---|---|---|
| `OnMissing.Strict` (a parse strictness variant) | the type is `ThrowError \| Allow \| AllowPending` — no `Strict` | conflicts |
| `traces gen-test` exists ("already turns a trace into a regression test") | only a code comment in `Tracing.fs`; not a command | traces-and-debugging, ai-coding-target |
| `traces replay --diff` (regression-testing built in) | `replay` exists but has **no `--diff`** mode | ai-coding-target, traces-and-debugging |
| `traces inspect`, `traces values` | not present | ai-coding-target |
| "existing `traces export`/`import` machinery" | no traces export/import; the real reuse target is **seed** export (`pmSeedExport`) | publishing-and-sharing |
| "16-plus traces subcommands" | ~10 real subcommands | traces-and-debugging |

## Unconfirmed (could not verify; now flagged as candidate/assumed)

| Claim | Status | Doc |
|---|---|---|
| `view --with-trace` overlay flag | no `with-trace`/`--trace`/overlay found in the cli | ai-coding-target (flagged) |
| `test.suite.*` telemetry stream | not found (other four event families confirmed) | ai-coding-target (flagged) |
| `dark uncommit` / `dark revert` | **confirmed absent** — `cli/scm/` has `commit`/`discard`/`rebase`/`merge`/`branch`/`log`/`status`/`showCommit`, no `uncommit`/`revert`. Proposing them as new is correct. | editing-and-refactor (proposal validated) |

## How to use this

If you're about to build to a claim the bench or a design doc leans on, check it here
first. The **[ok]** rows are safe to depend on. The **[flag]** rows need a confirm-or-
build step before anything rests on them. The **[fixed]** rows are already corrected in
the docs but are listed so the correction is auditable.
