# pretty-printer

**Goal:** Provide a Wadler/Leijen-style width-aware pretty-printer library (a composable `Doc` type with a width-driven `render`) plus a CLI driver over fixed fixtures.

**Kind:** greenfield

## Acceptance criteria
- [ ] Exposes `text`, `line`, `hardLine`, `empty`, `concat`, `concatSpace`, `concatLine`, `nest`, `group`, `render`, `vsep`, and `hsep` with the documented signatures.
- [ ] The same `Doc` renders differently at different widths; `group` collapses soft lines to spaces when content fits and breaks them otherwise.
- [ ] `pretty-cli simple 80` and `pretty-cli simple 5` both print `hello world` (a `text` doc never wraps).
- [ ] `pretty-cli inline 80` prints `a b c`; `pretty-cli inline 4` prints `a\nb\nc` (three lines).
- [ ] `pretty-cli nested 10` indents the inner lines by 2 spaces when broken.
- [ ] `pretty-cli list 80` prints a single line; `pretty-cli list 10` prints one element per line.
- [ ] `pretty-cli record 80` prints all fields on one line; `pretty-cli record 20` prints each field on its own line.
- [ ] `pretty-cli deep 80` fits the whole structure on one line; `pretty-cli deep 30` lets nested groups make independent fit-or-break decisions.
- [ ] No rendered line exceeds the requested width.
- [ ] `pretty-cli simple 0` and `pretty-cli simple -5` exit non-zero (invalid width); an unknown fixture exits non-zero with an "unknown fixture" error.
- [ ] `pretty-cli --help` prints usage and exits 0.
