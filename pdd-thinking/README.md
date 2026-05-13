# PDD — Pseudocode-Driven Development

> An experimental fork of Darklang where the interpreter materializes its own source code on demand via LLM, in parallel, speculatively, with traces as the durable artifact.

**Branch:** `pdd` (local-only, off `main`, **never pushed**)

## The five claims (memorize these)

1. **The source is lazy.** Names + signatures; bodies materialize on demand.
2. **The trace is the program.** Source files are sketches; the trace is the authoritative record.
3. **Types are the coordination protocol.** Pending references carry sig hints; parallel materializations agree via type unification.
4. **The runtime is tolerant.** Missing things substitute defaults; eval keeps moving; recoveries are auditable.
5. **The human is a materializer.** When find and generate fail, the human is the third path.

**Pitch:** *"The runtime materializes its own source code on demand, in parallel, speculatively, with the LLM as both author and search index — and traces are the artifact."*

**Anti-pitch:** don't say "Copilot for runtime" — that misses every interesting claim.

## Current plan of record (heavy-hitters H1–H4)

What's needed for users to build & visualize full Dark programs that take advantage of this:

| # | Goal | Status |
|---|---|---|
| **H1** | `dark pdd run <expr>` CLI command | Not yet wired |
| **H2** | Implicit `Pending` from unresolved parser names — turns any `.dark` file into a PDD program | Not yet wired |
| **H3** | Interactive annotated HTML view (code with state badges + log side-panel) | Module exists (`PDDHTMLView.fs`); CLI installs it as default sink |
| **H4** | Promotion of materialized fns to the durable package tree | Not yet wired |

H3 details (the visualization payoff): a live HTML file that shows function cards with state badges (✓ real / ⋯ in-progress / ▼ fake / ↻ cached / ✗ failed), an event log side-panel, self-refreshing every 1s. Zero browser deps. See `DESIGN.md` §HTML View for the architecture.

**The single sentence:** *A user should be able to type `dark pdd run "<some pseudocode>"`, open the HTML view in their browser, and watch their code light up — green for real, yellow for materializing, gray for fake — with logs streaming to the side.*

## What's already built (live in code)

- `FQFnName.Pending` variant + `PackageManager.materializeFn` field (mechanism layer).
- `Interpreter` arms for `Function(Pending p)` execution-point + apply paths.
- `Dval.defaultFor : TypeReference -> Dval` for tolerant-runtime fallback.
- `PDDMaterializer.fs` — real OpenAI HTTP call + JSON-response parser + mini-body-parser handling `42L`, `"x"` identity, `x + 1L` / `-` / `*` arithmetic.
- `PDDHTMLView.fs` — interactive HTML renderer with EventSink integration.
- `PDDMaterializer.PDDEvent` + `currentSink` — lifecycle event stream.
- 39/39 PDD unit + integration tests (`./scripts/run-backend-tests --filter-test-list PDD`).

## What's *not* yet built

H1, H2, H4 above. Plus deferred design-spec stuff: parked-frame scheduler, find path, capability gates, recovery policy beyond raise-FnNotFound, real `trace show` viewer, sig consensus, deep-materialize annotation. None blocks H1–H4.

## Hard rules

- **Never push `pdd`.** Local-only by design. Cherry-pick later if anything ships.
- **Commit after every successful compile.** Free, atomic, easy to revert.
- **30-minute rule on stuck:** revert and try a different angle.
- **OpenAI key** lives at `~/.config/darklang/llm-keys.env` (mode 600). Never written to any repo file. Spend so far ≈ $0.0012 of the $10 budget.
- Build is two-pass after Dark type changes: `touch backend/src/LibExecution/package-ref-hashes.txt && build`.

## How to enter

1. Read this file (you're here).
2. `DESIGN.md` — sectioned design depth (LibExecution, scheduler, sig, tolerance, capabilities, human, tracing, HTML view).
3. `EMPIRICAL.md` — what we verified empirically about LLM behavior + open questions + red-team.
4. `DEMOS-AND-BUDGETS.md` — concrete programs to build toward + spike envelopes.
5. `archive/` — earlier iteration-by-iteration docs. Don't read unless tracing a specific decision.

When in doubt: `git log pdd ^main` for the total diff. The total diff is the source of truth.
