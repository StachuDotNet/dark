<!-- TASK CONTEXT START -->
# Active Task - Planning Phase

You have been given a task to complete. Your job is to:

1. **Research** the codebase to understand what needs to be done
2. **Create a plan** with specific, actionable todos
3. **Signal** when planning is complete

## The Task

Sqlite Spike -- Access, DSL

We embed Sqlite in our CLI/Runtime.
Internally, we access a data.db to store various data, in our host language, F#, using (library).

We'd like to avail access to this DB, as well as to user .db files, in our language Darklang, with a minimal set of Builtins.

Separately, a DSL for querying the DB might be cool.
Hack on that, but keep it separate/'above' the main solution, so I can remove it in case it's ugly.

Test what you can, ideally in .dark tests.
Update CLI and VS Code editing experience if relevant.

## Your Instructions

1. Read and understand the relevant code
2. Create `.claude-task/todos.md` with a detailed checklist of specific tasks
3. **When planning is complete**, write "ready" to `.claude-task/phase`:
   ```bash
   echo "ready" > .claude-task/phase
   ```
   This signals the TUI that you're done planning.

4. Tell the user: "Planning complete. Review .claude-task/todos.md. Execution will start automatically."

## What happens next

The automated loop will:
- Run you repeatedly until all todos are done
- You read CLAUDE.md and .claude-task/todos.md each iteration
- Mark todos as [x] when complete
- Write "done" to .claude-task/phase when ALL todos complete

## Important

- Be thorough in research before creating the plan
- Keep todos specific and actionable
- Include testing in the plan
- You can interact with the user now during planning
- **COMMIT your plan** before signaling ready (git add . && git commit -m "plan: <task summary>")

<!-- TASK CONTEXT END -->

@AGENTS.md
