Following is some feedback on the PDD-specific docs, that I'd like you to systematically address.

Here are the specific docs I'm giving feedback on

- projects.md
- ALGORITHM.md
- CLI-PROJECT-SURVEY.md
- FRONTIER.md
- CLAIMS.md
- README.md (the one around PDD)
- REFLECTION.md

General feedback:
- we need a clean


file-specific feedback
- REFLECTION.md
	- kill the numbers. I'll generate good ones later.
	- generally consolidate the reflection to be a bit tighter. remove all reflection from the SUMMARY.md docs
	- those summary.md docs don't really need to exist. the system should be that we have the spec files for what the projects are trying to accomplish, and there's one per-bench-sweep document reporting the results. or there's a dir per bench sweep, and we summarize in a final .md doc at the end, never printing the intermediary ones
	- generally, all of the results should be in a results subdir -- which should

- SUMMARY.md docs
	- as described elsewhere, these should be split. one long-existing doc for the spec, which should have a simple goal line and a simple 'acceptance criteria' list. the learnings should be extracted separately from the design/desire
	- no need to mentino other systems
- projects.md
- ALGORITHM.md
- CLI-PROJECT-SURVEY.md
- FRONTIER.md
	- I'm not sure the point of this doc. Is this design or something? the title is unleading
	- 
- CLAIMS.md
	gsome fn's functionality is fully delegated to an LLM system. so it's sort of forever lazy.
- README.md (the one around PDD)


I see the eventual structure of pdd notes being something like
- design
- projects
- results (raw data of benching, some per-sweep .md report summaries)
- issues and improvements
	- .md file for each category of issue space, with subsections for various suggestions for fixes, etc.
	- lots of REFLECTIOn.md should end up here
- meta-reflections
	- thoughts on how the process itself is going. feeds into design
	- some of REFLECTION.md should end up here


generally tighten. consolidate. little content would need to be in more than one file, while rn I feel like there's repeated stuff.



generally review all pdd notes. there are things incl in README.md that are outdated - we don't anticipate so many pdd commands, for example - dark prompt just starts a background agent that builds the thing, and the CLI enters a state where the user is watching stuff. there's some option to run the agent/prompt in the background.
I don't think we need the README.md at the end - or at least, it should be really thin

we need to punt the removal of .dark files until after the baseline sync+stability stuff is done. update docs accordingly. It's just not a realistic project in the short/medium term, since as F# and Dark code references each other quite often/tightly. and unless we have a good way to have a stable environment, handle sync, and handle migrations of dark environments (including some process for upgrading core things like language locally, while still using some backed-up or dev-ready DB of old package code), we can't really remove the .dark files. we should iterate all of those blockers explicitly in one consolidated place. Oh, in fact, there's a document "removing .dark files" that attempted to address this in the past.



we need to fully separate the OPS (modeling of state and mutating of state) from the projections of such. Like the M and V part are distributed, but the actual _projections_ of the update should likely happen on specific instances. but with good systems so the recovery of distribution issues (like race conditions of branches across instances pointing a name to different hashes) has a way to be resolved, via the core/low conflicts=and-=resolutions system designed somewhere. The core SQLite DB that handles sync and such probably needs to be separate from branch-/session-specific projections like package items and such. I'm not sure how to do that split cleanly, please think on this.

the VIEW-SKETCHES.md document is beautiful. Extend it wildly, given more recent ideas. Try not to remove anything there that's already perfect.

20-elevator-pitches.md: remove 20- from file name. Change "You write names + signatures" to "You ask for software" and adjust accordingly, also informed by the other feedback and your own thoughts. generally update this doc a bit, it's a bit outdated.

improvements.md 
- remove the very early preamble gray text. 
- as noted elsewhere, consolidate issues and suggestions into the other place(s)
- I don't want to build a big CLAUDE.md. I just want for-ai to be much more helpful, removing most needs to do follow-up calls. it should be a composed document, with some dynamic/expanding content that loops in other docs, etc. Eventually we can have it informed by specific needs of the project/task/user at hand. Maybe the core for-ai doc would just list a bunch of document hashes and names -- then a follow-up command would be `dark docs hash1 hash2 hash3` and they'd be concated beautifully
- I hate 'dark suggest'
- idk why we start with 3.1  clean that up
- kill "Known runtime gaps" section



CLI-PROJECT-SURVEY.md
- kill section 1 what darklang gives you
- generally fold (just the projects) into the appropriate dir
- no need to assign letters to classes
- remove section on suggested ordering
- the meta stuff like cross-cutting test criteria shouldn't be in some design file not this


ALGORITHM.md
- the text/ideas here are a bit dated. "first non-failure wins" is not as nuanced as we are these days.
- First pass (or later), fn bodies might be an LLM wrapper or LLM agent wrapper or text with expected type (dummy value)
- worth more iterations generally










-----


OK -- there are some things 'below' pdd that need addressing too. some feedback on that now...


projects.md:
- kill sections: sources, suggested first-pass ordering
- cross-cutting test critera should be elsewhere, consolidated
- remove all mentions of specific iterations (like 'added iter 28')
- group projects only by category, not phase
- in the format: remove modules and language. add whether it's green/brownfield (most of thse are greenfield so far. not sure yet how to 'emulate' brownfield work well, but it's very important)


plan.md -- I have  two documents printed w/ this title. idk if they're the same at different times, or different docs. I recorded my notes, do with them wwhat you will.

batch 1:
- I have a vague feeling like the paper I have in front of me, which I think has whatever is in this file, is a bit outdated, by a few weeks of thinking. evaluate. 
- I think a simple client/server module would work best. the server is my desktop PC, always on, alawys available to people on the tailscale network
- make sure DARK_ACCOUNT is gone (like I think it has been since this doc, but could be wrong). if not, add a todo to follow up there
- remove key files section, and schema facts worth remembering
- and step 0 of impl order
- iterate more on the impl order sggestions. tighten.
- goal: 2 local release builds syncs, one acting as the server, one as client. various branches, efforts, experiments completed. stuff wiritten in dark, using AI
- generatlly tighten the doc
- I don't like env vars and would rather things in CLI's adjacent .darklang dir

batch 2:
there's a document somewhere plan.md. It's a bit old, but somehwat the basis for the more recent thinking.
It's a bit long - I need you to analyze it, tell me what parts are already covered by other docs, what's not, and what things should be extracted, removed, etc.
in it, remove the section on open questions, as well as specific metrics shortlist, and phasing, and risks/failure modes, and references
try to keep the rest of the text well represented _somewhere_, here or otherwise. and at the end





EVENT-STREAMING-AND-PARKING.md
- remove v0 design blurb
- remove "the third substrate piece" sentence
- remove PDD mentions -- this document should be fully within the "syncing and stable" portion of work, before PDD work really starts
- actually rename things from Stream to EventBus
- I don' need the "compared to event sink"
- iterate again on design of impl. does this fit well as a design into LibExecution, fit the vibe of ProgramTypes, what the CLI describes it wants to be, etc.?
- (how) does it pave the way for sync and stability? I want that story solved with the least total amount of code. I think there's a beautiful layering possible where the event-streaming stuff is THE core tech to sync -- stream events, play them back, detect conflicts, those maybe are other streams, etc. a system composed on each other, that when is set somehow allows me to eventually remove .dark files
- where's the F#/Dark split here? Ideally, the F#-side stuff is thin, but really good and tight and well designed. You kknow --- "enough" but minimal
- don't need connections to other substrate sketches, or main cdoe
- ok to think even deeper here - do we need to solve for async/concurrency with/for/before this, at a core dark-language level? Replacing Ply with our own scheduling stuff? Who knows!
- 




STABILITY-AND-SHARING.md
- the thoughts of definitions > stable was actually talking more about PDD stability -- what the algorithm is sort of reaching for as it does iterative development. migrate where appropriate, kill the rest of this .md file. oh, but also keep the wire protocol section somewhere - it was good. I hope we woulnd't need the little SYNCEVENT SCHEMA section tho




CONFLICTS-AND-RESOLUTIONS.md
- more details on SCM op-vs-op. I think that's the most important category of conflicts
- there are parse-time, run-time, and dev-time, and at-rest conflicts. all evaluation basically happens in those few states. oh and maybe some playback variant of runtime
- anyway, give me more details on the conflicts possible int aht category, and more thoughts relatedly
- the conflict timings are really the conflict types. I think?
- don't get bogged down in or limited by thinking of specific SQL schemas. ideally this is all modeled in a core way that works composably for all sorts of dark applications including sync and stability, and AI agent development (both separate and composable). anyway, kill the persistence sectino I think. projection is somehow separate from ops, in a composable way
- 


in a loop: create a feedback-revised.md doc that takes the thoughts in feedback.md and better organizes them into a file of - [ ] markdown todos that a follow-up agent will do.
I expect a 5min loop running for 2 hours. it's important to do it well. Don't edit any other files in the process. after setting up the original feedback-revised.md, give me a /loop to start the process.
The whole 2 hours is just for planning the real agent loop. I expect that following loop to be a 10min loop that goes for many hours, then print-md's a bunch of files I should read.
it should 



There are really a few categories of documents I currently have in mind, and will be worth looking at during this loop and the following one:
- Getting to 'Stable and Syncing'
- Removing .dark files from the repo ('package bootstrapping')
- PDD
- Good for AI agents
	should include topics like feedback-from-agent.md, as well as all sorts of other things. overlaps with other things like reviewing/managing/editing software, running software, etc.
- Good for review/managing software
- Good for editing software
- Later/Other



Feel free to read/involve other things from the dev vault in the process. I might refject htheir involvement after the first no-op planning loop


some goals for the effort:
- fewer+smaller .md files on this computer. less repitition, less garbage, less outdated phrasings
- have clarity on how to focus on the sync and stability work, letting PDD and later work rest for a few days
- eventual migrate some .md files to the obsidian vault
- organize some recent stuff I added to the obsididian vault - it's not sorganized well rn esp the stuff I added to the 90.Stachu folder
- 

do not push anything to upstream git remotes. 



at the end of this I generally want a list of .md files to print and review again, now that we've updated everything. should likely include everything touched in this wave.


the real, absolute goal for dark right now is: stachu's existing print-md script is stored in dark. I'm able to inspect it, change it, have changes synced to my other computers, via the good solu;tion. if ocean wants, she can fork it. it's listed as an 'installed' apps when I go to 'dark apps' in the CLI.



BOOTSTRAP.md
- hard part: F# references dark, references F#. what if things change, etc? What if lang/ops change? How can local dev work? We'll need to add package code and F# code back and forth, while keeping old stuff going, in a way that only affects the local machine. but what about CI, etc.? we need a way to account for all of this.
- let's get sync working, _then_ worry about this. assume central server
- idk what T3 T4 etc are about


READY-WORK.md
- kill theme A
- and B (for reasons mentioned elsewhere)
- kill this doc. fully rewrite it at the end of this process. like a "next-steps.md" thing.

IDENTITY.md
- idk what this is for. generally puntable.
- make it thinner, directional
- rename IdentityKind to Identity. account doesn't include thjat as a field. Ideitnty of | Human of AccountID | Agent of id * owner: Identity
- kill TrustProfile
- in account record, don't need kind, ownerID, trustProfile, archivedAt
- tracing cares about identity/source of _intent_. rest can be stripped. Intent(/reason/context) per Identity+(Dark) Instance.
- kill 'cross cutting' section
- and any attempt at 'phasing' or involving in some larger process - document is pure


CAPABILITIES.md
- pure fns are always allowed
- need to iterate more on Capabilities type
	- HttpClient -- should be sophisticated. there are notes on these specific restrictions somewhere in the vault.
	- HttpServer: probably inspired off of that
	- random: yes/no
	- time: yes/no
	- file system: yes/no for now, but w/ commentary about future sophisticated options
	- language: same -- the pure/safe things we don't need perms for, but we probably need analysis about how to reflectively eval, etc
	- matter: same
	- other CLI stuff: same? Idk needs more investigation
- after rethinking the nuance needed, re-design the implications to LibExe from top to bottom
- is Set<Capability> the right thing? Idk if builtins even need to register that publicly - they should just be able to reach into `state.` or something to look for 
- I don't think the Interpreter should have to check anything. Only the builtin? Or, maybe builtins register a checkCapabilities _function_ that's called? Idk what's ideal. the latter could be cached, but maybe it'd add code/redundancy? Idk.
- more nuance than `--ask`. human interactions are async, maybe timeout and fallabck to non-human options after some amount of time or if eval demands them, etc.
- maybe builtin grance are instance-specific for now? What other controls might we want for the future?
- how does frame-parking work?
- for now, we do _no_ interactive grants. it complicates things and makes it hard for you to test stuff. but we can sketch/create structure
- remove "llm prompt-side" section
- love 'user-defined fns' - we can build that quickly, somewhere. again, part of the 'ops vs projections' considerations
- kill sections: sequencing, what this unlocks
- replace per-assembly default caps thing w/ more nuanced section, once
- kill schema section. we can get to that after design is nice
- remove 'connection to previable' section

maybe ths is all really 'distributed event sourcing and MVU-like ideas, branched and stuff'. think about that somewher ein an .md file




write an additional doc on:
- "CLI structural dark ProgramTypes editor"
- structural editor like dark-classic or hazel
- powered by tiny LLM loop: given this keyboard shortcut and the current state, (how)_ should the rendering of this view chagne?
- caching used to make it pretty darn fast
- modeling of the editor editable in the editor lol
- design the UI, make fake 'views' for me to review, list the relevant components top to bottom, high-level bullets
- steal from other generic component UI lib ieas like from 'clay'
- should eventually work for html too



COMPOSABLE-MVU.md
- one big composed App, not composed Model. there's some App type defined in dark that has a 'runner' (built in either F# or Dark, or some combination) that fits into the rest of this sync picture. relates to op-playback and other things
- generally think more, esp as it realtes to the rest of this work
- maybe even flattens into some larger topic around distributed op playback, idk



Dark Async Plan.md
- this is from my coworker.  related to event streams, playback/projections, sync, etc.
- has been worked on separately from the notes I've been building up
- needs a review. what do you think about it?
- I sort of suspect we should kill Task/Ply and roll our own async thing from scratch...is that stupid? We want to REALLY control async behavriour, park threads, manage nested processes, have Dark UXs inspect what's going on at all times with _opt-in_ debug symbols being used, all sorts of magic. maybe we need to work at a lower level to have those things?
- maybe read the whole of Concurrency in .NET, or read a bunch of other stuff about how to do async stuff at a low level
- don't touch her document, but use thse thoughts^ and her doc to inform changes to other async-realted docs we've been talking about



package-system-layers.md
- generally this doc feels a bit outdated, but there might be some uful
- my critiques of it: the 'layers' should really be composed/composable ops/apps/projections - somehow. but the specific layers/features could use a redesign given those thoughts. iterate heavily on the doc. plan ahead for what you might do
- maybe "Harmful" notifisications noting fns are bad are some event stream/system. could be an opt-in extension, or built in
- package dependencies are just one projection
- ops/things may be communicated through an instance even if a specific extension/usage/runner of those things isn't activated on that instance
- I don't like the "shared table shape" at all. each thing might need their own specific considerations for the projections. and yeah, thing that use both of them may need to know about all underlying data shapes and types


cli-daemon-mode.md
- how should this change given recent developments/thinkings/notes around ops, sync, projections, async, etc. especially async
- do we need one of those .sock/.pid/.version things per session/branch running? per background service?
- the perf implications to the CLI interactions is only one _benefit_. we need to think through this more, other implications etc


beam-vs-dark.md
- update thhis doc a bit given thoughts elsewhere. try to make it shorter than it is now. don't update any other docs based on these thoughts
- (maybe do this in a background agent to prevent such)
- i like your idea around the 'mailbox' - F# has some cool mailbox thing that works well. that could work in a plan9/smalltalk sorta way distributed for dark



type App is something like -= 
	{ name 
	data
	msg
	views
	cmd
	conflicts
	resolutions
	autoResolutions
	constraints

	  }


App is some special type, composed, etc.

the app value needs to be editable somehow, by either people (via CLI) or agents

most conflicts are OK. just something (data, condisionts) we don't like and can get to later





please evaluate the following thoughts:

simplify darklang greatly
for now we just support:
Timestamped set of ops,
a modeling of their conflicts (or constraints),
a way to sync all of this, some 'projections'


one solution for Sync is to define and respect one magical App type
type App = 
	{  name: String
		data: 
		views: List/Set, with ID/Hash. package values/fns?
		//msg:
		//cmd:
		ops:
		conflictsAndResolutions: 
		projections, DBs:
		constraints: 

	  }

	  distributed playback is _managed_. you can build simpler apps on top of this system, ikeeping as much as possible transparent to the end user.

op-playback itself should be hot-swappable. the generic thing shouldn't have to think about conflicts, resolutions, etc, I think? Idk.
_each_ projection can deal with conflicts. idk


some other special types to respect:
	runtime tests/constraints
	at-rest constraints/tests
		https://fable-hub.github.io/Scriptorium/



if a user's system has an implementation of this App type, we respect it and they can run it, etc.
we rebuild a fork of the current CLI experience as an App, solving sync/dist along the way
there must be baseline views to support all sorts of things...
we need a view engine or something along the way


even if a user 'users' your app and has the managed data, they should be able to contribute to views, inject refactors, etc.


maybe via one composable 'app' type:
- package values of this magically get their own _management_ or something, idk
- or, the CLI has stuff around this, idk


- there are auto-views, based on data. either by reflection or by LLM code generation


app shape

support ref keyword to get reference to like hash
probably, it's just a global function like print, that we teach the parser/NR

we need a composable parser written in darklang. can be compiled through magic to tree-sitter or something. Like we could have a DSL of types/fns, and some darklang code (and subsystem) compiles that down into something good.
'compile' as a builtin asap



I just want the smallest most composable system for data and apps
Accessible by CLI, replicates current functionality, extracts core and added stuff, composed, allows us somehow to remove .dark files, involves parser

Crons as a distributed app
one we officially support, but is an extension of the default CLI
I guess daemons is a thing. Maybe start() or something, idk

projections
One is the list of conflicts 
Can ignore, usually?


