# Cross-cutting test criteria

These conventions apply to **every** project, regardless of category. They are
deliberately kept out of the individual spec files so each spec stays a tight
goal + acceptance-criteria list. A later step decides where these finally live.

A project is "good" if it satisfies all of the following:

1. **Argument parsing.** `--help` / `-h` exits 0 with usage. An unknown flag
   exits non-zero, ideally with a suggestion.
2. **Exit codes.** 0 on success, 1 for a generic error, 2 for a usage error,
   3+ for domain-specific failures; documented in help.
3. **Error messages.** Errors go to stderr, one line, no stack trace unless
   `--verbose`.
4. **Streams.** Piped stdin works; a TTY stdin prompts or shows help; piped
   stdout emits no ANSI unless `--color=always`.
5. **Idempotency.** For state-mutating tools, running twice yields the same
   observable state.
6. **Encoding.** UTF-8 in, UTF-8 out. Binary stays binary (no implicit decode).
7. **Signals.** On SIGINT: cursor restored, temp files cleaned, non-zero exit.
8. **Performance envelope.** A documented, measured budget (e.g. "<500 ms for
   10k-line input"), not a guess.
9. **Packaging.** Runs from a clean clone via the project's standard run entry
   point; any required environment is documented in `--help`.
10. **Reproducibility.** Tests seed RNG, virtualize the clock, and fixture-mock
    HTTP so runs are deterministic.

## Notes on shared verification patterns

- **External-verifier interop.** Where a project produces a standard artifact
  (archives, signed tokens, wire protocols), correctness is judged by an
  independent stock tool (`tar`/`unzip`, a reference JWT library, `redis-cli`),
  not by the project verifying its own output.
- **Mutation checks.** For library-shaped projects, substituting a naive
  implementation (e.g. `String.split(',')` for a CSV parser, an identity
  function for `group`, an early-return for an error-accumulating `apply`)
  should make at least one acceptance criterion fail — otherwise the criteria
  are too weak.
- **Cross-language determinism.** Where multiple language implementations of the
  same project exist, seeded/fixed-input output should be byte-equal across them.
