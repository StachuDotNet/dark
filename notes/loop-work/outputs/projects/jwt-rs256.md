# jwt-rs256

**Goal:** Sign and verify RS256 (RSA-PKCS1-v1.5 + SHA-256) JSON Web Tokens via a CLI, interoperable with reference JWT implementations.

**Kind:** greenfield

## Acceptance criteria
- [ ] `jwt-rs256 sign --payload payload.json --key private.pem` reads a JSON payload and a PEM private key, prints the JWT to stdout, exits 0.
- [ ] `jwt-rs256 sign` also accepts the payload on stdin with `--key private.pem`.
- [ ] The output JWT splits on `.` into exactly 3 base64url segments; the header decodes to `{"alg":"RS256","typ":"JWT"}`.
- [ ] The payload segment round-trips back to the input JSON (modulo key ordering).
- [ ] `jwt-rs256 verify --token <jwt> --key public.pem` prints the payload as compact JSON and exits 0 on a valid signature.
- [ ] `verify` with a tampered payload exits non-zero with "signature invalid".
- [ ] `verify` with the wrong public key exits non-zero with "signature invalid".
- [ ] `sign | verify` with a matching key pair returns the original payload.
- [ ] Cross-implementation interop: a token signed by a reference RS256 implementation verifies cleanly, and tokens signed here verify in a reference implementation, using the same key pair.
- [ ] HS256/HS512 are out of scope; the algorithm is not silently substituted.
- [ ] `jwt-rs256 --help` exits 0.
