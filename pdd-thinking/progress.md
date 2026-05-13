# Progress log

> Append-only iteration log. **Trimmed 2026-05-13 ~16:15** — older iteration entries removed for readability. Full granular log is in `git log pdd ^main` (each iteration is a commit pair).

## Phase summary

### Design loop (overnight, ~00:48 → 07:18 EDT, 29 iterations)

Produced docs 00–20 and the original `FINAL-REPORT-2026-05-13.md`. Decided:
- 5 claims (source lazy / trace = program / types coordinate / runtime tolerant / human = materializer)
- Parser strategy P3 (LLM emits structured JSON, not Dark source)
- Cheap-by-default ($10 spike budget, gpt-4o-mini at ~$0.00005/call)
- Stratey A consensus (first-to-write-wins)
- Demo 6 (HN headline) is the north star
- v4 system prompt verified empirically (~75-85% first-try syntax)

5-page final report printed at 06:32 EDT.

### Coding loop, session 2 (~07:48 → 10:35 EDT, tasks A-F + 7a + 8)

Tasks 1-8 all completed. End-of-session state captured in `SESSION-2-REPORT-2026-05-13.md`. 45 commits, 24/24 PDD tests green. Real OpenAI HTTP call wired but body logged not executed.

### Coding loop, session 2 continued (~14:50 → 15:50 EDT)

Mini-parser + arithmetic + end-to-end test:
- iter 7 (commit `4a714d1f2`): mini-parser handles `42L` and `"x"` identity. 30/30 green.
- iter 8 (commit `6eeeaa47e`): mini-parser handles `x + 1L`, `x - 3L`, `x * 7L`. 34/34 green.
- iter 9 (commit `b32cf4262`): `addOne 5L → DInt64 6L` proven end-to-end through real interpreter with Stdlib.Int64.add. **35/35 green.**

### Strategic pivot (~16:00 EDT)

Stachu: "what's the most heavy-hitting stuff so I can build full Dark programs?" + "interactive view — not logs, but code with annotations + logs to the side."

New plan-of-record: `21-heavy-hitters-plan.md`. H1-H4:
1. **H1**: `dark pdd run <expr>` CLI command
2. **H2**: implicit `Pending` from unresolved parser names
3. **H3**: interactive annotated HTML view (the big visualization payoff)
4. **H4**: promotion to durable package tree

Older plan docs (10, 17, 18) marked historical.

## Current state (live)

- 47+ commits on `pdd`, local-only, never pushed.
- 35/35 PDD tests green.
- Wire is end-to-end for the addOne shape: Pending → materialize → interpret → return.
- LLM HTTP call works (verified via ~/bin/pdd-materialize, ~$0.0012 spent total).
- Real LLM materializer ready to plug into a CLI command (next iter).

## Logging tradition

Each coding-loop iteration appends a 2-3 line entry to this file with: time, what changed, build/test status, commit hash. Older entries trimmed as session progresses to keep total readable.

---

## Iterations in flight (current session, after pivot)

### 2026-05-13 16:00 — strategic pivot
- Stachu: "heavy-hitting stuff to build full Dark programs" + "interactive view".
- Created `21-heavy-hitters-plan.md`. H3 became "interactive HTML annotated view" not "stderr stream".
- Marked older plan docs as superseded.
- Commit `0c160233c`.

### 2026-05-13 16:15 — doc tightening pass
- Trimmed this `progress.md` aggressively (was 6831 words, target ~600).
- Adding HISTORICAL banners to docs whose plan is now superseded.
- Tightening prose where stale.
