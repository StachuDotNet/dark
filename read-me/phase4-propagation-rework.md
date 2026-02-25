# Phase 4 — Propagation Rework (future PR)

With deterministic hashing in place, there may be opportunities to
simplify or evolve the propagation design.

- [ ] Rethink: do we still need `PropagateUpdate` as an op type, or
  is it just a batch of `AddFn + SetLocation` ops?
- [ ] `SetLocation` is idempotent — can we simplify the repointing logic?
- [ ] Rewrite `Propagation.fs` if a cleaner design emerges
- [ ] Rewrite `AstTransformer.fs` for hash-based references
- [ ] Rewrite `PackageOpPlayback.fs` propagation handling
- [ ] Tests:
  - F# (`Tests/`): same propagation input -> same results (determinism)
  - Darklang (`testfiles/`): change a dep, verify dependents update
- [ ] Verify: full test suite passes
