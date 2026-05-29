# Darklang CLI Project Survey

A landscape of CLI scripts/apps to build with Darklang, grouped by how much of
the language/runtime each one exercises. For each class there are example
projects plus test criteria, so an AI agent can attempt to build them and
report back on friction points. Goal: eventually beat bash / python / lua at
every class of CLI work.

## 1. What Darklang gives you (short inventory)

**Runtime model.** Code lives in a persistent, versioned **package tree**
(owner.module.name) with SCM-style `branch / commit / merge / rebase`. CLI
scripts are the "file-like" escape hatch: a `.dark` file runnable via
`./scripts/run-cli eval '<expr>'` or `run <fn>`.

**Rich stdlib (Darklang.Stdlib.\*):**
- Text / structure: `String`, `Char`, `List`, `Dict`, `Option`, `Result`,
  `Tuple2/3`, `Regex`, `Json`, `AltJson`, `Base64`, `Bytes`, `Diff`, `Html`,
  `Uuid`
- Numbers: `Int8/16/32/64/128`, `UInt8..128`, `Float`, `Math`
- Time: `DateTime` (parse, arithmetic, ISO formatting)
- Network: `HttpClient` (incl. `StreamingHttpClient`, SSE), `Http`,
  `HttpServer` (with `matchRoute`, `jsonResponse`, etc.)
- Crypto: `Crypto` (md5, sha256, sha384, sha1hmac, sha256hmac), `X509`
  (public-key extraction only)
- Persistence: `DB` (built-in key/value with `query`, `queryOne`, `count`, etc.)
- CLI-specific (`Stdlib.Cli.*`): `File` (read/write/glob/watch/withLock/atomic),
  `Process` (exec/spawn/pipe/shellPipeline/runWithTimeout/runWithEnv),
  `Stdin` (readKey, readLine), `Path`, `Dir`, `Env`, `Host` (OS detection),
  `Posix` (sleep, signals via kill, chmod, etc.), `Bash`/`Zsh`/`Fish` (rc
  manipulation), `Gunzip`, `UI.Color` (ANSI), `UI.Spinner`, `UI.Progress`,
  `UI.Prompt` (ask/confirm/select), `UI.Table`
- Retry: `Stdlib.Retry`
- LLM / AI: `Darklang.LLM.Agent` with tool use, web search, multi-turn,
  provider abstraction (`providers/*`). Examples ship for git-commit agent,
  news digest, code agent, url-reader, json-extractor, image-describer,
  ollama-translator, research, multi-model.
- Protocols: `Darklang.ModelContextProtocol` (MCP server framework with
  tools/resources/prompts), `JsonRpc`, `LanguageServerProtocol`
- Inspection: `LanguageTools`, `Deps`, `PM` (package manager)

**Builtins (`Builtin.*`, 600 fns):** POSIX bindings (`posixOpen`, `posixFlock`,
`posixGetuid`, `posixSpawnAndWait`, `posixUname`, `posixReadlink`, ...),
directory ops, env, file, process spawn, reflection.

**Proven TUI.** The in-tree `Darklang.Cli.Apps.Outliner` is a full
keyboard-driven outliner editor (markdown/OPML export, list picker, text
editor, watchable) — TUIs are already doable in pure Dark.

## 2. What Darklang does NOT give you

Searched the package tree and builtins. Notable absences:

- **No async / concurrency primitives.** No `async`, `await`, promises,
  channels, threads, or `Parallel.*` (outside LLM tool invocation). A
  downloader doing 10 parallel requests is not directly expressible.
- **No raw sockets.** No TCP, UDP, Unix-domain, WebSocket client/server, or
  raw bytes-over-wire. Everything network is HTTP(S) through `HttpClient` or
  shelled-out `curl`.
- **No SQLite / Postgres / Redis client.** `Stdlib.DB` is the *internal*
  package-tree persistence, not a general SQL driver.
- **No CSV, YAML, XML, TOML.** JSON only. OPML exists as a one-off. CSV
  would need hand-rolled escaping.
- **No image / audio / video / PDF** libs. No `sharp`/`pillow` equivalent.
- **No compression creation.** `Gunzip` decompresses; no gzip, tar, zip
  creation.
- **No non-HTTP crypto.** Hashes + HMACs + X509 public-key parsing only. No
  AES, RSA, Ed25519 signing, JWT signing, bcrypt/argon2, TLS tunneling.
- **No signal handling** other than sending signals. Can't trap SIGINT to
  clean up a TUI gracefully (you rely on terminal state being restored).
- **No streaming stdin line iterator.** `readLine` / `readKey` exist, but
  `readKey` errors out when stdin is a pipe (not a terminal) — observed:
  `Cannot read keys when ... console input has been redirected`.
- **No `let rec` at top-level in `eval`**, and nested function definitions
  are banned. Recursion requires a named package function.
- **No first-class modules** (can't `let c = Darklang.Cli.Colors`).
- **No streaming JSON parse** — you get the whole tree. Memory-bound.
- **Limited subprocess streaming.** `Process.spawn` exists but there's no
  visible line-by-line stream reader for long-running children in the
  ordinary case.
- **Live-programming friction.** Writing real code means adding functions to
  the package tree, committing, etc. For one-shot scripts this is heavier
  than `python script.py`.
- **Integer ergonomics.** Literals need suffixes (`5L`, `5l`, `5UL`, `5s`),
  `/` isn't int-division, cross-size conversion is explicit.

## 3. Project classes

For each class: (a) what capability it exercises, (b) example projects, (c)
testing criteria. Grade the class — **Easy**, **Stretch**, **Hard**, or
**Out-of-reach** — based on stdlib coverage.

---

### Class A — Pure computation / text toys (Easy)

Capability: pure functions, stdin/stdout, stdlib only.

**Projects**
1. **unit-convert** — convert `10 mi to km`, `72 F to C`, `1 GiB to MB`.
2. **password-gen** — length, charset classes, pronounceable, copy-to-file.
3. **dice** — `3d6+2`, `4d6 drop lowest`, stats mode.
4. **uuid / ulid / nanoid** — generate N of them, with flags for format.
5. **age** — `age 1988-07-14` → years/months/days.
6. **roman** — arabic ↔ roman numeral converter.
7. **tipcalc / loan / bmi / compound-interest** — classic 100daysofcode toys.
8. **regex-test** — feed pattern + sample, show all match groups.
9. **morse** — text ↔ morse, optional timing diagram.
10. **leet / rot13 / caesar / vigenère** — string ciphers.

**Testing criteria** (apply to each):
- Correct output for ≥5 canonical inputs (table-driven tests).
- Bad-input path returns non-zero exit and a readable error on stderr.
- `--help` / `-h` shows usage and exits 0.
- No crash on empty input, very long input, non-ASCII input.
- Deterministic output where applicable (fix the RNG seed for `password-gen`
  tests).

---

### Class B — File / filesystem utilities (Easy → Stretch)

Capability: `Stdlib.Cli.File`, `Dir`, `Path`, `Posix`, `Regex`.

**Projects**
1. **tree-lite** — print directory tree with size column, filters, depth.
2. **renamer** — bulk rename by regex, dry-run, undo log.
3. **dedup** — find duplicate files by sha256, report savings.
4. **grep-lite** — search text across files, with globs and colored output.
5. **tac / head / tail / wc** — reimplementations, test against coreutils.
6. **linecount-by-ext** — `cloc`-lite, aggregate by extension.
7. **finder** — like `find`, with glob + `-exec` support (via `Process.exec`).
8. **watch-run** — `entr`-lite: watch paths, rerun command on change
   (needs `File.watchLoop`). **Stretch.**
9. **file-mover** — dedup, normalize names, create directory hierarchy from
   regex captures.
10. **extract-emails** / **extract-urls** — grep-and-normalize from text trees.

**Testing criteria**
- Create a scratch dir in `/tmp` with known contents; verify behavior then
  rm -rf.
- Large-file path: 10 MB+ file shouldn't OOM.
- Symlink handling: don't follow infinite loops.
- Exit codes: 0 on matches, 1 on no matches for grep-like, >1 on error.
- Race: `renamer` dry-run must not touch disk — verify with `stat -c %Y`
  before/after.
- Encoding: UTF-8 content in filenames & file bodies round-trips.

---

### Class C — HTTP client / API consumer (Easy)

Capability: `HttpClient`, `Json`, `Retry`.

**Projects**
1. **weather** — lookup via wttr.in or OpenWeatherMap, show current + 3-day.
2. **hn-top** — fetch Hacker News top stories (Firebase API), pretty-print.
3. **gh-stars** — list a user's starred repos, filter by language.
4. **dad-joke** / **trivia** — fetch from icanhazdadjoke/opentdb.
5. **link-check** — walk markdown tree, HEAD every URL, report dead links.
6. **public-ip** — call ifconfig.co / ipify, display with location.
7. **short-url-resolver** — follow HTTP redirects, show final URL + chain.
8. **pastebin-upload** — POST content to a pastebin, return URL.
9. **currency** — convert using exchangerate.host.
10. **lichess-tv** — show currently running top Lichess games.

**Testing criteria**
- Mock endpoint or record fixtures in `/tmp` with a tiny `HttpServer`
  shim; tests run offline against it.
- Retry behavior: simulate a 503 and verify exactly N retries (use the
  shim).
- Timeout: server sleeps longer than the timeout, client must fail cleanly.
- Auth: for token-requiring APIs, ensure `DARKLANG_*` env is read, not
  hardcoded.
- JSON shape drift: drop a required field in the fixture and confirm a
  typed error, not a crash.

---

### Class D — Data munging / report generators (Easy)

Capability: `Json`, `List`, `Dict`, `Regex`, `DateTime`, `String` (padEnd /
padStart), `UI.Table`.

**Projects**
1. **jq-lite** — filter + project + sort over JSON from stdin.
2. **json2table** — stream records → aligned table.
3. **gh-pr-digest** — fetch PRs via `Darklang.GitHub`, table by author/age.
4. **log-summarize** — tail a log file, group by regex bucket, print top-N.
5. **flashcards** — drill from a JSON deck, track attempts in `DB`.
6. **habit-tracker** — mark/unmark per day, streak calc, ASCII calendar.
7. **ical-next** — parse iCal (hand-rolled), show next 5 events.
8. **cron-describe** — parse `*/5 * * * *`, print in English.
9. **markdown-toc** — emit a table-of-contents for a markdown file.
10. **release-notes** — group commits between two refs by conventional-commit
    type.

**Testing criteria**
- Golden-file tests: input fixture + expected output file.
- Unicode: input containing em-dashes, emoji, RTL text renders without
  column misalignment (`Stdlib.String.length` is bytes-aware — test this
  explicitly).
- Empty input produces empty output, exit 0.
- Sort stability: given equal keys, original order preserved.

---

### Class E — Interactive CLI / REPL (Stretch)

Capability: `Stdlib.Cli.UI.Prompt.{ask,confirm,select}`, basic loops.

Note: these are **non-TUI** — single-prompt / multi-prompt interactive
flows where the terminal stays in cooked mode.

**Projects**
1. **init-project** — ask name/license/language, scaffold a repo.
2. **git-commit-wizard** — conventional-commit picker, diff preview.
3. **ssh-config-add** — prompt for host / port / key, append to `~/.ssh/config`.
4. **cookie-jar** — save/load snippets, fuzzy-select to copy.
5. **env-doctor** — check required binaries/envs, propose fixes.
6. **tldr-picker** — pick a command, show tldr page with paging.
7. **dotfile-installer** — walk a config tree, prompt y/n for each link.
8. **recipe-picker** — ingredients inventory, suggest matching recipes.

**Testing criteria**
- Golden-path: feed canned answers via expect-style harness (Dark's
  `cliProcessIO` or raw `readLine` over pipes where possible); assert
  filesystem/output state.
- `^C` path: SIGINT terminates without corrupting on-disk state (test via
  `Process.spawn` then kill).
- Idempotency: running twice produces the same tree.

---

### Class F — Full TUI applications (Stretch)

Capability: `Stdlib.Cli.Stdin.readKey` (requires real TTY), ANSI redraws,
`Host.getTerminalWidth/Height`, state-machine screens. Precedent exists in
`Darklang.Cli.Apps.Outliner`.

**Projects**
1. **todo-tui** — keyboard-navigated list, checkbox, file-backed.
2. **git-log-tui** — scroll commit list, preview diff pane.
3. **process-viewer** — `htop`-lite: parse `/proc`, refresh every N ms.
4. **file-picker** — `fzf`-lite over a rooted tree, fuzzy match, preview pane.
5. **markdown-reader** — render headers/bold/italic/code, scroll, outline nav.
6. **kanban** — columns, drag cards with keys, JSON on disk.
7. **clock / pomodoro** — big-font ASCII clock, Pomodoro state machine.
8. **snake / 2048** — grid games.

**Testing criteria**
- Harness: spawn the binary under `script(1)` or a pty wrapper, feed a
  keystroke sequence, snapshot screen at each step, diff against
  golden frames.
- Resize: shrink terminal mid-run; redraw must not corrupt.
- Exit path: `q` and `ESC` both restore cursor and reset ANSI state; verify
  no residual escape codes in post-exit output.
- Persistence: kill hard (`kill -9`) mid-edit; next run recovers without
  data loss (atomic `File.writeAtomic`).
- Performance: 60 fps-ish redraw at 200x80 for simple grids.

---

### Class G — Local HTTP server apps (Stretch)

Capability: `HttpServer` + `matchRoute` + `Json` + `DB`.

**Projects**
1. **url-shortener** — POST `/shorten` returns slug, GET `/:slug` redirects.
2. **paste-bin** — plain-text upload, expirable.
3. **feed-aggregator** — poll N RSS-ish feeds, expose JSON.
4. **status-page** — ping list of URLs, render HTML with
   `responseWithHtml`.
5. **webhook-relay** — accept incoming webhooks, fan out to subscribers.
6. **static-file-server** — serve a dir with MIME detection.
7. **oauth-echo** — complete an OAuth dance, print the token (dev tool).
8. **metrics-collector** — accept `POST /metric`, `GET /dump` as prom-ish text.

**Testing criteria**
- Black-box: start server on a random port, `HttpClient` from a second
  Dark process hits it, assert response.
- Concurrency: N simultaneous clients — **expect this to be weak**; document
  whether the server serializes or parallelizes.
- Error path: malformed JSON → 400 with readable body.
- Idempotency: POST /shorten same URL twice → same slug.
- Shutdown: SIGTERM drains in-flight requests within ≤ 1s.

---

### Class H — LLM-powered CLIs (Easy, once keys are set)

Capability: `Darklang.LLM.Agent` with `withShellTool`, `withWebSearch`,
`withSystemPrompt`, `withMaxTurns`. Precedent in
`packages/darklang/llm/examples/`.

**Projects**
1. **commit-msg** — already exists — reference to copy.
2. **pr-review** — read `git diff main...HEAD`, post a critique.
3. **code-explainer** — glob a path, summarize per file.
4. **docstring-filler** — rewrite a file's fns with doc comments (emit diff).
5. **bug-triage** — given an error trace + repo, suggest file+line.
6. **natural-shell** — "show me the 5 largest files modified this week" → sh.
7. **pr-titler** — given a diff, write conventional commit title.
8. **daily-standup** — read git log + calendar, draft standup bullets.

**Testing criteria**
- Determinism: `withTemperature 0.0` and fixed model; snapshot responses.
- Tool-use: assert the transcript shows the expected tool calls fired.
- Cost guard: `withMaxTokens` / `withMaxTurns` respected.
- Error path: API key missing → crisp error, exit 1.
- Offline fallback: behind a flag, use a canned response fixture.

---

### Class I — MCP servers (Stretch)

Capability: `Darklang.ModelContextProtocol.serverBuilder`, `tools`,
`resources`, `prompts`.

**Projects**
1. **mcp-git** — expose `status/diff/log/blame` as MCP tools.
2. **mcp-fs** — read/write/grep a rooted directory, with path-guard.
3. **mcp-darkdb** — expose the local `Stdlib.DB` to an MCP client.
4. **mcp-calendar** — wrap ical/tasks file as tools.
5. **mcp-issue-tracker** — JSON-file backed, for a solo dev.

**Testing criteria**
- Conformance: run `modelcontextprotocol/inspector` against it; every
  declared tool is discoverable and invocable.
- JSON-RPC framing: malformed input produces the correct error code.
- Path traversal: `mcp-fs` rejects `../` outside the root.
- Concurrency: two clients in parallel don't interfere.

---

### Class J — Shell-first orchestrators (Easy)

Capability: `Cli.Process.{exec,spawn,pipe,shellPipeline,runWithTimeout}`.

**Projects**
1. **backup** — tar via shell-out, sha256, optionally rsync target.
2. **dotfiles-sync** — `git fetch`/`diff`/`merge`, report.
3. **batch-ffmpeg** — glob files, run ffmpeg per file, progress bar
   (shells out — Dark itself has no video lib).
4. **service-monitor** — periodic `systemctl is-active`, notify on change
   (needs a notifier — shell out to `notify-send` on Linux).
5. **docker-cleanup** — safe `docker rm` for exited containers older than N.
6. **port-check** — `nc -z` wrapper batched across hosts.
7. **parallel-runner** — run N commands with a concurrency cap.
   **Stretch**: without concurrency primitives, fakes parallelism by
   spawning processes via `Process.spawn` and polling `isRunning`.

**Testing criteria**
- Non-zero exit from child surfaces as a `Result.Error` — assert mapping.
- Timeout: command sleeps > timeout, Dark kills it; child reaped.
- Parallelism test for `parallel-runner`: wall-clock should scale roughly
  `total_time / concurrency`.
- Output capture: stdout and stderr separable.
- Environment propagation: `runWithEnv` masks secrets from logs.

---

### Class K — Scheduled / long-running daemons (Stretch)

Capability: `Posix.sleep`, `Process.spawn`, `File.withLock`,
`File.writeAtomic`.

**Projects**
1. **cron-lite** — run a config of jobs with catch-up semantics.
2. **backup-daemon** — every N hours, snapshot + rotate.
3. **log-rotate** — size/age-based rotation for a dir of logs.
4. **health-checker** — curl N URLs, alert via webhook on change.
5. **heartbeat** — ping uptime service every 5 min with jitter.

**Testing criteria**
- Single-instance: `withLock` prevents overlap; verify with two concurrent
  starts.
- Clock skew: virtualize `DateTime.now` in tests to assert schedule math.
- Survives transient HTTP failures (retry budget).
- Graceful stop: SIGTERM causes in-flight job to finish, then exit.
- Log hygiene: rotates itself; never fills disk.

---

### Class L — Dev-tool-like projects (Stretch)

Capability: parsing, rendering, diffing, integration across several
stdlib modules.

**Projects**
1. **mini-git** — blob/tree/commit stored in `DB`, with `log`/`checkout`.
2. **code-stats** — walk repo, per-extension/author lines changed over time.
3. **static-site-gen** — markdown → html via `Html` helpers, templates.
4. **semver-bump** — read package manifest, compute next, write.
5. **changelog-gen** — parse conventional commits between tags.
6. **lint-rule-runner** — run N regex rules over a codebase.
7. **tree-sitter-queryer** — (project has `tree-sitter-darklang/`) query
    source files with s-expr queries.
8. **dep-graph** — for a Dark project, walk `Deps` module, render DOT.

**Testing criteria**
- Golden-tree tests: fixture repo in `tests/fixtures/`, run tool, diff
  against expected.
- Idempotency where applicable.
- Handles bad UTF-8 bytes without panicking.
- Tool completes on a 1000-file fixture under N seconds (soft budget).

---

### Class M — Out of reach / requires major stdlib work

Listed so agents don't waste time here. Each is blocked by a missing
capability.

| Project class | Blocker |
|---|---|
| Parallel/async downloader (10 concurrent reqs) | no async/concurrency primitives |
| WebSocket chat, MQTT client | no sockets |
| TCP/UDP port scanner, netcat-lite | no sockets |
| Redis / Postgres direct driver | no sockets / no wire protocol lib |
| SQLite CLI wrapper | no SQLite driver (shell out to `sqlite3` works as workaround) |
| Image resize/convert, ASCII-art from PNG | no image lib |
| Audio player / recorder | no audio lib |
| PDF generation / extraction | no PDF lib |
| Encrypted vault (AES) | only hashes/HMAC in stdlib |
| JWT signer (HS256 OK, RS256 needs RSA) | no RSA signing |
| Tar/zip creation | only gunzip decompress |
| CSV with RFC-4180 escaping at scale | no CSV lib; hand-roll feasible but tedious |
| YAML / TOML config reader | no parser in stdlib |
| Real-time rogue-like with non-blocking input | `readKey` blocks; no input timeout primitive |
| Service daemon with signal trapping | can send signals, can't `trap` SIGHUP/SIGINT |
| Streaming JSON over large files | `Json.parse` is whole-tree |
| Machine-learning, matrix math | no numeric/BLAS lib |
| GUI app | out of scope (terminal-only) |
| Windows COM / macOS AppleScript integration | no platform-specific bindings |
| Plugin systems via native shared libs | no FFI |

**Workarounds that push some of these into "Stretch":**
- Shell out to `curl` in parallel via `Process.spawn` + poll → imperfect but
  unlocks many "parallel HTTP" projects.
- Shell out to `jq`, `yq`, `sqlite3`, `openssl`, `ffmpeg`, `convert` — Dark
  becomes glue. This works but cedes the "better than bash" claim.

## 4. Cross-cutting test criteria (every project)

A Darklang CLI project is "good" if:

1. **Argument parsing.** `--help`/`-h` exits 0 with usage. Unknown flag
   exits non-zero with a suggestion.
2. **Exit codes.** 0 success, 1 generic error, 2 usage error, 3+ domain-
   specific; documented in help.
3. **Error messages.** Go to stderr. One line, no stack trace, unless
   `--verbose`.
4. **Streams.** stdin piped: works. stdin a TTY: either prompts or shows
   help. stdout piped: no ANSI codes unless `--color=always`.
5. **Idempotency.** Running twice produces the same observable state (for
   state-mutating tools).
6. **Encoding.** UTF-8 in / UTF-8 out. Binary stays binary (no implicit
   decode).
7. **Signals.** SIGINT: cursor restored, temp files cleaned, non-zero exit.
8. **Performance envelope.** Documented budget (e.g., "< 500 ms for 10k-line
   input"). Measured, not guessed.
9. **Packaging.** `./scripts/run-cli run <pkg>.<fn>` works from a clean
   clone. Any required env documented in `--help`.
10. **Reproducibility.** Tests seed RNG, virtualize clock, fixture-mock HTTP.

## 5. Suggested agent-task template

When you hand one of these to an AI agent, give it this structure:

```
PROJECT: <name>
CLASS: <A-L>
GOAL: <1-paragraph description from §3>
CAPABILITY BUDGET:
  - allowed: Stdlib.*, Builtin.*, Cli.Process.exec (for X)
  - forbidden: shelling out to tool Y (we want Dark-native)
DELIVERABLES:
  - package module under Darklang.Community.<name> OR a .dark script in
    user-code/darklang/scripts/
  - tests under Darklang.Community.<name>.Tests
ACCEPTANCE:
  - criteria from §3 for class + §4 cross-cutting
  - specific test fixtures in tests/fixtures/<name>/
REPORT BACK:
  - what worked
  - where stdlib was missing (be specific: function name you wanted)
  - where syntax fought you (quote the error)
  - whether the package-tree workflow helped or hurt vs. plain scripts
  - estimated lines vs. equivalent bash/python
```

The "report back" section is the point: each project is a probe into where
Dark is already ahead of bash/python/lua and where it needs filling in.

## 6. Suggested ordering for a first pass

If you want a diverse signal cheaply, run one project from each class in
this order:

1. **A** password-gen (10 min sanity check)
2. **B** grep-lite (exercises `File.glob`, `Regex`, ANSI)
3. **C** gh-stars (HTTP + JSON + auth)
4. **D** json2table (Dict/List/pretty-print)
5. **J** parallel-runner (probes the concurrency story)
6. **E** init-project (probes prompting)
7. **F** todo-tui (probes TUI beyond the outliner)
8. **G** url-shortener (probes HttpServer + DB)
9. **H** pr-titler (probes LLM.Agent outside examples)
10. **K** cron-lite (probes long-running + file locks)
11. **I** mcp-fs (probes MCP server-side)
12. **L** changelog-gen (probes "real dev tool" polish)

At that point you'll know, per class, whether Dark is beating bash/python
or needs stdlib additions.
