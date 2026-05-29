# validation-applicative

**Goal:** Provide an error-accumulating `Validation` applicative type (distinct from `Result`) plus a thin CLI driver exercising it.

**Kind:** greenfield

## Acceptance criteria
- [ ] Exposes a `Validation<err, a>` ADT with `Valid(a)` and `Invalid(List<err>)` constructors.
- [ ] Exposes `valid`, `invalid`, `map`, `apply`, `combine2`, `combine3`, and `toResult` with the documented signatures.
- [ ] `apply` handles all four cases; when both arguments are `Invalid`, it concatenates the error lists rather than picking one.
- [ ] `validation-cli combine2-int 3 4` prints `7` and exits 0.
- [ ] `validation-cli combine2-int 3 abc` prints `not-int: abc` and exits 1.
- [ ] `validation-cli combine2-int xyz abc` prints both errors on separate lines and exits 1 (accumulation, not short-circuit).
- [ ] `validation-cli combine3-int 1 2 3` prints `6`; `combine3-int x 2 y` prints two errors and exits 1.
- [ ] `validation-cli form name=Alice email=a@b.com age=30` prints `OK` and exits 0.
- [ ] `validation-cli form name= email=invalid age=-5` prints all three errors (empty name, no `@`, non-positive age) and exits 1.
- [ ] `validation-cli to-result valid:5` prints `Ok(5)`; `to-result invalid:e1,e2` prints `Error([e1, e2])`; both exit 0.
- [ ] `validation-cli --help` exits 0.
