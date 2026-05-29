# Loop operations — reusable machinery insight

A set of frozen docs spec'd the machinery for running an overnight cross-language
coding bench (queues, orchestration, cadence, dashboards, launch). Almost all of
it is ephemeral run-state for one specific night and does not belong in a design
tree. What follows is only the process insight worth carrying forward — how to
shape an overnight loop that produces real numbers by morning — plus the real
product blockers the bench surfaced.

## How to structure an overnight loop queue

The durable shape is a per-task lifecycle with a small, fixed set of phases
(plan, implement, verify, reflect, extract, tear down) and a single source of
truth for state on disk. The lessons that generalize:

- **State lives in one file the scheduler rewrites after every transition.** That
  file is the recovery point. A crash, a Ctrl+C, or a 3am reboot then costs
  nothing — the loop re-reads state on restart, skips terminal tasks, re-polls
  in-flight ones, and continues. Anyone who wakes up expecting results should get
  them even if the machine bounced overnight.
- **Make writes atomic (temp file plus rename).** A crash mid-write must not
  corrupt the one file the whole run depends on.
- **Lock the run so two instances can't fight.** A per-run lock file (with a
  stale-lock takeover when the holding process is dead) prevents a manually
  triggered run and a scheduled one from corrupting shared queue state. Use
  parseable, prefixed task IDs so the loop can filter its own tasks from any
  unrelated work sharing the same queue, and never touch tasks it didn't create.
- **Throttle by concurrency, not by a hard budget — at least while calibrating.**
  On a first run you don't yet know per-task cost or time. Concurrency times
  wall-time naturally bounds the run; a hard cap on calibration night just
  forfeits the data you need to set the cap properly. Compute medians from the
  first run, then use a soft multiple of those as a warning (not a kill) on later
  runs. Keep cost visible as a quality signal even when it isn't a blocker.
- **Separate "the agent stopped" from "the work is correct."** Conflating the two
  is the classic self-grading trap: agents skew positive when they judge their
  own output. The orchestrator only knows the agent halted; an independent
  verifier decides pass or fail, and the recorded result keeps both facts
  distinct.
- **Tear down the workspace but keep the evidence.** Purge the built artifact
  between attempts so nothing peeks at a prior attempt, but retain transcripts,
  metrics, and the agent's own reflection. The reflection is valuable even when
  partial — capture it on cut-off too.

## Reuse existing orchestration rather than building fresh

When something already handles the agent loop — queueing, rate-limit backoff,
turn budgets, status tracking, session summaries — extend it in place rather
than fork or rebuild. The durable principle is the clean line: **one layer
orchestrates the agent loop; a separate layer evaluates the output.** Keep the
eval layer's concerns (specs, rubrics, metric extraction, comparison,
dashboards, pricing) entirely out of the orchestrator. Key the eval metadata off
the orchestrator's task ID and store it separately. The orchestrator's job
stops at "the agent ran."

## Cadence lessons

- **Match the schedule to actual usage, not to a tidy cron.** A nightly-cron
  assumption was wrong once usage turned out to be manually triggered every day
  or two, plus a slow-moving cross-language baseline. Calibrating per-run
  medians on a fixed "first N nights" basis is the wrong shape when runs don't
  fire nightly. Let the human kick off the fast-moving series on demand; reserve
  the schedule for the parts that genuinely recur.
- **Gate the expensive run on the cheap one.** Run a small canary set first; if
  it fails (auth, startup flake, broken pipeline), skip the full run that night.
  No point burning a full run's budget on a pipeline that's already broken.
- **Cache the slow-moving comparison series and invalidate on real change.**
  Reference implementations in other languages don't change between runs, so
  carry their results forward and only re-run when something that actually
  affects them changes (the spec, the pricing model, the runtime pin, the prompt
  template, or a staleness deadline). This keeps a nightly run cheap without
  losing the comparison.
- **Have a per-attempt cut-off so one stuck task can't drain the whole run.**
  Whichever fires first of a turn cap or a wall-clock cap ends the attempt; the
  agent gets a chance to wrap up rather than a hard kill. A task that hits
  cut-off the same way several runs running should be auto-demoted to "skip on
  future runs," with an explicit override to re-test after a fix ships.

## Reproducibility: the prompt is part of the measurement

Any change to what the agent is sent — the system prompt, the task template, or
sampling knobs like temperature and turn count — starts a new measurement
series. Hash the full prompt-plus-settings bundle into the run identifier so
results from different prompts are never silently compared. Practical defaults
for an eval (as opposed to an interactive assistant): temperature at zero for
determinism, generous output-token ceiling so complex tasks aren't truncated,
and a turn cap high enough to cover the largest task as a runaway detector
rather than a real budget.

What to put in the prompt and what to leave out also generalizes: point the
agent at the tools that objectively exist in its environment, but don't prescribe
*how* to think (no "plan first, then code"), don't show example solutions, don't
name the rubric's checks, and don't tell it to be terse or verbose when you're
measuring those very properties. The rule of thumb: an instruction is fair only
if it's true regardless of the approach the agent takes.

A cheap, high-leverage move worth noting: surfacing a tool tip in the *retry*
prompt (e.g. pointing the agent at trace/replay tooling after a failure) can
improve the fix-on-second-attempt rate with no code change at all — and is itself
worth A/B-testing.

## End-detection without scraping the transcript

Have the agent emit a small, unambiguous done-signal (an XML-style phase tag is
less false-trigger-prone than a magic string) and have the orchestrator write
the resolved phase to a single-line state file. Poll that file rather than
regex-matching the live transcript. If the orchestrator already parses these
tags, the eval layer inherits the parsing for free and reads one source of
truth. Distinguish harness flakes (auth error, process crash) from genuine agent
failures so a broken environment doesn't get scored as the agent's fault.

## Auth: prefer the flat-rate subscription, track cost as a proxy

Running on a flat-rate subscription (host-side OAuth) instead of a metered API
key makes the marginal cost of a high-volume nightly run effectively zero. Still
measure tokens and convert them to API-equivalent dollars for a comparable,
shareable cost figure — that number is the quality-and-quota proxy, not real
spend. The genuine risk to track is subscription *quota* exhaustion: a heavy
night can eat into the human's daily working quota, so warn when a single run
consumes a large fraction of it. Do not export an API key for the run; that
silently switches to metered billing.

## What a results dashboard should show

Two artifacts, both static files regenerable with one command, both shareable by
copy-paste / file-copy — no server, no JS framework, no hosting story:

1. **A per-run report** — what just happened: headline metrics, a per-task
   pass/fail table, a short diagnostics list, and a "what changed since last
   run" delta. This is the morning-read artifact.
2. **An over-time view** — are we improving, with the comparison languages as
   reference lines.

Design rules that generalize:

- **Show the absence of data as "not run," not as zero.** A column for a series
  that hasn't run yet should render blank, never a misleading zero.
- **Don't fake synchrony between series that run on different cadences.** When a
  fast-moving series is compared against a slow-moving baseline, stamp each cell
  with the timestamp of the data behind it and state plainly that the comparison
  uses the latest of each. A naive join on "this period's runs" produces empty
  cells and makes it look like something didn't run when it did.
- **Group results by prompt/version hash; label regime changes.** Never plot
  across a prompt change as if it were one continuous series.
- **Numbers have to be real, not pretty.** Polishing report cosmetics before the
  numbers are trustworthy is overcooking. The definition of done is that a
  colleague can read it without explanation.
- **Link to per-run detail; don't render it.** Drill-down lives on disk by path;
  the dashboard points at it.

## Real product blockers surfaced

The blockers doc named genuine Dark-product issues (as opposed to setup chores
like configuring a fork, seeding a pricing file, or checking auth — those are
operational, not product). The one real product blocker worth carrying forward:

- **Non-TTY startup flakiness in `dark serve`.** Started from a non-interactive
  shell (the only way a background harness can launch it), the server binds its
  port on only a fraction of attempts; the rest hang with an empty log and no
  bound port. This blocks any HTTP-shaped task run unattended. The probed
  workaround is a port-poll readiness check with retries, but the real fix is a
  readiness signal from the server itself — a `--ready-fd` flag (or equivalent)
  that writes a byte once the listener is bound, so a caller can wait
  deterministically instead of polling. Small change, removes a whole class of
  flake. Worth tracking as a product issue independent of any bench.

A softer product-adjacent signal also recurred across these docs and is worth a
design note: an unattended agent loop wants every CLI command to behave well
non-interactively — terse plaintext or structured output, no banners or ANSI
when there's no TTY, and a deterministic done-signal. That "behaves well headless"
property is itself a Dark-as-AI-target design requirement, not just bench
plumbing.

## Triage

- **queue-mechanism.md** — KEEP-FOLDED: the resumable-state, atomic-write,
  per-run-lock, concurrency-as-throttle, and keep-evidence-on-teardown lessons.
  Dropped the specific phase enum, the JSON schema, and tonight's scheduler
  config as ephemeral.
- **multi-orchestration.md** — KEEP-FOLDED: the "extend existing orchestration,
  one layer runs the loop / another evaluates" principle and the clean-line
  separation. Dropped the tool-specific field mappings and subcommand surface.
- **nightly-cadence.md** — KEEP-FOLDED: match-cadence-to-usage, gate-expensive-on-
  cheap, cache-the-slow-series, and per-attempt cut-off with auto-demotion.
  Dropped the cron minutes, dollar caps, and annualized projections as ephemeral.
- **tonights-queue.md** — DROP: ephemeral run-state (one night's specific project
  list and ordering). No reusable insight not already covered elsewhere.
- **launch-checklist.md** — DROP: ephemeral run-state (one night's timed,
  copy-paste runbook). The operational chores it lists are setup, not product.
- **dashboard-spec.md** — KEEP-FOLDED: the two-artifact model, absence-as-not-run,
  don't-fake-cross-cadence-synchrony, group-by-prompt-hash, real-not-pretty, and
  link-don't-render rules. Dropped the file layout and matplotlib/Jinja choice.
- **prompt-template.md** — KEEP-FOLDED: prompt-is-part-of-the-measurement,
  fair-instruction rule, eval-vs-interactive sampling defaults, retry-tip
  leverage, phase-tag done-detection, and subscription-auth-with-cost-as-proxy.
  Dropped the literal template text.
- **feedback-plan.md** — DROP: a checked-off iteration log for one round of the
  loop. Loop-process lessons it embodies are already captured in the existing
  what-the-loop-is-good-at / where-it-struggles / process-risks notes.
- **for-feriel.md** — DROP: a coworker handoff note; belongs in the vault, not a
  design tree.
- **blockers.md** — KEEP-FOLDED: the one real product blocker (`dark serve`
  non-TTY flakiness and the `--ready-fd` fix). Dropped the operational
  preconditions (fork config, pricing seed, auth check) as setup, not product.
- **README.md** — DROP: an index/exec-summary for the frozen doc set itself;
  superseded by this consolidation.
