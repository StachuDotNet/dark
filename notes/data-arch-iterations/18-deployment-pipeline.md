# Iter 18 — Deployment & release pipeline

Iter 17 flagged this as a gap. The user-facing "commit → live"
flow is what most developers do every day; getting it right is
how Dark feels in practice.

This iter sketches what `dark deploy` looks like and why the
content-addressed-ops architecture makes it dramatically
simpler than Heroku / Vercel / k8s / Lambda. The unique
property: **deploys are config flips, not builds**, because
the code is already in the package store as content-addressed
ops.

## The mental model

| In other systems | In Dark |
|------------------|---------|
| Source code → build artifact → image → registry → instance | Code is already content-addressed ops in the package store |
| Deploy = upload + start + warmup | Deploy = update one config: "route to commit X" |
| Rollback = redeploy old image (slow if uncached) | Rollback = update config back |
| Canary = service mesh + traffic split + monitoring | Canary = same config update with split-percentage |
| Branch preview = parallel build + deploy | Branch preview = a different commit hash served at a different URL |

The architectural primitive: a "deploy target" (e.g.,
`prod`, `staging`, `pr-42`) maps to a commit hash. Routing
sends each request to the daemon serving that commit's
handler set. Updating the map = deploying.

## The day-to-day commands

### Deploy

```
$ dark deploy
Current branch: feat-search
Latest commit: 8a3f2dd ("add full-text search")

Available targets:
  staging.myapp.dark.run     currently  6b9c1ee  (3h ago)
  myapp.dark.run             currently  6b9c1ee  (3h ago)
  pr-43.myapp.dark.run       currently  9e1c8dd  (1h ago)  (auto-deployed)

Deploy 8a3f2dd to which target? [staging] _
✔ Syncing ops to dark.run (3 new ops)...     87ms
✔ Validating handler signatures...           12ms
✔ Running pre-deploy hooks (1 hook)...       234ms
✔ Routing 100% of staging to 8a3f2dd...      8ms

Live at https://staging.myapp.dark.run
Total: 341ms

Rollback: dark rollback staging
```

Total deploy time: <500ms. The ops are typically already on
dark.run (the user's local daemon synced them on commit). The
"deploy" is mostly: validate, run hooks, flip routing.

Compare: typical Heroku deploy is 30-90 seconds (most of
which is `npm install`, build, image upload).

### Auto-deploy

```
$ dark deploy --auto feat-foo --to staging
Auto-deploy enabled.
Every commit to feat-foo will deploy to staging within 30s of push.

$ dark deploy --auto main --to prod --require-approval
Auto-deploy enabled, requires approval before going live.
```

Per-branch, per-target. Configurable rules: tests must pass,
approvers required, time-window restrictions.

### Promote between environments

```
$ dark promote staging --to prod
Currently serving:
  staging  8a3f2dd  ("add full-text search")  live 30m
  prod     6b9c1ee  ("fix navbar")            live 4h

Differences (3 commits):
  + 8a3f2dd  add full-text search
  + 7c4a1ff  refactor query parsing
  + 6f2b9aa  fix tokenizer edge case

Tests on 8a3f2dd: ✔ 8421/8421 passed
Trace replay against last 24h prod traffic: ✔ no regressions

Promote 8a3f2dd to prod? [Y/n] y
✔ Routing 100% of prod to 8a3f2dd...
✔ Live at https://myapp.dark.run

Rollback: dark rollback prod
```

Promotion is the same primitive as deploy, just from a known-
good source. The "trace replay against last 24h" check is
the iter 13 trace-replay machinery: replay recent prod traces
against new code, fail if outputs differ for any. Catches
regressions before users see them.

### Canary

```
$ dark deploy 8a3f2dd --to prod --canary 10%
Canary deploying 10% of prod traffic to 8a3f2dd...
✔ Live. 90% on 6b9c1ee, 10% on 8a3f2dd.

Watch: dark watch prod
Promote canary to 100%: dark promote prod-canary --to prod
Abort canary: dark canary-abort prod
```

Canary is a deploy with a split-percentage. Traffic routing at
the daemon: each request gets a deterministic split (based on
session ID or random) that routes 10% to new code, 90% to old.

The split percentage can ramp:

```
$ dark canary ramp prod --schedule 10%/30m, 25%/30m, 50%/30m, 100%
Canary ramping schedule:
  Now:         10%
  In 30m:      25%
  In 1h:       50%
  In 1h 30m:  100%
Auto-rollback: error rate >5%, latency P99 >500ms
```

Auto-rollback triggers based on metrics from the trace stream.
Failure detection is sub-second; rollback is sub-second.

### Watch

```
$ dark watch prod
Live metrics for prod (8a3f2dd, canary 10%):

                  6b9c1ee (90%)        8a3f2dd (10%)
RPS               1240                 145
P50 latency       45ms                 47ms
P99 latency       320ms                380ms
Error rate        0.1%                 0.3%
Memory            234MB                256MB

Recent errors (last 1m):
  [8a3f2dd] handler /api/search — RuntimeError "search index missing"
            ↳ trace: dark trace show 4f2e8aa
  [6b9c1ee] handler /api/login  — Http.Unauthorized
            ↳ trace: dark trace show 9aa3bf0

[Q to quit, R to refresh, T to view top traces]
```

Inline trace links: every error in the watch view is a
clickable trace ID for replay. Per iter 13: replay locally,
edit-and-fix-live, ship.

### Rollback

```
$ dark rollback prod
Currently serving:
  prod  8a3f2dd  ("add full-text search")  live 23m

Recent prod commits:
  6b9c1ee  4h ago      "fix navbar"
  3a9b2cc  yesterday   "feat: search"
  1d8f4ee  2 days ago  "fix: auth bug"

Rollback to which? [6b9c1ee] _
✔ Routing 100% of prod to 6b9c1ee...           8ms
✔ Live at https://myapp.dark.run

Took 87ms. (6b9c1ee was already cached; no re-fetch needed.)

What changed? dark deploys list prod --recent
```

Rollback is just deploy in reverse. Same primitive.

The 87ms includes: load-balancer config update (8ms), validation
(12ms), and propagation to all daemon instances. Sub-second is
typical because:
- Old commit's bytes are already on dark.run.
- Switching = updating one config value.
- No "warmup" needed: daemon already has the code in memory.

### PR previews

Auto-spawn for every PR:

```
$ git push origin feat-search
✔ Push successful.
✔ Auto-deployed to https://pr-43.myapp.dark.run

$ dark previews list myapp
  pr-42.myapp.dark.run    fix-navbar     6f8a1aa  closed
  pr-43.myapp.dark.run    feat-search    8a3f2dd  open
  pr-44.myapp.dark.run    refactor-auth  9e1c8dd  open
```

Each PR commit triggers a deploy to its preview URL. Reviewers
visit the preview, see the change live, comment.

PR closed (merged or rejected) → preview torn down. ~5 minutes
of "still live" buffer for late reviewers, then resources
reclaimed.

## Environment configuration

Each deploy target has its own config:

```
$ dark config show prod
prod:
  domain:    myapp.dark.run, myapp.com
  TLS:       auto (Let's Encrypt)
  vars:
    DB_HOST:        prod-db.myapp.com (encrypted)
    SENDGRID_KEY:   sg_xxxxx (encrypted)
    LOG_LEVEL:      info
  autoscale: 1-10 replicas
  region:    us-east

$ dark config set prod DB_HOST prod-db.myapp.com
✔ Updated. Effective on next deploy or `dark config reload prod`.
```

Config values are encrypted (per iter 16) and stored in the
config stream. Handlers read via `Config.get "DB_HOST"` — pure
fn, returns the value at handler invocation time.

Hot reload without redeploy:

```
$ dark config reload prod
Reloading config for prod...
✔ Active config updated. New requests use new values.
```

## Migration ops

A commit that requires DB schema changes:

```dark
// In a migration module:
Migration.add "v8: add posts.tags column" {
  forward = fun () ->
    Stdlib.DB.runSql "ALTER TABLE posts ADD COLUMN tags TEXT"
  backward = fun () ->
    Stdlib.DB.runSql "ALTER TABLE posts DROP COLUMN tags"
}
```

On deploy, daemon runs pending migrations:
- Forward: applied as ops in the migration stream.
- Backward: re-applied during rollback.

If a migration is non-reversible (data destruction), `backward`
returns `Error "irreversible"`. Rollback past such migration
requires manual data restore.

## Auto-rollback

Watch metrics; trigger rollback on spike:

```
$ dark deploy 8a3f2dd --to prod --auto-rollback-on \
    "error_rate > 5% for 5min" \
    "p99_latency > 500ms for 10min"
✔ Deployed. Watching for 30 minutes.

[12:34] Error rate jumped to 8% on 8a3f2dd.
[12:34] Auto-rollback triggered.
[12:34] Routed back to 6b9c1ee. Notification sent to deploy-channel.
```

Auto-rollback is a Dark fn that polls the trace stream for
metrics. If conditions trip, emits a deploy op rolling back.
~1 second from spike detection to rolled-back.

## Approvals

For prod or any sensitive target:

```
$ dark deploy 8a3f2dd --to prod
Approval required (per prod policy: 1 approver from ["alice", "bob"])

Sending approval request to alice@anthropic.com, bob@anthropic.com...
[Or use: dark approve <deploy-id>]

[12:30] Alice approved.
✔ Deploying...
```

Policy is a Dark fn:

```dark
DeployPolicy.set "prod" {
  requireApprovers = ["alice@anthropic.com"; "bob@anthropic.com"]
  requireApprovals = 1
  requireTests = true
  requireTraceReplay = true
  blockDuringHours = ["fri 18:00 - mon 06:00 UTC"]
}
```

Configurable per-target. Approvals are ops; queryable
("show me all prod deploys this quarter and who approved").

## What this beats

| Tool | Deploy time | Rollback time | Canary built in? | PR previews? |
|------|-------------|---------------|------------------|--------------|
| Heroku | 30-90s | 30-60s | No (manual) | No (paid) |
| Vercel | 60-120s | <5s | No | Yes |
| AWS Lambda | 30-90s | 30-60s | Limited | No |
| k8s | 60-300s | 30-60s | Yes (Argo Rollouts/Istio) | Manual |
| Render | 60-180s | 30-60s | No | Yes |
| **Dark** | **<1s** | **<1s** | **Yes** | **Yes** |

The order-of-magnitude advantage on deploy/rollback time is
real. It comes from: no build step + content-addressed cache
+ config-flip-not-restart.

The "felt" advantage is bigger:
- "Wait, my deploy already finished?" first-time-user moment.
- "I can rollback in real-time during an incident."
- "Canary is on by default for prod deploys."
- "Every PR has a working URL."

These compound into a different debugging culture (ship many
small things; rollback freely; iterate).

## What's tricky

### Long-running connections during rollback

A user has an open WebSocket. We rollback. New connections
route to old code; existing connections...?

Options:
- **Drain.** Keep old code routed for existing connections;
  new code for new connections. Both run side-by-side until
  old connections close.
- **Hard cut.** Existing connections terminated; clients
  reconnect to new code.

Default: drain, with timeout (e.g., 5 min). Clients with
connections >5 min get cut.

### Stateful migrations

Schema migration that requires downtime (rare in Dark — most
DB migrations are kill-and-fill ops). Handle:
- Block requests during migration window.
- Show 503 with retry-after.
- Migration ops can declare `requiresDowntime: true`; deploy
  warns, prompts for confirmation.

### Multi-app deploys

App A depends on App B's API. Updating both atomically:
- Deploy as a "release group" — multiple commits across multiple
  apps deployed atomically.
- All-or-nothing: either all targets update, or none do.
- Coordinated routing flip across apps.

This is unusual but supported. Most cases: deploy B first
(backward-compatible), then A.

### Config / code coupling

A deploy that requires new env vars:

```dark
// In the deploy:
[<RequiresConfigVar "STRIPE_KEY">]
let chargeCustomer (...) = ...
```

Deploy fails if `STRIPE_KEY` not set on the target. User must
set config first. Tooling tells them what's missing.

## Observability per deploy

Each deploy is an op in the `deployments` stream:

```dark
type DeploymentOp =
  { target: String              // "prod"
    commit: Bytes               // 8a3f2dd...
    actor: AccountId
    timestamp: DateTime
    canary: Option<Float>
    approvals: List<AccountId>
    rollbackFrom: Option<Bytes> // if a rollback
    metrics: PostDeployMetrics  // captured 5m post-deploy
  }
```

Per iter 11, projections over this stream give:

- **Deploy frequency dashboard.** Per app, per env.
- **Mean time to recovery.** Time from incident-detected to
  rollback-completed.
- **Deploy success rate.** Deploys that didn't trigger
  auto-rollback.
- **Approval pattern audit.** "Who approved what when."

Useful for engineering culture, compliance, and post-mortem.

## Edge cases

- **Daemon out-of-sync at deploy time.** Local daemon doesn't
  have the latest ops; user pushes to deploy. Daemon detects,
  fetches missing ops, retries.
- **Half-deployed state.** Deploy starts; network partition
  midway. Deploy is atomic (single config flip); either it
  applied or it didn't. Recovery: re-issue deploy command.
- **Cache miss on rollback.** Old commit's bytes evicted from
  dark.run cache. Re-fetch from peer (the user's laptop).
  Adds a few seconds to rollback.
- **Migration mid-canary.** Canary is at 10%, but migration
  ran (not reversible). Rollback would orphan post-migration
  data. Daemon refuses canary if migration is irreversible;
  forces full deploy.

## Self-hosted daemons

For users running their own daemon (not on dark.run): same
commands, same UX. The "deploy" is "tell my local daemon to
serve commit X for app Y." Domain / TLS managed externally.

This is important for the data-sovereignty story (per iter 12 /
16): users running PHI-touching apps may need to self-host;
they shouldn't lose deploy ergonomics.

## Self-driving deploys?

Long-term: based on test signal + canary metrics + traffic
patterns, the daemon could auto-promote canary to 100% if
healthy. Or auto-rollback if unhealthy. Or auto-roll-forward
to the next commit if both this commit and the previous
trigger errors.

The "auto-pilot" mode. Probably v2; needs the trace +
metrics infrastructure to be very reliable.

## Open questions

1. **Deploy hooks.** Pre-deploy / post-deploy fns to run
   external integrations (Slack notifications, status page
   updates). Standard. ~50 LOC of Dark per integration.
2. **Multi-region deploys.** Deploy to us-east first, watch
   for 30 min, then us-west. Useful for big rollouts.
   Configurable in the deploy config.
3. **Database failovers during deploy.** What if the DB is
   primary in us-east but the deploy targets us-west? Need
   coordination. Punt to v2.
4. **Cross-team deploys.** A team owns app A; another owns
   app B. Coordinated release. Requires multi-team approval
   workflow. Configurable.
5. **Compliance gates.** "Prod deploys must have a Jira
   ticket linked." Customizable hook fn that takes the
   deploy and approves/rejects.
6. **Deploy from CI.** GitHub Action / CircleCI runs `dark
   deploy --to staging`. Requires CI to authenticate with
   dark.run. Service account model.

## TL;DR

`dark deploy` is sub-second. Same for rollback. Same for
canary. PR previews are auto-spawned per branch. Promotions
between environments are one command + an auto trace-replay
regression check.

The unique advantages over Heroku/Vercel/k8s:
- 30-100× faster deploys (no build step).
- Instant rollback (config flip, not redeploy).
- Canary built-in (no service mesh setup).
- Trace-replay regression check before promotion.
- Environment configs are encrypted ops, hot-reloadable.
- Approval workflows are Dark fns; auditable.

This is the daily-driver UX. Combined with iter 13 (replay-
to-debug) and iter 15 (auto-migration on deprecation), the
deploy story makes Dark feel materially better than alternatives.

This is where the architecture investments pay off in user
experience. Sub-second deploys are real, not aspirational.
