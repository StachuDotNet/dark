# How the runner works

A batch is a full comparison. An attempt is one task, in one language, with one agent, for one repetition.

## Before a batch starts

The runner checks your settings, task text, CLI versions and available tools. New project folders must be outside Git checkouts, with no `AGENTS.md` or `CLAUDE.md` in their parent folders.

For Dark, it copies the selected binary and the files it needs to start into a private folder. It starts with an empty package store and a separate .NET cache. It does not copy your existing packages, logs or permissions. The binary must be able to set up a fresh store from its own package data.

Environment previews use temporary copies and delete them afterward. This setup happens before attempt timing starts.

The review screen shows the exact prompts, executable paths, launch options, environment changes, time limits and project locations. Each Dark path points to that attempt's own copy. If the prompts change after review, the runner asks you to review them again.

The runner uses your existing CLI login and settings. Your instructions, memory, hooks and plugins can still affect the agent. The runner cannot list everything the CLI may load.

## During an attempt

The runner creates a new project folder and CLI conversation. For Dark, it also copies the batch's unchanged binary into the attempt's own runtime folder and sets up a fresh store and cache. It records the paths, environment and setup time. Setup time does not count toward the attempt's duration.

Attempts run one at a time in shuffled order. The runner saves that order so you can check it later.

Timing starts just before the prompt is sent. The agent chooses how to build and test the program. The prompt names the language, gives the Dark path, version and optional documentation, and asks the agent to explain blockers without installing dependencies. Python and TypeScript tasks use the tools already available on the machine.

The default tasks ask the agent to test its work and explain the result. The runner does not run its own acceptance tests, require a `run.json` file, enforce an output format or send repair prompts.

Messages, commands and errors appear in the activity feed. Full CLI logs are also saved. Known secrets and common credential formats are redacted, but the logs can still contain private information.

An attempt can end with these statuses:

- **Completed:** the CLI finished without reporting an error. This does not prove the program works.
- **Environment blocked:** a permission or CLI problem stopped the attempt.
- **Limit reached:** the time ran out.
- **Cancelled:** you stopped the attempt.

## Time, tokens and cost

The default limit is 45 minutes per attempt. That includes the agent's own tests and repairs. Environment checks and project setup happen before timing starts.

Tokens and cost come from the CLI. Missing values stay blank. Input totals include cached input, and reasoning tokens are not counted twice. User prompts and model calls are recorded separately; Codex model-call counts stay blank when they are unavailable.

The runner groups results by task, agent, model, language, outcome and checking method, then uses medians for comparisons. Dark's time and cost are compared with matching completed sessions. Older results with automatic checks stay separate from newer sessions without those checks. Failed and blocked attempts keep their measured time and usage too.

## Cancelling and saving

Cancel stops the active command's process group and its ordinary child processes. A process that deliberately detaches may keep running. Notes you add are saved but never sent to the agent. You cannot reply to approval or clarification prompts during an attempt.

The output folder contains settings, tool versions, prompts, logs and run records, plus JSON and CSV results. Generated projects stay in their own folders. Each attempt's `final-project.json` records where its project lives.

After a batch finishes, the runner records file hashes in `frozen.json`. Reports check these hashes to make sure the saved evidence has not changed.

Finished batches and reports reload after a restart. Active attempts and waiting batches do not resume.

**Delete batch** asks for confirmation, then removes the batch's results, logs, reports, generated projects and private Dark runtimes. It keeps the configuration in the sidebar. Stop active work and wait for reports to finish before deleting a batch.

## The Dark report

Once a batch is finished and the runner is idle, **Create Dark report** starts a separate agent conversation. You can review its instructions in the dashboard first.

The runner checks the saved file hashes and gives the agent the results, Dark transcripts and CLI errors. Older batches may also include automatic check results.

The report is a short list of problems supported by those logs: repeated failed commands, confusing errors, missing functions or packages, and places where time may have been lost. It says whether each problem was resolved. It distinguishes a missing feature from something the agent failed to find, and uses only recorded timings and retry counts.

The report session has a 15-minute limit. Its report, logs, tokens, cost and time are saved separately and do not count toward the benchmark. Its findings are never sent back into attempts.

Click **View report** to read it. Use **Close** or Esc to dismiss it.
