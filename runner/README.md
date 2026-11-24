# Agent Language Benchmark Runner

Compare how Codex and Claude build the same tasks in Dark, Python and TypeScript. Everything runs locally through the agents’ command-line tools. The runner records whether each session finishes, how long it takes, and any token usage or cost the CLI reports.

## Start

You need Node.js 22 or newer and the agent CLIs you want to use. Log in through those CLIs before running a benchmark. Prepare your language tools first; the runner does not install dependencies.

From this project folder, run:

```sh
npm start
```

Open http://127.0.0.1:4317. No npm packages need to be installed. The runner currently supports macOS and Linux.

## Your first run

Opening the dashboard does not start an agent or use your quota. Try this small run first:

1. Name the configuration **First run**.
2. Under **Agents**, enable one CLI you have installed and logged in to. Check its executable path and leave **Model** on **CLI default**.
3. Under **Languages**, enable **Python** and turn off **Dark** and **TypeScript**. Make sure `python3` is installed first. You can add other languages after this works.
4. Open **Tasks** and select only **FizzBuzz**.
5. Open **Limits**. Set **Runs per task** to **1** and the time limit to **5** minutes.
6. Leave the output folder at its default. Click **Check environment** and fix any problems it reports.
7. Click **Review & run**. Read the prompt and settings, then click **Start batch**. Starting the batch uses your agent account and quota.
8. Watch **Agent activity**. When the attempt finishes, open it to read the transcript and use **Check code** to see the generated files. On macOS, you can also use **Run the program** to try it yourself.

If the attempt is **environment blocked**, read the error before retrying. A **completed** attempt means the agent session finished; check its tests and output to see whether the program works.

Once this works, enable more languages, agents or tasks to make a comparison. Your configuration is saved in this browser.

## Set up a comparison

1. Name your configuration and choose Codex, Claude or both. Set their executable paths and choose models from the selectors. **CLI default** uses the agent’s configured default; **Custom model…** lets you enter an exact name. Codex choices come from its local model cache. Claude choices come from a list maintained in `src/model-choices.js`; use a custom model if that list does not match your CLI's available models.
2. Choose the languages. Dark needs a local binary path; set it in the dashboard, or start with `DARK_PATH=/path/to/dark npm start` to prefill it. Its documentation and build label are optional. Python and TypeScript only need to be enabled.
3. Select tasks, repetitions, time limits and an output folder.
4. Check the environment, review the configuration and start.

An attempt is one task, in one language, with one agent. Attempts run one at a time in a shuffled order. Each starts with a new project folder and conversation. The default time limit is 45 minutes.

Before you start, the review screen shows every prompt and the settings used to launch the agents. The runner sends your task text and the language guidance shown there. It does not send follow-up prompts or ask the agent to repair its work.

The default tasks ask the agent to test its program and explain what happened. The runner does not check the program itself or require a particular command or output format. **Completed** means the CLI finished without reporting an error. Read the transcript to see whether the program worked.

### Permissions and network access

Agents can edit their project files and run commands without approval prompts. Codex uses `workspace-write`. Claude uses `acceptEdits` and runs shell commands inside its sandbox. Both can read files outside the project.

Network access is off unless a task lists **Network sites**. For example, GitHub-stats allows `api.github.com` and `github.com`. The runner passes this list to the sandbox, not to the agent's prompt. Requests to other hosts are blocked.

These sessions cannot show approval dialogs. If an action needs approval, it is denied. Permission problems can leave an attempt marked **environment blocked**. **Cancel** stops the agent and its ordinary child processes.

### Dark setup

Each Dark attempt gets its own copy of the binary and the files it needs to start, a fresh package store, and a separate .NET cache. The runner does not copy or open your original package store. Attempts do not share package changes.

The review shows these paths and environment variables. Copying the binary and setting up its store happen before timing starts. Your agent login, settings and memory still come from your usual CLI setup.

### Follow an attempt

The **Agent activity** feed shows messages, commands, results and errors. Expand a command to see more detail. The runner also saves the original JSONL logs and error output, and reloads recent activity after a restart. Notes you add are saved but are not sent to the agent.

## Read the results

Results are ordered from fastest to slowest. The comparison table shows only the task and Dark’s relative input tokens, output tokens, cost and time against other languages, using medians for the same agent and model. For example, **2× the tokens of Python** or **0.5× the cost of TypeScript**. Individual times and raw usage remain in the attempts table.

Failed attempts keep their measured time and usage. Missing measurements appear as **—**, not zero. Only CLI-reported cost is shown; subscriptions are not counted. These are CLI results and may differ from editor or desktop sessions.

Configurations are saved in your browser. Results, prompts and logs go in your chosen output folder. The runner saves JSON and CSV results and reloads finished batches and reports after a restart.

### Check the generated code

Click an attempt, then **Check code**, to browse its files. The viewer only reads the files.

On macOS, **Run the program** lets you try a command in a fresh copy of the project. The suggested command is a guess; you can edit it. You can see its output, errors and exit code. Dark commands use the attempt's own binary and package store through `$DARK`, including any permissions the agent granted there.

Manual runs use the macOS sandbox. It blocks access to your home folder and limits file writes to the temporary copy and the attempt's runtime folders. It allows network access and reads elsewhere on the machine. The command gets a small environment for finding tools and using temporary folders, rather than your full environment.

A manual run lasts at most 60 seconds and must wait until benchmarks and reports finish. Its results go in `manual-runs/`, separate from the benchmark results. Close the popup with **Close** or Esc. The original generated project stays in place; `final-project.json` records its location.

### Reports and cleanup

After a batch finishes, click **Create Dark report** to review the saved results and logs in a separate agent session. The report uses short, readable bullets to point out problems, repeated attempts, missing functions or packages, and what may have taken the most time. It avoids line-number citations and only claims timings the logs support. Click **View report** to read it in a popup, and Close or Esc to dismiss it. Analysis time, tokens and cost stay separate from benchmark results.

Use **Delete batch** beside the batch title to remove the entire batch: all attempts, results, logs, reports, generated projects and its private Dark runtimes. The sidebar configuration stays. Cancel active work and wait for analysis to finish before deleting.

Older results that used automatic checks stay saved. Comparisons keep those results separate from sessions marked completed without those checks.

The runner uses your existing agent login and settings; its memory, plugins and instructions may still influence a session. It does not copy authentication files, and it redacts known credentials from saved logs.

For a step-by-step explanation, read [What the runner does behind the scenes](HOW-THE-RUNNER-WORKS.md).

## Share with coworkers

Share the source through a private repository. Each person should clone it, install Node.js 22 or newer and their chosen agent CLI, log in with their own account, then run `npm test` and `npm start` from the cloned folder. No `npm install` is needed. Start with one small task, one language and one repetition; real attempts and reports use that person's agent quota. Dark requires a local binary supplied by that person.

Keep the dashboard on localhost; it is intended for one local user. Use trusted task prompts and a terminal environment without unrelated secrets. Agent processes inherit the server's environment and existing CLI configuration, including instructions, hooks and plugins. The agent sandboxes allow reads outside the project. The manual-run sandbox restricts file writes and home-folder reads, but allows network access and reads elsewhere; use a disposable machine or VM when stronger isolation is needed.

`results/` and `.cache/` are ignored by Git. Keep custom output folders outside the repository too. Saved prompts, transcripts and reports can contain private material despite credential redaction; review any artifacts separately before sharing them. Do not distribute your agent login files, local configuration or Dark binary with the source.

The two manual-run integration tests require macOS and are skipped on Linux. Run them in a normal macOS terminal: restricted environments can reject `sandbox-exec` with `sandbox_apply: Operation not permitted`. If this happens, test manual runs in a normal macOS terminal. Keep the sandbox enabled.

## Project folders

- `src/`: runner code, including the shared prompt builder.
- `public/`: dashboard page, styles and browser code.
- `benchmark-tasks/`: the nine default task prompts.
- `runner-tests/`: tests of the runner itself, using fake agents.
- `results/`: the default folder for saved results and reports.
- `.cache/`: remembers custom output folders so saved results can be found again.

## Work on the runner

Run `npm test` to check the runner without spending agent quota.

Use `npm run dev` while editing. It restarts the server when runner code changes, so use `npm start` for real benchmarks. A restart interrupts active work and loses the waiting queue. Refresh the page after changing dashboard files.
