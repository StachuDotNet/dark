# Iter 01 — the op binary format

The unified-model doc (§ 3) sketches the on-disk `ops` table:

```sql
CREATE TABLE ops (
  hash             TEXT PRIMARY KEY,
  stream           TEXT NOT NULL,
  key              TEXT NOT NULL,
  payload          BLOB NOT NULL,
  parent_op        TEXT,
  created_at       INTEGER NOT NULL,
  origin_instance  TEXT NOT NULL,
  signature        BLOB NOT NULL
);
```

Decent first cut, but it conflates several concerns: the wire format
that flies between instances, the at-rest format on disk, the format
the daemon hands to projection builders, and the format `applyOp`
deserializes back into a typed shape per stream. Worth pulling apart.

## Three formats, one logical op

```
                        ┌──────────────────────────┐
   typed Op<'a>         │ op shape, e.g. SetName,   │
   (per-stream)         │ AppendUserData, …         │
                        └─────┬────────────────────┘
                              │ LibSerialization.Binary write
                              ▼
                       ┌──────────────────────────┐
   payload : byte[]    │ stream-specific payload  │
                       └─────┬────────────────────┘
                             │
        wrap with envelope ─►┌────────────────────────────────────┐
                             │ envelope = (header || payload)     │
                             │ header   = {                       │
   wire / on-disk Op         │   schema_version,                  │
                             │   stream, key,                     │
                             │   parent_op, created_at,           │
                             │   origin_instance, signature       │
                             │ }                                  │
                             └────────────────────────────────────┘
                              │
                              │ hash := SHA-256(envelope)
                              ▼
                              hash || envelope on disk / wire
```

Three layers, each with a different evolution discipline:

1. **Typed Op shape (per-stream).** F# DU with binary writer/reader.
   Versioned freely (existing `LibSerialization.Binary` pattern). Adding
   a new case is a new tag byte. Existing readers know to skip
   unknown tags.

2. **Envelope.** Stream-agnostic. Holds metadata used for routing,
   sync, dedup, signing. Schema_version is the envelope's version,
   not the payload's. Frozen forever for v1; a v2 envelope would be
   a parallel format we add when the v1 shape can't carry something
   important.

3. **Wire/disk row.** A `(hash, envelope)` pair. Disk indexes the
   hash, the envelope is an opaque BLOB. Wire ships envelopes; hash
   is computed on receipt and verified.

This isolates: payload churn (every release adds new op shapes) doesn't
touch envelope schema; envelope changes are rare ALTERs on `ops.db`;
wire stability is roughly free since wire = on-disk envelope bytes.

## Concrete envelope, byte by byte

Designed for fast read/write, no JSON parsing on the hot path, all
fields present (no Options at this layer).

```
envelope := bytes_v1
bytes_v1 :=
  0x01                     # 1 byte: envelope schema version
  varint(len(stream))      # 1-3 bytes
  utf8(stream)             # N bytes; stream names are ASCII-ish, ≤ 64 chars
  varint(len(key))
  utf8(key)                # keys are content-addressed hashes for most
                           # streams (hex), or short strings ("main", "feat-x")
  16 bytes                 # parent_op hash (zero-filled if no parent)
  8 bytes                  # created_at, big-endian unix millis
  16 bytes                 # origin_instance UUID
  varint(len(payload))
  payload                  # opaque per-stream bytes
  64 bytes                 # Ed25519 signature over (everything above)
```

Notes:

- **Hashes truncated to 16 bytes** for `parent_op`. Today's PR keeps
  full hex hashes for content-addressing safety, so why truncate
  here? Answer: this isn't *the* content hash — it's a back-pointer.
  Birthday-collision-on-16-bytes within one stream's per-key chain
  is astronomically unlikely (we're not putting petabytes of ops on
  one chain). The full hash is recoverable from the parent's
  envelope-hash; truncation here saves ~16 bytes per op × millions
  of ops over time. **Reconsider:** maybe just keep it 32 bytes for
  simplicity. Pencil that as "decide before slice 3.5 lands."

- **Stream as string, not enum.** Tempting to make stream a u8 enum
  ("0=packages, 1=branches, …") to save bytes. Resist: streams will
  grow (apps create streams; the system creates streams for
  resolutions per § 8 of the unified-model doc), and a closed enum
  forces version-coordination across instances. Strings are the
  right shape here. ASCII names are 6-15 bytes typically.

- **Signature is part of the envelope.** Hashing includes the
  signature, so an op's hash is dependent on who signed it. This
  matters: two instances signing identical payload produce different
  hashes. That's correct — they really are different ops in the log
  (different provenance), and the projection chooses the canonical
  one via parent-op chains.

- **No length prefix on the envelope itself.** When read off
  `ops.payload BLOB`, SQLite gives us the length. When read off
  the wire, the transport layer (WebSocket frames or HTTP body)
  gives us the length. So the envelope itself is just the
  fields-in-order; no self-delimiting needed.

## Versioning the envelope

The first byte is `0x01`. If we ever need a `0x02` envelope —
say to add a `previous_resolution_for : Option<Hash>` field for
faster conflict-replay — readers branch on the first byte. We
only ship `0x02` envelopes when every reader is known to handle
them (so: a hub-coordinated rollout, not a unilateral one).

`0x00` is reserved as "empty / not-yet-set" so a corrupt zero-page
doesn't accidentally read as v1.

## Versioning the payload (per stream)

Each stream's payload is a separate F# DU; each DU follows the
existing pattern in `LibSerialization.Binary.Serializers/PT`:

- First byte = tag.
- Subsequent bytes per case.
- Adding a case = new tag.
- Removing a case = mark the tag dead, never re-use.
- Renaming a field = new tag, not in-place edit.

An older daemon receiving a newer-tag op should:
1. Be able to parse the envelope (envelope hasn't changed).
2. Hand the payload bytes to the per-stream payload reader.
3. Payload reader returns "unknown tag, skip" rather than crashing.
4. Daemon stores the op verbatim (envelope + payload bytes) but
   marks the projection "saw an op I can't apply; rebuild me when
   you're upgraded."

This skip-and-flag behavior is the difference between "every
instance must upgrade in lockstep" (terrifying) and "instances
upgrade independently, projections lag on lagging instances" (fine).

## What the daemon's API looks like

```fsharp
module LibOps.Envelope

type Envelope = {
    schemaVersion: byte                      // 0x01
    stream: string
    key: string
    parentOp: Hash option                    // None if first op on (stream, key)
    createdAt: int64
    originInstance: System.Guid
    payload: ReadOnlyMemory<byte>
    signature: ReadOnlyMemory<byte>          // 64 bytes
}

let writeEnvelope (env: Envelope) : byte[] = ...
let readEnvelope (bytes: ReadOnlyMemory<byte>) : Result<Envelope, ParseError> = ...

let computeHash (envBytes: ReadOnlyMemory<byte>) : Hash =
    SHA256.HashData(envBytes.Span) |> Hash.fromBytes

/// Used by the sync layer and the local appendOp path.
/// Returns the canonical (hash, envelope-bytes) pair that lives in ops.db.
let mintFromPayload
    (stream: string, key: string, parentOp: Hash option,
     payload: ReadOnlyMemory<byte>,
     instanceKey: PrivateKey, instanceId: System.Guid) : Hash * byte[] = ...
```

`mintFromPayload` is the only function that signs. Tests mock it.
Sync receivers don't sign — they just verify and store.

## Storage: `ops.payload` BLOB versus normalized columns

The unified-model doc has the envelope fields as separate columns
(`stream TEXT`, `key TEXT`, `parent_op TEXT`, `created_at INTEGER`,
`origin_instance TEXT`, `signature BLOB`) plus `payload BLOB`.
Why store the envelope split into columns instead of one BLOB?

**Pro split:** SQL filters on `stream`, `created_at`, `origin_instance`
are common. Indexes work. `SELECT * FROM ops WHERE stream='packages'`
is fast.

**Pro single BLOB:** Atomicity (one column to read/write); the row
hash doesn't depend on column-store representation; smaller pages
because shared prefixes (UTF-8 stream names) compress well in a
single column blob; matches the wire format exactly so writes from
sync are zero-copy.

**Resolution:** keep the split for v1. The query patterns demand
indexes. The `signature` and `payload` BLOBs already give us
efficient reads of the bulk parts. We pay one extra hash recompute
on send-over-the-wire (rebuild envelope from columns, hash, ship
hash || envelope), but receive-from-the-wire is zero-extra-work
(parse envelope from bytes, write columns, store).

The hash stored in `ops.hash` is the canonical one. We don't
recompute on every read — only on writes-from-payload-mint and
on `verifyIntegrity` audits.

**Aside:** the 16-byte UUID stored as TEXT (32 hex chars + dashes,
36 bytes) wastes ~2.25× vs storing as BLOB. If `ops.db` ever gets
big, packing UUIDs as BLOB is a one-time win. SQLite TEXT is more
ergonomic for sqlite3-shell debugging though, and that DX matters.
Defer.

## What this means for sync

Sync transport should ship **(hash, envelope_bytes)** pairs and
nothing else. Receiver:

1. Verify hash by recomputing SHA256 over the envelope bytes.
2. Parse envelope.
3. Verify signature using the origin_instance's public key (looked
   up from the hub's instance directory).
4. Check the (stream, key) is in scope per ACL (per-stream sync
   config, per-grant if cross-user).
5. INSERT OR IGNORE into `ops` table. Done.

No deserialization of payload required at the sync layer. Sync is
a content-addressed BLOB pump.

This is a meaningful simplification over what one might draw on a
napkin. The sync agent doesn't know what a `SetName` op is. It
ships bytes.

## Critique of myself

- **Signature scheme = Ed25519.** Why not? It's small (64 bytes),
  widely supported (System.Security.Cryptography), no key
  ceremony. Could also be a HMAC if we wanted shared-secret
  semantics, but Ed25519 is closer to "instance has identity
  rooted in public key, hub holds public keys, no shared secrets."
  Decision: Ed25519.

- **Replay attacks.** An op with `created_at` in the past, signed by
  an instance whose key has since been rotated — should that still
  be applied? Yes, because content-addressed dedup means the op
  either exists in `ops.db` already (idempotent ignore) or it's new
  (apply once). Key rotation doesn't invalidate past signatures;
  the public key is recorded at the time of signing in
  `instance_keys` (a hub table) versioned by issuance window.

  But: a rogue ex-instance whose token was revoked could ship
  retroactive ops. Mitigation: the *hub* is the gatekeeper for
  cross-user sync; it consults `instance_tokens.revoked_at` before
  forwarding. Instance-to-instance direct (P2P upgrade) needs the
  hub-issued ACL bundle to be checked.

- **Hash-of-signature dependency.** Including the signature in the
  hash means re-signing produces a different hash — we don't want
  to "re-sign" anyway, but it's worth flagging that this prevents
  signature-rotation-without-rehash. Probably correct: a re-signed
  op is a *new* op, even if it represents the same intent.

- **Should the envelope include `expires_at`?** Some streams want
  TTL'd ops (traces older than 7d → drop). Not really
  envelope-level: the `stream_config.retention_days` answers this
  per-stream; per-op TTL would only matter for things like
  short-lived presence updates, which probably shouldn't be ops at
  all (they should be ephemeral hub messages).

## Action items if this lands

- Add a new `LibOps` project with `Envelope.{write,read,hash,mint}`.
- Move per-stream payload writers under `LibOps.Streams.<name>.fs`.
- Sync layer accepts `IReadOnlyList<(Hash, ReadOnlyMemory<byte>)>`.
- `ops.db` schema as written in the unified-model doc, but with
  the additions above (`schema_version` byte is *inside* the
  envelope blob, not a column — it's a payload-protocol concern).
