# Darklang Changes Report: August 10 - September 13, 2025

## Overview
**Time Period:** August 10, 2025 - September 13, 2025
**Total Commits:** 22 PRs merged
**Changed Files:** 501 files (+40,014 insertions, -34,021 deletions)

## Major Architectural Changes

### 1. Package Constants → Package Values
The biggest semantic change was renaming "constants" to "values" throughout the entire codebase:
- Migration script: `20250820_000000_rename_package_constants_to_values.sql`
- Replaced all `PackageConstant` types with `PackageValue`
- Updated serialization, parsing, pretty printing, and runtime handling
- All `.dark` files updated to use `value` declarations instead of `const`
- Tree-sitter grammar updated to recognize `value` keyword

### 2. CLI Complete Rewrite
The CLI has been completely restructured with a modular command system:

**New Architecture:**
- Central registry system for command management
- Each command is a module with execute/help/complete functions
- Interactive navigation with alternate screen buffer support
- Command history with up/down arrow navigation
- Tab completion for all commands and arguments

**Available Commands:**

**Package Navigation:**
- `nav` (alias: `cd`) - Navigate between packages, modules, and entities with interactive mode
- `ls` (alias: `dir`) - List package contents
- `view` - View details of modules, types, values, or functions
- `tree` - Display package hierarchy in tree format
- `back` - Go back to previous location

**Execution:**
- `run` - Run a function or script
- `eval` - Evaluate a Dark expression
- `scripts` - Store, manage, and run Dark scripts (list/view/add/edit/delete/run)

**Installation:**
- `install` - Install the CLI
- `update` - Update CLI to latest version
- `uninstall` - Remove CLI installation
- `status` - Show installation status
- `version` (aliases: `--version`, `-v`) - Display CLI version

**Utilities:**
- `clear` (alias: `cls`) - Clear the screen
- `help` - Show help for commands
- `quit` (aliases: `exit`, `q`) - Exit the CLI
- `experiments` - Try out various WIP CLI experiments
- `ui` - Launch the Dark Classic UI in your browser

**CLI Experiments Added:**
- UI component catalog with 15+ terminal UI components
- Data entry demos
- Interactive forms and layouts
- Terminal UI abstractions for building TUIs

### 3. Model Context Protocol (MCP) Implementation
Full MCP server implementation for AI integration:
- Complete spec compliance with initialize/shutdown lifecycle
- Tools, resources, and prompts support
- Server builder pattern for easy MCP server creation
- Tracing, logging, and sampling capabilities
- Example test servers included

**MCP Structure:**
```
modelContextProtocol/
├── serverBuilder/     # High-level server builder API
│   ├── main.dark
│   ├── handleIncomingMessage.dark
│   ├── initialize.dark
│   ├── tools.dark
│   ├── resources.dark
│   └── prompts.dark
├── tools/            # Tool definitions
├── resources/        # Resource handling
├── lifecycle/        # Connection lifecycle
└── examples/         # Example servers
```

### 4. VSCode Extension Enhancements

**New Features:**
- Tree view in activity bar with Darklang icon
- Icons for different entity types (modules, types, values, functions)
- "Open Full Module" context menu action
- Refresh tree view command
- Semantic tokenization improvements
- New commands:
  - `darklang.lookUpToplevel` - Look up package elements
  - `darklang.openPackageDefinition` - Open package definitions
  - `darklang.openFullModule` - View entire module
  - `darklang.init` - Initialize workspace

**Tree View Implementation:**
- Collapsible/expandable nodes
- Different icons per entity type
- Checkbox states support
- Tooltips and descriptions
- Version bumped to 0.0.7

### 5. Language & Parser Updates

**Multi-level Module Declarations:**
- Can now declare nested modules in .dark files
- Improved module path handling

**Parser Changes:**
- Removed `const` keyword support
- Added `value` keyword support
- Grammar updates for better expression parsing
- List separator changed to `;` consistently

**Pretty Printer Improvements:**
- Better formatting for all program types
- Runtime error formatting enhancements
- Canvas and package pretty printing updates

### 6. Standard Library Expansions

**New Modules/Functions:**
- `Stdlib.Cli.FileSystem` - File system operations
- `Stdlib.Cli.Process` - Process management
- `Stdlib.Print` module added
- SQLite library additions
- Reflection capabilities in `BuiltinExecution.Libs.Reflection`

**Major Updates:**
- All stdlib modules reformatted
- JSON handling improvements
- HTTP client enhancements
- List operations optimizations
- String manipulation improvements

### 7. Testing & Infrastructure

**Removed:**
- Old CLI integration test framework (was in `packages/internal/cli/integrationTests/`)
- VHS recorder for CLI testing

**Fixed:**
- Pubsub emulator startup issues
- Dockerfile JAVA_HOME architecture handling
- HTTP client flaky tests disabled
- Language server crash prevention

### 8. File Organization

**New Directories:**
```
packages/darklang/cli/
├── core.dark              # Main CLI state and registry
├── ui.dark                # UI launcher
├── experiments/           # CLI experiments
│   ├── ui-catalog/       # Terminal UI components
│   └── demos/            # Interactive demos
├── execution/            # Run/eval commands
├── installation/         # Install management
└── old_notes/           # Archived code
```

**Deleted Legacy Code:**
- Old CLI implementation files (cli.dark, model.dark, update.dark, etc.)
- Old MCP server in `languageTools/_mcp-server/`
- Integration test framework

## Key Technical Details

### State Management
- New `AppState` type with command history, cursor position, and page navigation
- Support for multiple pages (MainPrompt, Experiments, InteractiveNav)
- Package navigation state tracking

### Interactive Features
- Alternate screen buffer for full-screen interactions
- ANSI escape codes for colors and cursor control
- Real-time tab completion with hints
- Command history with persistence

### Build System
- Automatic rebuilds via `./scripts/build/compile`
- Package reloads (~10s) when .dark files change
- .NET builds (~1min) when F# files change
- Logs in `./rundir/logs/` for monitoring

## Summary

This update represents a massive overhaul of the Darklang development experience, with the CLI being completely rewritten for better usability, the addition of full MCP support for AI integrations, significant VSCode extension improvements including a tree view, and a fundamental shift from "constants" to "values" throughout the language. The focus has been on developer experience, stability, and creating a more interactive and intuitive environment for Darklang development.