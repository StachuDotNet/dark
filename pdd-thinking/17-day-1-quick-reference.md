# 17 — Day-1 Quick Reference Card

Print this. Tape it next to your keyboard. **One page** of everything you need at the desk on Day 1.

---

## Get oriented (60 seconds)

```bash
cd /home/stachu/code/dark/main
git checkout pdd                    # local branch, never push
git log --oneline -10
cat pdd-thinking/00-LOOP-SUMMARY.md  # what we agreed last night
```

## The five claims (memorize)

1. The source is lazy.   2. The trace is the program.   3. Types coordinate.
4. The runtime is tolerant.   5. The human is a materializer.

## The pivot point in the code

**`backend/src/LibExecution/Interpreter.fs:317`** — the line `raiseRTE (RTE.FnNotFound ...)`. Change "give up" to "wait." Everything else follows.

## The three surgical LibExecution edits

| File | Line | What |
|---|---|---|
| `RuntimeTypes.fs` | 88-110 | Add `FQFnName.Pending of Pending` variant + `SignatureHint` |
| `RuntimeTypes.fs` | 1250 | Add `materializeFn` field to `PackageManager` |
| `Interpreter.fs` | ~304 | New `Function(Pending p) -> …` arm calling `materializeFn` |

Plus `ExecutionState` gains `recoveryPolicy`, `capabilityCheck`, `humanResolver`.
Plus `VMState` gains `pendingFrames`, `inFlight`, `materialized` dicts.

## The carving (Phase A, 60-min stop-loss)

Disable in `backend/fsdark.sln` (lines verified 2026-05-13):
- **line 44**: `LibCloud` project
- **line 61**: `Builtins.Http.Server` project

Trim `backend/src/Builtins/Builtins.CliHost/Builtins.CliHost.fsproj`:
- Remove `<ProjectReference Include="../../LibCloud/LibCloud.fsproj" />`
- Remove `<ProjectReference Include="../Builtins.Http.Server/..." />`

Then stub any LibCloud/Http.Server references in Builtins.CliHost source.

**Keep** (per Stachu): `LocalExec` (needed for `.dark` package reload), `LibParser` (for loading existing `.dark` files).

**Escape hatch:**
```bash
git checkout main -- backend/fsdark.sln \
  backend/src/Builtins/Builtins.CliHost/Builtins.CliHost.fsproj
./scripts/compile
```

## Build, two-pass (Dark type changes need it)

```bash
./scripts/compile
touch backend/src/LibExecution/package-ref-hashes.txt
./scripts/compile
```

## The OpenAI key

Location: `~/.config/darklang/llm-keys.env` (mode 600, dir mode 700, **outside the repo**).

Use:
```bash
set -a; source ~/.config/darklang/llm-keys.env; set +a
```

**Budget:** $10 total. Hardcode `gpt-4o-mini`. ~$0.00005 per cheap call. ~300K calls available.

## The v3 system prompt for `Generate.fs`

```
You generate Darklang function bodies. Reply with ONLY a JSON object
{"sig": "(<params>): <ReturnType>", "body": "<Darklang expression>"}.

Darklang syntax notes:
- Integers are SIZED: Int64 (default), Int8, Int32, etc. Never bare int.
- Generics use ANGLE BRACKETS: List<Int64>, Option<String>. Not List(...).
- Bindings: let x = expr in rest_of_expr (in is required).
- Lambdas: fun x -> body. NEVER use =>.
- Lists: [1L; 2L; 3L] (semicolons).
- Pipe: value |> fn. Parens for complex: (complex expr) |> fn.
- String concat: ++.
- Int division: Stdlib.Int64.divide. Not /.
- Stdlib: prefix with Stdlib. (e.g. Stdlib.List.map, Stdlib.Int64.add).
- Records: Type { a = 1L; b = 2L }. Field access: value.a.
- Enums: construct Option.Some 5L, match | Some x -> ...
- Recursion: call the function by its short name inside the body.
- Whitespace-sensitive (Python-like).
- Avoid redundant bindings like `let x = x in body`.
- Prefer the simplest possible implementation.

Return ONLY the JSON object. No markdown fences, no prose.
If you do not know what to write, set body to "()".
```

`max_tokens: 800`, `temperature: 0`.

## The 6 day-1 phases (target: 4-6 hours)

| Phase | What | When done? |
|---|---|---|
| A | Carve `LibCloud` + `Http.Server`, build green | `./scripts/compile` succeeds |
| B | Add `FQFnName.Pending` variant, fix `failwith "TODO pending"` on every match | Compile green again |
| C | Add `PackageManager.materializeFn` (stub: returns `EmptyBody`) | Compile green |
| D | Add `Function(Pending _) -> …` arm in Interpreter | Compile green |
| E | Add `Dval.defaultFor : TypeReference -> Dval` | Compile green |
| F | Write `Tests/PDD.Tests.fs` exercising one pending fn → DUnit | Test green |

Commit after every phase. Always.

## Acceptance check at end of Day 1

```bash
./scripts/compile && \
  ./scripts/run-backend-tests --filter Tests.PDD && \
  git log --oneline pdd ^main | wc -l
```

Expected: 4-6 commits on top of main, tests pass.

## Acceptance check at end of Day 3

```bash
./scripts/run-cli pdd run pdd_demos/01-add-one.dark
# Should print "6L" (or whatever the demo's expected output is)
```

## When in doubt

1. **Skim `12-glossary.md`** if a term is foreign.
2. **Skim `02-libexecution-changes.md`** if you forgot a field shape.
3. **Skim `03-find-vs-generate.md`** if the scheduler is confusing you.
4. **Skim `10-day-1-hacking-plan.md`** for the literal step-by-step.

## When stuck for >30 min

1. Stand up. Get water.
2. Reduce scope. Was the next step really Phase X, or are you adding Y to it?
3. Worst case: `git checkout main && git checkout -b pdd-v2` and start over with what you learned. The branch is throwaway.

## Telemetry to capture from Day 1

- Time spent on each phase (rough — for `progress.md`)
- Total LLM spend (sum of trace `cost` events)
- Total compile time (`time ./scripts/compile`)
- Test runtime (the F# test should be <1s)

## Push? **No.** Never.

This branch is local-only by design. If something on `pdd` should ship to `main`, cherry-pick the relevant commits onto a fresh, push-able branch. The constraint is forcing function as much as safety.

---

*See `10-day-1-hacking-plan.md` for the long form. This card is the at-a-glance.*
