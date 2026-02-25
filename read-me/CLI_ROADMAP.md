
### Critical Gaps

| Gap                 | Impact                           | Priority |
| ------------------- | -------------------------------- | -------- |
| No `edit` command   | Can’t modify existing code       | P0       |
| No `delete` command | Can’t remove individual entities | P0       |
| No `docs` command   | No in-CLI documentation          | P0       |
| No multi-line input | Complex code hard to write       | P1       |
| No `test` command   | Manual testing only              | P1       |
| No `check`/`lint`   | Must create to validate          | P1       |
| No `rename`/`move`  | Delete + recreate workflow       | P2       |
| No bulk operations  | One entity at a time             | P2       |

-----

## Proposed Command Additions

### P0: Core Missing Commands

#### `edit <location> <code>`

Modify an existing function, type, or value.

    dark edit @Darklang.MyModule.myFn "(x: Int64): Int64 = x + 1L"

Implementation notes: - Parse new code, validate it - Replace the entity
in WIP - Show diff of what changed

#### `delete <location>` (alias: `rm`)

Remove a single entity.

    dark delete @Darklang.MyModule.badFn
    dark rm @Darklang.MyModule.OldType

Implementation notes: - Check for dependents first (`deps usedby`) -
Warn if other code depends on it - Support `--force` to skip warning

#### `docs [topic]`

Access comprehensive documentation.

    dark docs                    # Overview / table of contents
    dark docs syntax             # Language syntax guide
    dark docs stdlib             # Standard library overview
    dark docs stdlib.list        # List module documentation
    dark docs for-ai-agents      # AI-specific guidance
    dark docs errors             # Error message reference
    dark docs contributing       # For platform developers

### P1: Quality of Life

#### `check <code>` (alias: `lint`, `validate`)

Validate syntax without creating anything.

    dark check "let x = 1L + 2L"
    dark check --file script.dark

#### `test [pattern]`

Run tests matching a pattern.

    dark test                           # Run all tests
    dark test Darklang.MyModule         # Run tests in module
    dark test --filter "parse"          # Filter by name

#### `diff [location]`

Show uncommitted changes.

    dark diff                           # All WIP changes
    dark diff @Darklang.MyModule.myFn   # Changes to one entity

#### `rename <old> <new>`

Rename with dependency updates.

    dark rename @Darklang.Old.name @Darklang.New.name

#### `move <source> <dest>`

Move to different module.

    dark move @Darklang.Utils.helper @Darklang.Core.helper

### P2: Advanced Features

#### `copy <source> <dest>`

Clone as template.

    dark copy @Darklang.Examples.handler @MyApp.handler

#### `module <path>`

Create empty module explicitly.

    dark module Darklang.MyApp.Utils

#### `import <package>`

Explicit dependency management.

    dark import Darklang.Stdlib
    dark import --list

#### `export <location> [file]`

Export to .dark file.

    dark export @Darklang.MyModule mymodule.dark
    dark export --all backup.dark

#### `load <file>`

Load from .dark file.

    dark load mymodule.dark
    dark load --preview mymodule.dark  # Show what would be created

-----

## AI Agent Autonomy Checklist

For an AI agent to be fully autonomous with the CLI:

### Must Have (Currently Missing)

  - `edit` command - modify existing code
  - `delete` command - remove entities
  - `docs for-ai-agents` - comprehensive guidance
  - `docs syntax` - language reference
  - `docs errors` - error explanations
  - `check` command - validate without creating

### Should Have

  - `test` command - automated testing
  - `diff` command - see what changed
  - `rename` command - refactor safely
  - Multi-line input mode
  - Better error messages with suggestions

### Nice to Have

  - `copy` command - templates
  - `move` command - reorganize
  - Bulk operations
  - File import/export
  - Interactive tutorials (`dark learn`)

-----

## Error Message Improvements

Current errors are often cryptic. Proposed improvements:

### Pattern: Include Suggestions

    Before:
      error: Unsupported expression in parser

    After:
      error: Unsupported expression in parser

      The parser encountered unexpected syntax at line 5, column 12.

      Possible causes:
      - Nested function definition (not allowed - extract to module level)
      - Missing parentheses around pipe LHS
      - Incorrect indentation

      Run `dark docs syntax/gotchas` for common syntax issues.

### Pattern: Link to Documentation

Every error should suggest relevant docs:

    error: Type mismatch: expected Int64, got String

      See: dark docs types/type-checking
      See: dark docs stdlib/int64 (for conversion functions)

### Pattern: Show Context

    error: Function not found: Stdlib.List.maps

      Did you mean: Stdlib.List.map ?

      Similar functions:
      - Stdlib.List.map      : ('a -> 'b) -> List<'a> -> List<'b>
      - Stdlib.List.map2     : ('a -> 'b -> 'c) -> List<'a> -> List<'b> -> List<'c>
      - Stdlib.List.mapFirst : ('a -> 'a) -> List<'a> -> List<'a>

-----

## Interactive Improvements

### Multi-line Input Mode

When creating complex code:

    dark fn Darklang.MyModule.complexFn
    > (input: String): Result<Data, Error> =
    >   let parsed = Json.parse input
    >   match parsed with
    >   | Ok data ->
    >     Result.Ok (processData data)
    >   | Error e ->
    >     Result.Error (wrapError e)
    > .
    Created function Darklang.MyModule.complexFn

### Interactive Edit Mode

    dark edit @Darklang.MyModule.myFn
    Current definition:
      (x: Int64): Int64 = x + 1L

    Enter new definition (. to finish):
    > (x: Int64): Int64 =
    >   let doubled = x * 2L
    >   doubled + 1L
    > .
    Updated function Darklang.MyModule.myFn

### REPL Mode

    dark repl
    Darklang REPL v0.1
    Type expressions to evaluate, :help for commands

    > 1L + 2L
    3L

    > let x = [1L; 2L; 3L]
    [1L; 2L; 3L]

    > x |> Stdlib.List.map (fun n -> n * 2L)
    [2L; 4L; 6L]

    > :def myDouble (n: Int64): Int64 = n * 2L
    Defined myDouble

    > myDouble 5L
    10L

    > :save @Darklang.MyModule.myDouble
    Saved as Darklang.MyModule.myDouble

    > :quit

-----

## Package Streaming (Future)

The user mentioned streaming packages so users only get what they need.

### Current State

  - All packages loaded at startup
  - Large initial load time
  - Everything available immediately

### Future State

  - Core packages loaded immediately
  - Additional packages loaded on demand
  - `dark import` triggers package fetch
  - Offline mode with cached packages

### Implications for Documentation

  - Docs for unloaded packages should still be available
  - `dark docs stdlib.crypto` works even if crypto not loaded
  - Loading a package also loads its docs

-----

## Metrics for Success

### AI Agent Autonomy

  - Can complete 90%+ of tasks without human intervention
  - Average task completion without errors
  - Time to first working code

### Documentation Completeness

  - All builtins documented with examples
  - All syntax rules documented
  - All common errors explained
  - All CLI commands documented

### User Experience

  - Time from `dark` to first function created
  - Error recovery success rate
  - Documentation discovery rate

-----

## Implementation Priority

### Sprint 1: Foundation

1.  `edit` command
2.  `delete` command
3.  `docs` command infrastructure
4.  `docs for-ai-agents` content

### Sprint 2: Documentation

1.  `docs syntax` (migrate from CLAUDE.md)
2.  `docs stdlib` (auto-generate from builtins)
3.  `docs errors` (common errors)
4.  `docs cli` (command reference)

### Sprint 3: Quality of Life

1.  `check` command
2.  `test` command
3.  `diff` command
4.  Better error messages

### Sprint 4: Advanced

1.  Multi-line input mode
2.  `rename`/`move` commands
3.  REPL mode
4.  `docs contributing` (for platform devs)
