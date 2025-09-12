# Darklang Developer Experience Design & Implementation

## Context & Urgency
My coworker and I have been working on a product named Darklang - a software language and platform. We've been focusing on particular pieces, and we're at the point where we need to pull things together into a cohesive Dev UX (or many UXs). 

I'm experiencing severe analysis paralysis and need to produce concrete artifacts for meetings tomorrow (coworker) and Sunday (advisor). I'm embarrassed by the lack of progress and need guidance to move forward systematically. Honestly I'm really anxious - I need to design the developer experiences and the underlying types, and I'm stuck.

## Primary Goal
**Actually Write Software in Darklang** - Design and implement comprehensive developer experiences for writing, sharing, and deploying software in Darklang across multiple environments (CLI, VS Code, web), with a foundation of source control and package management that supports both human and AI-powered development.

We sort of need to add a foundation of SCM+PM, and a DX on top of that. But it's feeling like lifting a house, adding a physical foundation, and then trying to reconnect the plumbing as we settle the house back down on the new structure.

## Things to Go Read, In Full
- [wip.darklang.com](http://wip.darklang.com/) - our new website in progress
- [blog.darklang.com](http://blog.darklang.com/) - mostly that old-ish post about Darklang Classic
- [stachu.net](http://stachu.net/) - the most recent post about our vision
- ~/vaults/Darklang Dev - development vault with notes (what docs should you look at?)
- ~/vaults/Darklang Internal - internal vault
- ~/code/darklang.com - WIP website source code
- ~/code/docs - outdated docs but might have useful context

### Read the Full Source of These
- ProgramTypes.fs - understand our type system
- RuntimeTypes.fs - how things execute
- Interpreter.fs - the execution engine
- PT2RT.fs - transformation layer
- LibPackageManager.fsproj and files - package management implementation
- CLI source:
  - Read the full CLI source
  - Cli.fs (the 'runner')
  - Imagine what the non-interactive and interactive CLI experiences could be
  - Imagine other experiences that could be powered by this platform, especially when more FRP-like abstractions are extracted
  - Read the relevant stuff in the dev vault about CLI
- VS Code integration:
  - Read the extension source
  - Imagine the minimal experience currently
  - Compare that against what a 'normal' language provides
  - Think: what are the gaps? where are our strengths?
- DB schema, migrations - understand our data model
- Note: builds happen automatically via ./scripts/build/compile running in background (don't manually rebuild)

## What We Have vs What We Need

### Currently Available
- **Darklang Classic (Previous Product)**: 
  - Online-only, our-cloud only
  - Edit stuff in place with feature flags
  - Used to have great immediate feedback
- **Current Implementation**:
  - Language definition with parser and pretty-printer
  - Execution engine
  - Basic CLI (one-off or installed)
  - Partial VS Code integration
  - Database-backed package manager
  - Automatic builds via background compilation
  - Package reloads (~10s when .dark files change, logs to ./rundir/logs/packages-canvas.log)
  - .NET builds (up to 1min for F# changes, logs to build-server.log)

### Critical Missing Pieces
- Actual developer flows - ways to write and share software from start to finish
- Source Control support (DB changes, ProgramTypes changes, RT stuff)
- Way to indicate what branch/patch we're on in CLI
- Ops and Patch/Branch types
- Tracing mechanisms (bring back from Classic)
- Persistent and shared CLI state across instances
- Package search that understands branch context
- Separation of names from definitions

## Core Concepts to Think About

### 1. Operations (Ops)
An Op is any change to the package manager. Classic had position-based ops (SetHandler, CreateDB, AddDBCol, DeleteTL, MoveTL, SetFunction, etc.). 

Some of those things still make sense, but most don't. We likely need things like AddType, MoveType, etc. Think about what ops would make sense.

Some values should be versioned and follow that whole system, but maybe others (like certain 'globals', secrets, etc) have Refactors that are applied to them over time. Maybe EditValue/EditData/Migrate is an Op? What are better names?

Some values should be versioned and follow that whole system, but others (like certain 'globals', secrets, etc) have Refactors that are applied to them over time. Maybe EditValue/EditData/Migrate is an Op.

Should be able to pull one/many ops from one Patch to another. Can one PatchOp be the inclusion of a parent patch?

### 2. Patches
- Atomic transformations containing Ops (Sync Patches of Ops)
- Optional names for human reference  
- For Patches to be accepted, they must be validated against local/dependent data
- Support for dependencies and merging
- Each Patch should contain not only Ops and changes, but also Views of those changes, Intent, TODOs
- Enable pulling ops between patches
- Can a patch depend on multiple patches? (for local CLI "pull in a few patches from upstream")
- Maybe some patches are just "pull in dep. patches X and Y"
- Start with really boring patches: add this fn
- "Patch rejected" flow needed
- Maybe we branch Patches as "Edits"? Each Edit View has Things in Focus in specific order

For validation:
```fsharp
let checkEdit (edit: Edit): Result<OpsToDo, ValidationError> =
  // Failures can be:
  // - New test fails
  // - Regression (old test that passed is now failing)  
  // - UnknownReference
  // - Conflicting edits
```

### 3. Sessions
Should sort of feel like tmux sessions. Some commands might be:
- `dark session [sessionA] add view`
- `session new`
- `session list`
- `session continue [name]`
- `session new --intent="work on Stdlib.List"`

AIs should be able to work on multiple sessions at the same time, and a human can jump in as is useful. Sessions have/produce _traces_ attached to them.

Maybe focused on one Patch, one Op (of a patch), or many ('pinned' sort of flow?).

Commands to think about: resume, id, new, kill, status.

Maybe environment variables and config vals are set per-session? Do sessions host LSP servers, MCP servers, and other long-running services (bwdserver, etc.)? "Attach to a session"?

WHat's a Session? where does that live? How is a Session copied/transferred/continued in a separate PC? how does an in-progress Patch get transferred/synced from one machine to another?

### 4. Instances & Sync
- Each CLI install = Instance (maybe a.k.a 'server')
- Central hosted Darklang server = Instance  
- Future: Browser WASM = Instance
- Syncs happen between Instances
- Sync protocol for copying, handling, negotiating, merging Ops
- The sync protocol is just a thing that copies, handles, negotiates, merges, etc. Ops
- Support offline work with full upstream fetch
- Both manual and automatic sync modes (fully-automatic "yolo just do things on main" should be supported)
- HTTP (or lower level) protocol needed for sync
- We should be able to sync any layer (Session, Patch) sort of independently
- What's the mechanism - fetch? push?
- Ocean and I can send each other .patch files initially (or use my home server as 'central')

### 5. Traces
- Old tracing mechanisms from Classic
- Thoughts on how tracing fits into the future of Darklang
- Not worth huge focus but worth sketching types for
- Sessions produce traces attached to them

## Artifacts to Produce

### 1. Changes to This Code

#### ProgramTypes.fs
I think this needs: Op/PatchOp, Patch, Session, Sync, and _some_ brief types around validation. But maybe this would be better in LibPM - I'm not sure. Certainly, the _details_ of how patches work and such shouldn't be in PT.fs, in any case.

#### (maybe: RuntimeTypes.fs)
Might need changes, not sure.

#### CLI impl
- new commands?
- new views?
- maybe we need settings around sync (manual/auto)?
- we probably need to display/represent "here's the patch we're currently focused on" or something?

#### VS Code impl
Don't need to do anything fully, but either sketch in the right direction, or write specs/todos, etc.

#### Builtins for:
- sessions (start, continue)
- patches
- ops
- status
- diff

#### Some work in LibPackageManager
I'm not sure if patch validation belongs here or in PT? (Probably in LibPM)

#### _Some_ database migrations
I'm not sure what stuff will live in SQL land, and what stuff will be maintained fully in F# or Darklang, but we'll need _some_ changes here. Maybe containing Ops, Patches, Sessions, etc.

### 2. Very Important: Documentation/Specs for Developer Flows

In various environments/clients/editors (CLI, VS Code, both) and various categories (dev, deploying, source control, testing, handling merge conflicts, PR reviews, manual and automatic "sync").

Think about flows:

#### General Development Flows
How do people write software? They:
- Think and plan todos
- Write code, run code, write+run tests
- Ship and switch between different efforts
- Sketch, search, lookup dependencies (both directions)
- Debug, review traces
- Communicate with team
- Add TODOs for later, add and review commentary
- Do source control
- Review and pull in dependencies
- Worry about software

How do we break these down? How does Dark tackle it all? What parts can we offload to other systems?

#### Core Development Setup
- Set up CLI locally, extension locally, LSP locally, MCP locally
- Edit a file and get GREAT feedback
- Save → updates to PM
- Somehow push/merge those changes upstream
- Set up development server
- Handle old-school git & files-on-disk flow (watch for .dark files)
- Run bwdserver

#### Source Control & Patches
- Totally CLI based: create patches, show them, merge them
- Do it all with AI, without any direct text input
- Start with really boring patches: add this fn
- "Patch rejected" flow
- Handling merge conflicts
- PR reviews
- Branch management showing "viewing reality at patch #89ab4e"
- As you're saving/during save approval, present changes made across views with questions about defaults/renames/deprecations

#### Testing & Deployment
- Writing and running tests
- Building and deployment
- Handling regressions
- Running tests for specific patches
- Can a user test a website per a specific patch?

#### AI Integration Flows
- Multiple parallel sessions for AI development
- Sketch it out in types and fn signatures first (maybe start with modules)
- Then fill things in
- Feedback from user throughout
- Voice integration the way I want
- Give AI raw tools to control VS Code, CLI state, etc.
- AI needs seamless way to review what fns exist
- Support for AI developing parallel features with current limitations in mind

#### Sunday Demo Flow
- Go to matter.darklang.com in web browser
- Navigate to Stachu.Darklang.presentation (value)
- View it naturally with default pretty-printing, or as AST, or RT stuff (instrs)
- Or with special view (found a WebView<ThisType> or WebView.for(tlid))
  - `type WebView<'T> = 'T -> Html`

### 3. Work on Websites

#### wip.darklang.com likely needs work

#### dev.darklang.com thoughts
Currently, the only 'branded' site we have is darklang.com. Lately we've been building wip.darklang.com - you can find the source at ~/code/darklang.com. We'd like to also add dev.darklang.com, inspired by the recent work. Check out thoughts in darklang dev vault.

dev.darklang.com should have:
- `/flows` - documented developer workflows with status (per flow mentioned, include status)
- `/glossary` - ProgramTime, ParseTime, Patch, Session, Instance, Op, Expr (MatchPattern, LetPattern, Expr)
- `/bugs`, `/features`, `/facets` - issue tracking (encode our facets and all that stuff)
- `/patches/[id]` - patch viewer
- `/compare/main...feature` - diff viewer  
- `/user-experiences/exp1` - place to store/track ideas/impls
- `/todos` - fill out and request input: feedback, PRs, reviews, advice, chats
- `/contribs` - contributions
- Design inspired by wip.darklang.com, content stolen from Darklang Dev vault
- Similar/overlapping URL structure as wip.darklang.com but with more messy content

Maybe app.darklang.com is where classic UI fork goes:
- `/sessions/dev-laptop-xyz`
- `matter.darklang.com/compare/main...feature/add-auth`
- `matter.darklang.com/patches/7a5c3d9e`
- Settings

Include tiny stupid demo flow:
```bash
ssh darklang.com
cd Darklang.
# update some html stuff (copyright at bottom of page)
# yeah, update dependencies  
# preview
# review patch
# merge
# check live site
# exit
```

### 4. Specs

#### Sync Protocol
- HTTP-based protocol for Instance communication
- Op validation and conflict detection
- Merge strategies
- Offline/online transitions

#### Patch Validation
```fsharp
let checkEdit: Edit -> Result<OpsToDo, ValidationError>
// Failures: test failures, regressions, unknown references
```

#### Conflict Resolution
- Model OpConflict/PatchOpConflict types
- Manual and automatic resolution strategies
- UI for conflict resolution in VS Code and CLI

### 5. A public-facing blog post on "how Darklang will work"
- Somewhat high-level but _informed enough_
- In my voice/style (per the blog.darklang.com post and the stachu.net post)
- fwiw we won't actually use this, really - just think it'll be nice for me to be able to read

### 6. A summary of everything done

### 7. A documentation for: what to discuss with coworkers
- A 'presentation' of these ideas+changes
- Open questions
- Some plan for 'next steps'

## In-progress and Related Plans

We need to adjust Values to have a Type col.

Maybe functions can _sometimes_ be globals too. As long as the type signature stays the same, allow quick transition, like with -classic?

## Critical Constraints & Considerations

### Package Management Evolution
- Separate names from definitions (support renaming without breaking deps)
- Split PackageName, PackageType, PackageConstant from the _name_ of those things
- Name has other relations including dependency-tracking, _current_ name, etc.
- Values need special handling (some versioned, some mutable/global)
  - Some values are malleable and simply referenced
  - Other values require dependents to update references
  - Maybe split Values from Globals if they sync differently
- Package search must understand branch context
- Package items need column for branch hash it was added on (find origin, check dependencies)
- Consider in-memory projections vs SQL storage
  - We don't need every feature in SQL instantly
  - If we only store Ops as patches, per-patch projections could be in F#
- Maybe useful to store/use item dependencies in DB for sync speed, patch validation
- When you switch branches, update DB with where type names point
- Hashing in progress (check Ocean's hashing PR)
- Switch from .dark files internally to using PM

### Multi-Environment Support
- Full functionality in CLI alone
- Full functionality in VS Code alone  
- Some DX available wholly in each environment
- Some DX should flow between environments (leave session in one place, pick up elsewhere)
- Seamless session transfer between environments
- Shared state persistence across CLI instances (how?)
- VS Code env continually supported by CLI
- CLI state should be persisted and shared across CLI instances
- Should we live in VS Code or CLI? How to combine?
- Maybe fully in web browser? What about classic UI?

### VS Code Specific Considerations
- Main goal: lovely, complete dev UX both fully independently in VS Code and cross-client
- Minimal custom/impl-specific JS in extension needed
- Virtual file system provider (see notes in dev vault)
- Package exploration in nice tree view
- SCM stuff (pending changes, commands, status bar)
- Any other relevant VS Code extensibility (debugging, LSP client, MCP client for Roo)
- Viewing and running tests
- Webview panels for patch review and conflict resolution
- Real-time stuff for new traces, new code (package items)
- URL schema stuff (dark://patch/ID/something)
- Each 'document' in tree can have customizable View ready for VS Code
- Generally design impl to be ready for other editors (emacs, micro, etc.)
- Many commands needed
- Expose VS Code stuff as low-level APIs, build stuff fully in Darklang around that
- Abstract out anything in JS so logic is fully in Dark

### AI Development Support
- Multiple parallel branches/patches  
- Clear visibility into package contents
- Support for exploratory development
- Voice integration readiness
- Thinking for how AI fits into things - CLI flow should consider this
- AI should exclusively support it
- Current limitations of AI developing parallel features need solving
- SCM and PM need to be ready for it

### View Flexibility
Users should be able to view code at any phase (relevant to both CLI and VS Code):
- WrittenTypes AST
- ProgramTypes AST (PT AST)
- Pretty-printed (with custom pretty-printing and syntax highlighting)
- RuntimeTypes (Instructions)
- Custom views for specific types

## Mechanics; Thoughts on Design/Impl

Think about these areas - not prescriptive, just things to consider:

### Package "matter"
- TODO thoughts on reshaping this a bit
- Note package search needs, etc
- What we have
- Hashing in progress
- Separating name from item
- And/or separating the content from the content hash? idk... maybe not.
- Maybe useful to store/use item dependencies in the DB? Could be useful for sync speed, patch validation, etc.
- Values are special and we respect those in various ways (TODO)

### What minimal implementation would work?
- Start with really tiny drafts of artifacts first?
- Feel free to edit code directly, even if we don't stick with it
- Which experiences are in the happy path, absolutely needed? Which things can wait?
- It's hard to anticipate all that a user will want to do, and when they'll want to do it

## Key Anxieties to Address
- Need concrete progress to show in meetings
- Analysis paralysis on design decisions
- Complexity of retrofitting SCM onto existing system
- Supporting both human and AI developers effectively

## Success Criteria
- Can create and share patches between you and coworker
- Basic developer flow works end-to-end
- Clear documentation of architecture and flows
- Confidence for upcoming meetings
- Foundation ready for community involvement

## How to Start

There's a TODOs.md file adjacent to this prompt that has some initial tasks to get you started. You should:
1. Read that TODO list first
2. Use it to track your progress as you work
3. Add to it as you discover new tasks or questions
4. Update it regularly - mark things complete, add new discoveries, reorganize as needed
5. It's a living document - iterate on it over time

Mention the PR in progress about referencing things by hash. I think we can progress without that though.
Mention dev.darklang.com stuff.

Maybe it'd be best to create really tiny drafts of artifacts first?
Feel free to edit code directly, even if we don't stick with it.

OH I need to tell it a bit about ideas regarding VS Code flow ideas, regarding virtual workspaces, files, etc. And tell it about other VS Code extensibility, like tree views, custom views, etc.

What's a reasonable flow or set of flows we can support without requiring an insane amount of work? The real goal is just sharing code between myself and my coworker.

Tell about the current limitations of AI developing parallel features, and the limitations there. We want to solve for that, so the SCM and PM and such all need to be ready for it.

## Additional References in Case They're Useful
(if they are useful, 'tree' them as needed)
- Point to a bunch of specific stuff in Darklang Dev vault (maybe CLI stuff, maybe point to some dynalists too?)
- The rest of the codebase
- Look at the patch structure of existing SCMs (git, pijul, val.town, unison)
- Refer to all existing PM/SCM code
- Point to DB, migrations, schema
- Point to RT.fs, PT2RT.fs, Interpreter.fs
- For each flow, include context/scenario
- Maybe point to Ocean's hashing PR - try to take that over
- Tell AI about how a name can be repointed or something. We need to reflect on the vision of...
- Read REAL CLI code, and CLI.fs (the 'runner')

## Open Questions & Design Decisions

### Critical Questions
- What are the minimal specs to move forward?
- How do we handle the transition from current .dark files to PM-based storage?
- What's the right abstraction for Values vs Globals?
- How do we make patch validation fast enough for real-time feedback?
- What's the optimal UI for branch visualization in constrained CLI environment?
- What's the actual concept of a Package? 
- How do different Darklang servers/hosts work?
- How do we sync some stuff but not other stuff?
- How are we gonna update the system of Modules and named things?
- If Refactors are just exprs, maybe Patches are an Expr that produces Ops?
- Should "PackageLocation" be abstracted out of PackageType.Name?
- Local client state needs to know when patch hasn't/has been validated
- How to handle conflicts between sequential operations in patches?

### Implementation Considerations
- Make illegal states unrepresentable
- Super/meta "structured editing" for validation states:
  ```fsharp
  | PreValidation of Patch
  | PostValidation of Patch * issues: ??
  ```
- ProgramTime/ParseTime Errors (PTEs) for illegal ops, bad names (NREs), type check errors
- Maybe PatchOps live within a Patch, one Op is CreatePatch or MergePatch
- For MergePatch to be accepted, validated against what's already there
- Includes conflict-resolutions and data migrations (as expressions)
- Model experience of local-only stuff (secret code that doesn't sync, patches that stay local)
- You can choose which patches get synced where
- SyncCheck rules needed

### Things to Think About Ahead of Time
- Look at the patch structure of existing SCMs
- We need validation of patches, modeling of Conflicts, plans to support both manual and automatic conflict resolution
- Might need to sketch start of PTEs (program-time or parse-time errors) - might be for illegal ops in patches, or bad names (NREs), or type check errors
- Local _client_ state needs to know when a patch hasn't/has been validated
- Make illegal states unrepresentable
- Think through Ops, Conflicts, Resolutions
- Sketch out a "PR" in various states, using these types, in an adjacent .fs file or something. 'Emulate' the flows

### Future Ideas
- Port cronchecker and qw to Darklang (queues and crons are just values)
- Port stachu.net or darklang.com as test project
- Write (dark) script triggered by Listener (listener.exe special CLI program)
- Model PatchOpConflict type, OpConflict thing
- Sketch "PR" in various states using these types, 'emulate' flows
- Don't want to deal with patches for MANY tiny changes
- Demo easy MCP servers (like old Slack bot gimmick) for Sunday

## Here's What I Want From You

What are some paths forward to bring us from where we are to where we want to be?

I suspect package items will need some context of what branch they're available in. As you merge, that hash col changes. And package search somehow knows what items to include, depending on current branch and 'above' branches. This seems like it could be complicated or need a big IN() thing. Which is probably OK for now!

I suppose we'll need some sort of Branch or Patch table, with dependencies represented.

I want a way to interactively explore the package tree. Maybe using wip.darklang.com/packages, but I think that needs some work to be _really fast_, likely supporting keyboard stuff, etc.

What's the actual concept of a Package? How do different darklang servers/hosts work? How do we sync some stuff but not other stuff?

Maybe the Package Tables are projections of patch stuff... but that doesn't quite work if we want to be able to work on multiple branches at once.

An Op is any change to the package manager. I suppose some of those are patch-merging, patch-creation, etc. Renames. AddType.

Maybe we need Ops for the things that are accepted, and Intents for the thing above that, which needs validation? Feels like an FRP thing.

Maybe PatchOps live within a Patch, and one Op is a CreatePatch or MergePatch or something. And for a MergePatch to be accepted, it's validated against what's already in there? And includes conflict-resolutions and such? And any migrations of data, in the form of expressions?

I'm overwhelmed and I need some guidance. Please work with me to produce some artifacts to get me going. My main goal is to have some real things to chat w/ my coworker w/ tomorrow, and more importantly have to bring to my advisor on Sunday. I'm embarrassed by the lack of progress. Let's make steps one by one, making sure we produce _some_ artifacts along the way.

## More Unorganized Thoughts

What do I actually want? I just need a way to develop software.

What's the minimal to get stuff done? How do we encode these options, and plan the whole story of implementation? Which experiences are in the happy path, absolutely needed? Which things can wait?

It's hard to anticipate all that a user will want to do, and when they'll want to do it. We need many views, they need to be composable, and you need to be able to jump to different things very quickly, to not break focus. IDEs do this a lot by composing (visually) different panes and such.

Should we live in VS Code or the CLI? How can we combine? Maybe fully in the web browser? What about the classic UI? Once we have a rough plan, how do we get other folks involved? Fill out dev.darklang.com/todos, request all sorts of input - feedback, PRs, reviews, advise, chats.

If we just keep adding commands and views, would a system just naturally develop? Folks will want to have their own system, not just living inside of a specific box we've built. They need to augment our system, actively. The only thing that needs to be consistent is the spec for syncing data. And we only have a pretty finite set of data - types, values, fns, etc.

Values might be best treated as a bit different than fns and types, but idk exactly how. (Because some values are malleable, and they're simply referenced, and the things that use those values shouldn't need to have any adjustment. And other values should require the dependents to update a reference etc. Idk when a Value is marked as one or the other. And how the syncing changes. Maybe we need to split Values from Globals or something. If they sync differently, then having them all in the same table is a bit weird.)

Introduce darklang gently. Show a user ProgramTypes, and RuntimeTypes, and slowly build the idea out naturally. No reason it should take more than 10mins.

Maybe stachu.net, my website, or darklang.com, is a good project to focus on. At this point, how hard/painful would it be to 'port' darklang.com to dark?

How are we gonna update the system of Modules and named things? I think Names needs to be split out some way.

Ocean and I can send each other .patch files (eventually those patch files are shared against a central server, but it's OK if we directly comm for now. Actually, maybe directly comm would be neat. Or maybe we use my home server for 'central').

Model the experience of local-only stuff too. Secret Stachu private code that I don't sync. Patches that _stay local_ basically. You can choose which patches get synced where. You should be able to have SyncCheck rules or something.

Write a (dark) script that's triggered by a Listener. A local Listener (local). `listener.exe` - maybe it's just a special CLI program that's running. But, we have a fancy way of setting it up.

Oh port cronchecker and qw to Darklang. Queues and crons are just values.

thanks.rust-lang.org

CLI needs to be able to show "viewing reality at branch #89db42a2"

dev.darklang.com should have some review of my reMarkable folder/docs setup

- Don't want to deal with patches and etc for MANY changes (esp. tiny)
- Another goal: switch from .dark files internally to using the PM

Sunday: demo easy MCP servers (like our old Slack bot gimmick)

## Outro

(some reminders)
Start by creating TODO

---

*I've tried my best to write this prompt to produce ideal results. That said, it could likely use some work. Can you please do your best to adjust it to improve it further? Please try to maintain my voice, generally. (Voice best seen in the blog.darklang.com post mentioned.)*

*This prompt consolidates extensive planning notes for Darklang's developer experience evolution. I'm experiencing analysis paralysis and need systematic progress toward a working SCM/PM system. The main goal is to have real things to chat with my coworker tomorrow and bring to my advisor on Sunday. Let's start with boring patches and build complexity. Actually write software in Darklang.*