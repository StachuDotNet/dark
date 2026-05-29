# Iter 19 — Backups & disaster recovery

The op-stream-as-master model changes what "backup" means.
Multiple peers automatically constitute replicas; replaying ops
gives you point-in-time restore for free; signatures detect
tampering. This iter inventories the failure modes the
architecture *doesn't* automatically cover, and the ceremonies
needed to fill those gaps.

## What replication already gives you

By default, with the daemon running on >1 device:

- **Real-time replication** to every peer.
- **Tamper detection** via Ed25519 sigs at the op level.
- **Point-in-time view** — replay ops to any timestamp.
- **Automatic divergence handling** — conflicts surface (per
  iter 04) instead of silently corrupting state.

For the common case (user has laptop + dark.run replica), this
covers most failures with zero ceremony. Lose the laptop —
sync from dark.run. Lose dark.run — sync from laptop. Hostile
edit — revert via op log.

The remaining gaps are scenarios where replication doesn't help.

## Failure modes & responses

| # | Failure | Replication helps? | Recovery |
|---|---------|---------------------|----------|
| 1 | Local disk corruption | Yes (sync from peer) | Detect via sig, rebuild from peer |
| 2 | Local disk death | Yes if multi-device | New device, sync, resume |
| 3 | Daemon writes bad ops | Partially | Skip-op + projection rebuild |
| 4 | User error (rm -rf, DropDB) | Yes | Op-log revert |
| 5 | Sync logic bug propagates corruption | No | Bisect ops, manual revert |
| 6 | Cryptographic key loss | No | Recovery seed (or data lost) |
| 7 | Adversarial peer | Mostly | Sigs prevent forging; access grants prevent unauthorized writes |
| 8 | Hub extended outage | Partially | P2P sync if configured |
| 9 | All peers offline simultaneously | N/A | Wait for peers; sync converges |
| 10 | Time-bomb data (daemon crashes on load) | No | Safe-mode boot |

The architecture handles 1-2, 4, 7-9 cleanly. The interesting
ones are 3, 5, 6, 10.

### #3 — Daemon writes bad ops

The daemon has a bug. It emits an op that's syntactically valid
(passes sig verification) but semantically broken (e.g.,
references a non-existent type). The op syncs to peers; their
projections fail.

Detection: projection rebuild failure. Daemon logs:
"Projection X failed at op Y: type 'Foo' not found."

Recovery:
- `dark op skip Y --reason "buggy daemon emitted invalid type"`
- Emits a `SkipOp` op pointing at Y.
- Projection rebuild re-runs, ignoring Y.

`SkipOp` ops are themselves first-class:
- Authored, signed.
- Other peers consume them and skip Y.
- Audit trail preserved (Y still in the log, marked skipped).

For a bug that emits many bad ops: bulk skip via a regex /
predicate. Surfaced in `dark op skip --since <ts> --pattern X`.

### #5 — Sync logic bug propagates corruption

Worst-case: sync code itself has a bug that misorders ops or
truncates state. All peers diverge silently.

Detection: harder. The op stream's content hashes catch
truncation (each op's parent-hash is recorded; if a peer's
local view is missing the parent of op X, X gets queued).
Misordering is the sneakier case.

Mitigation:
- Pin canonical order via parent-hash chain. Each op records
  its parent op's hash; peers must apply ops in chain order.
- Periodic "checkpoint" ops sign-merkle-root of the stream
  state. Peers compare merkle roots; mismatch = sync bug.

Recovery:
- Identify the bug, fix daemon, redeploy.
- Bisect to find the misordering point.
- Manual `dark op-replay --from <correct point>` rebuilds the
  affected projection.

This is the "we have a bug in our infrastructure" recovery
ritual. Rare but possible.

### #6 — Key loss

Tier-1 / tier-2 streams encrypted with user-held keys. User
loses passphrase + all device keychains + recovery seed.

The data is unrecoverable. Standard E2EE tradeoff.

Mitigations (the user should configure these on day 1):
- Recovery seed phrase (BIP-39 24 words).
- Multi-device setup (laptop + phone + work).
- Optional Shamir social recovery with N trusted people.
- Optional dark.run-managed escrow (24h delay + 2FA), opt-in.

If a user has none of these and loses their key: data lost.
Tools surface aggressive warnings during signup if no
recovery configured.

### #10 — Time-bomb data (daemon crashes on load)

A bad op causes the daemon to panic during startup. Every time
the daemon is started, it consumes the same op, panics again.

Recovery:
- `darkd --safe-mode` skips the projection-build phase; daemon
  comes up "naked" (no app handlers, just admin operations).
- User runs `dark op skip <bad-op-hash>`.
- Restart daemon normally; works.

Detection: panic on startup → safe-mode prompt next launch.

## Backup tiers

On top of replication, three backup levels:

### Tier A — Implicit (free, default)

Multiple devices == replicas. dark.run replica == backup.
Zero configuration.

Coverage: most failures.
Cost: free (already paid for via dark.run subscription / device
ownership).

### Tier B — Periodic snapshots (opt-in, ~free)

```
$ dark backup schedule --daily --at 03:00 --to s3://my-bucket/
✔ Daily backup scheduled. First run tonight at 03:00 UTC.
   Encryption: enabled (your master key required to restore).
   Retention: 30 days (older snapshots auto-purged).
```

What gets backed up:
- All ops in scope (user can filter by stream).
- Projection state (optional; rebuilds from ops anyway).
- Encryption keys (encrypted by master key).

Storage: typical app's full backup is GBs. Differential daily
backups are MB-KB. Storage cost on S3 is pennies per month per
typical user.

Restore:

```
$ dark restore --from s3://my-bucket/dark-backups/2026-05-09.tar.zst
Verifying backup signature...                 ✔
Decrypting with master key...                 ✔
Replaying 4521 ops...                         ✔
Rebuilding 8 projections...                   ✔
✔ Restored to /tmp/dark-restore-9aa3bf

Adopt as primary? [y/N]
```

Restore-to-temp by default; adopt-as-primary is explicit. Lets
you compare current vs. restored state before committing.

### Tier C — Off-site / paranoid (manual)

USB drive in a safe deposit box. Tape archive. Print the seed
phrase, store in a fire-safe.

Used by users with severe-loss-prevention needs (legal, medical,
estates). Standard storage rituals; daemon can format-conversion
to / from these as needed.

`dark backup export --to /mnt/usb-stick --format tar-zst-encrypted`

## Point-in-time restore

The op log is naturally point-in-time. To restore "what my data
looked like 3 days ago":

```
$ dark restore --as-of "2026-05-06 14:00" --to /tmp/restore-mon
Replaying ops up to 2026-05-06 14:00:00 UTC...
  4892 ops applied
  429 ops after target time skipped
✔ State at 2026-05-06 14:00 restored to /tmp/restore-mon

Inspect: dark cli --root /tmp/restore-mon
```

Useful for:
- "Did this row exist last week?"
- Forensics: "what did we ship on May 6?"
- Recovery: "before the bug landed, what was the state?"

The point-in-time view doesn't affect current state. It's a
read-only sandbox.

## Recovery rituals (concrete)

### "I lost my laptop."

1. Get new laptop.
2. Install Dark.
3. `dark login` — enter passphrase / seed.
4. Daemon syncs from hub or another device.
5. Resume work in ~minutes.

### "My disk failed, no recent backup, no second device."

1. New disk.
2. Install Dark.
3. `dark login`.
4. If hub backup exists (paid tier): sync from hub. Restored.
5. If no hub backup: data is lost. Standard tradeoff.

This is why the default for paid tiers should include hub-side
backup. Users opting out of hub backup should see prominent
warnings.

### "I deleted production data via a buggy handler."

1. `dark op-log my-app/users --since "1 hour ago"` — find the bad write op.
2. `dark op-revert <bad-op-hash>` — emit a revert op.
3. Projection rebuilds; data restored to pre-deletion state.

Time elapsed: 2-3 minutes. No restore-from-backup needed because
the data never left the op stream.

### "My deploy of new code corrupted the DB schema."

1. `dark rollback prod` — instant code rollback.
2. `dark migration rollback v8` — reverses the schema migration.
3. Verify DB state via `dark trace replay <recent-trace>`.
4. Diagnose, redeploy.

Total downtime: <1 minute.

### "Someone published a bad version of my package and I can't unpublish."

Per iter 15: unpublish doesn't exist. But:
1. Mark v1.4.1 as `Deprecated "Use v1.4.2 instead — see CVE-X"`.
2. Publish v1.4.2 with the fix.
3. Importers see the deprecation notice on next pull.
4. Auto-migrate fix included in deprecation: `migrate-to v1.4.2`.

Reach to all importers within ~hours (their next pull).
Aggressive: send notification to all importers via the hub.

### "My team's daemon got hacked. Adversary has our keys."

1. **Rotate immediately.** All encryption keys rotated; all sigs
   re-issued; all access grants revoked.
2. **Audit.** Use the trace stream to see what the adversary
   did. Likely they wrote ops; revert each.
3. **Refresh peers.** Notify all peers; old keys destroyed.
4. **Post-mortem.** Adversary still has the old key + ciphertext
   they captured before rotation; that data is permanently
   exposed. (Forward-secrecy limits.)

This is the worst-case scenario. Rotate, revert, audit. Same as
any compromised-system response, but Dark's machinery makes
rotation + revert ergonomic.

### "dark.run shut down."

1. User's local daemon already has all the data.
2. Set up a public-IP daemon (or self-host on AWS / Hetzner /
   home server).
3. Reroute domain DNS to new daemon's IP.
4. Resume.

If on tier-2 encryption: dark.run's shutdown affects only
hosting / sync brokerage. Data is fully theirs.

If on tier-1 only: dark.run had the keys; if the company
shutdown is hostile, keys may be inaccessible. Mitigation:
opt into multi-region replication and key escrow with a
second trusted party.

## Audit trail of recoveries

Every backup, restore, op-skip, op-revert is itself an op:

```dark
type RecoveryOp =
  | BackupCreated of dest: String * size: Int64 * timestamp: DateTime
  | RestoreFromBackup of source: String * targetState: PointInTime
  | OpSkipped of opHash: Bytes * reason: String * actor: AccountId
  | OpReverted of opHash: Bytes * reason: String * actor: AccountId
```

Recorded in a `recovery` stream. Queryable, audit-grade.

`dark recovery history` lists all recovery actions:

```
2026-05-09 14:33  Alice  OpReverted 8a3f2dd  "deleted users table by accident"
2026-05-08 03:00  System BackupCreated s3://... (12.4MB)
2026-05-07 03:00  System BackupCreated s3://... (11.8MB)
...
```

Useful for compliance, post-mortems, and "did anyone undo
my work?" checks.

## What's NOT covered

Honest scope:

- **Logical bugs in user code.** A bug ships; user data is
  written wrong; user notices a month later. The data isn't
  "corrupted" — it's wrong-by-design. Recovery: identify the
  affected op range, write a fix-up handler, re-process. Same
  as any system with a logic bug.
- **Pre-Dark data.** Migrating in from existing systems
  doesn't get retroactive op tracking for the pre-migration
  history. Backup-from-other-system before Dark migration is
  the user's responsibility.
- **External-system state.** A handler called Stripe with bad
  data; Stripe charged a customer wrongly. Reverting the Dark
  op doesn't refund the customer. Standard caveat for any
  system that has external side effects.

These gaps are documented in onboarding; users with strong
guarantees needed handle them externally.

## Backup costs

Per iter 12 / 15, ops are content-addressed and dedup-heavy.
Storage cost for a typical app:

- Raw ops: ~1-10 KB each, 1K-100K ops/day → 1MB-1GB/day.
- After zstd compression: ~30% size.
- After cross-op dedup: similar to above (most ops are
  unique).
- After cross-tenant dedup on dark.run (Stdlib bytes shared):
  10-30% reduction for stdlib-heavy apps.

S3 storage: ~$0.025/GB/month. A typical user's backup is
GBs / year; cost is well under $1/year.

Free tier: 10 GB cumulative backup storage. Paid: 100 GB. Above:
metered.

## Open questions

1. **Backup verification.** A backup might be silently
   corrupted on its way to S3. Periodic test-restore: the
   daemon downloads its own backup, verifies it restores
   cleanly, alerts if not. Default: monthly self-test.
2. **Cross-region replication.** dark.run-side, replicate
   user's data across multiple AZs / regions. Standard cloud
   pattern. v2.
3. **Compliance retention.** "Hold 7 years of records." Backup
   retention configurable per stream. Some streams retained
   indefinitely; others purged on schedule.
4. **GDPR right-to-be-forgotten.** A user requests deletion of
   their PII. Need: scan all streams, find ops referencing
   user, emit redaction ops. Audit trail of deletion. Iter 03
   touched this; flesh out a fuller workflow.
5. **Backup testing as CI.** A team's CI suite includes a
   "restore from yesterday's backup, run tests" job. Catches
   backup-corruption bugs early.
6. **Disaster recovery drills.** Recommended quarterly: test
   the full recovery path from a simulated disaster. Tooling
   to simulate: `dark drill --scenario disk-death`.

## TL;DR

The op stream + multi-peer replication = backups for free in
the common case. Tier B (cloud snapshots) and tier C (off-site)
fill the gaps for paranoid users.

Point-in-time restore is trivial: replay ops to a target time.
Op-log revert is the typical recovery for user error. Safe-mode
boot recovers from time-bomb ops.

E2E encryption (tier 2) means dark.run can store backups it
cannot read. Compliance benefit; data sovereignty preserved.

Recovery ceremonies are well-defined for each common scenario,
mostly under 5 minutes. Catastrophic loss (key gone, no peers,
no backups) is real but mitigated by signup-time recovery
configuration.

Storage cost is pennies per user per month — backup-friendly
business model.

The architecture's payoff: most "backup features" of other
systems are implicit here. The remaining ceremonies are small,
well-understood, and rare.
