# Task Todos - TODO/CLEANUP Resolution

## Overview
This task involves finding and documenting all TODO and CLEANUP comments in the codebase, categorizing them as low-hanging fruit vs complex tasks.

## Completed Planning Tasks
- [x] Search for all TODO comments (found ~576)
- [x] Search for all CLEANUP comments (found ~211)
- [x] Review and categorize items
- [x] Create comprehensive report at `.claude-task/todo-cleanup-report.md`

## Implementation Checklist

The following are specific, actionable items from the report that can be completed:

### Quick Wins (Can be done immediately)

- [x] Fix broken open source link in README.md:9
- [x] Rename `cki` variable to `consoleKeyInfo` in backend/src/BuiltinCli/Libs/Stdin.fs:25
- [x] Fix typo "simplfy" to "simplify" in migration comment (backend/migrations/20250717_214941_initial.sql:162)
- [x] Replace "TODO" placeholder in hover information messages (multiple files in packages/darklang/languageTools/lsp-server/hoverInformation.dark)
- [x] Update tree-sitter test template name (tree-sitter-darklang/test/corpus/_template.txt:2)
- [x] Remove or update LightTODO comment in scripts/builder:41
- [x] Check and update Zig version in Dockerfile:252
- [x] Pin dependency versions in Dockerfile:240 (commented out code, not active)
- [x] Move hardcoded values to config in vscode-extension/client/src/utils/http.ts:3

### Test Coverage Improvements

- [ ] Add more parser tests (backend/tests/Tests/LibParser.Tests.fs:37)
- [ ] Add more interpreter tests (backend/tests/Tests/Interpreter.Tests.fs:380)
- [ ] Add simple interpreter test cases (backend/tests/Tests/Interpreter.Tests.fs:55)
- [ ] Add nested lambda tests (backend/tests/Tests/TestValues.fs:520)
- [ ] Add MPEnum tests (backend/tests/Tests/TestValues.fs:173)
- [ ] Backfill RT type tests (backend/tests/Tests/Serialization.DarkTypes.Tests.fs:150)

### Description Replacements (Mechanical)

- [x] Replace "TODO" descriptions in PT2RT tests (backend/tests/Tests/PT2RT.Tests.fs - lines 1633, 1646-1667)
- [x] Replace "TODO" descriptions in TestValues.fs (backend/tests/Tests/TestValues.fs - lines 20-798)
- [x] Update error pretty-printing TODO messages (packages/darklang/prettyPrinter/runtimeError.dark)
- [x] Replace TODO in canvas pretty-printer (packages/darklang/prettyPrinter/canvas.dark:10)

### Documentation Updates

- [x] Complete error documentation (docs/errors.md:53)
- [x] Document or fix .mono directory issue (vscode-extension/.vscodeignore:15)
- [x] Update CODING-GUIDE automated package operation (CODING-GUIDE.md:92)

### Build System Improvements

- [x] Investigate and fix/remove i386 target support (scripts/build/build-parser:79)
- [ ] Add CI tests for CLI executable (scripts/build/build-release-cli-exes.sh:6)
- [ ] Implement parallel builds with gnu parallel (scripts/build/build-release-cli-exes.sh:40)
- [ ] Remove internet dependency from parser build (scripts/build/build-parser:10)
- [ ] Add signature verification to Dockerfile:270

### Formatter Additions

- [ ] Add SQL formatter to format script (scripts/formatting/format:151)
- [ ] Add Python formatter to format script (scripts/formatting/format:152)
- [ ] Add bash formatter to format script (scripts/formatting/format:153)

### VS Code Extension Features

- [ ] Implement branch rename API call (vscode-extension/client/src/commands/branchCommands.ts:141)
- [ ] Clarify branch label/name usage (vscode-extension/client/src/commands/branchCommands.ts:100)
- [ ] Implement instance content provider (vscode-extension/client/src/providers/darkContentProvider.ts:52)
- [ ] Implement branch list view (vscode-extension/client/src/providers/darkContentProvider.ts:118)
- [ ] Add AST view support (vscode-extension/client/src/providers/content/packageContentProvider.ts:22)

### Configuration Management

- [ ] Remove hardcoded instance name (packages/darklang/cli/syncService.dark:92)
- [ ] Get instance name from state (vscode-extension/client/src/ui/statusbar/statusBarManager.ts:19)
- [ ] Replace hardcoded API key placeholder (user-code/darklang/scripts/prompt.dark:1)

### LSP Server Improvements

- [ ] Implement batch request handling (packages/darklang/languageTools/lsp-server/lsp-server.dark:83-84)
- [ ] Handle unsupported LSP methods properly (packages/darklang/languageTools/lsp-server/handleIncomingMessage.dark)

### Database Schema Updates

- [ ] Consider adding updated_at column (backend/migrations/20251015_192755_package_schema_rewrite.sql:72)
- [ ] Add missing name field (backend/migrations/20250717_214941_initial.sql:12)
- [ ] Review and simplify primary key (backend/migrations/20250717_214941_initial.sql:162)

### Code Quality

- [ ] Split TestUtils.fs into smaller files (backend/tests/TestUtils/TestUtils.fs:1)
- [x] Tidy up code in Stdin.fs:191 (backend/src/BuiltinCli/Libs/Stdin.fs:191)
- [ ] Improve error messages in dict operations (backend/testfiles/execution/stdlib/dict.dark)
- [ ] Improve error messages in list operations (backend/testfiles/execution/stdlib/list.dark:295)

## Notes

- The full report with detailed context is available at `.claude-task/todo-cleanup-report.md`
- Total items found: ~576 TODOs, ~211 CLEANUPs
- Many items are blocked on larger refactoring efforts (type system, parser migration, etc.)
- Focus on low-hanging fruit that can be completed independently
