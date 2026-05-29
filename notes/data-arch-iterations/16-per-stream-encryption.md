# Iter 16 — Per-stream encryption

The data-sovereignty pitch from iter 12 said "dark.run is a
peer, not a server — your data lives on your laptop." That's a
storage-and-control claim, not a privacy claim. dark.run still
reads plaintext when handlers run there; logs and trace data
all sit in the clear.

This iter is about giving cryptographic teeth to the privacy
story. What does per-stream encryption look like? What does
dark.run see and not see? When can hosted handlers still work?
How do keys flow?

The threat model isn't "dark.run is malicious"; it's "dark.run
gets subpoenaed, hacked, or has a bad employee." Customers in
healthcare, finance, legal need to be able to say "even if
dark.run wanted to read this, they couldn't."

## Three tiers

Different users have different threat models. Three encryption
tiers, opt-in per stream:

### Tier 0 — TLS only (default)

What you get out of the box:
- TLS for all sync between daemons and hub.
- TLS for all client connections (CLI, editor, hosted handlers).
- No at-rest encryption.
- dark.run sees and stores plaintext.

Threat covered: passive network eavesdropping. Nothing else.

This is fine for non-sensitive data. Most apps are this.

### Tier 1 — At-rest encryption with daemon-held keys

What you get:
- Stream's ops encrypted at rest with a symmetric key.
- Key derived from user passphrase / hardware token.
- Daemon decrypts in memory to run handlers, project, etc.
- Sync moves ciphertext over TLS.

Threat covered: disk theft, cloud-provider scanning, casual
breach (where an attacker gets disks but not running daemons).

Not covered: dark.run's running daemon process holds the key;
a sophisticated attacker compromising the running daemon sees
plaintext.

UX:
- `dark stream encrypt my-app/users` — turns on at-rest for that
  stream.
- User prompted for / generates passphrase.
- All future ops encrypted; existing ones re-encrypted lazily.
- `dark stream decrypt` to roll back (rare, allowed).

This is "BitLocker for ops" — a reasonable default for
production apps. Probably the right setting most users want.

### Tier 2 — End-to-end encryption

What you get:
- Stream's ops encrypted with a key dark.run never sees.
- Only the user's daemons (laptop, etc.) can decrypt.
- Hosted handlers on dark.run can't read these streams.
- dark.run sees only ciphertext + metadata (timestamps, sizes).

Threat covered: dark.run subpoena, full compromise of dark.run
infrastructure, malicious dark.run employee.

Not covered: the user's own daemon being compromised.

UX:
- `dark stream encrypt my-app/medical-records --e2e` — turns on
  end-to-end.
- Daemon generates per-stream key; stores locally; never sends
  to dark.run.
- Hosted handlers see encrypted ops; can sync them, can't read.
- Handler logic that needs to read encrypted data must run on a
  daemon that has the key (user's laptop, or a peer with
  delegation — see below).

Tradeoff: hosted handlers can't process encrypted data. Users
either accept (run those handlers locally) or delegate keys
(tier 3).

### Tier 3 — Per-field encryption with key delegation

The future-state, cool but complex. Specific fields in a record
are encrypted; handlers running on dark.run can request the
field's key for a specific request, with audit trail.

```dark
type User =
  { id: Int64
    name: String
    email: String
    ssn: Encrypted<String>      // per-field encrypted
    medicalRecord: Encrypted<MedicalRecord>
  }
```

A handler that processes payments needs the SSN. It declares:

```dark
[<Handler>]
[<RequiresKey "ssn"; AuditedRelease>]
let processPayment (req: PaymentRequest) : Response =
  let user = Stdlib.DB.get Users req.userId
  let ssn = Encrypted.unwrap user.ssn  // requires the key
  // ... process ...
```

When deployed to dark.run, the handler is granted the SSN key
**only on invocation**, **for one request**, **with full audit
log**. The user's daemon (or a key-management peer) approves
each grant request — auto-approve based on a policy fn:

```dark
let ssnReleasePolicy (request: KeyRequest) : Stdlib.Result.Result<Unit, String> =
  if request.handler == "Mycorp.processPayment"
     && request.requestPath == "/api/payments"
     && request.requestId.isFresh ()
  then Ok ()
  else Error "not authorized"

KeyManagement.setPolicy "ssn" ssnReleasePolicy
```

Each grant produces an op in the `key-grants` audit stream.
User can `dark audit ssn-keys` and see "in the past 24 hours,
SSN key released to processPayment N times."

This is the hard one. Genuinely useful for compliance — "every
SSN access logged with reason and request context" — but the
infrastructure is complex. v2 work.

## Whole-stream tier 1 mechanics

Concrete:

### Key derivation

User passphrase → Argon2id KDF → 256-bit master key.
Master key + stream-id → HKDF → per-stream key.

Per-stream keys mean compromising one doesn't compromise others.
Master key never leaves daemon memory.

### Op encryption

```
encrypted_op = AES-GCM(stream-key, nonce, plaintext_op)
                where nonce = unique_per_op (random 96-bit + counter)
```

Each op header (timestamp, op type, parent-id, etc.) is
plaintext — needed for routing and ordering. Op body (content)
is ciphertext.

The daemon's serializer encrypts at write; deserializer
decrypts at read. Caches the per-stream key in memory.

### What dark.run sees

For a tier-1 encrypted stream:
- Op headers (when, what type, dependencies) — plaintext.
- Op bodies — ciphertext.
- Op signatures — verified against author key.

dark.run can:
- Store and replicate.
- Order by timestamp.
- Verify signatures.
- Run handlers that don't read this stream's content.
- Run projections IF the user releases the stream key to dark.run
  (which is what tier 1 implies — at-rest only).

dark.run cannot:
- Read op bodies without the key.
- (For tier 2:) Run anything that depends on op bodies.

### Key escrow / recovery

User loses passphrase = loses data. Three safety nets:

1. **Recovery seed.** At signup, user generates a 24-word
   BIP-39 seed; daemon derives master key from it. Seed
   stored offline by user. Can recover from any device with
   seed.
2. **Multi-device.** User's laptop + phone + work device all
   have the master key. Lose one, the others still work.
3. **(Optional) Social recovery.** Split master key with
   Shamir's secret sharing across N trusted people; M of N
   reconstitute. Standard tooling exists.
4. **(Optional) Hosted escrow.** dark.run holds an encrypted
   copy of master key, decryptable by user + 2FA + 24-hour
   delay. Compliance requirement for some users.

Default: recovery seed + multi-device. Escrow is opt-in
because it's a backdoor.

## Tier 2 (end-to-end) mechanics

Same as tier 1, but:
- The stream key never leaves user-controlled daemons.
- dark.run literally cannot decrypt.
- Hosted handlers can't access these streams' contents.

Sync still works: dark.run replicates ciphertext between
peers without seeing plaintext.

The user's threat model: even if dark.run is hostile, this
stream is safe.

The tradeoff: features that depend on dark.run reading
plaintext (most projections, hosted handlers, search, trace
analysis) are unavailable for these streams. Users running
their own daemons get full features even on encrypted
streams.

The pitch: "your most sensitive data lives on your machine.
dark.run helps you sync it. dark.run never sees it."

## What dark.run can do with metadata

Even for tier 2 streams, dark.run still sees:

- **Op count.** "User wrote 47 ops to stream X today."
- **Op timing.** "Op at 14:33:22, op at 14:33:25, ..."
- **Op size.** "Op was 1234 bytes."
- **Stream existence.** "User has a stream named medical-records."
- **Sync graph.** "User shared this stream with these other accounts."

This is metadata leakage. Standard tradeoff for any system
with a server-side router.

For the truly paranoid (intelligence-agency-grade threat model):
- P2P sync over Tailscale or Iroh — no hub at all.
- Stream existence hidden behind opaque IDs (no descriptive
  names visible to hub).
- Constant-rate cover traffic (op-padding so frequencies
  don't leak).

This is v3+. For now: tier 2 is "your bank's threat model,"
tier 3 is "your hospital's threat model," beyond is custom.

## Key rotation

Periodic rotation reduces blast radius if a key is compromised:

```
$ dark stream rotate-key my-app/medical-records
Rotating key for medical-records...
  Generating new key...
  Re-encrypting 1247 existing ops...
  ✔ Done. Old key retained for 7 days (in case of sync lag).
  ✔ All peers notified; will fetch new key.
```

After rotation:
- New ops encrypted with new key.
- Old ops re-encrypted with new key (lazy or eager — user choice).
- Old key marked retired; destroyed after grace period.

Compromise response: rotate immediately, force re-encryption,
notify users to delete cached old keys.

Forward secrecy isn't perfect (a peer who copied old ciphertext
+ old key keeps reading), but it's reasonable for the threat
model.

## Sigs, integrity, replay

Even with encryption, integrity matters:

- Each op signed with author's Ed25519 key.
- Signature covers ciphertext + nonce + key-id (the key version
  used to encrypt).
- Daemon verifies sig at decrypt time. Mismatch = reject.

This prevents:
- Tampering: ciphertext modification breaks AES-GCM auth tag.
- Replay: nonces are unique; daemon tracks seen nonces.
- Substitution: sig binds key-id, so you can't swap ciphertexts
  encrypted under different keys.

## Projections of encrypted data

A user-defined projection (per iter 11) is a Dark fn that runs
in the daemon. For tier 1 streams, the daemon has the key, so
projections work normally.

For tier 2 streams: the daemon needs the key. If projection runs
on the user's local daemon (which has the key), fine. If it
runs on dark.run (no key), the projection can only operate on
metadata (count, timestamps), not content.

API:

```dark
Projection.register {
  name = "medical-records.daily-count"
  appliesToStream = "medical-records"
  build = countByDay  // metadata-only
  privacy = ProjectionPrivacy.MetadataOnly
}
```

The `privacy` field constrains what the projection fn is
allowed to access. `MetadataOnly` projections can run on
dark.run (no key needed); `RequiresPlaintext` projections only
run where the key is.

## Encrypted streams in the trace stream

Traces include input data — much of which is sensitive. If a
trace records "Mycorp.processPayment {ssn: \"123-45-6789\"}",
that SSN sits in the trace ops in the clear.

Two solutions:

### Per-handler trace privacy

```dark
[<Handler>]
[<TracePrivacy "encrypted">]
let processPayment (req: PaymentRequest) : Response = ...
```

Traces of this handler get encrypted under the same key as the
sensitive data. dark.run sees encrypted traces; only key-holders
replay them.

### Auto-redaction

```dark
[<Handler>]
let processPayment (req: PaymentRequest) : Response =
  // Redact ssn in traces:
  Trace.redact req.body.ssn
  ...
```

The trace records `<redacted>` instead of the SSN. Loses some
debuggability but no decryption needed for analysis.

Default: no encryption (tier 0); user opts into per-handler
privacy.

## App handlers and encrypted streams

For tier 1: handlers run normally on dark.run with the key in
daemon memory. Same as plaintext.

For tier 2: handlers that touch encrypted streams must run on
a daemon with the key. Two options:
- **Run handlers locally.** User's laptop runs the daemon;
  handlers serve from there. Public IP required (Tailscale,
  ngrok, dynamic DNS).
- **Run handlers on a delegated peer.** A separate VPS the
  user owns + trusts; daemon there with the key.

User configures per-app:

```dark
App.config {
  name = "Mycorp.MedRecords"
  encryptedStreams = ["medical-records"]
  handlerLocation = HandlerLocation.UserDaemon  // or DelegatedPeer "myvps.com"
}
```

dark.run hosts the app's static parts (CDN, DNS) but not the
handler code's execution. Latency is higher; privacy is
absolute.

## What this looks like for a startup

Consider a healthcare startup using Dark:

1. **App setup.** They create `Acme.Health` on dark.run. Free
   tier or paid.
2. **Encrypt sensitive streams.** PHI data → tier 2 (e2e).
   Application logs, billing → tier 1 (at-rest).
3. **Handler placement.** Public-facing, non-PHI handlers
   (login, marketing site) on dark.run. PHI-touching handlers
   on a self-managed daemon (or partner-managed daemon, BAA-
   signed).
4. **Audit trail.** Tier 3 (per-field with grants) deferred
   until v2; for v1, they use tier 2 + auto-redaction in
   traces.
5. **Compliance.** They sign a BAA with dark.run for tier-1
   streams. Tier-2 streams don't need a BAA — dark.run can't
   see plaintext. (BAA is a HIPAA legal document; "they can't
   see it" simplifies the legal picture significantly.)

Pitch to that startup: "dark.run lets you ship a HIPAA-grade
SaaS in days. Sensitive data encrypted end-to-end; your CTO
sleeps better. Hosted infrastructure for the parts that don't
need encryption. One developer, one cloud bill, no compliance
nightmare."

This is a real market gap. Most "low-code platforms" don't
support this; most clouds (AWS, GCP) require manual encryption
glue.

## Performance

AES-GCM at ~5GB/s on a modern CPU. For typical workloads:
- 1000 op/sec at 10KB each = 10MB/s, ~2μs per op for crypto.
- Negligible overhead.

Key cache: per-stream key kept in memory. ~32 bytes per stream;
1000 streams = 32KB. Trivial.

Storage overhead: ~4 bytes per op (auth tag) + ~12 bytes per op
(nonce). ~16 bytes overhead per op. ~1.6% on 1KB ops, ~0.16% on
10KB ops.

Sync overhead: identical to plaintext (TLS already encrypts
the wire; encryption is transparent to wire format).

## Open questions

1. **Hardware-backed keys.** Modern laptops have Secure Enclave
   / TPM. Storing the master key there is much more secure than
   keeping it in daemon memory. Should be the default for
   tier-2 streams. iOS/Android: keychain. Linux: TPM via
   `tpm2-tss`. v2 work but worth a UX hook now.
2. **Key transport between user devices.** User adds laptop,
   then phone. How does the master key get from laptop to
   phone? Two options: (a) re-derive from seed phrase
   (paste seed on phone — friction); (b) QR-code scanning of
   wrapped key (laptop shows QR; phone scans). Standard pattern
   from Signal / 1Password.
3. **Org-shared keys.** Team sharing a tier-2 stream — each
   member has the key. New member added: existing member
   shares key with them via OOB channel (or via the daemon's
   existing access-grant mechanism). Member removed: rotate.
4. **Compliance certs.** dark.run could pursue SOC 2, HIPAA,
   ISO 27001. Tier-2 makes much of this much easier (smaller
   audit surface). v2 conversation; not architecturally
   blocking.
5. **Searchable encryption.** Some queries on encrypted data
   without decrypting. Hash-based lookups; comparison via
   structure-preserving encryption. State-of-the-art is
   limited; most workable for equality lookups only. Punt to
   v3.
6. **Quantum.** AES-256 is post-quantum-OK against Grover's
   (gives 128-bit equivalent). Ed25519 is not (needs CRYSTALS-
   Dilithium or similar). Plan for sig algorithm migration
   when quantum-resistant standards stabilize. v3.
7. **Side channels in handler timing.** A handler running
   different code paths based on encrypted data might leak
   info via timing. Standard concern; mitigation requires
   constant-time code at sensitive boundaries. Document but
   don't enforce.

## TL;DR

Three tiers:
- **Tier 0:** TLS only (default).
- **Tier 1:** At-rest encryption with daemon-held keys.
  Protects against disk theft, casual breach.
- **Tier 2:** End-to-end with user-only keys.
  dark.run cannot read; hosted handlers limited.
- **(Tier 3:** Per-field encryption with audited key
  delegation; v2.)

Symmetric AES-GCM for op bodies; Ed25519 sigs for integrity.
Per-stream keys derived via HKDF; master via Argon2id.
Rotation via op-emission; audit via log streams.

The pitch: **your most sensitive data is encrypted with keys
dark.run never sees.** Healthcare and finance customers can
make compliance claims that no other low-code platform can.

Falls out cleanly from the existing op-stream model — encryption
is just another transformation on op bytes. Implementation is
~1500 LOC of F# (key management) + Dark (policy fns + audit
streams). Two-month project.

This makes Dark not just "easier" than alternatives but
**structurally more private**. That's a unique market position.
