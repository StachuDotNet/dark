# Working on Dark when the packages live in a store

How to add a builtin and use it from Dark, reference a new package type or function from F#, try
it locally, and get it to everybody else. The one-line version: **the store is the source of
truth for Dark code, git is the source of truth for F#, and a git branch carries the package work
its F# depends on so the two merge as one thing.** `dark docs packages` is the short version.

## Where a build's packages come from

`package-set.txt` at the root says which:

    commit unset     built from `packages/` on disk, by reloading it (today)
    commit <hash>    fetched as a seed from a package server, at that commit

`scripts/build/prepare-package-set` is the one place that answers it, CI goes through it, and it
checks that the kernel and the package set agree before the build continues. Everything below
works in both modes except where it says otherwise.

## Adding a builtin and using it from Dark

A builtin is F#; the Dark that calls it is a package item. One change, one PR.

1. Add the fn to the `fns` list in the right `Builtins/Libs/<module>.fs` (`AGENTS.md` says how to
   pick the subproject).
2. Wrap it once in a Dark package fn. `tests/builtin` enforces exactly one wrapper and at least
   one caller.
3. `scripts/dev/build`. It ends with the check that matters here:

       All 206 kernel refs resolve, and all 735 builtins this package set calls
       exist in this kernel.

**Removing or renaming a builtin is two steps, and the check enforces the order.** Land the
package change that stops calling it, move the pin, then remove the builtin. The other way round
fails the build naming the builtin. Adding one is safe in a single step.

## Referencing a new package type or function from F#

The one case that needs a branch. F# names package items through `PackageRefs`, which resolve
from the store by name, with the hash in `package-ref-hashes.txt` as a fallback and a shape check
in between: a binding whose signature (fn) or declaration (type) does not match what this build
was compiled against is refused, loudly, and the pinned version used. A type nobody has pinned
yet, one your branch just authored, has nothing to compare against, so the store's answer is
taken.

    git checkout -b add-foo
    dark branch add-foo                      # same name; they travel together
    dark type /Darklang.LanguageTools.Foo '{ n: Int64 }'
    #   ...add `let foo = p [] "Foo"` in PackageRefs.fs and use it...
    scripts/dev/build                        # compiles; imports the bundle if any; `refs check`
    scripts/packages/bundle export           # ~1KB of JSON: package-branch.json
    git commit -a                            # the F# and the bundle

Export is explicit, like `git add`. Author the type before the F# that uses it runs: a ref is a
lazy closure, and the first code path that reaches an unresolvable one raises. The package
reload keeps your dark branch; only main ops you authored or pulled are replaced, and it says how
many.

**Your coworker** checks the branch out and runs `scripts/dev/build`, which imports the bundle
(idempotent) and checks the refs. On the wrong dark branch the build says so, completely:

    1 kernel ref(s) do not resolve against this package set:
      type Darklang.LanguageTools.Foo
    git is on `add-foo` and there is a dark branch called `add-foo`,
    but you are on dark main. Try `dark switch add-foo`.

**Reviewing it:** `scripts/packages/bundle show --source` renders what the bundle changes by
importing into a throwaway store, so review does not mean taking a stranger's ops into yours.

## Trying it locally

Your store is yours: no restrictions on what you author or rebind on your own machine,
`Darklang.*` included. A second store is enough for the two-machine shape (`dark branch export
add-foo f.json` from one, `dark branch import f.json` into the other). For a real server:

    DARK_MATTER_WRITE_SECRET=<secret> dark serve Darklang.Matter.router --port 9090

`scripts/testing/gates seed-serving` and `gates server-folds` drive the whole thing end to end.

## Getting it to everybody else

    dark push                 your own namespace, straight to the server's main
    dark branch push <name>   anything else, including `Darklang.*`

Your namespace is yours: log in, write a function, push it. `Darklang.*` is reviewed, and the
server refuses a direct push that binds it into main (403, naming the item, saying to push a
branch); a branch push is always accepted, and review is what moves it to main, so an abandoned
branch costs nothing. The server folds what you push, so it shows in `/m`, `/p` and seeds; it
never runs it. A pushed `val` is evaluated only on the machine that fetches it.

## The pin

    scripts/packages/pin head --url <server>    # pin to what it has now
    scripts/packages/pin --show | --unset

Checked before it is written, so a bad commit is refused at pin time. Re-pinning regenerates
`package-ref-hashes.txt`, and only re-pinning does: ordinary package work leaves the file alone,
and a pin bump is one reviewable diff of every kernel identity that moved. A pinned seed is
cached per machine under `~/.darklang/seeds`.

## When the op-log format changes

    dark store                 format, which commit it was cut at, which build cut it
    dark store upgrade         move the op log to this build's format (copies first)
    dark store rollback <n>    put back the copy `upgrade` took

`upgrade` only rewrites blobs; a change that moves content hashes is refused rather than half
done. A no-op until the first real format bump. A build older than the store's format says so and
names the file to move back.

## Things that will bite

- `dark log` on a branch shows the branch's ops, not main's commits: `dark --branch main log`.
- A bundle left behind after its branch merges is how somebody imports work from weeks ago;
  going back to dark main removes it.
- Your main can drift ahead of the pin if you `dark pull`; then your bundle may not be enough for
  anyone else. `bundle export` warns.
- Two branches adding the same name with different shapes both merge and the check catches it at
  the second merge, so main goes red rather than silently wrong; it is a conflict git cannot see.
