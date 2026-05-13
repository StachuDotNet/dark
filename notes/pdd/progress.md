# Progress log

> Append-only. Each loop iteration, add a short entry.

Format:
```
### YYYY-MM-DD HH:MM — iteration N
- Did: <files touched, ideas committed>
- Decided: <new opinions formed>
- Next: <what's queued for the next iteration>
- Commits: <git short-hash list>
```

---

### 2026-05-13 00:48 — iteration 0 (setup)
- Did: created `notes/pdd/` directory with `00-LOOP-SUMMARY.md`, `01-vision.md`, `02-libexecution-changes.md`, this `progress.md`.
- Read enough of `Interpreter.fs`, `RuntimeTypes.fs`, `Execution.fs` to ground the LibExecution design doc (#02). Identified the exact line that's the pivot — the `raiseRTE (FnNotFound)` in `Function(Package fn)` at `Interpreter.fs:317`.
- Decided: the LibExecution-side change is **three concrete additions** + **two supporting bits**: (1) new `FQFnName.Pending` variant, (2) `PackageManager.materializeFn`, (3) `VMState.pendingFrames` + scheduler. Plus tracing events + a `recoveryPolicy` hook for "tolerant runtime."
- Decided: bias toward `EmptyBody` (signature-only with default return) over delay/retry. Keeps the program moving.
- Decided: budgets are 1s/1s by default (find/generate). Configurable per call via `MaterializeOptions`.
- Next: write `03-find-vs-generate.md` (the scheduler), `04-signature-consensus.md` (parallel attempt coordination), `05-tolerant-runtime.md` (RTE recovery design in depth).
- Commits: pending (will batch first commit on `pdd` after a few more files).
