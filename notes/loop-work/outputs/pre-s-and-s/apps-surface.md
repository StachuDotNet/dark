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

## Open questions (in this doc only)

- **App declaration.** Is "this set of package items is an app named X with these caps"
  an explicit op (an `AppManifest` op) or a derived projection from a module convention?
  *Lean: an explicit op, so the declaration is first-class.*
- **Install = trust.** Installing grants the app's declared caps. `dark apps install`
  should show a cap summary and confirm — no interactive grants mid-run (per
  [capabilities.md](capabilities.md)).
