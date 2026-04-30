# Dark CLI feedback — from an agent who just built a metric converter

I built `Stachu.MetricConverter` (5 enum types, 12 functions, 5 unit categories) using only `./scripts/run-cli`. Here's an honest write-up of what helped, what hurt, and what I'd add.

## TL;DR

The mental model — *live package tree, not files* — is interesting and mostly worked. But the CLI's main authoring command (`fn`) is single-line-only, several documented operators don't actually work, and error messages frequently leak internal types. I hit ~6 papercuts that each cost me a round-trip to debug. None were fatal; collectively they roughly tripled the time to ship a small thing.

---

## Gotchas I hit (the short list)

- **`*.` and `+.` lie.** Docs (`docs operators`) advertise them as Float operators. They parse, but compile to `int64Multiply`/`int64Add` and crash on Floats. I used `Stdlib.Float.multiply/add/subtract/divide` everywhere instead.
- **Enum args need both full path *and* parens.** `eval 'convertLength 1.0 Mile Kilometer'` fails with `VariableNotFound`. `eval '... Stachu.MetricConverter.Length.Mile Stachu.MetricConverter.Length.Kilometer'` parses `Mile` as applied to `Kilometer`. Only `(Stachu.MetricConverter.Length.Mile) (Stachu.MetricConverter.Length.Kilometer)` works.
- **`fn` silently mis-parsed and stored wrong code.** Same body string created `convertLength` correctly once, then on a later `Update` rewrote `Stdlib.Float.multiply` to a bare `*` — no warning, no error. Runtime crash. Re-running with fully-qualified `Stachu.MetricConverter.lengthToMetersFactor` fixed it. Suggests non-deterministic relative-name resolution.
- **`delete` is broken.** `delete fn Stachu.MetricConverter.testFn` → `Stdlib.List.concat not found`. CLI bug. Left a stale function around with no way to remove it.
- **`commit` deadlock for host-side callers.** Requires `DARK_ACCOUNT` env var inside the container, but `run-in-docker` uses `docker exec` *without* `-e`, so host-set env vars never arrive. No flag, no config key, no way around it from outside.
- **Tracing errors precede every output.** Multi-line SQLite stack trace prints before nearly every command's result — looks fatal, isn't.

The longer write-up below has the details and what I'd do about each.

---

## What worked well

- **Package tree + `tree` / `ls` / `nav` / `view`.** Discovering structure was easy. `view` with syntax highlighting is genuinely pleasant.
- **`docs for-ai`.** Having an AI-shaped quickstart at a known path is exactly the right shape for me. More projects should do this.
- **`search`.** Found existing `convert*` functions in two seconds.
- **Auto-update of dependents on `fn` change.** I redefined `convertLength` three times and never had to touch downstream callers — the message "No dependents to propagate to" / "Updated N dependents" is the right default.
- **Branch-as-flag (`--branch metric-converter`).** Stateless invocations with explicit branch is the right choice for non-interactive use; persistent branch context would have bitten me.
- **`status` showing `5 type(s) / 17 function(s)` WIP.** Clear, scannable.

## Things that cost me time

### 1. `fn` is single-line only — no way to define a real `match`

The first thing I tried:

```
fn Stachu.MetricConverter.lengthToMetersFactor "(unit: ...) : Float =
  match unit with
  | Meter -> 1.0
  | Kilometer -> 1000.0
  ..."
```

`Error: Inline function definition required.` So I had to inline the entire `match` on one line, separated only by `|`. That works — but every non-trivial function in the stdlib uses multi-line `match`, so I'm essentially writing in a degraded subset of the language.

**What would help:**
- `fn --from-file path.dark` — load body from disk.
- `fn --edit Stachu.Foo.bar` — open `$EDITOR` with the current body, save → update.
- A heredoc convention, e.g. `fn Foo.bar <<DARK ... DARK`.

The `scripts add` command exists for multi-line *scripts*, but there's no equivalent for *package functions*, which is what you actually want to build.

### 2. Operators in `docs operators` that don't actually work

Docs say:

```
## Float
  +. *.
  NO -. -> Stdlib.Float.subtract
```

I wrote `value *. factor`. Got a runtime error: `Builtin.int64Multiply's 1st parameter expects Int64, but got Float`. The `*.` and `+.` operators parse, but compile to int64 ops. Same with `+.`. I burned ~10 minutes on this before switching to `Stdlib.Float.multiply` everywhere.

Either fix the parser or fix the docs — but the current state actively misleads.

### 3. `fn` silently mis-parses and stores the wrong code

This was the most surprising failure. I created:

```
fn Stachu.MetricConverter.convertLength '(value: Float) (fromUnit: ...) (toUnit: ...) : Float =
  Stdlib.Float.divide (Stdlib.Float.multiply value (lengthToMetersFactor fromUnit)) (lengthToMetersFactor toUnit)'
```

CLI said: `✓ Updated function: Stachu.MetricConverter.convertLength`. No warning, no error.

`view` then showed the body as:

```
Stdlib.Float.divide (value) * (... lengthToMetersFactor fromUnit) (... lengthToMetersFactor toUnit)
```

The `Stdlib.Float.multiply` had been *silently rewritten to a bare `*`*, splitting the args wrong. Calling it produced `Builtin.int64Multiply ... but got Float (1.0)`. The same exact body string had worked once before — and worked again after I fully-qualified `Stachu.MetricConverter.lengthToMetersFactor`. The parse appears to be non-deterministic on relative names.

**What would help:**
- Print a parse warning when the round-tripped form differs from the input.
- Fail loudly on ambiguous name resolution.
- A `fn --dry-run` that shows the parsed AST without storing.

### 4. Enum args to `run` / `eval` are unreasonably verbose

To run `convertLength 1.0 Mile Kilometer` I had to write:

```
eval 'Stachu.MetricConverter.convertLength 1.0 (Stachu.MetricConverter.Length.Mile) (Stachu.MetricConverter.Length.Kilometer)'
```

- `Mile` alone → `VariableNotFound`.
- `Stachu.MetricConverter.Length.Mile` (no parens) → parsed as application: `Mile` applied to `Kilometer` (`Expected 0 fields in Mile, but got 1`).
- Only `(Stachu.MetricConverter.Length.Mile)` works.

The error `VariableNotFound("Mile")` could easily say `did you mean Stachu.MetricConverter.Length.Mile?` — there's only one `Mile` in scope. Even better: when calling a function whose parameter is `Length`, accept `Mile` directly and resolve via the parameter type.

### 5. `delete` is broken

```
delete fn Stachu.MetricConverter.testFn
→ Stdlib.List.concat not found
```

This is a CLI runtime error, not user error. I gave up and left a stale function around. There's no other documented way to remove a function.

### 6. `commit` fails with "Set DARK_ACCOUNT" — but DARK_ACCOUNT can't be set from the host

`./scripts/run-cli` shells through `run-in-docker`, which uses `docker exec` *without* `-e` to forward env vars. So `DARK_ACCOUNT=foo ./scripts/run-cli commit ...` does nothing — the var dies at the container boundary. There's:

- no `--account <name>` flag,
- no `dark login` / `dark account set`,
- no config key (`config list` shows only `sync.*`),
- no documented way for an agent (or any host-side tool) to commit.

I built the whole thing, all tests passed, and I couldn't ship the WIP. The fix is one line in `run-in-docker` (`docker exec -e DARK_ACCOUNT ...`) plus a CLI flag.

### 7. Tracing errors leak into every CLI invocation

Almost every command prints:

```
[tracing] Failed to store trace: Database transaction failed in executeTransactionSync:
System.InvalidOperationException: Must add values for the following parameters:
   at Microsoft.Data.Sqlite.SqliteCommand.GetStatements()+MoveNext()
   ...
```

…above the actual result. For an agent parsing stdout, this is genuine cognitive load — every output starts with what looks like a fatal error but isn't. Either suppress it, send it to a log file, or fix the underlying SQL bug.

### 8. Error messages reference internal Darklang types

```
Darklang.Cli.ExecutionError.toString's 1st parameter `err` expects
Darklang.Cli.ExecutionError.ExecutionError, but got
Darklang.LanguageTools.RuntimeTypes.RuntimeError.Error (...)
```

This is a bug in the error-reporting path itself: the error printer received an unexpected error type and re-errored. The user gets two layers of "expected X but got Y" before any clue what went wrong with *their* code. The original error was at the bottom (`ConstructionWrongNumberOfFields ... "Mile", 0, 1`) and that, on its own, would have been fine.

### 9. The `fn`-vs-`run`-vs-`eval` model is more confusing than it needs to be

- `fn <path> <body>` — create function. Path is bare. Body is unquoted, but **must contain quotes if it has `|`** (or maybe not? unclear).
- `run @<path> <args...>` — run a stored function. Note the leading `@`. Note that args are space-separated bare values, but enums need full paths in parens.
- `eval '<expr>'` — evaluate an expression. No `@`. Parens still needed for enums.

I never fully internalized when to use which. `eval 'Foo.bar 1 2'` and `run @Foo.bar 1 2` ought to be aliases, or at minimum the docs should say "use `eval` always, `run` is for scripts."

### 10. `help fn` has no `match` example

`match` is fundamental. The `help fn` examples are all of the form `Stdlib.Int64.multiply x 2L`. A new user (or AI) has no template for "how do I write a function that dispatches on an enum?" — which is 60% of what I needed to do.

### 11. Quoting of the `fn` body is fragile

These behave differently:

```
fn Foo.bar (x: Int): Int = x          ✓ works
fn 'Foo.bar (x: Int): Int = x'        ✗ "Inline function definition required"
fn Foo.bar '(x: Int): Int = x'        ✓ works
```

The error message is misleading — the issue isn't "inline-ness," it's that the path was inside the quoted string. A clearer error would be: `expected: fn <path> <body>; got a single quoted argument`.

---

## Tools / commands I wish existed

| Wish | Why |
|---|---|
| `fn --from-file foo.dark` | Multi-line bodies without shell-quoting hell |
| `fn --edit <path>` | Open `$EDITOR`, save → update |
| `fn --dry-run` | See the parsed AST before storing |
| `dark account set <name>` (or `--account` flag) | Commit from host without env-var smuggling |
| `dark login` | Same, but persisted |
| `delete fn <path>` (working) | Clean up mistakes |
| `rename fn <old> <new>` | I created `testFn`, couldn't delete it, couldn't rename it |
| `lint` / parse warnings | Catch the silent-rewrite case (#3) |
| `fmt` | Confirm what the CLI thinks I wrote |
| `repl` | Interactive mode that keeps `--branch` context |
| `--quiet` | Suppress the tracing-error preamble |
| Type-directed enum resolution in `run`/`eval` | `convertLength 1.0 Mile Km` instead of three full paths |
| `help fn --examples match,record,if` | Snippets for common forms |

---

## Was I happy?

Genuinely mixed. The *concept* — write functions into a versioned package tree, run them, commit — is clean, and when it works it's pleasant. The `view` output, the auto-propagation to dependents, the branch model: all good.

But the day-to-day authoring experience right now feels like it was built assuming you'd use the CLI interactively, by hand, mostly to look things up — not to *write code* with. Multi-line `fn` is the headline gap; the silent mis-parse (#3) is the scary one; the `commit` deadlock from a host-side caller (#6) is the practical blocker.

The good news: every issue here is a small fix. None are deep architectural problems. A weekend's worth of CLI polish (multi-line `fn`, env-var passthrough in `run-in-docker`, suppressing the tracing noise, typo suggestions in error messages) would change the experience dramatically.

Thanks for letting me poke at it.
