---
title: tar-zip-creation
tier: S
class: app
modules: [Stdlib.Cli.File, Stdlib.Cli.Dir, Stdlib.Cli.Process]
languages: [dark, ts, py, go, rust]
expected_outcome: fail-likely
known_blockers: [no-archive-creation]
framework_hint: null
core: false
---

# Description

A command-line archive tool: `tar-zip create <archive.tar.gz> <dir>` produces a gzipped tar archive of the given directory. `tar-zip create-zip <archive.zip> <dir>` produces a ZIP archive instead.

The agent picks one or both subcommands; the rubric tests whichever was implemented. The standard format must be honored — a real `tar` / `gunzip` / `unzip` running on the archive must extract it cleanly to a directory whose contents byte-match the original.

For TS, the natural implementation is `tar-stream` + `zlib` (gzip) for tar.gz, or `archiver` / `adm-zip` / `yauzl` for ZIP. For Py, `tarfile` + `gzip` for tar.gz, `zipfile` for ZIP — both stdlib. For Go, `archive/tar` + `compress/gzip` (stdlib) or `archive/zip`. For Rust, the `tar` crate + `flate2` or the `zip` crate. **For Dark today, no compression / archive-creation primitives exist** — `Stdlib.Cli.File.Gunzip` decompresses but there's no symmetric `gzip-create`, no tar header writer, no ZIP central-directory writer.

The cross-language interop check: the archive must be extractable by stock `tar -xzf` / `unzip` — those are the external verifiers Dark can't fake.

# Behaviours

- `tar-zip create out.tar.gz src/` produces `out.tar.gz` containing all files from `src/` (recursive). Exits 0.
- `tar -xzf out.tar.gz -C /tmp/extracted/` succeeds (stock GNU tar, not Dark's parser). Files in `/tmp/extracted/` byte-match the originals in `src/`.
- File modes are preserved (executable bit, at least).
- Symlinks are preserved as symlinks (not dereferenced silently).
- Empty dirs are preserved.
- `tar-zip create-zip out.zip src/` produces a ZIP with the same contents (if implemented).
- `unzip -d /tmp/extracted/ out.zip` extracts identically.
- `tar-zip create out.tar.gz /no/such/dir` exits non-zero, error mentions the missing directory.
- `tar-zip create existing-file.tar.gz src/` overwrites an existing archive without prompting.
- `tar-zip --help` exits 0.
- A 100 MB directory (lots of small files) packs to a similar-size archive in linear time. Wall-clock check: should take seconds, not minutes.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. Create a small fixture directory:
   ```
   mkdir -p /tmp/fixture/{a,b/c}
   echo hello > /tmp/fixture/a/x.txt
   echo world > /tmp/fixture/b/c/y.txt
   chmod +x /tmp/fixture/a/x.txt
   ```
2. `tar-zip create /tmp/test.tar.gz /tmp/fixture` — should produce `/tmp/test.tar.gz`.
3. **External-verifier check**: `tar -xzf /tmp/test.tar.gz -C /tmp/extracted/` (stock GNU tar). Verify:
   - `/tmp/extracted/fixture/a/x.txt` exists, contains `hello`, has the executable bit.
   - `/tmp/extracted/fixture/b/c/y.txt` exists, contains `world`.
   - `diff -r /tmp/fixture /tmp/extracted/fixture` produces no output.
4. `file /tmp/test.tar.gz` should report `gzip compressed data, ...`. If it reports something else, the gzip header is broken.
5. **For Dark specifically**: examine the source. Did the agent:
   - (a) Hand-roll the tar header format + gzip compression? Heroic but unlikely (gzip is non-trivial).
   - (b) Shell out to `tar -czf` / `zip` via `Stdlib.Cli.Process.exec`? Workaround. Acceptable per the workaround precedent in §M.
   - (c) Honestly fail and report the gap?
6. If (b), check the agent didn't *just* call the external tool unconditionally — they should at least be checking arguments and producing useful errors before delegating.
7. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- tar-zip create /tmp/test.tar.gz /tmp/fixture
- tar-zip create /tmp/test.tar.gz /no/such/dir
- tar-zip --help

---

**Why this is `fail-likely`**: Dark has `Gunzip` (decompression) but no compression-side primitive — no gzip-encode, no tar header writer, no ZIP central-directory writer. Agent paths:
- **Sequential bytes-by-hand implementation** — possible in theory (gzip and tar are documented file formats) but absurd as a bench task. Hundreds of lines of bit-twiddling.
- **`Stdlib.Cli.Process.exec("tar", ["-czf", ...])`** — the workaround. Cedes the language-level claim but produces a working artifact.
- **Honest report** + stub error.

**The longitudinal value**: when Dark adds gzip-encode + tar / ZIP writers, this spec flips. Until then, Dark is dependent on shelling out to system tools — which is exactly what the project-survey §1 inventory says about Dark's archival story today.

**Cross-spec note**: `tar-zip-creation` and `parallel-downloader` are both "shell-out workarounds exist" specs. Bench tracks how often Dark agents reach for `Stdlib.Cli.Process.exec` as a workaround across the catalog — could become a §6 diagnostic metric "process-exec-as-workaround count."
