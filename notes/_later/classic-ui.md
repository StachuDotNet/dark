Bringing Back the Darklang-Classic UI
=====================================

My coworker and I have been working on Darklang for a while.

A few years ago, we did a big fork of the repository, which we detailed here:
	https://blog.darklang.com/an-overdue-status-update/
	(read this in full!!!)

More recently, we've been looking at our efforts in various directions, and need to wrap progress up in some UX to _actually write software in Darklang_.

We have partial experiences in the CLI (packages/darklang/cli) and in VS Code (vscode-extension), but we certainly haven't painted the full picture of a dev UX.

So, we're thinking about going backwards a little bit, making _something_ work with the old UI, forked for our needs.

A while back, I forked our old repo, removing most/all of the old backend, and much of the client/frontend, sans the structured editor -specific stuff.

Anyway, the idea is to take:

-   that old fork
-   an ApiServer spec -- sketching out
-   recent efforts in Darklang-next -- especially the CLI

and to 'rebuild' the old ApiServer backend and data store, in Dark-next.

So that folks get the 'same' or similar experience that they had in Dark-classic, but with Dark-next stuff.

Here are some thoughts:

-   the old editor made heavy use of "Fluid" the

Towards this, I imagine you/we have some TODOs...

-   read the ApiSErver spec
-   read the old ApiServer implementation (see ~/code/classic-dark/backend/src/APiServer) along with some supporting code in (src/LibBackend)
-   read the 'classic' implementation
-   reimplement LibBackend and ApiServer in Darklang, using things that actually exist
-   while writing backend code, make sure that packages continue to reload appropriately. the odds are that if things reload OK< the code might actually work
-   you may need to implement more stuff to support this work
    -   static asset storage and hosting, to host the UI
    -   (that^should be seeded in the .db file shipped with our stuff, along w/ the packages)
    -   raw sqlite access, in a way that feels 'appropriate' for Darklang
-   here's the actual experience that I want
    -   my coworkers download the CLI
    -   they run a 'ui' command (new)
    -   it (magically, with static assets and other magic) loads the app for them, and they can work w/ it in their browser
-   you can/should largely disregard anything nonessential; Secrets, Workers, and Crons can wholly wait for 'later' whenever that is.
    -   we don't need Traces... yet
-   keep everything pretty plain
    -   when in doubt, leave it out
-   the SqlCompiler stuff is broken - so you might be best served to keep User Store / DB usage really minimal. In fact, it may be best to invent a new 'raw' access (in LibBackend, with builtins where appropriate) to sqlite, and handle as much as possible in Darklang land, rather than implement everything in F# land (too many I/O mappings, etc)
-   it might be useful to remove any additional stuff currently in the client that we no longer need, before adding stuff
-   do what you need to in ordedr to 'host apiserver' in dark-next
    -   probably just a .dark canvas adjacent to the other one with load, with some addl stuff somewhere to make sure we actually load that stuf...
-   oh this is big: we need to move from Fluid to something that's... well, not custom, and not fluid. I know there's monaco and there are other editors. The point is that we'd love some in-browser thing, especially if it supports LSP stuff, that we can work with to deal with text blobs
    -   relevantly, a lot of the encoding/decoding stuff will need adjustment or rewrite
-   Oh - our backend no longer compiles to wasm... not sure what we'll have to do about that lol
-   the ProgramTypes and such that existed at the time of the fork was a LONG time ago. please don't make any assumptions that things are matching up correctly
-   please work in reasonable chunks, and expect me/us to review+commit after each chunk
-   ask questions as you have them
-   we don't really need source control for now...

Some final context

-   OK. So, you're currently sitting in the dark-next repo, named just 'dark'
-   adjacent to this repo is dark-client-fork where the 'old ui' is. I imagine we need to pull that whole thing in... ooh that's messy. Maybe we just leave them seaparate dev containers for now? idk. _something_ is acting as a backend for that repo currently, but I forget what.
-   the ApiServer specs are sitting in the classic-dark repo adjacent to this one, in a report named API_SPECIFICATION.md

probably best to start by sketching out a tree of TODOS in a todos.md file that we iterate on over time