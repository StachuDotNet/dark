---
title: jwt-rs256
tier: M
class: app
modules: [Stdlib.Crypto, Stdlib.Json, Stdlib.Base64, Stdlib.X509, Stdlib.Cli]
languages: [dark, ts, py, go, rust]
expected_outcome: fail-likely
known_blockers: [no-rsa-signing]
framework_hint: null
core: false
---

# Description

A command-line JSON Web Token tool with two subcommands: `sign` produces an RS256-signed JWT from a payload + private key; `verify` validates a JWT against a public key, returning the payload on success.

The point of this project is **RS256-specific**: HMAC-based JWTs (HS256, HS512) are out of scope. RS256 = RSA-PKCS1-v1.5 + SHA-256. The agent must implement the asymmetric-crypto signing path — produce a signature with the private key, validate with the public key.

For TS, the natural implementation is `jsonwebtoken` (`jwt.sign(payload, privateKey, { algorithm: 'RS256' })`) or `jose`. For Py, `pyjwt` with `cryptography`. For Go, `github.com/golang-jwt/jwt`. For Rust, the `jsonwebtoken` crate. **For Dark today, no implementation path exists** — `Stdlib.Crypto` covers SHA-family hashes, HMAC, MD5, and `Stdlib.X509` extracts public keys, but there's no RSA signing primitive. (`Stdlib.X509` is read-only — public-key extraction only, per project-survey §1.)

The agent must produce the standard JWT format: `<base64url-header>.<base64url-payload>.<base64url-signature>`. Header is `{"alg":"RS256","typ":"JWT"}`. Signature covers the bytes of `<base64url-header>.<base64url-payload>`. RFC 7519 compliance is the contract.

# Behaviours

- `jwt-rs256 sign --payload payload.json --key private.pem` reads payload from a JSON file and key from a PEM file, prints the JWT to stdout, exits 0.
- `jwt-rs256 sign` with stdin payload and `--key private.pem` works equivalently.
- The output JWT, when split on `.`, has exactly 3 base64url-encoded segments.
- The header (segment 1, base64url-decoded then JSON-parsed) contains `{"alg":"RS256","typ":"JWT"}`.
- The payload (segment 2) round-trips back to the input JSON bytes (modulo key-ordering, which JSON-equality testing handles).
- `jwt-rs256 verify --token <jwt> --key public.pem` reads a JWT and public key, prints the payload to stdout (as compact JSON), exits 0 on valid signature.
- `jwt-rs256 verify` with a tampered payload (signature doesn't match) exits non-zero with "signature invalid".
- `jwt-rs256 verify` with the *wrong* public key exits non-zero with "signature invalid".
- A round-trip test: `jwt-rs256 sign | jwt-rs256 verify` (using matching key pair) returns the original payload.
- Cross-language verification: a JWT signed by Py's `pyjwt` should verify cleanly with this tool's `verify` (using the same public key). Tests interop with reference implementations.
- `jwt-rs256 --help` exits 0.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. Generate a 2048-bit RSA key pair using `openssl`:
   ```
   openssl genrsa -out priv.pem 2048
   openssl rsa -in priv.pem -pubout -out pub.pem
   ```
2. Create a payload: `echo '{"sub":"1234","name":"Alice"}' > payload.json`.
3. `jwt-rs256 sign --payload payload.json --key priv.pem` → expected: a 3-segment JWT printed.
4. **Cross-tool interop check**: pipe the JWT through a reference implementation (e.g. `python -c "import jwt; print(jwt.decode(open('jwt.txt').read(), open('pub.pem').read(), algorithms=['RS256']))"`); successful decode is the cross-language gate. If pyjwt rejects with "Signature verification failed", the agent's RS256 is broken.
5. Round-trip: `jwt-rs256 sign --payload payload.json --key priv.pem | xargs jwt-rs256 verify --key pub.pem` → should print the original payload.
6. Tamper test: take the JWT from step 3, change one character in the payload segment, run verify → must reject with "signature invalid".
7. **For Dark specifically**: examine the source. Did the agent:
   - (a) attempt RS256 and produce broken output (the most likely failure mode given `no-rsa-signing`)?
   - (b) shell out to `openssl rsautl -sign` or `openssl dgst -sha256 -sign priv.pem` (workaround)?
   - (c) substitute HS256 silently (wrong — the spec says RS256, agent should not substitute)?
   - (d) honestly report the gap and produce a stub that exits non-zero with the explanation?
   The agent's `SUMMARY.md` should explain. **(c) is the worst outcome** — silent algorithm substitution would pass naive tests on a Dark-only sweep but fail the cross-language interop check (step 4). The spec relies on step 4 to catch this.
8. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- jwt-rs256 sign --payload payload.json --key priv.pem
- jwt-rs256 verify --token <some-jwt> --key pub.pem
- jwt-rs256 --help

---

**Why this is `fail-likely`**: RS256 = RSA signing + SHA-256. Dark's `Stdlib.Crypto` has SHA-256 but no RSA signing primitive; `Stdlib.X509` is public-key-extraction-only. The agent's most-honest path produces an artifact that *attempts* the signature path (using whatever hashes Dark exposes), then either:
- Fails the cross-language interop test (self-verification step 4) — Dark's "signature" doesn't validate against a real RSA verifier.
- Succeeds locally only because both sign and verify are using the same wrong algorithm.
- Shells out to `openssl rsautl -sign` (workaround; cedes the language-level claim).

**The longitudinal value**: when Dark adds RSA signing, this spec flips to passing — including the cross-language interop step. Bench detects it. **The cross-language step (step 4) is what makes this spec robust** — without external verification, Dark could "pass" with internally-consistent broken output. With it, the spec measures real-world JWT-RS256 compatibility.
