# The Dark coding agent

`dark agent -p "build me X"` builds X in Dark, on a branch of its own. With no prompt at a
terminal it is a conversation. The agent is package code (`Darklang.Agent`, `Darklang.Cli.Agent`)
around one pure function, and every part of it is something Dark already has: the store, branches,
the repl, tests, traces and the effects policy.

## The loop

    step : State -> Event -> State * List<Command>        packages/darklang/agent/loop.dark

Events are what happened: a prompt, a model reply, a tool's result. Commands are what to do next:
call the model, run a tool, show a message, save the session, finish. `step` does nothing itself.
The runner (`Cli.Agent.drain`) carries out each command and feeds what it produced back in as an
event, in a fold over a bounded range rather than recursion. So a conversation replays from its
events, and the tests in `tests/agent/loop.dark` drive `step` with lists of them and no network.

Every tool call in one model reply runs in order, and all their results go back in one message.
The history is append-only: thinking blocks come back signed and bound to the conversation before
them, so nothing earlier is ever edited. Tool output is capped where it is made instead (30,000
characters, head and tail).

## The model

`Darklang.Agent.Anthropic` speaks the Messages API, streamed: the request (cache breakpoints on the
system prompt, the last tool and the last message; `output_config.effort`; server-side
`fallbacks`), and a fold of the SSE events into a `Reply`. The fold is pure, so a recorded stream
tests it; `--replay <file>` runs a whole session from recorded streams. A 429, 5xx or overloaded
answer is retried after 2, 4 and 8 seconds. The key is read from `secrets/anthropic.key` beside the
store.

## Tools and effects

Each tool declares the effects it has (`LanguageTools.Permissions.Effect`). A session allows
`file-read`, `package-read` and `package-write` (on its own branch only); `--allow` adds more. A
call that needs an effect the session lacks is put to the person at a terminal, and otherwise
refused, with the model told which flag would allow it. Bash runs in a network namespace of its
own unless `http` is allowed.

`ask` puts one question to the person at the terminal; in print mode the model is told to decide
and say so.

The Dark tools (`dark`, `view`, `search`, `define_fn`, `define_module`, `edit_fn`, `eval`,
`typecheck`, `test`) run batches in the session's repl, in-process, so every write answers with
the typecheck it caused (`docs/repl.md`). A batch may not change the instance (`permissions`,
`config`, `install`, ...) or move work off the branch (`merge`, `switch`, `rebase`), and a
`run --allow` inside it may grant only what the session has.

## Sessions

A session is a record in the store's config (`agent.session.a1`) holding the conversation, the
branch and the usage. It has a branch of its own (`agent-a1`) and a repl of the same id, so
`dark @a1` works in what the agent built and `dark merge` takes it. `--continue` and `--resume`
pick a session up. `--fork <id>` starts a new session from another's conversation on a branch off
its branch; the parent's draft is committed first, because a new branch starts from commits.

## The prompt

The system prompt is composed from `PromptSection` package values
(`Darklang.Agent.Prompt.sections`): who the agent is, Dark's syntax where it differs from F#, and
how to work. `Me.Agent.Prompt.sections`, when it exists, replaces the list, so changing the
prompt is a package edit, typechecked like any other. AGENTS.md or CLAUDE.md from the working
directory up is added as project instructions.

## Output

Text by default: progress on stderr, the answer on stdout, then the session's draft and its cost.
`--output-format stream-json` writes the same events Claude Code's print mode does (`system`
init, `assistant`, `user` with tool results, `result` with usage and `total_cost_usd`), so a
harness built for one runs the other.
