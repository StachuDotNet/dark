# Editing and refactor

The agent's own edit-rebuild-recheck loop — changing code it already wrote. The lens: today every change is re-expressed as a full **op** (resend the whole body, re-author the prior version, hand-edit each caller), even when the intent is a small structured delta. These items push the surface toward expressing intent directly and toward cheap **projections** of "what have I touched."

(Adjacent: the "read-before-overwrite" guard and parse-error feedback live in diagnostics-and-errors.md; sticky branch context lives in cli-ergonomics.md; reviewer-facing change views live in agent-workflow.md.)

## `dark edit` — AST-aware function editing

**Issue**: `dark fn Foo.bar = …` requires resending the full body even for a one-line change. A fifty-line function with one bug costs roughly fifty lines of output every iteration. The naive fix is a Claude-Code-style text replace (`--replace <old> --with <new>`), but that is the wrong shape for Dark: Dark is AST-centric and image-based, so there is no persisted text for "old text → new text" to bite on.

**Candidate fix**: design the edit surface around AST nodes, not strings. Shapes to consider:

- `dark edit <fn> --rename-binding <old> <new>` — rename a `let` binding inside a fn body.
- `dark edit <fn> --replace-call <old-fn> <new-fn>` — swap a function-call node.
- `dark edit <fn> --map-leaves <pattern> <replacement>` — AST-aware pattern match.

Whole-body rewrite stays as the fallback when no structured operation fits. Pure CLI surface, no language change — but the API must respect the image-based model rather than pretending bodies are strings.

## `dark rename <old> <new>` with caller auto-update

**Issue**: there is a `branch rename` but no top-level rename for package items. An agent that picks the wrong name must view the bad name, `fn --update` it, grep for callers, and edit each one by hand — token-expensive and error-prone.

**Candidate fix**: `dark rename Old.Name New.Name` updates the package-tree item *and* every reference in one move, erroring if the new name conflicts; `--dry-run` previews the call-site list. The export/import machinery that already relocates items between trees is the natural base — rename is the within-tree variant.

## `dark uncommit` / `dark revert <commit>`

**Issue**: `dark discard` only unwinds uncommitted changes. Once an agent commits a bad change there is no equivalent of `git reset HEAD^` or `git revert`; the agent has to manually re-author the prior version, and sometimes abandons instead.

**Candidate fix**: two complementary commands. `dark uncommit` pops the most-recent commit on the current branch back into uncommitted state, preserving the work. `dark revert <commit>` creates a new commit that undoes a prior one. Together they cover "I just committed a typo" and "this old commit was wrong."

## `dark since <ref>` — session-scoped change view

**Issue**: agents iterating across many `fn` calls lose track of their own working set, especially in a large package tree. `dark log` shows commits and `dark status` shows uncommitted work, but neither answers "everything I've touched this session, regardless of commit boundary."

**Candidate fix**: `dark since <ref>` lists every package item created or modified since `<ref>` — defaulting to the session start recorded in `rundir/.dark-session-start` — with a `view` snippet per item. Same underlying machinery as the reviewer-facing `dark review` in agent-workflow.md; the only delta is agent-facing (session-scoped) versus human-facing (review-scoped).

## Surface `merge --dry-run` / `rebase --status` in the prompt

**Issue**: both commands already exist — `merge --dry-run` checks whether a merge is clean without doing it, `rebase --status` checks for conflicts without rebasing — but agents don't reach for them, so cross-branch operations stall or get abandoned. This is a strength to surface, not a gap to build.

**Candidate fix**: a prompt-template line that mentions both commands when the agent is about to merge or pre-flight a branch move. Zero Dark-code change; bundles into a prompt-only wave alongside the gen-test promotion in traces-and-debugging.md.
