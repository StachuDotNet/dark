# Darklang Changes Summary: Aug 10 - Sep 13, 2025

## Key Changes (22 PRs, 501 files changed)

### 1. CLI Completely Rewritten
- **New command system:** nav, ls, view, tree, run, eval, scripts, experiments, ui
- Interactive package navigation with tab completion
- Command history and alternate screen support
- Terminal UI experiments with 15+ components

### 2. Constants → Values
- All "constants" renamed to "values" across entire codebase
- Migration applied to database, parser, and all .dark files

### 3. MCP (Model Context Protocol) Support
- Full spec-compliant MCP server implementation
- Server builder API for easy AI tool integration
- Tools, resources, and prompts support

### 4. VSCode Extension Updates
- New tree view in activity bar showing packages/modules/functions
- Icons for different entity types
- "Open Full Module" context menu
- Version 0.0.7

### 5. Other Notable Changes
- Multi-level module declarations in .dark files
- New stdlib modules: Cli.FileSystem, Cli.Process, Print
- SQLite library additions
- Removed old CLI integration test framework
- Fixed Docker builds and language server crashes

## Bottom Line
Major focus on developer experience: better CLI, AI integration via MCP, improved VSCode tooling, and cleaner language semantics.