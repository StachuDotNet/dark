# Advisor doc — final review pass

State: HTTP router source inserted under the `serve` demo, stream
composition example inserted under "Composable like a list…".

What follows is a tight pass on what's left. Nothing structural — just
the small things to look at before sending.

---

## A. Loose ends I'd resolve before sending

### A.1 — `vuszcOeLzCu-3cqEum0pctuA` is now a stub

It's the very first item under "We got some good on-track work done…":

> "We've been building various projects with Agents+Dark, taking notes
> around problems faced, recording #s of tokens used, time-to-done,
> etc."

Used to have the bench-corpus children under it; you moved those into
the new section `5NSZqssAiK5Zzpso4ySN95WT`. Now it's a one-line stub
with no kids, immediately followed by an empty spacer and then the
"We've solved some of the direct pain points…" sub-header.

Two options:
- **Delete it** — the bench corpus section now carries this framing.
- **Make it a one-liner lead-in** — e.g., "We've been running agents
  through a small corpus of projects, recording tokens / time / pass-rate
  (see below)." — and rely on it to tee up the bench section that
  comes near the end.

I lean delete. It currently reads like a leftover.

### A.2 — Bench-corpus section is far from the routine + numbers it pairs with

Right now within section 1 the order is:
1. `vuszcOeLzCu-3cqEum0pctuA` (stub)
2. **Wins** — Blobs/Streams, HTTP, docs, deprecate, typechecker, package deps, tracing
3. **Identified pain points** (the long flat TODO list)
4. **Routine** (`fAjwdyFNAGeAs9zQsdd_b6xz`) + harness convergence (`sIksM-…`) + rough numbers (`_waewBtnN-…`)
5. `5NSZqssAiK5Zzpso4ySN95WT` **Bench corpus**

The bench corpus is the *what* that the routine + numbers are *about*.
Reading top-to-bottom, the advisor sees "we run an A/B test → here
are rough numbers → oh, on these projects". That's backwards.

Action: **move `5NSZqssAiK5Zzpso4ySN95WT` to sit immediately after the
routine** (`fAjwdyFNAGeAs9zQsdd_b6xz`), before harness/numbers. Or
even before the routine — "here's the corpus" → "here's how we run
it" → "here's the rough output". Smaller move, big readability win.

### A.3 — `Q9dS6x43y1NlhnqKleU_EzAE` is still a thin section header for one item

"And made some improvements that should eventually improve the AI's UX"
introduces only the **tracing expansion** bullet. Either:
- Delete the lead-in (tracing becomes another peer-level "we've solved"
  item), OR
- Add a one-liner about, say, the better `dark search` ranking work
  if anything else fits.

Lowest effort: delete the lead-in.

### A.4 — Wording inside the *Annoying blocker* section is repetitive

The first three bullets under `6LKm6XjKkSVjbeV9PLUlxnld` all say "I want
Dark daily; sync between machines is the blocker":

- `p8RiugkdCxRVI6UElykf2w_4` "I actively want to use Dark to manage my
  personal software -- local .sh scripts I run etc"
- `71PcqL1dchiRHGz4mU6IrPlQ` "the primary thing blocking MY usage of
  darklang all day long is inability to sync my experience between
  machines"
- `V6MCgr0BmK9mYdGePD0dr8iW` "for my .sh scripts, I just sync them with
  syncthing or git depending on the context, but I have no real way to
  do this, so far, with Dark code"

You said earlier (when I proposed this) you wanted them all kept. Now
that I'm reading the doc cold, the redundancy reads as
emphasis/sincerity rather than padding — your call. If you want it
tighter, two bullets suffice (combine the first two; keep the
syncthing line).

### A.5 — `QWaMsYGl2sQEAxP3LnmWpKZQ` "build more things in Dark; aggressively expand Matter" with child "libraries and apps"

The "libraries and apps" child overlaps with `jr4hpnlC-jQ6pJfJIrtXK14o`
"Diversify the bench" (which also calls out library ports + multiple
app shapes). Two options:
- Delete `QWaMsYGl2sQEAxP3LnmWpKZQ` entirely — Diversify already
  covers it.
- Or tighten its content: "expand Matter — libraries first, apps
  second" makes the prioritization explicit instead of implicit.

### A.6 — `KJVY8CNWveezM3LO_wJ9VlOC` is a sibling of Blobs/Streams but at the same indent

> "Rest of codebase adjusted accordingly -- updated file IO, HTTP
> client+server, crypto/bas64 stuff, etc."

Sits as a peer of the *Blobs* and *Streams* subsections. Reads fine
as "and we updated callers"; the only nit is the typo: **bas64** →
**base64**.

---

## B. Spelling / minor

| Item | Issue |
|---|---|
| `KJVY8CNWveezM3LO_wJ9VlOC` | "crypto/**bas64**" → "crypto/**base64**" |
| `aC1ng_GaD18JJU2QgTHu0EgC` child `ECcry2WBuI_m74O3y6TxXe63` | "(unless you put a flag on saying 'harmful fns ok')" — fine, but the parenthetical-as-its-own-bullet is awkward; could merge into parent: "functions marked Harmful halt at runtime — unless you flag 'harmful fns ok'." |
| `Pjedzo7sJYC0WdCUHo7DG4R8` | Now reads "context: had a hacky streaming httpclient (separate from our main one) which we used to stream LLM responses, and no other context in which streaming was available". Long sentence. Could cut to: "Context: had a hacky one-off streaming HttpClient for LLM responses; nothing else streamed." |

---

## C. The two new code blocks I just inserted

### Router source (`JmDV8XfGZYdwahOq8gmnRdRJ`)

Lives as a sibling under the HTTP server section, right after the
`serve` demo. Light edit to the source: dropped the `// CLEANUP`
comment, dropped the `rootHandler` (kept the two used in the demo
output), kept the type annotations and the explicit `Handler`
constructor — they're load-bearing for "this is just Dark code".

If you want it tighter still, you can drop the type annotations on
`helloHandlerFn` / `echoHandlerFn` (Dark infers them) — but I'd keep
them in for the advisor since they make the API obvious at a glance.

### Stream example (`qoM8YLgkLjsZLbtyHWiZ8oV6`)

Sits right after "Composable like a list…", which is where the claim
to demonstrate is. Picked the simple `filter / map / take / toList`
chain over the SSE example because it's runnable and matches the
literal API listed in the parent bullet.

If you want a more "this is what we actually use it for" example
instead, I can swap to a sketch of streaming an LLM response through
`Sse.parse`. Less concrete, more representative.

---

## D. Suggested apply order

Smallest first, all low-risk:

1. Fix typo `bas64` → `base64` in `KJVY8CNWveezM3LO_wJ9VlOC`
2. Delete `vuszcOeLzCu-3cqEum0pctuA` (A.1, the stub)
3. Delete `Q9dS6x43y1NlhnqKleU_EzAE` (A.3, thin lead-in)
4. Move `5NSZqssAiK5Zzpso4ySN95WT` to land after the routine (A.2)
5. Decide on A.4 (blocker-section trim) and A.5 (Matter overlap)

Say which of these you want and I'll apply in one pass. Otherwise, the
doc is in shape to send.
