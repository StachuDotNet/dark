# PR: print-md as an App (the capstone)

The spine's floor effort 10 — the **north star**. Not new substrate; the *integration* that
shows every floor piece composing into the actual goal: print-md lives in Dark, edits sync
across the tailnet, and `print-md file.md` runs it. Built from
[apps-surface.md](../pre-s-and-s/apps-surface.md) + [capabilities.md](../pre-s-and-s/capabilities.md)
+ the sync chain ([sync.md](sync.md)).

**Goal.** `print-md` is a Dark App (an entrypoint fn + declared caps). `dark apps install
print-md` makes `print-md file.md` runnable from the shell. An edit to its fn on the desktop
syncs to the laptop. Today's `print-md` is bash (read → Pandoc → WeasyPrint → `lp`); the Dark
App does the same via an `fs-read` builtin + **three** subprocess spawns (`pandoc`, `weasyprint`,
`lp`) — there's no Dark markdown renderer to reuse (prework-verified below).

**Prereqs.** The whole floor: ops⊥projections (3), Tailscale (5), sync (7), autosync (9) — and
apps-surface (install=alias) + capabilities (fs-read + spawn). This PR adds *no* new substrate;
it's the thinnest possible layer on top.

## What "an App" is here

An App is **package items + a manifest value**, no new format. The manifest is an ordinary
Dark value (stored as ops like everything else):

```fsharp
type AppManifest =
  { name : String                       // "print-md"
    entrypoint : FQFnName.Package        // Darklang.Stachu.PrintMd.main
    caps : Capabilities }                // what install will grant (capabilities.md)

let printMdApp : AppManifest =
  { name = "print-md"
    entrypoint = fqfn "Darklang.Stachu.PrintMd.main"
    caps = { fileSystem = true                                  // read the .md
             cliHost = true                                     // spawn weasyprint + lp
             httpClient = None; httpServer = None; random = false; time = false } }
  // cliHost is boolean now (capabilities.md); print-md wants the *structured* form — a spawn
  // allow-list ["pandoc"; "weasyprint"; "lp"] — which is the "structured later" cliHost refinement.
```

The entrypoint. **Render is two spawns, not a Dark library call** — see the grounding note:

```dark
let main (args: List<String>) : Int64 =
  let file = args |> List.head |> unwrap
  let md   = Stdlib.File.read file                          // fs-read cap
  // md → html via pandoc, html → pdf via weasyprint — same chain as the bash original
  let html = Cli.spawn "pandoc" ["-f"; "markdown"; "-t"; "html"] md   // spawn cap (stdin=md)
  let pdf  = Cli.spawn "weasyprint" ["-"; "out.pdf"] html             // spawn cap (stdin=html)
  Cli.spawn "lp" ["-o"; "sides=two-sided-long-edge"; "out.pdf"] ""    // spawn cap
  0L
```

> **Verified against `main` (prework).** There is **no markdown→HTML/PDF anywhere in Dark** — a
> backend `git grep` for `pandoc|wkhtmltopdf|toHtml|→pdf` across `packages/**/*.dark` finds
> nothing, and there is **no `Darklang.Markdown` module**. The outliner's `markdown.dark` is the
> *opposite* direction and the wrong shape: `Markdown.export (doc: Outliner.Document) : String`
> serializes an **outline tree → nested-bullet markdown text** (and `import` parses markdown back
> into an outline) — it neither takes plain markdown nor produces HTML/PDF. **So the capstone
> cannot "reuse the outliner render."** The render path is exactly the bash original's external
> chain — `pandoc` (md→html) then `weasyprint` (html→pdf) then `lp` — via the **existing
> `Cli.posixSpawnAndWait`** spawn builtin (the same primitive Tailscale uses; confirmed in
> `Builtins.Cli`, [tailscale.md](../pre-s-and-s/tailscale.md)). This means **three** spawns, not
> two, and the cap allow-list is `["pandoc"; "weasyprint"; "lp"]`. **Open question to resolve when
> built:** `posixSpawnAndWait`'s signature is `(program) (args) → Result<(exit*stdout*stderr)>`
> with **no stdin parameter** — pandoc/weasyprint read their input on stdin, so either the spawn
> builtin needs a stdin-bytes parameter added, or the chain writes temp files between stages
> (`md→pandoc→tmp.html→weasyprint→out.pdf`). The temp-file route needs **no new builtin**; the
> stdin route is the one possible builtin extension flagged below.

## .fs changes — minimal (apps are Dark)

| File (on `main`) | Change |
|---|---|
| `Builtins.Cli` | **Spawn exists** — `posixSpawnAndWait (program) (args) : Result<(exit*stdout*stderr)>` (prework-verified, [tailscale.md](../pre-s-and-s/tailscale.md)). The gap: **no stdin parameter**. Either add a stdin-bytes arg (the one possible new builtin) or pipe via temp files between stages (no builtin). Decide at build time. |
| `LibExecution/RuntimeTypes.fs` | **No change** — `AppManifest` is an ordinary Dark value, not a runtime type. PT untouched. |
| — | Everything else is Dark: the App, the `dark apps` surface, the alias. |

## .dark changes — the bulk

- **`Darklang.Stachu.PrintMd`** — the App (manifest value + `main` + a thin render wrapper).
- **`dark apps` command surface** (apps-surface.md): `install` (register a shell alias →
  the entrypoint fn + grant declared caps), `list` (projection of installed manifests), `run`,
  `remove`. The alias: `install` writes a tiny shell shim on `PATH` (`print-md` → `dark run
  Darklang.Stachu.PrintMd.main "$@"`) so typing `print-md` routes through Dark.

## SQL/schema — none

The App's items are ordinary package ops (already synced). The **alias** and the **cap grant**
are per-instance *settings* (a serialized Dark value in `.darklang/settings`, per the keystone)
— not SQL, not synced.

## The end-to-end walkthrough (the goal, made concrete)

```
# on the desktop (the hub)
$ dark apps install print-md
  print-md needs: FileSystem(read), Cli(spawn: weasyprint, lp)
  grant + alias 'print-md'? [Y/n] y
✓ installed · alias 'print-md' → Darklang.Stachu.PrintMd.main

$ print-md report.md
✓ rendered report.md → out.pdf · sent to printer (2-sided)

# edit the App's fn on the desktop — ordinary ops, autosync pushes them
$ dark edit Darklang.Stachu.PrintMd.main   # tweak the CSS/render
✓ committed · synced to tailnet

# on the laptop (already on the tailnet, autosync pulling)
$ dark apps                       # the manifest synced in as ops
NAME      KIND  STATUS
print-md  app   installed-elsewhere   (run `dark apps install print-md` to alias locally)
$ dark apps install print-md      # alias locally (caps are a local grant)
$ print-md notes.md               # runs the SAME fn the desktop edited
✓ rendered · printed
```

The edit on the desktop reaches the laptop because the App's fn is just package ops on the
synced stream (efforts 3/7/9). Install is per-instance (the alias + cap grant are local
settings), so each machine opts in — exactly the capabilities + apps-surface design.

## Test plan

| Step | Test | Done-signal |
|---|---|---|
| App runs locally | `.dark` (`PrintMd.tests`): `main ["fixture.md"]` with spawn mocked | returns 0; render called with the md |
| install aliases | `.fs`/`.dark`: `apps install print-md` then invoke the alias | alias routes to the entrypoint fn |
| caps enforced | run without the grant | blocked: "print-md needs Cli(spawn: weasyprint)" (capabilities.md) |
| cross-machine | author the fn on A, autosync, `dark apps` on B shows it; install + run on B | B runs A's edited fn |

## CLI impact

New: `dark apps install|list|run|remove` + the generated `print-md` shell alias. This is the
apps-surface; `print-md` itself becomes a first-class shell command routed through Dark.

## UX change

The headline change of the whole effort: **a script you edit on one machine, that just works on
all your machines.** Before: `print-md` is a bash file you'd `scp` around. After: it's a Dark
App that syncs itself — edit once, runs everywhere on the tailnet.

## Risks / problems not yet raised

- **The alias mechanism.** A generated shell shim on `PATH` is simple but per-shell (bash/zsh/fish
  differ) and needs a clean uninstall. Alternative: a single `dark` dispatcher + a `~/.darklang/bin`
  on `PATH`. Pick one.
- **Subprocess fidelity / stdin.** The bash original pipes md→pandoc→weasyprint on stdin; the
  prework-confirmed `posixSpawnAndWait` has **no stdin parameter** (verified). Match the pipe
  exactly (exit codes, stderr) by either extending the spawn builtin with a stdin-bytes arg, or
  staging through temp files (`tmp.html`, `out.pdf`). Temp-file staging needs no new builtin and
  is the lower-risk first cut. This is the one place a new/extended builtin may be needed.
- **Install-elsewhere UX.** A synced-in App shows as available but not locally aliased/granted —
  the two-step (sync brings the code, install grants+aliases locally) must be obvious, not
  surprising.

## Above / below

- **Below:** the entire floor (3/5/7/9) + apps-surface + capabilities. This PR is pure
  integration.
- **Above:** nothing — this *is* the goal. `dark apps fork` (Ocean forking print-md) is the
  later-bucket follow-on.
