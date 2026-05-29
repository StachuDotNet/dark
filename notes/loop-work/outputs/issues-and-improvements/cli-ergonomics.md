# CLI ergonomics

Cross-cutting friction in how the CLI presents itself to a non-human caller: output shaped for a terminal, state that doesn't persist between invocations, flags that exist on some commands and not others. The lens: the CLI emits **human projections** (banners, ANSI trees) by default and keeps **ops state** (the current branch) only in the user's head. Each item makes the default friendlier to an agent without taking anything away from the human.

## Bare `dark` — agent-friendly orientation on non-TTY

**Issue**: bare `dark` prints an ASCII-art banner plus ANSI-colored command groupings. Lovely for a human, noisy for an iterating agent, and the banner tokens are paid on every cold session.

**Candidate fix**: detect non-TTY callers (no terminal capabilities) and emit a terse plaintext orientation — one line per command group, no banner, no ANSI — reusing the same logic that already powers `--no-color` handling. No prompt change needed; the saving is automatic. Open question, shared with the `--json` audit below: for non-TTY output, is dense plaintext or JSON the better default? It varies by command, so the audit informs this before it ships.

## `--json` audit and roll-out

**Issue**: `--json` exists on five commands (`traces list`, `find`, `stats`, `hotspots`, `follow`) and is missing from nine of the most-used ones — `tree`, `search`, `status`, `log`, `view`, `branch`, `deps`, `docs <topic>`, and `traces view`. The nine without are exactly the orient → search → view → log → status commands agents reach for most. Worst of all is `builtins`, which doesn't ignore `--json` but coerces it into a *search filter*, fooling an agent into thinking it filtered.

**Candidate fix**: roll `--json` across the nine missing commands and fix the `builtins` coercion bug. Each emits a structured shape mirroring its human view (e.g. `tree --json` → `{owner, modules: [{path, fns, types}]}`), behind a shared formatter so adding `--json` to a new command is a one-liner. The meta-principle: any command an agent may consume should ship `--json`, and new commands should design for it by default. Caveat before shipping: not every command wants JSON as its non-TTY default — favor JSON for parser-friendly cases (search, deps) and dense plaintext where bracket overhead hurts (tree, list); re-audit per command. Parseable output is denser than ANSI, so median tokens per run drops and agents stop shelling out to grep/awk to massage `view`.

## Sticky branch context

**Issue**: branch state doesn't persist across invocations, so every command needs `--branch <name>`. Agents forget and silently land on `main`.

**Candidate fix**: write the current branch into `rundir/.dark-branch` (gitignored); every command reads it unless `--branch` overrides, and `dark branch switch <name>` updates it. The state is visible via `dark status` (add a `branch:` line once this lands). It does introduce hidden state — but that state already lives in the user's head; this just makes it inspectable. The `--branch`-per-command count drops to roughly one (the initial switch) and accidental-main commits go to zero.
