# The apps surface (`dark apps`)

The surface a user actually touches. The north-star litmus for the whole effort:

> Stachu's `print-md` script lives in Dark. He inspects it, changes it, those changes
> **sync** to his other machines, Ocean can **fork** it, and it shows up under
> **`dark apps`** as an installed app.

(Sync and fork are the *vision*; this doc specs the local surface — install, list, run.
The cross-machine and fork parts are the S&S and later layers built on top of it.)

## What an "app" is

An app is nothing new — it is an [`App` value](distributed-event-sourcing.md) plus the
package items it is made of. `print-md` is an App whose state is the script's package
items and whose `views` render its CLI surface. Because an app is just an App over an
op stream, there is no separate "app package format."

## Install is boring: an alias to a Dark fn

**`dark apps install` should be simple and boring.** Installing an app basically
**creates an alias** that points at calling some Dark fn — so that typing `print-md`
at the shell runs that fn through Dark. Nothing more clever than that for now: register
the name, point it at the entrypoint, grant the app's declared capabilities
([capabilities.md](capabilities.md)).

## The `dark apps` command surface

Deliberately small — most of it is a projection of installed `App` values:

```
dark apps                 # list installed apps (a projection: name, entrypoint, running?)
dark apps install <ref>   # register an alias to the app's entrypoint fn + grant its caps
dark apps <name>          # inspect: source, views, declared caps
dark apps run <name>      # ask the daemon to host it (or it auto-starts on use)
dark apps remove <name>   # drop the alias (local only)
```

`dark apps` (no args) is a **projection**, not a table to maintain. `dark apps fork` is
**punted to the later bucket** — forking an app (Ocean's branch + data migration) is the
vision, not near-term surface.

## The print-md walkthrough (the near-term target, local)

1. **Stored in Dark.** `print-md` is authored as package items and declared an app
   (its entrypoint fn + the caps it needs: filesystem read, process-spawn for the
   printer). Authoring is ordinary ops.
2. **Inspect + change.** `dark apps print-md` shows its source; editing a fn appends
   ops. No separate "app build step."
3. **Installed = aliased.** `dark apps install print-md` makes `print-md` runnable from
   the shell, routed through Dark.
4. **Run + listed.** `dark apps run print-md` (or just invoking the alias) has the
   daemon host it; `dark apps` lists it with running status.

The cross-machine part — the same edit showing up on the laptop — is the **sync layer**
(in the S&S bucket, which references this doc, not the other way round). Ocean forking it
is the **later** layer. Both are built *on* this surface.

## Install is a floor; everything else is an app

A fresh install is deliberately boring: **a CLI with a seed DB and a few capabilities.**
That's the floor. Everything else — including things that *feel* built-in — is an app you add
on top, each built on the ones below:

```
fresh install ─► boring CLI + seed DB + a few caps          (the floor)
      │ dark apps install sync
      ▼
   + sync app ─► dark apps install scm / pdd / outliner / print-md / my-website
      │
      ▼
   your environment = a composable, syncable stack of Dark apps
```

The point: **SCM, PDD, and the outliner aren't privileged subsystems — they're (or become)
Dark apps too**, no different in kind from `print-md` or a personal website, script, or
reMarkable tool. "Rebuild SCM as a Dark app" is the same move as installing any other. So
`dark apps` isn't a feature *of* the CLI — it's how you assemble the whole environment.

### Extensions: optional builtins (like DLLs)

Some apps need new **effectful primitives** (builtins). Those load **optionally, like DLLs**:
an *extension* is a bundle of builtins plus **its own capability structure**
([capabilities.md](capabilities.md)) and the **runners that respect its special types** (or
just bindings + mappings). The boring install ships a common core; an app that needs more
pulls in the extension that provides it. *Open:* where extensions live, what the reasonable
common set is, and whether these are better called "platforms." (Pure-Dark apps need no
extension — only ones reaching new effects do.)

## Open questions (in this doc only)

- **App declaration.** Is "this set of package items is an app named X with these caps"
  an explicit op (an `AppManifest` op) or a derived projection from a module convention?
  *Lean: an explicit op, so the declaration is first-class.*
- **Install = trust.** Installing grants the app's declared caps. `dark apps install`
  should show a cap summary and confirm — no interactive grants mid-run (per
  [capabilities.md](capabilities.md)).
