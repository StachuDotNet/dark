# Darklang Package Manager Refactor — Unified Prompt & Analysis Document
## For Local Code-Aware Agent (CLI) — Review, Diagnose, Recommend, Fix

---

# 0. Purpose of This Document

This file is a **single, consolidated prompt** for a local code-aware AI agent.  
It captures:

- your **overall goals**,  
- the **problem space**,  
- the **state of the current WIP branch**,  
- the **decisions that need to be made**,  
- the **technical constraints**,  
- the **known blockers**,  
- the **progress so far**,  
- and the **actions needed to finish the refactor**.

This is **not a final design**.  
It is a **prompt for the agent** to:

1. Review all code in this branch (committed, staged, unstaged).  
2. Diagnose mismatches, errors, half-converted code, dead paths, and regressions.  
3. Identify missing pieces, structural issues, and conceptual mistakes.  
4. Recommend specific fixes, PR cleanup steps, and code changes.  
5. Help converge toward a **working, test-passing, merge-ready solution**.

---

# 1. High-Level Goals (Updated)

The two driving goals:

## **Goal A — Complete the refactor to hash-based identity**  
Meaning:

- All package items (types, fns, values) are referenced by **Hash**, not UUID.  
- Hash = deterministic, content-addressed identity.  
- Names and locations are entirely separate (via SetName ops).  
- Builtins no longer rely on hardcoded hashes in F#.  
- ProgramTypes and parsing stages support structural hashing.  
- Mutually recursive functions/types hash deterministically via SCC logic.

## **Goal B — Finish, clean up, and merge the current branch**
Meaning:

- CLI must work again.  
- Tests must run and pass.  
- Big parsing workflows must work (WT2PT, PT2RT, package loading, etc.).  
- No more partial-UUID → hash mixtures.  
- No more temporary hacks.  
- Eliminate dead abstractions.

These two goals are tightly linked.

---

# 2. Problem Space (Full Summary)

The Darklang Package Manager must support:

- deterministic sync across instances  
- stable content-addressed identities  
- recursive definitions  
- distributed editing  
- merging branches and sessions  
- human-friendly naming separate from identity  
- program execution referring only to structural identity  

This requires changes across:

- ProgramTypes  
- WrittenTypes → ProgramTypes transition  
- hashing algorithm  
- parser  
- PackageManager  
- DB schema  
- builtin handling  
- naming and location logic  
- meta fields (description, deprecation)  
- CLI tooling

---

# 3. Three Major Known Blockers (Restated)

Any solution must resolve these.

## 3.1 Name Sensitivity in Hashing  
Old attempt included names to differentiate structural types.  
Result: renames caused identity churn.

Need:  
Decide whether identity = pure structure or structure + stable concept tag.

## 3.2 Builtins Hardcoded in F#  
Every change created breakage.  
Need:  
Stable way for runtime to reference builtins without embedding their hash.

## 3.3 Mutually Recursive Functions  
Hash cycles wrecked determinism.  
Need:  
SCC hashing across all mutually recursive functions (and possibly types).

---

# 4. Progress So Far (What Exists in the Branch)

Your branch currently includes:

- Large-scale changes toward using `Hash` everywhere.  
- Many ProgramTypes adjustments.  
- Some incremental hashing logic.  
- ESelf added for recursion.  
- Partial removal of UUID usage.  
- Updated parsers (in progress).  
- PackageOps being rewritten or partially adjusted.  
- Tests currently failing.  
- CLI broken in parts.  
- Hash computation incomplete or inconsistent.  
- Many commented-out sections pending decisions.  
- Temporary hacks still in place.  

The branch is **mid-refactor**, not yet consistent or converged.

---

# 5. TODOs for Local Agent — Review Checklist

The agent should:

### **5.1 Scan entire branch for:**
- remaining UUID references  
- mismatched identity models  
- NameResolution structures still assuming UUID  
- Incomplete transitions in PT, WT2PT, RT  
- Code paths that assume identity = location  
- Tests referencing old identity semantics  
- CLI paths referencing UUID-based item lookups  
- Hash computation stubs  
- Dead code left behind  

### **5.2 Report on:**
- Where hashing logic is incomplete or incorrect  
- Whether structural normalization is implemented  
- Where order-dependence still leaks into hashing  
- Whether ESelf is sufficient or SCC analysis is needed now  
- Where builtin references are wired incorrectly  
- Which parts of WT2PT and PT are inconsistent  
- Where references are mixed-hash/UUID  
- Failure modes currently preventing CLI/test runs  

### **5.3 Suggest specific refactors:**
- exact places to convert to new identity model  
- recommended hashing module structure  
- recommended SCC detection insertion points  
- recommended builtin handling model  
- recommended edits for PT and PackageManager  
- recommended cleanup for tests and CLI  
- recommended invariants  

### **5.4 Provide migration/cleanup suggestions:**
- which files can be deleted  
- which types should be merged or eliminated  
- which data paths need explicit rewrites  
- which modules need renaming  
- where to insert assertions  

---

# 6. Decisions That Need To Be Made (Agent Should Identify Implications)

The local agent should comment on tradeoffs regarding:

## **6.1 Identity Model**
- pure structural hash  
- hash + concept ID  
- hash + category tag  
- location-free identity  

## **6.2 Metadata Model**
Should descriptions, examples, and deprecations be attached to:

- definition (hash)?  
- location?  
- concept ID separate from definition?  

## **6.3 Builtin Strategy**
Options the agent must evaluate:

- small DB of builtin definitions → hashes  
- embedded AST definitions  
- "builtin namespace" hashed as synthetic canonical value  
- seeded mappings with fake hashes  

## **6.4 Parser Resolution Strategy**
- multi-phase WT → PT → hashed PT → RT?  
- direct PT hashing?  
- placeholder references resolved post-hashing?  
- partial SCC extraction in WT2PT?  

## **6.5 PackageOp Format**
Should PackageOps:

- represent SCCs explicitly?  
- store placeholder identities until hashed?  
- enforce canonical hash?  

## **6.6 Bootstrapping**
How:

- builtins initialized  
- initial DB seeded  
- F# runtime references builtin items  

---

# 7. Specific Requests for Local Agent

The agent should produce:

### **7.1 A full diagnosis report**
- list of remaining UUID-based references  
- broken or inconsistent hashing paths  
- bugs or regressions introduced  
- mismatched invariants  

### **7.2 A prioritized TODO list**
- blocking fixes  
- non-blocking cleanup  
- required design decisions  

### **7.3 Suggested code changes**
- functions to rewrite  
- data types to modify  
- modules to split or collapse  
- specific lines to change or delete  

### **7.4 A proposal for completing the branch**
- finalize identity model  
- finalize hashing algorithm  
- finalize builtin strategy  
- ensure CLI works  
- ensure tests work  
- ensure big parsing workflows (e.g. large modules) work

### **7.5 Merge-readiness evaluation**
- identify risks  
- identify missing tests  
- identify API changes  

---

# 8. Prompt Style Instructions

The agent should:

- Prefer structured, highly technical reports  
- Cross-reference specific files and line numbers  
- Produce lists of missing invariants  
- Suggest explicit code edits  
- Avoid abstract descriptions—show concrete diffs  
- If helpful, request additional info from you  
- Document assumptions  

The agent may also:

- propose new invariants  
- propose interface simplifications  
- propose schema changes  
- propose refactoring WT2PT/ProgramTypes/Parser  
- propose hashing module design  

---

# 9. Final Section — Open Questions and Notes (Verbatim)

```
maybe PT stuff should be managed wholly in UserDBs and dark-managed sqlite tables. idk

how do Descriptions relate to package items and locations?
are descriptions copied over?
are descriptions bound to a definition, or location, or combination?
how are they iterated? By Ops?
what about 'examples'? how do those fit into things?

we could seed initial package items with F#-encoded ASTs.
could embed .db in executable.
do we have mutually recursive builtins?

do we need a concept distinct from definition and location?
```

The agent should weigh all of this against your branch.

---



---

# 10. Core Concrete Questions for the Agent to Address

The agent should explicitly think about and comment on the following:

## 10.1 ProgramTypes.TypeReference

- How should `TypeReference.TCustomType` change now that package items are referenced by hash?
- Should `TCustomType` store:
  - `NameResolution<FQTypeName.FQTypeName>` where `FQTypeName.Package = Hash`?
  - Anything else (e.g. concept ID, category tag)?
- Does `TFn` need to change to be more hash-aware, or can it remain as a pure structural function type constructor?

## 10.2 Expr Variants and References

For each of these, what exactly should the referenced payload be in a fully hash-based world?

- `EFnName`  
  - How should it represent resolved names?  
  - Should it hold `NameResolution<FQFnName.FQFnName>` with hash-backed package refs only?

- `ERecord`  
  - How should it represent the record type?  
  - Should it reference the record’s type via hash-based `FQTypeName`?

- `EEnum`  
  - How should enum type references and case names be represented?  
  - Do we allow multiple locations pointing at the same enum hash?

- `EValue`  
  - How should it carry a reference to a package-level value by hash?

- `ESelf` / `EArg`  
  - Are `ESelf` and `EArg` sufficient to express recursion and arguments in the new model?  
  - Do we need an additional construct (e.g. “cycle index” expression) to handle mutually recursive fns and/or types cleanly?

The agent should look for **inconsistent or incomplete transitions** among these expression forms.

## 10.3 Ops and PackageOp

- How, if at all, should `PackageOp` change to support:
  - SCC-aware hashing,
  - purely hash-based identities,
  - and removal of UUID usage?
- Are new ops needed (e.g. `AddFns` batch op for mutually recursive definitions)?
- Should existing ops like `SetTypeName/SetFnName/SetValueName` be extended or simplified in light of the new identity model?

## 10.4 DB Schema

- Does the current DB schema support:
  - hash-based identity as the primary key,
  - location → hash mappings,
  - possible “concept” layer (definition vs location vs concept),
  - metadata binding (descriptions, examples, deprecations)?

- What DB changes (new tables, columns, constraints) are implied by:
  - moving off UUIDs,
  - supporting SCC hashing,
  - representing builtins as first-class package items?

The agent should outline any **necessary schema migrations** or adjustments and how they interact with PackageOps and the hashing model.



# End of Unified Prompt Document
