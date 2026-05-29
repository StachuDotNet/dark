# Publishing and sharing

The most user-visible gap on the list. The promise: an agent builds a thing in Dark, you immediately share it with a friend, and the friend runs it on their own machine with no setup. What exists today is a seed-extraction (`export-seed`) that produces a minimal `seed.db`, not a runnable app. The lens here is the missing **projection from package-tree state to a self-contained artifact** — the runtime is already a single launcher plus dlls plus DB; nothing packages the project's closure into one redistributable.

## `dark publish <project> --out <path>` — the headline missing tool

**Issue**: an agent finishes a project and the human can't ship it, because no command turns package-tree state into something a friend can run. Without this, the whole friend-can-run promise is aspirational.

**Candidate fix**: `dark publish Darklang.MyProject --out ./myapp` produces a directory (or zip) containing the Cli launcher, only the dlls actually referenced by the project's transitive package-tree closure, a stripped `data.db` holding only the project's package items, and a thin `myapp` shell that sets `DARK_RUNDIR` and execs the launcher at the project's main fn. The friend runs `./myapp serve` or `./myapp run main` — no devcontainer. Stretch: a `--single-file` mode that self-extracts to `/tmp` on first run (Go-binary style), trading slightly slower cold-start for removing the directory-of-stuff wart.

## Reproducible builds

**Issue**: if `dark publish` reads any filesystem state outside the package tree, the friend gets a slightly different artifact than the author tested, and sharing silently breaks.

**Candidate fix**: `dark publish` reads exclusively from the named project's transitive closure in the package tree. Hardened by a CI gate that re-runs `publish` on a fresh clone and byte-compares the artifact; a mismatch points straight at a hidden filesystem dependency. This is a constraint on `dark publish`, not a separate command — `publish` lands first, the byte-equal gate hardens it second.

## `dark export` / `dark import` — run-without-publish

**Issue**: the simplest share is "the friend already has Dark and runs the same code." Dark's package-tree-first model supports this, but there's no canonical "export this project as one thing / import it" pair.

**Candidate fix**: `dark export <project> > project.darkpack` and `dark import < project.darkpack`, building on the existing seed export/import machinery (`LibDB.Seed.export` and the `pmSeedExport` builtin — verified to exist; note there is *no* `traces export`/`import` to reuse, despite an earlier assumption). Smaller than a published binary. Complementary to `dark publish`, not a replacement: `publish` is for "friend has nothing," `export`/`import` is for "friend has Dark." Both should ship.

## `dark publish --target wasm` — browser distribution (deferred)

**Issue**: the easiest path to "share with a friend on their phone" is a URL.

**Candidate fix**: a WASM target that compiles the project plus a slim runtime to a single `.wasm` and HTML harness, so the friend opens a URL and the code runs in their browser sandbox. Out of scope for now — a stretch goal listed for completeness so the `--target` flag stub has a home in the `dark publish` design today.
