
- binary serializers can _generally_ be adjusted to be fine when PT changes. like, (TODO THINK MORE)
-


- design each phase to be testable by you

- update the do


if no PT->PT path is given, refuse to upgrade or something


backups aren't meant for local dev - they're only a useful for thing for deployed instances. (when someone downloads the release exe and does an install - and later installs a new version of darklang, which)
local dev, it's OK to wipe etc. there should be a remote repository of old .db backups, which are loaded for local dev's needs. probably need a tiny site (written in and hosted by darklang on whatever new central server) to explore the past .db files, download them, etc.

think for a bit about the binary serializers and how they can adjust over time as PT stuff changes. I think we can somehow support migrations or something by keeping multiple versions of some things around, and encoding versions or something in the blobs

break things down a bit - have a root.md file, keep it minimal with the major steps, and have it refer to other .md files in that dir. all TODOs should be formatted as `- [ ]` style. any qeustsions, put in a separate questions.md doc for me to address. think of things as a dependency _graph_ not just a linear set of steps. identify/determine what are the lowest-level steps.

for now, maintain all serializer versions. I'll purge old versions over time as I see fit.

right now the binary serializer

the push/pull mechanism might be best to be working like it does locally -- with rebases and merge. basically, local should be updated before it can push, etc. Go study how the SCM stuff currently works (via the cli docs). focus on a client/server mech rather than P2P

spec things out as much as you can. be thorough, but _brief_ (not wordy - just enough for me to review and approve of).

think _even harder_ about the boostrapping chicken/egg stuff. we've tried this before and failed. I think you have the rough ideas right, though.

PT changes should _not_ require a re-parse. at the end of this, no .dark files in the repo. Rather, the F# code should have some way to map from old PT to new PT. I don't think this will happen any time soon, though, so we can realistically punt that problem. if you do solve for it at all, keep the solution super minimal.

keep in mind that we're solving for 2 contexts: the 'production' context (the central server and the client instances on my host machine and my coworker's machine), and local dev. We don't need to solve for all problems in both contexts.

"For two people, "one of you runs the server" is probably fine." - nah we'd like a separate server. because it needs to be accessible across the internet, etc. I do have a big PC that's always on that I could expose via tailscale or soemthing, though. That's prob simpler than something else. But I'm not sure how that "server" should be set up - will our CLI app need adjustment, or do we need a separate CLI app that the "server" CLI app? hmm. anyway, it should always be on and accessible, somehow.

the backups only need to happen on the server, I think.

dark has branches. how does sync/push/pull relate to branches? what about WIP ops? (prob, WIP isn't ever synced)

> What does "sharing a branch" mean concretely? "I push my branch to the server, you pull it and can see my functions" — is that the workflow?
yep, that's right. no need for real-time anything, or any sort of automatic sync.

we do care about preserving commit history.

user_data doesn't need to be synced.

yes, when we create fns/types/vals via the CLI, they only live in the DB. and that's intended.

to be clear, _all_ of package code will be packaged in a .db every release.

Hmm: when I'm working on a git branch of dark (like, building dark itself) and writing package code to be referenced by F# (via PackageIDs etc), how would that relate to darklang branches? how could my local dev environment point to a specific darklang branch for the duration of some effort, in other words? maybe some env var, idk. maybe the names of the branch correspond by convention to something like "pr/1234".

> The git branches `export-import-work` and `start-language-canvas-export-and-pretty-print` — is there anything salvageable there, or are they stale experiments?
Abandon them, they'll just distract us.