# Bench projects

> The 10-project Phase-1 starter set + the wider candidate catalog drawn from the AI-generated [Darklang CLI Project Survey](#sources). Target: ~100 projects, 4 tiers, balanced across categories.

## Format

Each project spec lives at `evals/projects/<name>/spec.md` with frontmatter:

```yaml
---
title: <kebab-case>
tier: T | S | M | L     # trivial / small / medium / large
modules: [Stdlib.X, Stdlib.Y, ...]
languages: [dark, ts, py]
---
```

The `spec.md` body has a Description (what the agent reads), Behaviours (what the rubric checks), and Smoke commands. The agent only sees `spec.md`; the rubric files and gold reference are blocked from the agent's cwd. See [`plan.md`](plan.md) §4.0.

---

## Phase 1 starter set — 10 projects

These are vetted, tier'd, ready-to-build. Tier mix is intentional — 2 T, 4 S, 3 M, 1 L — so we shake out the harness on cheap projects before the rubric runner has to handle network IO.

1. **`hello-cli`** *(T, Stdlib.Cli)* — Print `Hello, <name>!` where `<name>` is the first positional arg.
   - Rubric: exits 0 on `hello-cli World` with stdout exactly `Hello, World!\n`; exits non-zero on missing arg with a stderr usage line. Tests: arg parsing + stdout discipline. Smallest possible end-to-end harness check.

2. **`fizzbuzz`** *(T, Stdlib.Int64, Stdlib.List)* — Print 1..N, replacing multiples of 3 with `Fizz`, 5 with `Buzz`, 15 with `FizzBuzz`. N from arg.
   - Rubric: `fizzbuzz 15` produces the canonical 15-line output. Tests: control flow, integer arithmetic, list/loop idiom — exposes the agent to Dark's pipe + match style early.

3. **`csv-to-json`** *(S, Stdlib.String, Stdlib.Json)* — Read RFC-4180 CSV from stdin, emit a JSON array of objects to stdout (first row is the header).
   - Rubric: round-trips a 5-row sample; quoted commas inside fields preserved; emits valid JSON parseable by `jq`. Tests: parsing, error-tolerance, JSON serialization. Catches the agent skipping `Stdlib.Json` and rolling its own.
   - **Caveat**: per the [project survey](#sources) §2, Dark has no CSV stdlib — agent has to hand-roll RFC-4180 escaping. That's the test.

4. **`word-count`** *(S, Stdlib.String, Stdlib.Stream)* — `wc`-alike: report `<lines> <words> <bytes>` for stdin.
   - Rubric: matches `wc` output for an ASCII fixture and for a UTF-8 fixture. Tests: streaming over input, byte vs char distinction (Dark's UTF-8 strings vs `Stdlib.String.byteCount`). Surfaces the "lines vs codepoints" pitfall.

5. **`temperature-cli`** *(S, Stdlib.Float, Stdlib.Cli)* — Subcommands `to-c <F>` and `to-f <C>`; print result to 2 decimals.
   - Rubric: 4 unit conversions correct; unknown subcommand → non-zero + usage. Tests: subcommand routing — minimal viable test of the command-dispatch idiom that bigger projects will reuse.

6. **`url-shortener-cli`** *(S, Stdlib.DB, Stdlib.Crypto, Stdlib.Cli)* — `add <url>` returns a 6-char slug; `get <slug>` prints the URL or exits 1; persists across invocations via the package tree.
   - Rubric: persistence verified (add + new process + get works); slug collisions handled; invalid URLs rejected. Tests: Dark's persistence story directly — *this is the project that most differentiates Dark vs TS/Py* (no DB setup, no migration).

7. **`todo-cli`** *(M, Stdlib.DB, Stdlib.DateTime, Stdlib.Uuid, Stdlib.Cli)* — `add`, `list`, `done <id>`, `rm <id>`, with timestamps.
   - Rubric: 8 invocations across 2 sessions exercise add/list/done/rm; `list` shows pending only by default, `--all` shows completed; ids stable. Tests: richer data model, query filters, time. **The user's `Goals for Week.md` explicitly calls out "TODO app" as a target** — concrete validation.

8. **`http-healthz`** *(M, Stdlib.HttpServer, Stdlib.Json, Stdlib.DateTime)* — Serve `GET /healthz` returning `{"status":"ok","uptimeMs":N,"version":"<sha>"}`.
   - Rubric: server starts on a free port within 5 s; `curl /healthz` returns 200 + valid JSON; `uptimeMs` increases between two requests; `GET /` returns 404. Tests: HTTP server lifecycle, JSON response, simple state.
   - **Caveat probed iter 15** *(downgraded from prior framing)*: vault `where we're a bit short.md` warned about an `httpServerServe` lambda-cache regression where handlers using `List.map`, `routeRequest`, etc. *"will fail at runtime."* Probed by running `./scripts/run-cli serve Darklang.DemoData.HttpServerTest.router --port 7777` and `curl http://127.0.0.1:7777/`. **Result: the canonical `Stdlib.HttpServer.routeRequest` (which uses `Stdlib.List.filter (fun h -> …)`) executed successfully on the first request, returning 200 + body "Welcome to Darklang HTTP Server!"** The regression is therefore *not* universal on this branch — at least one canonical lambda-using router works on a first request. A noisier issue did surface: the hot-reload mechanism races itself with "Address already in use" exceptions in the server log, which the harness needs to filter from `agent_abandoned` heuristics. **Open**: multi-request test was inconclusive (second probe didn't connect cleanly). The cache-reuse failure mode the vault warned about may still bite on N>1 requests; iter-N+ probe should re-run with proper warmup.

9. **`webhook-echo`** *(M, Stdlib.HttpServer, Stdlib.Json, Stdlib.DB)* — `POST /` stores body + headers + ts; `GET /` returns the last 10 received in JSON.
   - Rubric: 3 POSTs + 1 GET round-trip; body byte-equal; headers case-insensitive in lookup; tail order is most-recent-first.

10. **`paste-bin`** *(L, Stdlib.HttpServer, Stdlib.DB, Stdlib.Crypto, Stdlib.Html)* — `POST /` with text body returns a slug + `Location` header; `GET /<slug>` returns the text (Content-Type negotiated: `text/plain` default, `?html=1` returns syntax-highlighted via `Stdlib.Html`); `DELETE /<slug>` with the right token.
    - Rubric: 6-step flow (post → get → get?html=1 → delete-wrong-token → delete-right-token → get-404); slug format `[a-z0-9]{8}`. Largest in the starter set; the project most likely to expose harness flakiness early.

**Reference-implementation policy**: an agent (Claude Code, no time budget cap) writes the `dark/`, `ts/`, and `py/` reference impls; a human spends ≤30 min reviewing each before it's accepted as the gold copy.

**Phase 1 specifically uses 3 of the 10**: `hello-cli`, `csv-to-json`, `url-shortener-cli`. The other 7 are queued for Phase 2.

### Phase 2 expansion candidates — additional 5 vetted projects (added iter 28)

Pulled from the survey's class structure but tightened with concrete rubrics. Each is implementable today on `main` (no runtime gaps to wait on). Tier mix designed to *not* lean further into HTTP — that's already covered by #8/#9/#10 — and instead cover the pure-CLI / parsing / filesystem / data-munging spread that the §8 "picking projects that flatter Dark" risk warns about.

11. **`password-gen`** *(T, Stdlib.Cli, Stdlib.Crypto, Stdlib.Int64)* — Generate `--length N --classes upper,lower,digit,symbol` random passwords. `--seed N` for deterministic test mode.
    - Rubric: `password-gen --length 16 --seed 42` produces a stable 16-char output (assert exact); `--length 0` → exit non-zero with usage; `--classes lower` produces only lowercase (assert charset); `--length 100` runs under 50 ms (perf budget).
    - Why this tests Dark: Survey class A. Forces the agent to find a randomness source in stdlib (`Stdlib.Crypto` has SHA hashes; "true RNG" path probably wants a builtin). Tests cli-arg parsing in a way that doesn't double-up with `hello-cli`. **The trivial-tier outlier**: most T projects test arg-parsing trivially; this one demands a small algorithm.

12. **`cron-describe`** *(S, Stdlib.String, Stdlib.List, Stdlib.Cli)* — Take a 5-field cron expression as arg, print English: e.g. `cron-describe "*/5 * * * *"` → `"every 5 minutes"`; `"0 9 * * 1-5"` → `"at 09:00, weekdays"`.
    - Rubric: 6 canonical inputs match expected English (table-driven); malformed (4 fields) → non-zero with specific error message; `--help` exits 0. Stretch case: `"15,30,45 * * * *"` → `"at minutes 15, 30, and 45"`.
    - Why this tests Dark: Pure parsing + string assembly. No I/O, no infra, no HTTP. Token-cost should be similar across Dark/TS/Py — the *fix-iteration delta* (§6 #3) signal is cleanest on projects like this where the language doesn't help or hurt at the structural level.

13. **`markdown-toc`** *(S, Stdlib.String, Stdlib.Cli, Stdlib.Regex)* — Read a markdown file from stdin or `--file path`; emit a table-of-contents block (`-` bullets, indented by header level, with `[Title](#anchor)` links).
    - Rubric: feed a 12-header fixture, output exactly matches a golden TOC; ATX-style (`#`) and Setext-style (`=====` underline) both work; nested levels indent correctly; non-ASCII titles produce the expected GitHub-flavored anchor (lowercase, hyphens, strip punctuation).
    - Why this tests Dark: Survey class D. Real-world utility — every dev tool needs this. Stresses Regex / String — and crucially, **the stdlib byte-vs-codepoint pitfall** (already a known Dark concern per `word-count` rubric).

14. **`jq-lite`** *(M, Stdlib.Json, Stdlib.AltJson, Stdlib.List, Stdlib.Dict, Stdlib.Cli)* — Read JSON from stdin; apply a filter expression: `.foo`, `.foo.bar`, `.foo[0]`, `.foo | select(.x > 5)`, `.foo[] | .name`. Subset of jq.
    - Rubric: 8 filter expressions across an array fixture; output matches `jq`'s output for each (modulo whitespace); invalid filter → non-zero + helpful error; bench: parse a 10MB JSON without OOM.
    - Why this tests Dark: Survey class D. **High-stress on `Stdlib.Json` / `Stdlib.AltJson` ergonomics.** If Dark's JSON handling has rough edges, this surfaces them. Comparing the agent's parser-implementation strategy across Dark/TS/Py (do they reach for an existing library? hand-roll? regex-hack?) is high-signal.

15. **`dedup`** *(S, Stdlib.Cli.File, Stdlib.Crypto, Stdlib.Cli, Stdlib.Dict)* — Walk a directory; for each file, compute sha256; report duplicate groups (`sha256: file1, file2`); summary line `N duplicate groups, M total bytes reclaimable`.
    - Rubric: scratch dir in `/tmp` with 5 files (2 of which duplicate, 1 a symlink that should not follow itself); verify exit-0 + exact duplicate-group lines + summary; symlink-follow-loops fail-safely; large file (10 MB random bytes) doesn't OOM.
    - Why this tests Dark: Survey class B. **Filesystem stress.** `Stdlib.Cli.File` + `Crypto` integration. Tests how the agent reasons about side effects (it has to walk a tree, read every file, hash, then aggregate). Per the survey: "real-world utility — `fdupes` / `rdfind` analog." Confirmed by the survey to be feasible on `main`.

### Library-port candidates — different shape from app projects (added iter 29)

**The gap noticed iter 29**: the 15 vetted projects above are all *applications* (run, do something, exit). A meaningful fraction of useful Dark code is *libraries* — ports of well-shaped APIs from typed-functional ecosystems (F#, Elm, OCaml, Haskell). These projects stress Dark differently: **API design and type-system ergonomics**, not stdout discipline or HTTP routing.

**Why F# / Elm / OCaml specifically**: shared lineage. Pure functions, immutable data, ADTs, exhaustive match, no async expected (Dark has no async — see [plan.md](plan.md) §2 gap list). A port lands cleanly without re-architecture; differences are mostly in syntax + naming. Convex's "TypeScript fits the agent's training data" advantage doesn't apply here — these libraries' *original languages* are also under-represented; Dark starts on more equal footing.

**Rubric mechanism for libraries** *(differs from apps)*: the library author also writes a thin CLI driver (`lib-cli <op> <args>`) that exposes the library's public API as a subcommand. The harness rubric shells out to the driver, preserving the language-agnostic rubric-runner invariant from [plan.md](plan.md) §4.0. Driver is part of the deliverable; tested as part of acceptance.

#### 8 candidate library ports

Each lists: source ecosystem & library, target Dark module name, public-API surface (3–6 functions), tier, why it fits.

16. **`parser-combinators`** *(M, Stdlib.String, Stdlib.List, Stdlib.Result)* — Port of [Elm `parser`](https://package.elm-lang.org/packages/elm/parser/latest/). Public API: `succeed : a -> Parser a`, `fail : String -> Parser a`, `map : (a -> b) -> Parser a -> Parser b`, `andThen : (a -> Parser b) -> Parser a -> Parser b`, `oneOf : List<Parser a> -> Parser a`, `chompWhile : (Char -> Bool) -> Parser ()`. Target: `Darklang.Parser`.
    - Rubric: a driver that exercises 8 small grammars (number, identifier, JSON-bool, comma-separated list, balanced-parens, …). Each grammar is an `op` in `parser-cli`. Round-trip 6 inputs per grammar; shape errors carry position info.
    - Why: parser combinators are *the* Elm library; agents have seen them. Tests Dark's higher-order-function ergonomics + type inference on `Parser<a>`. **The library that most stresses type-system polish.**

17. **`mvu-runtime`** *(M, Stdlib.List, Stdlib.Dict)* — Port of the Elm Architecture (Model-View-Update). User vault explicitly calls out *"MVU everywhere"* (`~/vaults/Darklang Dev/04.Ethos/Composable/MVU everywhere/`). Public API: `program : { init: Model; update: Msg -> Model -> Model; view: Model -> View } -> Program`, plus a `runProgram` driver. Target: `Darklang.MVU`.
    - Rubric: a counter program (Msg = Inc | Dec | Reset; assert state after a sequence of msgs). Plus a todo-list program (Msg = Add String | Remove Int | Toggle Int). Driver feeds msg sequences from stdin; output is the final view-rendered state.
    - Why: the user vault flags MVU as core ethos. Library port doubles as ethos validation. Tests Dark's enum/match story under load.

18. **`url-builder-parser`** *(S, Stdlib.String, Stdlib.Dict, Stdlib.Result)* — Port of [Elm `Url`](https://package.elm-lang.org/packages/elm/url/latest/). Public API: `parse : String -> Result<Url, ParseError>`, `toString : Url -> String`, `Url.Builder.crossOrigin`, `Url.Parser.s`/`Url.Parser.string`/`Url.Parser.int`/`Url.Parser.</>`. Target: `Darklang.Url`.
    - Rubric: round-trip 10 URLs (with query params, fragments, escaped chars, IPv6 hosts); 5 builder cases produce expected strings; 5 invalid inputs produce typed errors.
    - Why: small, well-shaped, immediately useful. Real test of Dark's String/Result ergonomics. Doesn't depend on missing primitives.

19. **`validation-applicative`** *(S, Stdlib.Result, Stdlib.List)* — Port of [Haskell `Validation`](https://hackage.haskell.org/package/validation) (error-accumulating, distinct from `Result`). Public API: `Valid : a -> Validation err a`, `Invalid : List<err> -> Validation err a`, `map`, `apply` (the magic — accumulates errors), `combine2`/`combine3`. Target: `Darklang.Validation`.
    - Rubric: a form-validation driver — `validate-form name=foo email=invalid age=-5` returns *all* errors, not just the first. Compare to `Result` (which would short-circuit at first error). 6 input scenarios.
    - Why: small but conceptually rich. Demonstrates Dark's ability to host an ADT distinct from `Result` with different semantics. **Pedagogical value for agents** — the API teaches a real distinction.

20. **`pretty-printer`** *(M, Stdlib.String, Stdlib.List)* — Port of Wadler/Leijen pretty-printer (e.g. [F# Fantomas's `PrettyPrint`](https://github.com/fsprojects/fantomas), [OCaml `Format`](https://v2.ocaml.org/api/Format.html), [Haskell `prettyprinter`](https://hackage.haskell.org/package/prettyprinter)). Public API: `Doc` type with `text`, `line`, `nest`, `group`, `indent`, `<+>` (concat-with-space), `</>` (concat-with-line), and a `render : Int -> Doc -> String` (Int = max width). Target: `Darklang.Pretty`.
    - Rubric: 6 doc fixtures rendered at widths 20, 40, 80; output exact-matches a golden file at each width. Includes a multi-line list and a nested record.
    - Why: heavy use of recursion and combinator composition. Tests Dark's value-type and recursion ergonomics. Real utility — `dark show <hash>` and friends would benefit from this.

21. **`json-pointer`** *(S, Stdlib.Json, Stdlib.AltJson, Stdlib.String)* — Port of [RFC 6901 JSON Pointer](https://tools.ietf.org/html/rfc6901). Public API: `parsePointer : String -> Result<List<Token>, ParseError>`, `evaluate : Pointer -> Json -> Option<Json>`, `set : Pointer -> Json -> Json -> Json`. Target: `Darklang.JsonPointer`.
    - Rubric: 8 standard JSON Pointer cases from the RFC (root `""`, `/foo`, `/foo/0`, escaped `~0`/`~1`, etc.) on a fixture document. Plus 3 `set` cases. Plus 2 invalid pointers → typed errors.
    - Why: tightly-scoped spec to implement; small RFC; tests `Stdlib.Json` ergonomics adjacent to `jq-lite` (#14) but at a lower level.

22. **`pcg-random`** *(S, Stdlib.Int64, Stdlib.UInt64, Stdlib.Cli)* — Port of [PCG random](https://www.pcg-random.org/) (or a simpler LCG if PCG's bitops are awkward in Dark). Public API: `seed : Int64 -> Generator`, `nextInt : Int -> Int -> Generator -> (Int, Generator)`, `nextFloat : Generator -> (Float, Generator)`, `shuffle : List<a> -> Generator -> List<a>`. Target: `Darklang.Random`. Pure functional — generator state threaded through every call.
    - Rubric: deterministic — `seed 42` then 100 `nextInt 0 99` calls produce a specific sequence (asserted byte-equal); `shuffle [1L; 2L; 3L; 4L; 5L]` is stable for a given seed.
    - Why: forces the agent to thread immutable state through a pure-functional API — a pattern that comes up in many domains. Also the foundation for `password-gen` (#11) when its `--seed` mode is honest.

23. **`csv`** *(S, Stdlib.String, Stdlib.List, Stdlib.Result)* — Port of [F# CsvProvider](https://fsprojects.github.io/FSharp.Data/library/CsvProvider.html) or [OCaml CSV](https://github.com/Chris00/ocaml-csv) — RFC-4180 read/write. Public API: `parse : String -> Result<List<List<String>>, ParseError>`, `parseWithHeaders : String -> Result<List<Dict<String, String>>, ParseError>`, `write : List<List<String>> -> String`, `writeWithHeaders : List<String> -> List<Dict<String, String>> -> String`. Target: `Darklang.Csv`.
    - Rubric: 8 test cases from RFC-4180 (quoted commas, embedded newlines, CRLF vs LF, escaped quotes); round-trip preserves bytes. Library is the foundation for the existing `csv-to-json` Phase 1 application (#3).
    - Why: closes a *known* stdlib gap (per project-survey §2: "No CSV ... hand-rolled escaping required"). After this lands, future bench projects don't have to reinvent CSV. **Library that retires app-level reinvention** — the ideal kind of port.

#### Why these library ports specifically (and what's NOT here)

- **No async/concurrency libraries** — Dark has no async primitives ([plan.md](plan.md) §2 gap list). `Lwt`, `Hopac`, `async/await`-based libs are out of reach.
- **No FFI / native libs** — `lens` (Haskell) requires Template Haskell; `Cstruct` requires bit-twiddling that Dark may not support cleanly. Cut.
- **No reactive / signals libs** — Convex-style reactivity is in the [plan.md](plan.md) §2 gap list. Not portable.
- **Picked: small, pure, well-shaped libraries with strong type APIs.** Each one teaches a *pattern* (parser combinators, MVU, validation applicative, lenses-via-records) that compounds across future Dark code. Library ports are *seed crystals* for a richer Dark stdlib.

Future expansion candidates (Phase 3+): `darklang-time-parsing` (better than ISO-only — port `js-joda` or `chrono`), `state-monad` / `reader-monad` (small, architecturally informative), `html-builder-elm-style` (port the full Elm `Html msg` API; partial today), `lens-via-records` (Haskell-style optics, structurally suited to Dark's record updates), `regex-combinators` (typed regex builder distinct from the existing `Stdlib.Regex` string-based API).

---

**Total vetted projects after iter 28+29: 23.** Spread (apps + libraries combined):

| Tier | Apps | Libraries | Total | Phase |
|---|---|---|---|---|
| T | hello-cli, fizzbuzz, password-gen | — | 3 | P1: hello-cli; P2: rest |
| S | csv-to-json, word-count, temperature-cli, url-shortener-cli, cron-describe, markdown-toc, dedup | url-builder-parser, validation-applicative, json-pointer, pcg-random, csv | 12 | P1: csv-to-json + url-shortener-cli; P2: rest |
| M | todo-cli, http-healthz, webhook-echo, jq-lite | parser-combinators, mvu-runtime, pretty-printer | 7 | P2 |
| L | paste-bin | — | 1 | P2 |

Distribution: 3 T / 12 S / 7 M / 1 L. Library ports add 8; deliberately *no* HTTP additions in this expansion (§8 risk "flattering Dark").

**Still need**: ~77 more vetted projects to hit the 100 target. Phase 2's 30-project goal needs 7 more (15 apps + 8 libraries = 23 today). The catalog below has the candidate pool to pull from.

---

## Wider candidate catalog (drawn from the project survey)

> **Provenance**: this catalog is largely lifted from `~/vaults/Darklang Dev/02.Project Management/Current Experiment/project-survey.md`, an AI-generated landscape that the user has *not yet validated as the final 100*. Treat it as a **candidate pool to draw from**, not a canonical commitment. Items move from this pool into the bench only after a human (a) approves the rubric and (b) confirms the gold reference passes.

The survey organises projects into 12 classes (A–L) plus an out-of-reach class (M). For each class: capability → example projects → test criteria. The full survey has detailed test criteria for each class; the summary below lists project candidates and the survey's difficulty grade.

### Class A — Pure computation / text toys *(Easy)*

Capability: pure functions, stdin/stdout, stdlib only. Map closely to bench tier T.

Candidates: `unit-convert`, `password-gen`, `dice` (`3d6+2`-shaped), `uuid/ulid/nanoid`, `age` (`age 1988-07-14` → years/months/days), `roman` (numeral converter), `tipcalc`/`loan`/`bmi`/`compound-interest`, `regex-test`, `morse`, ciphers (`leet`/`rot13`/`caesar`/`vigenère`).

Test criteria (every Class A project): correct output for ≥5 canonical inputs (table-driven); bad-input → non-zero exit + readable stderr; `--help`/`-h` exits 0; no crash on empty / very long / non-ASCII input; deterministic output (fix RNG seed for `password-gen`).

### Class B — File / filesystem utilities *(Easy → Stretch)*

Capability: `Stdlib.Cli.File`, `Dir`, `Path`, `Posix`, `Regex`.

Candidates: `tree-lite`, `renamer` (bulk regex with dry-run + undo log), `dedup` (sha256), `grep-lite`, `tac`/`head`/`tail`/`wc` (test against coreutils), `linecount-by-ext`, `finder` (`find` + `-exec`), `watch-run` (`entr`-lite — **Stretch**, needs `File.watchLoop`), `file-mover`, `extract-emails` / `extract-urls`.

Test criteria: scratch dir in `/tmp`; large-file 10MB+ shouldn't OOM; symlinks don't loop; UTF-8 round-trip in filenames + bodies.

### Class C — HTTP client / API consumer *(Easy)*

Capability: `HttpClient`, `Json`, `Retry`.

Candidates: `weather` (wttr.in / OpenWeatherMap), `hn-top`, `gh-stars` (auth filter by language), `dad-joke`/`trivia`, `link-check` (walk markdown, HEAD all URLs), `public-ip`, `short-url-resolver` (follow redirects + chain), `pastebin-upload`, `currency` (exchangerate.host), `lichess-tv`.

Test criteria: mock endpoints via a tiny `HttpServer` shim; retry/timeout behaviours; auth via env var not hardcoded; typed error on JSON shape drift.

### Class D — Data munging / report generators *(Easy)*

Capability: `Json`, `List`, `Dict`, `Regex`, `DateTime`, `String`, `UI.Table`.

Candidates: `jq-lite`, `json2table`, `gh-pr-digest`, `log-summarize` (group-by-regex), `flashcards` (track attempts in `DB`), `habit-tracker` (ASCII calendar), `ical-next` (parse iCal hand-rolled), `cron-describe` (`*/5 * * * *` → English), `markdown-toc`, `release-notes` (group commits by conventional-commit type).

Test criteria: golden-file fixtures; Unicode/emoji rendering; empty input → empty output; sort stability.

### Class E — Interactive CLI / REPL *(Stretch)*

Capability: `Stdlib.Cli.UI.Prompt.{ask,confirm,select}`. Non-TUI — single-prompt / multi-prompt flows in cooked mode.

Candidates: `init-project` (scaffold), `git-commit-wizard`, `ssh-config-add`, `cookie-jar` (snippets fuzzy-select), `env-doctor`, `tldr-picker`, `dotfile-installer`, `recipe-picker`.

Test criteria: feed canned answers via expect-style harness; SIGINT must not corrupt on-disk state; idempotency (run twice → same tree).

### Class F — Full TUI applications *(Stretch)*

Capability: `Stdlib.Cli.Stdin.readKey` (real TTY), ANSI redraws, `Host.getTerminalWidth/Height`. Precedent: `Darklang.Cli.Apps.Outliner` (in-tree, full keyboard-driven outliner editor).

Candidates: `todo-tui`, `git-log-tui`, `process-viewer` (`htop`-lite, parses `/proc`), `file-picker` (`fzf`-lite), `markdown-reader` (render headers/bold/code), `kanban`, `clock`/`pomodoro`, `snake`/`2048`.

Test criteria: pty wrapper, snapshot screen at each step, diff against golden frames; resize + redraw; `q`/`ESC` restore cursor + reset ANSI; `kill -9` mid-edit → no data loss (atomic `File.writeAtomic`).

### Class G — Local HTTP server apps *(Stretch)*

Capability: `HttpServer` + `matchRoute` + `Json` + `DB`. **Caveat**: known lambda-cache regression (see Phase 1 #8 caveat).

Candidates: `url-shortener` (Phase 1 includes it), `paste-bin` (Phase 1 includes it), `feed-aggregator` (poll RSS-ish, JSON), `status-page` (`responseWithHtml`), `webhook-relay` (fan out), `static-file-server` (MIME), `oauth-echo`, `metrics-collector` (prom-ish).

Test criteria: black-box on random port via `HttpClient` from a 2nd Dark process; concurrency expected weak — document; malformed JSON → 400; idempotency (POST same URL → same slug); SIGTERM drains in ≤1s.

### Class H — LLM-powered CLIs *(Easy, once keys are set)*

Capability: `Darklang.LLM.Agent` with `withShellTool`, `withWebSearch`, `withSystemPrompt`, `withMaxTurns`. Precedent in `packages/darklang/llm/examples/`.

Candidates: `commit-msg` (already exists), `pr-review` (read `git diff main...HEAD`), `code-explainer`, `docstring-filler`, `bug-triage`, `natural-shell` ("show me 5 largest files…"), `pr-titler`, `daily-standup`.

Test criteria: `withTemperature 0.0` + fixed model + snapshot; assert tool calls fire; `withMaxTokens`/`withMaxTurns` respected; offline fixture fallback.

### Class I — MCP servers *(Stretch)*

Capability: `Darklang.ModelContextProtocol.serverBuilder`, `tools`, `resources`, `prompts`. **Note**: this class is a Phase 4+ deliverable per [plan.md](plan.md) §7 boundary (MCP is ecosystem-reach, not bench-scope) — but the survey lists candidates anyway.

Candidates: `mcp-git` (status/diff/log/blame), `mcp-fs` (read/write/grep with path-guard), `mcp-darkdb`, `mcp-calendar`, `mcp-issue-tracker`.

Test criteria: `modelcontextprotocol/inspector` conformance; JSON-RPC framing; path traversal rejection; concurrent clients.

### Class J — Shell-first orchestrators *(Easy)*

Capability: `Cli.Process.{exec,spawn,pipe,shellPipeline,runWithTimeout}`.

Candidates: `backup` (tar via shell + sha256), `dotfiles-sync`, `batch-ffmpeg`, `service-monitor` (periodic `systemctl is-active`), `docker-cleanup`, `port-check` (`nc -z` batched), `parallel-runner` — **Stretch** (no concurrency primitives — fakes via `Process.spawn` polling).

Test criteria: child non-zero exit → `Result.Error`; timeout kills + reaps; stdout/stderr separable; `runWithEnv` masks secrets.

### Class K — Scheduled / long-running daemons *(Stretch)*

Capability: `Posix.sleep`, `Process.spawn`, `File.withLock`, `File.writeAtomic`.

Candidates: `cron-lite`, `backup-daemon`, `log-rotate`, `health-checker` (alert via webhook on change), `heartbeat` (ping uptime service with jitter).

Test criteria: `withLock` prevents overlap (verify with two starts); virtualize `DateTime.now` for schedule math; transient-failure retry budget; SIGTERM-then-finish; self-rotates logs.

### Class L — Dev-tool-like projects *(Stretch)*

Capability: parsing, rendering, diffing across several stdlib modules.

Candidates: `mini-git` (blob/tree/commit in `DB`), `code-stats`, `static-site-gen`, `semver-bump`, `changelog-gen`, `lint-rule-runner`, `tree-sitter-queryer`, `dep-graph` (DOT render).

Test criteria: golden-tree fixtures; idempotency; bad UTF-8 doesn't panic; soft perf budget on 1000-file fixture.

### Class M — Out of reach (don't waste agent time)

Listed so agents know to skip. Each is blocked by a missing capability.

| Project | Blocker |
|---|---|
| Parallel/async downloader | no async/concurrency primitives |
| WebSocket chat, MQTT client | no sockets |
| TCP/UDP port scanner | no sockets |
| Redis / Postgres direct driver | no sockets |
| SQLite CLI wrapper | no SQLite driver (workaround: shell out to `sqlite3`) |
| Image resize/convert | no image lib |
| Audio player/recorder | no audio lib |
| PDF generation/extraction | no PDF lib |
| Encrypted vault (AES) | only hashes/HMAC; no AES |
| JWT signer (RS256) | no RSA signing (HS256 OK) |
| Tar/zip creation | only `Gunzip` decompress |
| CSV with RFC-4180 escaping at scale | no CSV lib (hand-roll feasible — see Phase 1 #3) |
| YAML / TOML config | no parser in stdlib |
| Real-time rogue-like with non-blocking input | `readKey` blocks; no input-timeout primitive |
| Service daemon with signal trapping | can send signals, can't `trap` SIGHUP/SIGINT |
| Streaming JSON over large files | `Json.parse` is whole-tree |
| Machine learning, matrix math | no numeric/BLAS |
| GUI app | terminal-only |
| Plugin systems via native shared libs | no FFI |

**Workarounds that push some "out of reach" into Stretch**: shell out to `curl` / `jq` / `yq` / `sqlite3` / `openssl` / `ffmpeg` / `convert`. Works but cedes the "better than bash" claim.

---

## Cross-cutting test criteria (every project, all classes)

A Darklang CLI project is "good" if:

1. **Argument parsing.** `--help`/`-h` exits 0 with usage. Unknown flag → non-zero with a suggestion.
2. **Exit codes.** 0 success, 1 generic error, 2 usage error, 3+ domain-specific; documented in help.
3. **Error messages.** Go to stderr. One line, no stack trace, unless `--verbose`.
4. **Streams.** stdin piped: works. stdin TTY: prompts or shows help. stdout piped: no ANSI unless `--color=always`.
5. **Idempotency.** Running twice → same observable state (state-mutating tools).
6. **Encoding.** UTF-8 in / UTF-8 out. Binary stays binary.
7. **Signals.** SIGINT: cursor restored, temp files cleaned, non-zero exit.
8. **Performance envelope.** Documented budget (e.g. "<500 ms for 10k-line input"). Measured, not guessed.
9. **Packaging.** `./scripts/run-cli run <pkg>.<fn>` works from a clean clone.
10. **Reproducibility.** Tests seed RNG, virtualize clock, fixture-mock HTTP.

---

## Suggested first-pass ordering (from the survey)

If we want a diverse signal cheaply, run one project from each class:

1. **A** `password-gen` — 10-min sanity check
2. **B** `grep-lite` — exercises `File.glob` + `Regex` + ANSI
3. **C** `gh-stars` — HTTP + JSON + auth
4. **D** `json2table` — Dict/List/pretty-print
5. **J** `parallel-runner` — probes concurrency story
6. **E** `init-project` — probes prompting
7. **F** `todo-tui` — probes TUI beyond Outliner
8. **G** `url-shortener` — HttpServer + DB
9. **H** `pr-titler` — LLM.Agent outside examples
10. **K** `cron-lite` — long-running + file locks
11. **I** `mcp-fs` — MCP server-side *(Phase 4+)*
12. **L** `changelog-gen` — real dev-tool polish

After this 12-project sweep we'll know per-class whether Dark beats bash/python/lua or needs stdlib additions. **Phase 2 should consume this ordering.**

---

## Sources

- `~/vaults/Darklang Dev/02.Project Management/Current Experiment/project-survey.md` — primary source for §A–M. AI-generated, awaiting human approval.
- `~/vaults/Darklang Dev/02.Project Management/Current Experiment/where we're a bit short.md` — runtime-gap caveats (lambda cache, async, native tests).
- `~/vaults/Darklang Dev/02.Project Management/Current Experiment/Goals for Week.md` — confirms TODO-CLI is in scope.
- The Phase 1 starter set (10 projects) was originally written by the loop in iteration 2 (see [`research-log.md`](research-log.md)) before the survey was located. It overlaps the survey but was vetted independently.
