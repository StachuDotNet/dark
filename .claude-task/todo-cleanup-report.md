# TODO and CLEANUP Report - Low-Hanging Fruit

This document catalogs TODO and CLEANUP items from the Darklang codebase that appear to be straightforward to resolve without requiring extensive architectural changes or deep domain knowledge.

## Category 1: Missing/Broken Links and URLs

### Fix open source blog link
**File:** `README.md:9`
**Text:** `Darklang is [open source](https://blog.darklang.com/TODO)`
**Resolution:** Replace the `/TODO` with the actual blog post URL for the open source announcement.

---

## Category 2: "TODO" Placeholder Text in Code

These are instances where "TODO" is used as placeholder text in descriptions, error messages, or test data.

### Replace "TODO" in test parameter descriptions
**Files:** Multiple test files
- `backend/tests/Tests/PT2RT.Tests.fs:1633` - `description = "TODO"`
- `backend/tests/Tests/PT2RT.Tests.fs:1646-1647` - Multiple parameter descriptions
- `backend/tests/Tests/PT2RT.Tests.fs:1656-1657` - More parameter descriptions
- `backend/tests/Tests/TestValues.fs:20` - `description = "TODO"`
- `backend/tests/Tests/TestValues.fs:31,37,49-69` - Many enum and field descriptions

**Resolution:** Replace these with actual descriptions. For test values, these can be brief like "Test parameter" or "Example value for testing" or more specific based on context.

### Replace TODO in hover information
**Files:** `packages/darklang/languageTools/lsp-server/hoverInformation.dark`
**Lines:** 296, 484, 509, 624, 657, 718, 810, 946, 1024, 1176, 1199, 1446, 1637, 1818, 1883, 1976, 2038, 2124, 2158, 2649, 2758, 2815
**Text:** `"TODO: add description when /// comments are supported"`
**Resolution:** Update these to say something more helpful like "Description not yet available" or "No description provided" until the feature is implemented.

### Replace TODO in error pretty-printing
**File:** `packages/darklang/prettyPrinter/runtimeError.dark`
**Lines:** 304-308, 489
**Text:** Various `"TODO ..."` error messages
**Resolution:** Implement proper error messages for:
- `NoExpressionsToExecute`
- `NonIntReturned`
- Metadata display
- Catch-all RTE formatting

### Replace TODO in prettyPrinter functions
**File:** `packages/darklang/prettyPrinter/canvas.dark:10`
**Text:** `"TODO"`
**Resolution:** Implement or remove the placeholder return value.

---

## Category 3: Documentation and Comments

### Update error documentation
**File:** `docs/errors.md:53`
**Text:** `TODO continue to update this doc, starting here.`
**Resolution:** Complete the documentation of error types and their meanings.

### Fix VSCode ignore comment
**File:** `vscode-extension/.vscodeignore:15`
**Text:** `# TODO figure out what's adding the .mono directory`
**Resolution:** Investigate what creates the `.mono` directory and either add it to .vscodeignore or document why it shouldn't be ignored.

### Complete tree-sitter test template
**File:** `tree-sitter-darklang/test/corpus/_template.txt:2`
**Text:** `[TODO name this test]`
**Resolution:** Either remove this template file if unused or provide a proper name/example.

---

## Category 4: Simple Code Improvements

### Remove LightTODO comment
**File:** `scripts/builder:41`
**Text:** `# LightTODO revisit this`
**Resolution:** Review the code and either fix the issue or remove the comment if it's no longer relevant.

### Pin dependency versions
**File:** `Dockerfile:240`
**Text:** `# TODO pin to a recent stable version (i.e. 3.1.37)`
**Resolution:** Pin the dependency to a specific stable version.

### Check for Zig updates
**File:** `Dockerfile:252`
**Text:** `# TODO Occasionally, check https://ziglang.org/download to see if we're using the latest version`
**Resolution:** Check the current version against the latest, update if needed, and document the version check frequency.

### Verify signature
**File:** `Dockerfile:270`
**Text:** `# TODO: verify signature`
**Resolution:** Add signature verification for the downloaded file or document why it's not necessary.

### Fix hardcoded values
**File:** `vscode-extension/client/src/utils/http.ts:3`
**Text:** `// CLEANUP: don't hardcode these values`
**Resolution:** Move hardcoded values to configuration or environment variables.

### Add proper auth when available
**File:** `vscode-extension/client/src/services/accountService.ts:7`
**Text:** `// TODO: Replace with proper auth when available`
**Resolution:** Either implement proper auth or update the comment to reference when/how this will be done.

---

## Category 5: Missing Test Cases

### Add parser tests
**File:** `backend/tests/Tests/LibParser.Tests.fs:37`
**Text:** `// TODO: order these by simplicity, and add more tests`
**Resolution:** Organize existing tests and add more test cases.

### Add interpreter tests
**File:** `backend/tests/Tests/Interpreter.Tests.fs:380`
**Text:** `// TODO: add more tests`
**Resolution:** Add more test coverage for the interpreter.

### Add more simple tests
**File:** `backend/tests/Tests/Interpreter.Tests.fs:55`
**Text:** `// CLEANUP back fill with more simple stuff`
**Resolution:** Add basic/simple test cases to improve coverage.

### Backfill with more RT types
**File:** `backend/tests/Tests/Serialization.DarkTypes.Tests.fs:150`
**Text:** `// CLEANUP backfill with more things from RT`
**Resolution:** Add test cases for more RuntimeTypes.

### Test nested lambdas
**File:** `backend/tests/Tests/TestValues.fs:520`
**Text:** `// TODO: test nested lambdas`
**Resolution:** Add test cases for nested lambda expressions.

### Test MPEnum
**File:** `backend/tests/Tests/TestValues.fs:173`
**Text:** `// TODO: test MPEnum`
**Resolution:** Add test coverage for MPEnum (match pattern enum).

---

## Category 6: Formatting Script TODOs

### Add formatters for multiple languages
**File:** `scripts/formatting/format`
**Lines:** 151-153, 164
**Text:**
- `# TODO sqlfmt for sql`
- `# TODO add python`
- `# TODO bash formatter`
- `# CLEANUP there's no JS/CSS files at the moment`

**Resolution:**
- Add SQL formatting with sqlfmt
- Add Python formatter (e.g., black or ruff)
- Add bash formatter (e.g., shfmt)
- Add JS/CSS formatting when needed (or remove comment if not needed)

---

## Category 7: Build and CI TODOs

### Add i386 target support
**File:** `scripts/build/build-parser:79`
**Text:** `# TODO it seems i386 targets aren't working - commented out for now`
**Resolution:** Investigate why i386 targets aren't working and either fix or permanently remove support.

### Add CLI executable testing
**File:** `scripts/build/build-release-cli-exes.sh:6`
**Text:** `# TODO run some test in CI against the current-runtime executable`
**Resolution:** Add CI tests for the CLI executable to verify it works.

### Improve parallel build
**File:** `scripts/build/build-release-cli-exes.sh:40`
**Text:** `# TODO: do better with gnu parallel or with this solution that I couldn't make work`
**Resolution:** Implement proper parallel builds using gnu parallel.

### Remove internet dependency for build
**File:** `scripts/build/build-parser:10`
**Text:** `# TODO don't require internet connection for this!`
**Resolution:** Cache or bundle dependencies so builds work offline.

---

## Category 8: Simple Refactorings

### Extract constants and improve variable names
**File:** `backend/src/BuiltinCli/Libs/Stdin.fs:25`
**Text:** `// CLEANUP rename cki to something better`
**Resolution:** Rename `cki` to a more descriptive name like `consoleKeyInfo`.

### Split large test file
**File:** `backend/tests/TestUtils/TestUtils.fs:1`
**Text:** `// CLEANUP: split this file into smaller files`
**Resolution:** Break up the large TestUtils file into logical smaller modules.

### Tidy code
**File:** `backend/src/BuiltinCli/Libs/Stdin.fs:191`
**Text:** `// CLEANUP tidy`
**Resolution:** Clean up and refactor the code at this location.

---

## Category 9: Error Message Improvements

### Improve error messages in dict operations
**File:** `backend/testfiles/execution/stdlib/dict.dark`
**Lines:** 61, 85
**Text:** `// CLEANUP improve the error message to:`
**Resolution:** Update error messages to be more descriptive and helpful.

### Better error message for list operations
**File:** `backend/testfiles/execution/stdlib/list.dark:295`
**Text:** `// CLEANUP this error message is not ideal in 2 ways`
**Resolution:** Improve the error message based on the noted issues.

---

## Category 10: Remove Dead/Commented Code

### Remove unused equality check code
**File:** `backend/testfiles/execution/language/elambda.dark:1`
**Text:** `// CLEANUP fix equality checks of lambdas`
**Resolution:** Either fix lambda equality or remove the dead code.

### Remove old parser code once migration complete
**File:** `backend/testfiles/execution/language/elambda.dark:109`
**Text:** `// CLEANUP include this once we've switched over to the new parser`
**Resolution:** Uncomment/include this code once the new parser is fully deployed.

### Remove incomplete code
**File:** `tree-sitter-darklang/grammar.js:445`
**Text:** `//CLEANUP: we are using .NET suffixes for integers (e.g. 1L for Int64) temporarily until we remove the old parser`
**Resolution:** Clean up temporary .NET suffix handling once old parser is removed.

---

## Category 11: Simple Database/Schema TODOs

### Add updated_at column
**File:** `backend/migrations/20251015_192755_package_schema_rewrite.sql:72`
**Text:** `--CLEANUP consider an updated_at col`
**Resolution:** Add an `updated_at` timestamp column if useful for auditing.

### Simplify primary key
**File:** `backend/migrations/20250717_214941_initial.sql:162`
**Text:** `PRIMARY KEY (canvas_id, name, version) -- TODO: simplfy PK`
**Resolution:** Review if the composite PK can be simplified (typo: "simplfy" -> "simplify").

### Add name field
**File:** `backend/migrations/20250717_214941_initial.sql:12`
**Text:** `-- TODO include name`
**Resolution:** Add the missing `name` field to the table.

---

## Category 12: Configuration and Hardcoded Values

### Don't hardcode instance name
**File:** `packages/darklang/cli/syncService.dark:92`
**Text:** `// CLEANUP: don't use a hardcoded default instance name here`
**Resolution:** Read instance name from configuration instead of hardcoding.

### Get instance name from state
**File:** `vscode-extension/client/src/ui/statusbar/statusBarManager.ts:19`
**Text:** `// TODO: Get actual instance name from instance manager/state`
**Resolution:** Fetch the instance name from the actual state instead of using a placeholder.

---

## Category 13: VS Code Extension TODOs

### Implement actual branch rename
**File:** `vscode-extension/client/src/commands/branchCommands.ts:141`
**Text:** `// TODO: Implement actual branch rename API call`
**Resolution:** Implement the branch rename functionality.

### Clarify branch name/label usage
**File:** `vscode-extension/client/src/commands/branchCommands.ts:100`
**Text:** `// CLEANUP: do we need branch?.label || branch?.name`
**Resolution:** Determine whether to use label, name, or both, and simplify the code.

### Implement content providers
**File:** `vscode-extension/client/src/providers/darkContentProvider.ts`
**Lines:** 52, 118
**Text:**
- `// TODO: Implement instance content provider`
- `// TODO: Implement branch list view`
**Resolution:** Implement the missing content provider views.

### Add AST view support
**File:** `vscode-extension/client/src/providers/content/packageContentProvider.ts:22`
**Text:** `// TODO: Add AST view support`
**Resolution:** Add support for viewing AST representation in the extension.

---

## Category 14: API Key and Secret Management

### Get API key from secret
**File:** `user-code/darklang/scripts/prompt.dark:1`
**Text:** `let apiKey = "TODO: get from secret"`
**Resolution:** Replace hardcoded placeholder with actual secret retrieval.

### Replace TODO secret with Secret type
**File:** `packages/darklang/openai.dark:4`
**Text:** `// TODO: This should be a secret Secret<String>`
**Resolution:** Change the type to use proper Secret type when available.

---

## Category 15: Simple Type System TODOs

### Use proper types instead of unknownTODO
**Files:** Many throughout codebase using `VT.unknownTODO`, `unknownDbTODO`, `typeArgsTODO`
**Examples:**
- `backend/src/LibExecution/ValueType.fs:10-12`
- `backend/src/BuiltinExecution/Libs/List.fs:363`
- Many others

**Resolution:** Replace placeholder `unknownTODO` types with actual proper types once the type system is more mature.

---

## Category 16: Deprecation Comments

### Make package operation automatic
**File:** `CODING-GUIDE.md:92`
**Text:** `(CLEANUP: make this happen automatically)`
**Resolution:** Automate the package reload process mentioned in the coding guide.

---

## Category 17: LSP Server TODOs

### Handle unsupported LSP methods
**File:** `packages/darklang/languageTools/lsp-server/handleIncomingMessage.dark`
**Lines:** 137, 440, 444-445
**Text:** `log "TODO we should do something with this"` and similar
**Resolution:** Implement handlers for these LSP methods or log them more appropriately.

### Handle batch requests
**File:** `packages/darklang/languageTools/lsp-server/lsp-server.dark:83-84`
**Text:** `log "TODO - Got batch request; not yet set to handle these"`
**Resolution:** Implement batch request handling in the LSP server.

---

## Category 18: Comment Cleanup

### Remove resolve comment when implemented
**File:** `packages/darklang/languageTools/lsp-server/fileSystemProvider.dark:252`
**Text:** `// TODO: remove magic`
**Resolution:** Remove the "magic" comment once the implementation is properly abstracted.

---

## Summary Statistics

- **Total TODOs found:** ~576
- **Total CLEANUPs found:** ~211
- **Low-hanging fruit identified:** ~100+ items across 18 categories
- **Most common types:**
  - Placeholder "TODO" text in descriptions and error messages
  - Missing test coverage
  - Documentation gaps
  - Simple refactorings (variable renames, constant extraction)
  - Hardcoded values that should be configurable

## Next Steps

1. Start with Category 2 (replacing "TODO" placeholder text) as it's the most mechanical
2. Address Category 3 (documentation) to improve developer experience
3. Work through Categories 4-6 (simple improvements and tests)
4. Address Categories 7-8 (build improvements and refactorings)
5. Tackle the remaining categories based on priority

Many of these items can be resolved without deep architectural knowledge and would make good "first contribution" tasks for new team members.
