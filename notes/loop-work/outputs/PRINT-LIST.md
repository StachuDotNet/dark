# Print list — final re-review (section 11)

The curated set to `print-md` at the end of the wave, in reading order. **Not yet
printed** — printing is the single final step, run only when the wave is done
(~08:00 or on your return).

Deliberately **excludes the 125 mechanical project specs** in `projects/` — that
would be a paper flood, and they read fine on screen. Print `projects/README.md`
(the category index) instead; pull individual specs to paper on demand. Everything
below is the prose/design content actually worth reviewing on paper (~40 files).

## Orientation (read first)

1. `README.md` — how to review this tree; structure
2. `STATUS.md` — per-pass log of what changed
3. `feedback-coverage.md` — every feedback.md point → status + home
4. `open-decisions.md` — every unsettled design decision, triaged by leverage
5. `grounding-against-main.md` — design claims checked against the live codebase (ok/fixed/flag)
6. `vault-organization.md` — section 10 recommendation (vault is off-limits to the loop)

## design/ (in thematic / priority order)

5. `design/README.md` — the design index
6. `design/distributed-event-sourcing.md` — **keystone**
7. `design/apps-surface.md` — `dark apps` (north-star surface)
8. `design/event-bus.md`
9. `design/conflicts.md`
10. `design/sync.md`
11. `design/async.md`
12. `design/cli-daemon.md`
13. `design/hot-reload.md`
14. `design/cohabitation.md`
15. `design/remote-access.md`
16. `design/package-system-layers.md`
17. `design/bootstrap.md` — removing `.dark` files (punted)
18. `design/pdd.md`
19. `design/algorithm.md`
20. `design/claims.md`
21. `design/pdd-elevator-pitches.md`
22. `design/capabilities.md`
23. `design/identity.md`
24. `design/composable-mvu.md`
25. `design/view-sketches.md`
26. `design/structural-editor.md`
27. `design/dark-virtual-files.md`
28. `design/research/beam-vs-dark.md`
29. `design/research/swamp-vs-dark.md`
30. `design/research/visibility-vs-dark.md`

## issues-and-improvements/

31. `issues-and-improvements/README.md`
32-38. the 7 category files (`agent-workflow`, `cli-ergonomics`, `diagnostics-and-errors`,
   `discovery-and-search`, `editing-and-refactor`, `publishing-and-sharing`, `traces-and-debugging`)

## meta-reflections/ and results/

39. `meta-reflections/README.md` + its 4 theme files
40. `results/README.md` + `results/benchmark-targets.md`

## projects/ (index only — not the 125 specs)

41. `projects/README.md` (category index)
42. `projects/_cross-cutting-test-criteria.md`

## Capstone

- `next-steps.md` — the thin successor to READY-WORK (themes A/B killed, bootstrap
  punted): the priority-ordered Stable & Syncing path to the north star. **Written;
  on the list.** May get a light final refresh at print time if anything shifted.

## Print command (for the end)

From `notes/loop-work/outputs/`, e.g.:

```
print-md README.md STATUS.md feedback-coverage.md vault-organization.md \
  design/README.md design/distributed-event-sourcing.md design/apps-surface.md \
  design/event-bus.md design/conflicts.md design/sync.md design/async.md \
  design/cli-daemon.md design/hot-reload.md design/cohabitation.md design/remote-access.md \
  design/package-system-layers.md design/bootstrap.md design/pdd.md design/algorithm.md \
  design/claims.md design/pdd-elevator-pitches.md design/capabilities.md design/identity.md \
  design/composable-mvu.md design/view-sketches.md design/structural-editor.md \
  design/dark-virtual-files.md design/research/*.md \
  issues-and-improvements/*.md meta-reflections/*.md results/*.md \
  projects/README.md projects/_cross-cutting-test-criteria.md next-steps.md
```
