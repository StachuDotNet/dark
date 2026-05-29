---
title: cron-describe
tier: S
class: app
modules: [Stdlib.String, Stdlib.List, Stdlib.Cli, Stdlib.Int64]
languages: [dark, ts, py, go, rust]
expected_outcome: pass
known_blockers: []
framework_hint: null
core: true
---

# Description

A command-line tool that translates a 5-field cron expression into plain English. The user passes a cron expression as a single argument; the program prints a one-line natural-language description to stdout.

The program supports the standard cron field syntax: literal numbers, ranges (`1-5`), step values (`*/15`), comma-separated lists (`15,30,45`), and the wildcard `*`. Day-of-week accepts both numeric (`0`–`6`) and short-name (`mon`, `tue`, …) forms. The program does *not* support extended formats like Quartz's seconds-field or `@yearly` shortcuts.

The program reads no stdin and writes no files. Stdout receives one descriptive line; stderr receives error messages on bad input. Exit non-zero on malformed input.

# Behaviours

- `cron-describe "*/5 * * * *"` produces output containing "every 5 minutes" (case-insensitive substring match acceptable to the rubric).
- `cron-describe "0 9 * * *"` produces output containing "09:00" or "9:00 AM".
- `cron-describe "0 9 * * 1-5"` produces output containing both "09:00" (or "9:00 AM") and "weekdays" (or "Monday through Friday").
- `cron-describe "15,30,45 * * * *"` produces output naming all three minute values: 15, 30, 45.
- `cron-describe "0 0 1 * *"` produces output containing "midnight" or "00:00" *and* references "the 1st" (day-of-month).
- `cron-describe "* * * * *"` produces output containing "every minute".
- `cron-describe "0 0 1 1 *"` references both January and the 1st (annual New Year's pattern).
- A 4-field input like `cron-describe "0 9 * *"` exits non-zero with a clear error mentioning the field count.
- A 6-field input exits non-zero with a clear error.
- A non-numeric character in a numeric field (e.g. `"x * * * *"`) exits non-zero with a clear error.
- `cron-describe --help` (or `-h`) prints usage and exits 0.
- An unknown flag exits non-zero with a usage line.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. `cron-describe "0 9 * * 1-5"` — eyeball that the output reads like English describing weekday 9 AM.
2. `cron-describe "*/30 * * * *"` — output mentions "every 30 minutes" or equivalent.
3. `cron-describe "0 22 * * 0"` — output mentions 10 PM (or 22:00) and Sunday.
4. `cron-describe "@daily"` — *should fail* (out of scope); error mentions field count or unsupported syntax.
5. `cron-describe ""` — empty string fails cleanly, doesn't crash.
6. `cron-describe "0 9 * * 1-5" "extra-arg"` — extra positional args error rather than silently ignore.
7. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- cron-describe "0 0 * * *"
- cron-describe "*/15 * * * *"
- cron-describe "0 9 * * 1-5"
- cron-describe "bad input"
- cron-describe --help

---

**Role**: §6 #3 (fix-iteration delta) channel. **Fix-iter-delta-pure**: pure parsing + string assembly. No I/O, no DB, no networking, no time-of-day. Token cost is similar across Dark / TS / Py / Go / Rust — the language doesn't help or hurt at the structural level. Any fix-iter delta measured on this project is signal about the agent's *prompt-and-feedback loop*, not about stdlib ergonomics. **The cleanest single-project signal for §3.3 verification work.**
