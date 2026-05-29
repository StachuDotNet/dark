---
title: dark suggest <natural-language>
section: 3.1 Discovery
priority: P1
harness_signal: tokens-to-first-relevant-fn (downstream of search)
phase: Phase 1 unblocked / Phase 3+ stretch (embeddings)
---

# `dark suggest <natural-language>` — affordance for "what handles X?"

**Problem**: agents know what they want to do ("parse JSON", "hash a string") but not which module does it. They typically end up calling `docs stdlib` then drilling into 4–5 modules. `Agent Next Steps.md` calls this out as item #4 (token budget).

**Proposed fix Phase 1**: a thin wrapper around full-text search across `view <name>` doc strings, scored by overlap. **Unblocked today** — verified iter 30 that `Darklang.LLM` is rich (11 example modules, multi-provider) but full-text doesn't need any of that — `Stdlib.String.contains` over package-tree docstrings is enough.

**Proposed fix Phase 3+ stretch**: replace with an embedding-based search using `textEmbedding3Small` or equivalent. **Blocked today** — verified iter 30 that `Darklang.WIP.AI.OpenAI.Embeddings` has the types (`CreateEmbeddingResponse`, `textEmbedding3Large/Small/Ada002`) but the **Functions list is empty**. WIP namespace; no callable embedding fn exists. Stretch waits until that ships.

**Harness signal**: tokens-to-first-relevant-fn drops further; the metric `dark search` invocations per run drops (replaced by one `dark suggest` call).

**Adjacent finding (iter 30)**: `Darklang.LLM.Examples.CodeAgent` exists as prior art for an in-Dark coding agent. **Worth a future survey iteration** — its system prompt / tool-use patterns may inform our §4.7 harness prompt template, and our migration-to-Dark milestone (when the harness eventually ports to Dark per §4.0).
