# PDD (prompt-driven development)

> An experimental fork of Darklang where the interpreter materializes its own source code on demand via LLM, in parallel, speculatively, with traces as the durable artifact.

**Branch:** `pdd` (local-only, off `main`, never pushed).

## What it is

Free text becomes running code. You hand the system a request; an LLM decomposes it into a Dark expression, and any unresolved function names auto-materialize via LLM, in parallel, until the interpreter can run the result. Caches mean a repeated prompt skips the LLM entirely. A tiny taste:

```bash
$ dark prompt "compute doubleIt of 4"
DInt64 8L
```

It works end-to-end today on real cases — recursion, list transforms, even a CSV column demo — and there are known gaps (LLM-natural FP idioms with tuples and non-existent Stdlib names still trip the parser). See `CLAIMS.md` for the reframed core claims and `FRONTIER.md` for where the F# substrate should grow.

## Where it stands

This is a spike, currently resting. We do **not** anticipate a wide surface of PDD commands. The shape we expect: `dark prompt "<request>"` starts a **background agent** that builds the thing, and the CLI drops into a **watching** state (with the option to let it keep running in the background). The command itself is waiting on real implementation — so we are deliberately **not** over-building the command surface now. Most of what the early spike spread across `dark pdd ...` subcommands should instead be automatic (cache) or fold into normal SCM (promote, history, diff, revert).

## Reading order

See `TOC.md` for the full design loop. `git log pdd ^main` is the source of truth for the actual diff.
