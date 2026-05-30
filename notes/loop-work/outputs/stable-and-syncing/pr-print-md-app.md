# PR: print-md as an App (the capstone)

The spine's floor effort 10 — the **north star**. Not new substrate; the *integration* that
shows every floor piece composing into the actual goal: print-md lives in Dark, edits sync
across the tailnet, and `print-md file.md` runs it. Built from
[apps-surface.md](../pre-s-and-s/apps-surface.md) + [capabilities.md](../pre-s-and-s/capabilities.md)
+ the sync chain ([sync.md](sync.md)).

**Goal.** `print-md` is a Dark App (an entrypoint fn + declared caps). `dark apps install
print-md` makes `print-md file.md` runnable from the shell. An edit to its fn on the desktop
syncs to the laptop. Today's `print-md` is bash (read → Pandoc → WeasyPrint → `lp`); the Dark
App does the same via builtins + two subprocess spawns.

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
             cli = Some { spawn = ["weasyprint"; "lp"] }        // render + print
             httpClient = None; httpServer = None; random = false; time = false } }
```

The entrypoint, reusing the outliner's `markdown.dark` for render:

```dark
let main (args: List<String>) : Int64 =
  let file = args |> List.head |> unwrap
  let md   = Stdlib.File.read file                 // fs-read cap
  let html = Darklang.Markdown.toHtml md           // reuse outliner/markdown.dark
  let pdf  = Cli.spawn "weasyprint" ["-"; "out.pdf"] html   // spawn cap
  Cli.spawn "lp" ["-o"; "sides=two-sided-long-edge"; "out.pdf"] ""   // spawn cap
  0L
```

## .fs changes — minimal (apps are Dark)

| File (on `main`) | Change |
|---|---|
| `Builtins.Cli` (`CliHost`) | Confirm a `spawn`/subprocess builtin exists with stdin piping + captured stdout (weasyprint reads stdin). If not, add it — the one possible new builtin. |
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
- **Subprocess fidelity.** WeasyPrint reading stdin + writing a file via the `spawn` builtin must
  match the bash pipe exactly (exit codes, stderr). The one place a new/extended builtin may be
  needed.
- **Install-elsewhere UX.** A synced-in App shows as available but not locally aliased/granted —
  the two-step (sync brings the code, install grants+aliases locally) must be obvious, not
  surprising.

## Above / below

- **Below:** the entire floor (3/5/7/9) + apps-surface + capabilities. This PR is pure
  integration.
- **Above:** nothing — this *is* the goal. `dark apps fork` (Ocean forking print-md) is the
  later-bucket follow-on.
