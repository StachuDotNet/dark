# Diagnostics and errors

How the CLI tells the agent something is wrong, and how early. The lens: the runtime already *knows* the things an agent needs — the likely-fix for a parse slip, which callers stopped typechecking, the nearest real name to a miss — but it doesn't **project** that knowledge into the error or response. Each item turns a dead-end message into an actionable one, usually one turn earlier than today.

(The empty-`search` half of "did you mean" is in discovery-and-search.md; the runtime-error half is below.)

## Parse errors carry suggested fixes + `docs syntax` pointer

**Issue**: parse errors are message-only. The agent sees `unexpected ','` with no hint that lists use `;` not `,` — a known Dark gotcha that today produces an opaque hundred-line stack trace with no separator pointer.

**Candidate fix**: parse errors include a three-line excerpt with the offending position underlined, the most-likely-fix heuristic ("did you mean `;`?"), and a literal `→ See: docs syntax` pointer. The heuristic table starts small (comma-in-list → `;`, `@`-in-string → `++`, `let` inside `let` → no nested fns) — six to eight entries, grown over time. Cuts attempts-before-first-successful-fn and first-parse-success failures.

## Auto-emit diagnostics after every write

**Issue**: after `dark fn` parses successfully, the agent doesn't learn that three callers no longer typecheck until it explicitly asks — wasting a turn per write. Anthropic's own Claude Code guidance recommends giving the model automatic error detection after edits; Dark has the dependency graph to do exactly that and doesn't surface it.

**Candidate fix**: after every successful `fn` / `type` / `val`, append a diagnostics block to stdout listing any new parse or type errors anywhere in the package, the callers of the changed name (from `deps`) and whether each still typechecks, and a one-line affected-scope summary. Behind a `--quiet` flag for human callers so it doesn't spam the REPL. Agents catch self-inflicted breakage one turn earlier; rework ratio drops.

## `dark fn` requires `--update` for existing names

**Issue**: an agent can silently overwrite an existing function it never read first. This is hard to detect from outside and corrupts the gold-reference invariant if it leaks into an eval run — a clobber that produces no error at the moment it happens.

**Candidate fix**: if the fully-qualified name already exists, `dark fn` errors with guidance baked into the message — `name 'X' already exists; use \`view X\` to inspect, then \`fn --update X …\` to replace`. The view-then-update discipline is enforced by the error string itself, so no agent-side prompt change is needed. Expect a brief rise in command-exec failures as agents hit the new guard, then a drop as the convention solidifies and clobbering bugs disappear.

## "Did you mean" on a runtime not-found

**Issue**: a reference to a name that doesn't exist — say `Stdlib.NoSuchModule.foo` — errors with a bare `not found` and no candidate, a loud "agent gives up and re-implements" trigger.

**Candidate fix**: suffix "X not found" runtime errors with the top three fuzzy matches (trigram or Levenshtein over the package tree, refreshed on commit). This is the runtime-error sibling of the empty-`search` handling in discovery-and-search.md — same cheap fuzzy index, applied at the error surface instead of the search surface.
