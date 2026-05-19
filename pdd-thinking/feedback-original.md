Following is various feedback on various docs in pdd-thinking.
Record # of files and total LOC before compare against the 'after' at the end of the consolidation/tidying.

create a system to process this feedback over the course of an hour or two. Likely, update this .md file to have a bunch of - [ ] checkboxes, set up a tiny loop command to point to this file, update the file to say "do the next thisng in this list", set up a 10min cron tackling bucket by bucket, etc.


Misc notes, crossing files, to be written/addressed somewhere -- oin the .md files
- an unresolved/unfound name that we try to run the body of should likely yield some sort of 404 'event' in some broad 'event stream' that some above thing is listening for, in a graph of computation
- the algorithm idea should be extracted to its own document, with notes that it's incomplete. I mean really we're just buildiing a recursive coding agent, and hoping that we can take advantages of dark's strenths to make it good. Find and Generate are some strategies towards coding, but I suspect we'll need more than that, and should think from ground up. The psuedocode we include, anywhere, should be higher-level and not very F#y (esp relates to 03 - find vs generate)
- "the trace is the program" is really overstated. The program is still a growing set of package items, and the traces are used in conjunction with the interpreter and agent to turn the initial prompt gradually into working software. anyway
- we should extract the core claims (e.g. source is lazy) to its own document, and remove from others. lots of duplicatino currently, and some conflicts
- don't mention "anti-vision" anywhere
- remove any mentions of like "by 8am" - we're beyond the spike and just looking to tidy notes at this point
- the real implementation of the 'materialization' and such should really be fully written in Dark, on top of SCM+CLI stuff. Some lower-level F# core stuff will need to change to allow for this
- generally tighten notes, but try to keep all useful content
- builtin permissions / capability should be done before starting the real PDD efforts. and the document should be framed as such. but we should hook into that system throughout the eventual PDD work, as we may need to ask for more permissions, etc., throughout the process
- "human in the loop" needs more thought -- not really a "fallback materializer" -- generally the human is useful for all sorts of things: review of code, initial promptinmg and spec-adjustments, review of initial types, writing code, etc. whatever system we have around 'parking' threads should consider that humans' activity may be more async than other stuff, though.
- Tracing.fs has been changed quite wildly. I think we need fewer changes, with exposure via builtins. anyway, reduce surface area design a bit
- we don't actually want so many 'pdd' commands -- we want something more like a claude code experience, but more interactive. anyway, calm down on tracing stuff a bit
- elevator pitches are nice. could tighten a bit, but generally looks nice. just keep that doc to just the pitch(es) though, without "what I'm not claiming" and other BS
- generally I'm not a fan of documents named like "14-demo-programs.md" - I'd rather organize things by folder, in some recursive way, in a way I can naturally dig into whatever I want in any order.
- I'm not a huge fan of "connection to other docs" sections
- suspect we could kill spike budget doc(s)
- I hate the jsonl sidecar. all should exist in the sqlite .db, one way or another (raw sql tables, or more likely a UserDB or other UserDB-like new construct we build out)
- I hate glossaries, remove
- in README.md, I think we can kill the heavy-hitters status section
- hate 'pdd run' and 'pdd demo' and 'pdd cache' commands. that all feels like stuff that should be kinda automatic - I just wanna either prompt for some software and run it, or start an eval of some incomplete code, and have stuff fill it in along the way, involving me as appropriate.
- where somewhere: "each eval is separately debuggable, as it's going. and traces can be replayed+debugged"
- on a conflict of "doesn't exist," currently we (usually) just fail. we need to build our conflicts+resolutions system better. such that in some scenarios we fail, or others we wait for the implementation, parking the thread until ready. the conflicts and resolutions system should be trickled throughout the whole of LibExecution being some new low-level concept
- speaking of low-level concepts, event streams should be a lower-level thing in LibExe as well. I think the agent infra probably rests on top of that. maybe the SCM sync should, too. idk if it makes sense, but are event _graphs_ a thing? with waiters, etc. I like what you did with an EventSink somewhere. but that's probably simpler than what we need, idk
- I think this system should all sit on some 'composable MVU apps' infra -- whatever that means (we have relevant notes somewhere on this machine but not worth digging too deep rn)
- the real point here isn't just fns (per WRAP-UP.md) - it's recursive live development, powered by and integrated with AI, notes, types, values, traces. generate it all as we need it, integrate it into a holistic experience full stack in/for darklang, given its interconnected nature. evaluate the ab;ility to do this, as much as possible in dark, note the lower-level F# changes tahtw ill need to be done to support tghis along with any sql db changes (incl breaking down into multiple DBs)
- write somewhere: "need to build refactors into the lang/system, and involve them in this process somehow"
- reframe "the source is lazy" to "the source often starts as lazy. gradually existant, expanteded, typed"
- darklang.com/gradual should exist. maybe sketch the content somewhere (in a vault?)
- we _should_ track the "done-ness" of some fn, but maybe in a different way to how we've been doing it. at first, it's just an idea, with a name, eventually has: signature, body, tests, connected code, description. we iterate on it all until it feels good.
- maybe we don't need a new ID concept to refer to a fn, but WIP should instead refer to other things by location -- could be helpful simplification
- when WIP becomes real/commitedd, we should update references to be by hash, so they're stable long-term.
- we should do a better job of separating WIP from committed stuff. while also considering we need to sync the branch ops, package ops, etc. How does WIP fit into that world?
- searching for dark matter needs to be SO fast. drafting v0 of ANY code needs to also be SO fast. we need benchmarks around that, etc.
- prompts need to be some kinda low-level concept we respect (not in F# but lower-level darklang code -- it's a special/pinned type, Prompt)
- generally, more stuff like "search for values by type" will likely be involved in supporting this agent. maybe sketch related surrounding stuff somewhere
- maybe: "dark prompt <text> starts a _daemon_ of a PDD recursive coding agent. and we watch it. maybe we spawn it and get back a thread id or something, and start a watcher and some viewer? Idk. we need to make that beautiful, with minimal code and very customizable per-user and environment"
- 'dark pdd promote' shouldn't exist - should just be part of "normal" scm flow, sitting on top of that
- same for dark pdd history. PDD is "considered" as we build the baseline SCM but it's also optional (someone should be able to use dark without AI)
- the coordinator or whatever you called it - that's super core.
- put the pdd-thinking notes into source control if you haven't already
- I don't care about v1/v2/v3 of the system prompt. or generally, any history of how our resaerch on this topic has gone
- in EMPIRICAL.md, I don't care about the project-level section, or the smoke detectors sectino, or pithy line worth kieeping
- somehow, the user should _see_ the highest-level fn, in focus, as parts of it are being filled in etc. Like if I prompt to write me some software, I want to see what's going on. waht's the highlevel thing I should be viewing? which things are resolved, or materializing, or whatever? Sketch various versions of that view/experience, at various points in time. I should be able to 'dive into' anything I want - what AI threads are going, what they're doing, what traaces haverun, what tests are being added, code being materialized. we don't need anything to be rela yet, just high-level pretty sketches
- basically, we want to re-eval until the results feel good. keep faking impl and "continuing" traces (or fresh) as needed
- our csv example, after the prompt, should likely _know_ somehow (implicitly) to extract the csv as a value or file or something early on
- in DEMOS-AND-BUDGETS.md: "acceptance summary" and later are all garbage, delete

notes on FINAL-REPORT-2026-05-13.md
- remove For and TLDR part at start
- extract th eclaims
- and the algorithm (though, I do like the wording here)
- remove LibEe changes section
- remove everything after "find vs generate - the scheduler"
- and eventually consolidate into some other doc(s)


notes on source code:
- LibExecution shouldn't know anything about PDD. just small changes to support that stuff being built in dark
- I suspect we'll need to write a Dark interpreter in Dark before long. maybe one 'default' one that just runs stuff and fails if not found. and another fancy 'expanding' one like the one I'm talking about here
- idk how hot reloading applies to all of this. think on this from 'first principals' towards the end of this, add a .md, tight
- this whole system should work on ops, conflicts, resolutions (incl default resolutions) -- and the SCM stuff should be the same -- working off the same "base" stuff in LibExe. what is that? how does it sync?
- the "html view" should really be written in dark, served via dark

the end goal here is to push the branch w/ a bunch of notes so I can pick back up on the topic.a nd to identify the "pre" steps and lower-level things to tackle, like how the scheulding of the impl braeks down