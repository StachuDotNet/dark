<!-- TASK CONTEXT START -->
# Active Task - Executing Phase

You are in an automated execution loop. Work through the todos systematically.

## The Task

Tidy VS Code extension page

We have a VS Code extension that's WIP, very alpha.
It requires the CLI has already been downloaded (from GH Releases) and installed (run 'install' in the exe).

Please update the extension's "landing page" to note such, and include some brief text explaining what Darklang is, and the fact that it's a WIP, with the extension (at this point) meant for internal testing/usage.

Tangentially, somehow demand the vscode lockfile to be in sync with the package.json. We keep running into a dumb issue in CI.

## Current Todos

# Task Todos

<!-- Claude will populate this during planning -->

- [ ] (Planning) Research and understand the codebase
- [ ] (Planning) Create detailed implementation plan
- [ ] (Planning) Update this todo list with specific tasks


## Instructions

1. Find the next uncompleted todo (marked with [ ])
2. Complete it
3. Mark it done in .claude-task/todos.md (change [ ] to [x])
4. Run tests to verify
5. Continue to next todo

## Commits

Commit early and often as you make progress:
- Short casual commit messages (e.g., "add user auth", "fix login bug")
- No attribution/co-author needed
- Commit after completing each logical chunk of work
- Don't wait until the end to commit everything

## When Done

When ALL todos are complete and tests pass:
- Make a final commit if there are uncommitted changes
- Write "done" to .claude-task/phase
- The loop will exit

## If Stuck

If stuck, just exit - the loop will restart you.
Leave notes in .claude-task/todos.md about what's blocking.

<!-- TASK CONTEXT END -->

# This is the main Darklang monorepo. Please assist in the development of this language+platform.

## External resources:
- team notes: ~/vaults/Darklang Dev
- in-progress website: wip.darklang.com
- posts on blog.darklang.com
- most recent post on stachu.net
- other source code
  - website (WIP) ~/code/darklang.com
  - docs (outdated) ~/code/docs

### Key Directories

- **`backend/`** - F# backend implementation, type system, execution engine
- **`packages/`** - Darklang packages organized by namespace -- the bulk of user-facing code is here.
- **`rundir/`** - Runtime directory with logs and temporary files
- **`scripts/`** - Development and build scripts

## Regarding Builds
you should never try to manually rebuild code or reload packages.
All of these things happen automatically, thanks mostly to ./scripts/build/compile running all the time in the background, building stuff and logging as it does.

just be patient, poll those logs, and your changes will take effect. eventually

package-reloads are higher level, happen whenever you change a .dark file, take about 10s, and log to ./rundir/logs/packages-canvas.log.

.net builds are lower level, happen whenever you change an F# file, and take up to a min to load, and log to build-server.log. when they finish, they trigger a package reload too, 'just in case'

## Regarding Darklang Syntax

### Critical Rules
- Darklang is whitespace- and indentation-sensitive - proper indentation and line breaks are critical
- No nested function definitions allowed - extract all functions to module level
- The LHS of a |> needs parentheses if it's complex: `(Stdlib.List.range 0L 100L) |> Stdlib.List.map fn`
- List items are separated by `;`
- Cannot use `/` operator for Int64 division - use `Stdlib.Int64.divide`
- Cannot use `-` operator for Float subtraction - use `Stdlib.Float.subtract`
- Reserved keyword: "function" is reserved in F#, use "fn" instead for field names
- ++ is for string concat; @ doesn't exist - use Stdlib.List.append to combine lists

### Record Construction
- When constructing records, ensure the `{` is never to the left of the type name
- Correct: `RecordType { field = value }` or multi-line with proper indentation
- Wrong: Type name and opening brace misaligned

### Enum Construction
- When constructing enums, need typename before case name: `EnumType.CaseName`
- When deconstructing in match expressions, use only case name: `| CaseName ->`

### Function Arguments
- Check parameter order carefully - e.g., `Stdlib.String.join` expects list first, then separator
- `Stdlib.List.range` expects start and end values, both inclusive
