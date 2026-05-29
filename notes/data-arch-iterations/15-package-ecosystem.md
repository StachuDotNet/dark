# Iter 15 — Package ecosystem & composition

This is the "what does Dark feel like once there are 10K
developers" iter. Today's package model is single-instance:
all packages live in one DB; sharing is by F# checkin; there's
no "publish to a registry" or "import someone's lib" story.

The new architecture (content-addressed ops + sync + hub)
enables a richer ecosystem. This iter sketches the shape:
discovery, authoring, versioning, importing, composition,
trust, deprecation. The aim: design choices that scale to
thousands of authors without recapitulating npm's pathologies.

## The four flows

A successful ecosystem has four user flows:

1. **Find** — "I need a JWT validator." Search.
2. **Use** — "Import Mycorp.JWT into my project."
3. **Author** — "I wrote a fn, want to share it."
4. **Maintain** — "Someone needs me to fix a bug; release v1.0.1."

Each must be one or two commands. If any of them is friction-
heavy, the ecosystem stalls (cf. golang's `dep` saga).

## Discovery — `dark search`

```
$ dark search "jwt"
Mycorp.JWT                 ★ 1.2K  v1.4.0  2 days ago     by alice@anthropic.com
   "RFC 7519 implementation, async-safe, RS256/HS256/Ed25519"

Stachu.AuthHelpers         ★ 87    v0.3.1  1 week ago     by stachu@dark.run
   "Web auth utilities; JWT, sessions, OAuth flows"

Bob.SimpleJWT              ★ 12    v0.1.0  3 months ago   by bob@example.com
   "Minimal JWT verify, learning project — not for production"
```

Output:
- Package name (canonical, namespaced).
- Star count (community endorsement, aggregated by hub).
- Latest version + when released.
- Author + verification level.
- One-line description (from package metadata).

Searchable fields:
- Package name + namespace.
- Description, fn names, fn docstrings.
- Type signatures (search by signature: `dark search --sig
  "(String) -> Result<JWT, Error>"`).
- Tags / categories (community-curated).

The search index is a Dark-app-on-the-hub. Implemented as a
projection over the global "published packages" stream
(per iter 11 — projections-as-Dark). Updates land within
seconds of publish.

`dark search --json` for tooling. `dark search --local` to search
only your own / locally-cached packages.

## Use — `dark import`

Top of any module:

```dark
import Mycorp.JWT @ v1.4.0
import Stachu.HttpHelpers @ HEAD     // latest unstable
import Bob.Numerics @ git:abc123     // pinned to specific commit
```

Resolution at compile time:
- Daemon checks local cache for the requested version.
- Cache miss: hub fetches the package's ops; verifies signature;
  caches locally.
- Future imports of the same version are instant (cached).

`@ vX.Y.Z` resolves to a specific commit hash (the release tag),
immutable. So `@ v1.4.0` means "the bytes that produced v1.4.0 at
publish time" — not "whatever the author calls v1.4.0 today."

This means **no separate lockfile**. The version pin in source
IS the lockfile. No `package-lock.json` drift; no `yarn.lock`
merge conflicts.

`@ HEAD` resolves to the latest published commit, NOT the
author's WIP. So importing HEAD is reproducible across
fetchers (assuming you fetch within the same minute) and
eventually pins to the resolved hash on `dark commit`.

`@ git:HASH` lets you pin to a specific unreleased commit.
Useful for "I'm using a fork that's not yet a release."

## Author — `dark publish`

```
$ dark publish Mycorp.JWT v1.4.0
Building Mycorp.JWT...
  84 fns, 12 types, 3 modules
  Public API: 14 fns, 5 types
Verifying...
  ✔ Tests pass (142/142)
  ✔ Type-checked
  ✔ No unresolved imports
  ✔ Documentation coverage: 87% (warning: 18 fns undocumented)
Signing with key: alice@anthropic.com (Ed25519)
Pushing to hub.dark.run...
Published v1.4.0 at hash 8a3f2dd
URL: https://dark.run/packages/Mycorp.JWT/v1.4.0
```

The "publish" op:

1. Verifies build (tests pass, type-checks).
2. Bumps version (semver-compliant: major.minor.patch).
3. Signs with author's key.
4. Pushes the package's ops to the hub's `published-packages`
   stream.
5. Returns a permanent URL.

`dark publish --dry-run` for a check without push.

`dark unpublish` is **not a thing**. Once published,
permanent. (Critical to avoid the "left-pad" disaster — a
single unpublished package broke half of npm.) Yanked
packages can be marked deprecated, but not removed. Anyone
who imported them keeps working.

## Maintain — `dark release`

```
$ dark release Mycorp.JWT
Current: v1.4.0 (3 commits ago)
Changes since v1.4.0:
  + add Ed448 support (commit 4d2f1aa)
  ~ fix HS512 edge case (commit 3a9b2cc)
  - deprecate verifyLegacy (commit 1d8f4ee)

Detected changes affect public API: minor + breaking
Suggested version: v2.0.0 (breaking deprecation)
Override? [v2.0.0] _

Release notes (auto-extracted from commits):
  ## Breaking changes
  - Mycorp.JWT.verifyLegacy is deprecated; use Mycorp.JWT.verify

  ## New features
  - Ed448 support (alongside Ed25519, RS256, HS256, HS512)

  ## Bug fixes
  - HS512 sig validation accepts variable-length keys per RFC

Edit notes? [Y/n] _
... user edits notes in $EDITOR ...

Release v2.0.0 of Mycorp.JWT? [Y/n] y
Publishing...
✔ v2.0.0 published. Notify subscribers? [Y/n] y
   42 subscribers notified.
```

Detection of "what changed":
- Daemon walks ops since last release.
- Categorizes: new public fns / removed / signature changes /
  deprecations.
- Suggests semver bump:
  - any signature change → major
  - new public fns → minor
  - bug fixes only → patch

Subscriber notification: per iter 03's session-aware grants,
"subscribers" are accounts that imported this package. They
receive a `package-updated` notification in their daemon;
their next `dark update` shows the new version.

## Trust and verification

### Author identity

Each package is signed with the author's key:
- Free tier: account-bound key (Ed25519 generated at signup).
- Verified author: account email confirmed via challenge;
  shown with green checkmark.
- Org account: company-verified (DNS TXT record proof).
- Package may be signed by multiple authors (org maintainers).

Importers verify on fetch:
- Signature matches stored author public key.
- If mismatch: refuse import with explicit error.
- If author key has been rotated: verify against the rotation
  history (hub-managed).

### Reputation and warnings

Each package gets surfaced metadata:
- Number of imports (downloads).
- Star count.
- Maintainer activity (last commit, last release).
- Open issue count (if using Dark-hosted issues).
- Community ratings: 1-5 stars + optional review.
- Auto-flagged warnings:
  - "Author hasn't released in 12 months" (potentially abandoned).
  - "Less than 10 imports total" (untested in the wild).
  - "No tests in package" (quality concern).
  - "Imports a known-vulnerable transitive dep" (security).

`dark search` shows these inline. Importing a flagged package:

```
$ dark import Bob.SimpleJWT
⚠️  Bob.SimpleJWT v0.1.0 has 2 warnings:
    - "Less than 10 imports total"
    - "Author hasn't released in 12 months (last: 2025-05-08)"
Continue? [y/N]
```

### Optional security audits

Public packages can opt into automated security audit. The hub
runs a fixed Dark fn (defined in `Hub.Audits.security`) over
the package's ops. Output is a structured report.

`dark search Mycorp.JWT --audit`:

```
Audit report (run 2026-05-08):
  ✔ No use of unsafe primitives
  ✔ No outbound HTTP without explicit grant
  ⚠ Uses Stdlib.Crypto.legacyMD5 (deprecated)
  ✔ No PII handling without trace redaction
  Risk: Low
```

Audits can be re-run. Sponsors / orgs pay for "verified" audit
status (a manual review pass on top).

## Versioning model

### Three pointer types

```
@ commit-hash    immutable, exact bytes — strongest
@ vX.Y.Z         immutable, points to a specific commit
@ HEAD           mutable, latest commit at fetch time
```

In source code: prefer `@ vX.Y.Z`. Use `@ HEAD` only for
internal experimentation.

### Semver enforcement

The hub enforces semver-on-publish:
- Major bump required if signatures change.
- Minor bump required if new public fns added.
- Patch bump only if all fns unchanged in signature.
- Forbid: re-publishing same version with different content.
- Forbid: regressing version numbers.

Validation runs at `dark publish`; fails the publish with a
clear message.

### Dependency resolution

A project imports `Mycorp.JWT @ v1.4.0`. That package internally
imports `Foo.Crypto @ v0.5.0`. Project also directly imports
`Bar.Utils @ v2.0.0`, which imports `Foo.Crypto @ v0.6.0`.

Two versions of `Foo.Crypto`. What now?

**Approach 1: Allow.** Both versions exist side-by-side
in the daemon's package store; each importer gets the version
it asked for. Zero conflicts.

This is genuinely possible because Dark fns are content-
addressed: the same name with different hashes coexists. The
F# / SQL world rejects this; we don't have to.

**Approach 2: Force resolution.** Pick one version (say, the
higher), warn on incompatibility. Recommend authors stay
backward-compatible.

Recommendation: **approach 1.** Two versions of the same lib
side-by-side is fine because there's no global namespace
collision (every fn-hash is unique). Cost: more disk space
(two copies cached). Worth it for zero-pain dependency hell.

This is unique to content-addressed languages. Take advantage.

### Package metadata

Each package has a metadata file (a Dark value):

```dark
let metadata : Package.Metadata = {
  name = "Mycorp.JWT"
  version = "1.4.0"
  description = "RFC 7519 implementation, async-safe, multiple algos"
  authors = ["alice@anthropic.com"]
  license = "Apache-2.0"
  tags = ["auth"; "crypto"; "rfc7519"]
  homepage = "https://github.com/mycorp/dark-jwt"
  documentation = "https://docs.mycorp.com/jwt"
  repository = "https://github.com/mycorp/dark-jwt"
}
```

Indexed by hub for search.

## Composition: apps using apps

Two distinct flavors:

### As a library (in-process)

```dark
import Mycorp.JWT @ v1.4.0

let handle (req: Http.Request) : Http.Response =
  let token = req.headers |> List.find (fun (k, _) -> k == "Authorization")
  match token with
  | None -> Http.unauthorized
  | Some (_, t) ->
      match Mycorp.JWT.verify t myKey with
      | Ok payload -> Http.ok payload
      | Error _ -> Http.unauthorized
```

Fns are called directly. No RPC. Same daemon, same process.

### As a service (cross-app RPC)

A user runs `Mycorp.UserService` as a service on dark.run.
Another user wants to call its API:

```dark
import Mycorp.UserService.Client @ v1.0.0

let listMyTeam (req: Http.Request) : Http.Response =
  let users = Mycorp.UserService.Client.listUsers (filter = "team:engineering")
  Http.ok users
```

`Mycorp.UserService.Client` is a thin Dark module the service
author publishes alongside the service. Calls into the client
fn turn into HTTP RPC to `myservice.dark.run/api/users`.

The author of `UserService` provides:
- The service's running implementation (handlers).
- The client lib (typed wrappers).
- Version compatibility metadata.

Importers don't have to know about HTTP details — just call the
typed fns.

This is "Stripe SDK for arbitrary user services," automatic.

### Interop modes

Three modes for any cross-app call:

1. **Same daemon.** Caller and callee share a daemon
   (same machine, same user, just different apps). Direct fn
   call. Sub-millisecond.
2. **Same hub.** Caller and callee on dark.run. Hub routes
   internally. ~10ms.
3. **Cross-network.** Caller on user's laptop, callee on
   `myservice.dark.run`. Standard HTTP/WS over the internet.
   ~50-200ms.

Same source code; daemon picks the optimal mode at call time
based on locality. The author writes one client; uses everywhere.

## Forking

```
$ dark fork Mycorp.JWT --as Stachu.MyJWT
Forking Mycorp.JWT v1.4.0 to Stachu.MyJWT v0.1.0...
✔ Created Stachu.MyJWT in your local package tree
  Tracking upstream: Mycorp.JWT @ v1.4.0
  Local edits: 0
```

Fork is a copy with a tracked upstream:
- Same code, new namespace.
- Daemon remembers "this was forked from X at version Y."
- I can edit, commit, publish my own version.
- Upstream commits visible: `dark fork-status` shows divergence
  from upstream.
- Upstream patches can be pulled: `dark fork-pull` brings
  upstream changes to my fork (with my edits applied on top).

This is `git fork` semantics. Lower cost than git fork (no
separate repo / hosting); higher value (typed merge of upstream
changes via the conflict-resolution machinery from iter 04).

Useful for:
- Patching a lib I don't own.
- Customizing for my project.
- Temporary fork pending upstream PR.

PR back to upstream:

```
$ dark pr-upstream Stachu.MyJWT
Sending PR to Mycorp.JWT...
✔ PR #42 created: "Fix HS512 edge case"
  https://hub.dark.run/Mycorp.JWT/pulls/42
```

PRs are ops too — sync mechanism reuses sync infrastructure.
Author of upstream sees the PR; merges (auto-applying ops with
attribution); fork can re-sync from upstream.

## Deprecation propagation

Author marks an old fn deprecated:

```dark
[<Deprecated "Use newFn instead — see migration guide">]
let oldFn (x: Int64) : Int64 = ...
```

The deprecation flows:

1. Author's local commit includes the deprecation op.
2. On `dark release`, deprecation propagates to the published
   version.
3. Importers running `dark update`:

   ```
   ⚠ Mycorp.JWT v1.4.0 deprecates 3 functions used by your code:
     - Mycorp.JWT.verifyLegacy (used in Mycorp.MyApp.handler:42)
       → use Mycorp.JWT.verify instead
     - Mycorp.JWT.signLegacy (used in Mycorp.MyApp.api:84)
       → use Mycorp.JWT.sign instead
     - Mycorp.JWT.parseUnverified (used in Mycorp.MyApp.utils:12)
       → use Mycorp.JWT.parse instead

   Auto-migrate? [Y/n]
   ```

Auto-migration: if the deprecation op carries a `migrate-to`
expression (e.g., `verifyLegacy x → verify x defaultKey`), the
update can apply the rewrite to importers' code. With diff
preview before commit.

This makes ecosystem evolution painless. The library author
deprecates with a migration; importers upgrade in one command.

## Per iter 03's privacy: package visibility

```
public         in search; anyone can import
unlisted       not in search; anyone with the URL can import
private        explicit grants required; not in search
org-internal   visible to org members only
```

Default for new packages: depends on account type.
- Free tier user: defaults to public; can opt-in to unlisted
  (with limits).
- Indie+ tier: free choice.
- Org tier: defaults to org-internal; explicit publish step
  for public.

## Money: who gets paid?

The ecosystem has a few revenue paths:

### Free open-source packages

Anyone can publish; importers fetch for free. Hub absorbs
storage cost (cheap). Most packages are this.

### Paid packages

Author sets a price ($X/month per importer, or $X one-time).
Hub handles billing, takes a cut (10-30%).

```dark
import Mycorp.PremiumWidget @ v1.0.0  // requires active license
```

Importing on dark.run: license verified via daemon → hub.
Importing on user's laptop: license cached, periodically
re-verified.

### Service-as-a-package

Author runs a service; importers pay per call (or per month
unlimited). Author sets the rate; hub bills + takes a cut +
remits.

This is "AWS Marketplace for Dark services" — friction-free
monetization for service authors. Compare to Stripe writing a
billing layer themselves; here it's two hub-managed configs.

### Open-source with optional support

Free import; optional paid support tier ($X/month for
priority bug fixes, on-call SLA, etc.). Author advertises in
package metadata; hub facilitates billing.

## Open questions

1. **Spam.** Drive-by package publishing for SEO. Mitigation:
   require non-trivial test coverage, descriptions, valid
   namespace. Hub marks low-quality packages as such.
2. **Squatting.** User registers `Mycorp.MyLib` despite not
   being Mycorp. Mitigation: namespaces tied to verified
   identities; `Mycorp.*` namespace gates on org verification.
3. **Trademark / IP claims.** Standard takedown process. Hub
   has authority to mark a name as disputed; original publisher
   can move to a different namespace.
4. **Imports of imports.** Transitive deps and what triggers a
   re-import. Recommendation: lazy fetch on first reference;
   cache; invalidate on `dark update`.
5. **Cross-language interop.** A user wants to call a non-Dark
   service. Foreign-fn-import: define a Dark wrapper that
   makes HTTP calls; treat the wrapper as a normal Dark
   package. Same model as service-as-package, with a
   different implementation.
6. **Bandwidth caps.** Free tier hub fetches limit per month
   per account. Paid users uncapped. Local cache mitigates.
7. **Mirrors.** Org-internal mirror of the hub. `Mycorp` runs
   `mirror.mycorp.com` for offline / firewalled environments.
   Same protocol; private peer of the hub.
8. **Search ranking.** What signals matter? Star count, import
   count, recency, author reputation, audit status. Could
   become a SEO/gaming target. Iterate. Algorithm in Dark on
   the hub (per iter 11 — projection-as-Dark — search ranking
   is just a fn).

## TL;DR

Discovery via hub-hosted search. Use via `dark import @
version`. Author via `dark publish`; releases are signed,
immutable, semver-checked. Forks are first-class with typed
upstream-merge. Deprecations propagate to importers with
auto-migration. Per-stream privacy controls visibility.

The unique advantages over npm/cargo/etc.:
- **No lockfiles** (version pins are content hashes).
- **No dependency hell** (multiple versions coexist).
- **Typed cross-service calls** (libs and services share the
  same import).
- **Auto-migration on deprecation** (libraries can ship code
  rewrites).
- **Forks as first-class** (sync upstream, push back as PR).

The hub itself is a Dark app, eating its own dogfood (per iter
12). Search, audit, billing — Dark code, hot-swappable, debug-
gable via traces. The hub team's debugging surface is the same
as any user's — full eat-your-dogfood culture.

This iter's biggest insight: **content-addressing the package
store collapses dependency hell.** Two versions coexist
without conflict; every import is reproducible; lockfiles are
unnecessary. That alone is a generational improvement over
npm, and it falls out of the architecture for free.
