# Darklang in the browser (WASM)

The runtime, parser, LibDB and SQLite compiled to WebAssembly and published as a static site.
Live at https://dark-wasm.fly.dev.

- `index.html`: a directory of the pages. With a query it is the answer, raw: `/?cmd=<argv>`
  prints what `dark <argv>` prints, `/?fn=<name>` is `view`.
- `cli.html`: the real Dark CLI in an xterm.js terminal, against a real package store. No params
  opens the workbench with a shell on the right that takes `dark <command>` lines (`?panel=0`
  hides it). `?cmd=<argv>` runs a command then drops to the classic prompt; `?fn=<name>` is
  `view`; `?env=DARK_CLASSIC=1` is the classic prompt.
- `eval.html?e=<expr>`: an expression box and what `dark eval` prints.
- `repl.html`: the older REPL over an in-memory package snapshot.

## Build, serve, deploy (from the repo root, inside the container)

```
dotnet publish backend/src/Wasm/Wasm.fsproj -c Release -o rundir/wasm-repl   # ~6 min (AOT)
backend/src/Wasm/make-store.sh                # the store the CLI boots from; re-run when packages change
python3 backend/src/Wasm/generate-snapshot.py # the REPL's snapshot (repl.html only)

./scripts/run-cli permissions allow http-server 9090
./scripts/run-cli permissions allow file read /home/dark/app/rundir/wasm-repl/wwwroot
./scripts/run-cli serve Darklang.WasmReplServer.router --port 9090   # host port: scripts/dev/host-port

backend/src/Wasm/deploy/deploy.sh             # nginx image of the publish -> fly app dark-wasm
```

Wipe `rundir/wasm-repl` before a publish, or stale fingerprinted files pile up. `deploy.sh` is
the hand deploy of the stable site; CI does the per-branch previews below.

## A preview per PR

CI publishes this site from every commit that would be deployed and puts it on the preview
host (`build-wasm`, `deploy-preview`; `deploy/preview/ci-deploy.sh` has the rules):

    https://wasm.darklang.com/            directory of what is deployed
    https://wasm.darklang.com/pulls/<n>/  the CLI built from PR #n
    https://wasm.darklang.com/main/       the CLI built from main
    https://wasm.darklang.com/tag/        the CLI as last released (only the latest is kept)

A PR gets a preview when its author is on `PREVIEW_AUTHORS` (the three of us by default), and
nothing else does: previews run a branch's JS on a public URL, so a stranger's PR should not
get one by default. Fork builds never see the tokens anyway, which is the guard that actually
holds. The store is a fresh migration in the runner, so no secret can be in it. The PR gets
one comment with the link, edited on later pushes.

The host keeps the latest tag, main, and open PRs, ~25 MB each; every main deploy removes
`/pulls/<n>/` for PRs no longer open. A removed preview, an old tag URL, or a typo is a 404
whose page points at `/tag/` and `/main/`. The tab keeps nothing: the store is in memory and a
reload is a fresh one.

The host is the fly app `dark-preview` (`deploy/preview/`: `fly.toml`, `Dockerfile`,
`nginx.conf`, `regen-index`, `gone.html`, `preview.css`): one nginx over one volume, a
directory per site, `_framework` kept as `.gz` only. `host.sh upload|remove|cleanup` works
from a laptop with a logged-in flyctl, which is how to test any of this without a PR. Every
preview shares that origin, so nothing with a cookie or a token may ever be served from it.

### Saying what a branch is for

By default the preview is this site, built from the branch. Three ways to point a reviewer at
the thing, cheapest first:

- A URL in the PR description: `cli.html?cmd=<argv>` runs a command and hands over the prompt
  (`?cmd=outliner`, `?cmd=view Stdlib.Eq`), `?fn=<name>` lands on an item, `eval.html?e=<expr>`
  is an expression box, `/?cmd=<argv>` prints the answer and nothing else.
- A `showcase.json` in `wwwroot/`, read by the branch's index page and by the directory:

      { "title": "Traits",
        "blurb": "Eq and ToString as stdlib traits; sum and product over Zero and One.",
        "landing": "cli.html?cmd=view Darklang.Stdlib.Eq",
        "tries": [ { "label": "sum over a trait", "url": "eval.html?e=List.sum [1L; 2L]",
                     "what": "resolves Zero and Add at the call" } ] }

  `landing` is where the directory card goes (default `cli.html`); `tries` become cards at
  the top of the branch's index. Delete the file before merging, or leave it if main should
  say the same.
- A page of your own in `wwwroot/`, and `landing` pointing at it. `site.js` gives it
  `dark.boot`, `dark.run(argv)` and `dark.invoke`; `eval.html` is the smallest example.

What a preview cannot show is what the browser cannot do: processes, an HTTP server,
persistence across reload, push to the relay. `serve --dev`, `dark ps` and sync-heavy branches
won't demo this way; say so in the PR rather than let a reviewer hunt for it.

## How it is put together

`Host.fs`: `Cli.Boot` fetches `data.db.br` into emscripten's in-memory filesystem, inflates it
through the runtime's own brotli decoder, warms SQLite and runs `growIfNeeded`; `Cli.RunCli`
builds the same execution state the native CLI builds and calls the entry point; `RunCommand`
runs one command with its output captured. `Browser` is the seam to the page: keys in
(`PushKey`/`PushPaste`), output out (`DrainOutput`, drained by the page on a timer), size
(`SetTerminalSize`). `BrowserBuiltins` replaces seven builtins (stdin, terminal size and
session info, clear); everything else is the real `Builtins.Cli`/`CliHost`/`Matter`.
`site.js` holds the pages' shared boot and helpers.

`RunAOTCompilation` is on because the interpreter's F# `task` loop could not suspend a second
time under the mono interpreter. A few big leaf assemblies stay interpreted; interpreting the
BCL wholesale asserts when compiled F# generics call into it. `InvariantGlobalization` drops ICU.

## The store, and the secret

`make-store.sh` copies this clone's `rundir/data.db`, deletes `config_v0` (relay url, push
cursors, and `sync.secret.<url>`, the write secret shared between a person's machines and
production) and the sync tables, asserts `config_v0` is empty, scans the bytes for a stored
secret key, and only then writes the file. It never reads `~/.darklang` or `cli-config.json`.
`deploy.sh` checks the bytes again. Keep it that way.

## Testing headless

```
node backend/src/Wasm/headless-check.mjs <url> <script.js> [timeout-s] [screenshot.png]
```

Drives playwright's chromium over CDP with Node's WebSocket. The script is evaluated in the page
every 2 s until it returns a string starting `DONE` or `FAIL`; other returns are progress.

## Known gaps

- Everything is per tab and in memory; reload and the store is fresh.
- No push from the tab (no secret ships). Pull from a server is untried.
- Posix: cwd, env, mkdir, rmdir, unlink, rename, listDir and stat answer through `System.IO`;
  descriptors, symlinks, chmod, kill and processes answer ENOSYS. HTTP: same-origin and
  CORS-enabled hosts only; no server.
- Cold start: 17.7 MB gzip of runtime plus a 6.6 MB brotli store, then ~1 s of warm-up.
