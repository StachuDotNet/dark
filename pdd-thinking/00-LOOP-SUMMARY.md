# PDD — Documentation Index

> The complete `pdd-thinking/` directory, organized by status.

## What this is

An experimental fork of the Darklang F# codebase implementing **pseudocode-driven development as a runtime feature**: the interpreter materializes its own source code on demand via LLM, in parallel, speculatively, with traces as the durable artifact.

**Branch:** `pdd` (local-only, off `main`, **never pushed**)
**Current state:** 47+ commits, 35/35 PDD tests green, end-to-end addOne (5L → 6L) demo works through real interpreter with real Stdlib builtins.

## The five claims (memorize)

1. **The source is lazy.** Names + signatures; bodies materialize on demand.
2. **The trace is the program.** Source files are sketches; the trace is the authoritative record.
3. **Types are the coordination protocol.** Pending references carry sig hints; parallel materializations agree via type unification.
4. **The runtime is tolerant.** Missing things substitute defaults; eval keeps moving; recoveries are auditable.
5. **The human is a materializer.** When find and generate fail, the human is the third path.

Anti-pitch: don't say "Copilot for runtime" — that misses every interesting claim. Right framing: "the runtime materializes its own source on demand."

---

## 📋 Current plan of record

**[`21-heavy-hitters-plan.md`](21-heavy-hitters-plan.md)** — H1–H4 (CLI command, implicit Pending, interactive HTML view, promotion to package tree).

Working through:
| Iter | Task |
|---|---|
| Next | Task #14 (EventSink) → Task #11 (CLI) → Task #13 (HTML view) → Task #12 (implicit Pending) → Task #10 (promotion) |

## 📚 Read in this order (if printing for reading)

### Vision & framing (durable)
| # | What |
|---|---|
| 01 | `01-vision.md` — five claims, algorithm, paradigm |
| 12 | `12-glossary.md` — pinned terminology + anti-glossary |
| 20 | `20-elevator-pitches.md` — 60s / 3min / 10min for different audiences |

### Core design (durable; partially implemented)
| # | What | Status |
|---|---|---|
| 02 | `02-libexecution-changes.md` — interpreter changes | Done (RuntimeTypes + Interpreter live in code) |
| 03 | `03-find-vs-generate.md` — scheduler | Mostly speculative; only generate is wired |
| 04 | `04-signature-consensus.md` — coordinating parallel attempts | Strategy A in spirit; not formally exercised |
| 05 | `05-tolerant-runtime.md` — recovery policies | Stub: raise FnNotFound on None |
| 06 | `06-builtin-permissions.md` — capability gates | Design only |
| 07 | `07-human-in-loop.md` — human as third path | Design only |
| 08 | `08-tracing-as-artifact.md` — JSONL trace + replay | JSONL log lands in rundir/; replay deferred |
| 11 | `11-open-questions.md` — what I'm unsure about | Ongoing |
| 14 | `14-demo-programs.md` — 6+ demos to build toward | Demo 1 (addOne) and a fragment of Demo 2 work |
| 15 | `15-spike-budgets.md` — eng-time / API$ / cognitive | Still relevant |
| 16 | `16-prompt-shapes.md` — empirical gpt-4o-mini outputs | v4 prompt in code |
| 19 | `19-red-team.md` — what could go wrong | Several risks not yet hit |
| 21 | **`21-heavy-hitters-plan.md`** — **current plan of record** | In flight |

### 📚 Historical (superseded by the heavy-hitters plan, kept for trail-of-thought)
| # | What |
|---|---|
| 09 | `09-carving-the-codebase.md` — sln carving (was skipped) |
| 10 | `10-day-1-hacking-plan.md` — Day-1 6-phase plan (done) |
| 13 | `13-libpdd-materializer.md` — separate-project plan (we kept it inline) |
| 17 | `17-day-1-quick-reference.md` — cheat sheet (Day-1 done) |
| 18 | `18-minimum-viable-spike.md` — 4-hour fallback path (done in 1 day) |

### 📸 Snapshots (point-in-time reports)
| File | What |
|---|---|
| `FINAL-REPORT-2026-05-13.md` | End of design loop (~07:30 EDT). Was printed at 06:32. |
| `SESSION-2-REPORT-2026-05-13.md` | End of first coding session (~10:35). |
| `progress.md` | Trimmed iteration log. Full granular history in `git log pdd ^main`. |

---

## Hard rules (still in force)

- **Never push** `pdd`. Cherry-pick later if anything's worth keeping.
- **Commit after every successful compile.** Free, atomic, easy to revert.
- **30-minute rule on stuck:** revert and try a different angle.
- **No `--no-verify`, no destructive rebases.** (Standing memory.)
- **Keep the `_assert-in-container` shim** in any script that has it.
- **OpenAI key lives at `~/.config/darklang/llm-keys.env`** (mode 600, outside the repo). Never written to repo files.
- **Spend tracking**: ~$0.0012 of the $10 budget used to date.

## How to enter a workstream

Next session, read in this order:
1. This file (you're here).
2. `21-heavy-hitters-plan.md` — current plan.
3. `SESSION-2-REPORT-2026-05-13.md` (with caveats — stale test counts) for what's-in-code.
4. Pull other docs in only when a specific need surfaces.
