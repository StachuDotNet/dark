# url-shortener-cli

**Goal:** Provide a CLI URL shortener whose `add`/`get` mappings persist across separate process invocations.

**Kind:** greenfield

## Acceptance criteria
- [ ] `url-shortener-cli add https://example.com` prints a 6-character slug matching `[a-zA-Z0-9_-]{6}` and exits 0.
- [ ] A slug created by `add` is retrievable by a separate-process `get <slug>`, printing the original URL exactly (the load-bearing persistence test).
- [ ] `get <unknown-slug>` exits non-zero with a clear "not found" error.
- [ ] `add not-a-url` (no scheme) exits non-zero with an "invalid URL" error; `add ""` exits non-zero.
- [ ] Calling `add` twice on the same URL succeeds both times (same-slug or new-slug each time both acceptable).
- [ ] Slug collisions against a different existing URL never silently overwrite — the program retries or errors.
- [ ] Produced slugs are URL-safe, matching `[a-zA-Z0-9_-]{6}` exactly.
- [ ] An optional `list` subcommand, if implemented, prints all mappings one per line as `<slug>\t<url>`.
- [ ] An unknown subcommand exits non-zero with usage.
- [ ] `url-shortener-cli --help` exits 0 with usage.
