

let's clearly separate the following topics by dir in these notes:
  -  "good for AI agents" ( like claude code) to use as a cohesive _tool_, where they own the loop
  - PDD (where we own the loop/process)
Some things overlap, but the former is sort of a 'base' for the latter


before killing, out of meta reflections, extract _one_ .md file I can use for all sorts of jobs for 'looping' work later, given my preferences


pull out the "pre-work" to Stable and Sharing into its own dir
  apps
  tailscale stuff
  more?


ensure docs only reference stuff in earlier/below sections. e.g. S&S stuff shouldn't reference PDD stuff. identify the buckets, the dependencies, then read and reread all .md files until you're sure this is true
same level of thoroughness for: try to ensure _little_ content existing in multiple files. I want to reduce total lines of .md files.
similarly, if there are CLEAR lines where we should consolidate or split .md files, do so. Ideally we'd end with a few less total than we start with

goal: solid pre-S&S and S&S sections/dirs/specs. Enough to implement, other stuff we can _really_ dig into later


misc quick wins:
- the remote-access.md doc
  - there should be a core tailscale doc in the pre-work section. with as much as is needed for S&S.
  - the rest of this doc should be migrated to a 'later' doc
  - rename that doc to remote-access-and-control.md


hot-reload.md
- punt to 'later'
- if you want to _think about_ the hot reloading needs, and update anything related to async or projections / op-playback to help guide things towards a solution taht will support hot-reloading, be my guest. but don't mention hot-reloading there


the "Dark working notes" README.md can be killed

view-sketches.md
- belongs to PDD



ops and playback
- really imagining a .db per: branch, branch+app. in addi;tion to the core ops-and-sync.db or whatever that is (probably more like just dark.db or core.db or something idk)


kill open-decisions.md


next-steps.md
- let's reframe this doc to steps-towards-print-md-sync.md
- it should be detailed and refer to other documents in a step by step fashion a future AI can follow
- I don't see "separating ops from their projections" really mentioned here like I'd expect
- where/how does the event bus fit into the rest of LibExe, ProgramTypes, etc?
- identity binding stuff -- ok to punt, or keep very thin, just enough for whatever we might need to sync between me and my coworkers safely 
- we don't need an 'explicitly not next' section
- only include 'open decisions' in any specific document, not in a summaryt hing
- ideally WIP is synced -- but I don't know how to do that safely. So I suppose we can punt it


onto a bucket of S&S and pre-S&S stuff...


async.md
- ah! we want async to be usually invisible for users in dark. unless they have specific needs around scheduling, there should be no syntax or anything different between async or non-async stuff.
- update this report with estimates of efforts needed, steps involved, etc.
- is this DarkAsync thing a good idea? is the idea of building our own scheduling a good idea? evaluate.
- how does this all relate to event leading to op playback and that whole dance? Do we need to do both efforts together, or is it async first then "separating ops from playback" separately? Does the EventBus stuff relate to this, in other words/


event-bus.md
- keep focused. split/punt: stuff needed for S&S, and later work. want enough to get by.
- stop mentioning the Stream thing. talk about EventBus independently.
- keep iterating here, especially as it relates to the App, async, MVU stuff, etc.
- tighten things

conflicts.md
- should really be conflicts-and-resolutions.md
- for now, we need _just enough_ of this for S&S. split/punt appropriately.
- good core ideas but let's not go overboard


distributed-event-sourcing.md
- feels like maybe there's room to merge this with some other file, idk
- regarding "projections.db"...
  - I'm more imagining one DB per branch/session that we're working with. Maybe per dev server or something. and maybe one .db per 'active' or long-running app. not sure.
- I'm not sure if the outlined local.db is ideal. Many pieces of data are simple fields, not really ideal in a sql table. maybe it should be JSON or a serialized blob of a dark value or something
- the whole 'the app is live, forkable' section can be punted for later. just need enough for S&S here, for now


identity.md
- idk if we need this at all yet for S&S. likely punt to later
- the 'intent' should be a reasonably structured thing, known in PT in a nice stable way


example-app.md
- the existing cli "outliner" might be a better app to consider, generally. it's real, and known, and composed.
- adjust this whole doc after reconsidering the App shape (mentioned elsewhere)
- the 'views' thing in particular needs work. we need some sort of identifier or something, so 'above' apps have a way to use 0-N of the UXs, as they choose. maybe the 'views' is a record type the above thing can reach into, idk. 


apps-surface.md
- dark apps fork is certainly puntable
- the second sentence 'the notion of stable' is useless in this doc
- dark app install should be really simple/boring -- just basically create an alias poiinting to the calling of some dark fn. so that if I type 'print-md' there's something globally set up to run through Dark, or something
- kill "how it rests on the substrate" section, useless


sync.md
- kill second sentence
- "sharing has three modes" -- no. for now, we simply trust other parties on the Tailscale network. Keep things simple.
- approvals are NOT needed. Not ops. The sectonis around NS ownership and approval flows should be killed.
- as such we don't need "token mode" either - remove mentions
- punt P2P work (but don't actually ... see notes later)
- open decisions, default sync target: explict, opt into auto sync. maybe managed by some sync daemon
- persistence: commits/branches are also managed by (different/above?) ops
- not answered here or elsewhere: separate ops from projections. may be good, for perf and handling multiple ai sessions concurrently. each projection ignores ops irrelevant to branch/session

composable-mvu.md
- probably worth folding into 1+ other docs as noted in the preamble
- rename 'empty' to 'init' and maybe it takes args
- compare the App structure to other, more mature systems (in Elm, F#, etc). Is ours reasonable? Does it really make sense, or need some refinement to make distribution of eventsourced MVU apps "work"? Iterate iterate iterate.
- 'mapping the pdd viewer onto this model' - no, outliner is a better thing to focus on. briing that to ths world. then, print-md will be super easy

package-system-layers.md
- fold ideas wholly into other docs

capabilities.md
- "a capability grant is an op" no not at all. capability grants are more like per-instance settings, separate from ops and projections
- keep iterating on the nuanced capabilities type. be explicit+thorough, work on shapes.
- "s Set<Capability> even..." no. Choose B, see notes later.
- not answered: how does a user set allowsances and see failures/blockers? Via CLI somewhere. How do we model in PT, RT, expose in the CLI?


cohabitation.md
- belongs in PDD world, puntable
- "the materializer isn't a special subsystem" - it should be. but part of low-level F# ideas, handled below PDD
- "what constitutes 'an app'": any dark code _running_ or installed. I think. many runs are very short-lived and GC'd. that said, a .db per app (mentioned selwhere) could be a terrible idea, since traces would then be spread across many .db files, etc. think through.

cli-daemon.md
- I don't care about the document's history. for this or any doc
- split a bit
  - 1. supporting long-running daemons in the CLI
  - 2. the specific per-branch(?) daemon -- what t needs, projections, how it interacts w/ other stuff, etc
- "one daemon per machine..."
  - but underlying data structures may be much better if we have one _core_ sync daemon, and one per projectionm? I'm not sure, think through. don't be too wild though
- daemons are just Apps, should show in appss menu, managed there


bootstrap.md
- try thinking, for a while: once s&s s done, what are the steps here? we can't _start_ yet, but we might as well start thinking




-----


ok now for some PDD notes..

pdd.md
- PDD, we own the loop 
- maybe no prompt command - just `dark "request"` and it goes. some requests may be "build/run software that..." some requests might just be "cd to wherever json stdlib is." just an open intent and we figure it out
- fold into readme.md or readme into this.

ai-coding-target.md
- not quite PDD. this should be in the "good for AI agents" section/dir, thought of independently.


---

now some notes on the meta/reflections...

"meta-reflections/README.md": kill
PRINT-LIST.md: kill
STATUS.md: kill
feedback-coverage.md: kill
grounding-against-main.md: kill
loop-operations.md: kill
process-risks.md: kill
where-the-loop-struggles.md: kill
what-the-loop-is-good-at.md: kill
vault-organization.md and overwrite-map.md: consolidate these two. at the end of EVERYTHING, re-evaluate the content from scratch




---

OK -- now for thoughts I emailed myself last night.
Anything here that should inform an existing doc: do so. take your time. iterate. You have so many hours.
anything that's not represented: 



Each app has its own db
Most are really temporary or otherwise gc'd
Others stick around for a while 
Sync is built on top of this system 

There are some core internal tables and they own others 
What's the most minimum f# we need what does pt look like how does this inform the plan towards my script syncing?
Instance per app or something idk
And the core one coordinates the rest and sync. That's it job

Maybe everything is in the root .darklang sir
Imagine the whole folder structure 

Install first just has boring CLI with seed db and few capabilities 
Can add extensions, install sync app and other apps, built on top of each other 
Can maybe remove scm and rebuild that as a dark app
Same with pdd
And outliner
And all sorts of terminalapps, and websites, and scripts, and rM stuff

Each "repo" is an ops DB 

We exist in the central install, but for some people, additionally as other repos across my file system 
And there's a system for syncing data just the way I want it to
Set upstream and stuff

Load built-ins optionally, including their structure for capabilities. Like dlls or something. What's reasonable/common? Where would they live? Are these "extensions" or "platforms" or something?

Each extension comes with some runners that respect special types
Or just have binding and mappings

Skills and mcp servers and "evals" are dumb. We just need function and data and teata


-----


I'm going to go away for 24hrs shortly. How can you set yourself up to digest these thoughts, and iterate as much as possible, until I get back?
The goal is to spec out EVERYTHING needed between 'main' and "print-md script is stable and syncing across various dark instances"

don't push anything upstream. commit along the way.
set yourself up with a loop that you end up working on this the whole time. early work should be focused on tidying this document so it's well organized and somehow encourages you to add todos over time, check off todos over time, etc. and REALLY focus on tightening the S&S and pre-S&S work as much as possible. so that, come sunday when I get back, I do a final review, adjust a few small things, and say "go" in some new A session against some file you develop, to start some Pre-S&S work, towareds the fun stuff. and I should come back w/ confidence we have a real plan for from here to print-md sync.


for each intended effort/PR: sketch the shape. high-level code, uncompiled, with some empty function bodies, psuedocode. iterate on THAT as much as you can.
identify good types, tests, needs, UX touchpoints, preqrequs we can pull out into "earlier" efforts, etc.

anyway -- I have 30mins before I leafe for a trip. set things up to be ready for a 24hr loop, 5mins each.