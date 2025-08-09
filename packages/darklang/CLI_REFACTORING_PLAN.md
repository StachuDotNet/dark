# CLI Refactoring Implementation Plan (Revised)

## Progress Tracking
- [x] Phase 1: CLI2 Foundation & System Commands **COMPLETE**
- [x] Phase 2: Navigation & Development Commands **COMPLETE**
- [x] Phase 3: Enhanced Help & Categorization **COMPLETE**
- [ ] Phase 4: CLI2 Testing & Quality
- [ ] Phase 5: CLI2 Cutover & Migration

## Overview

This document outlines a **pragmatic refactoring** of the Darklang CLI to solve the real pain points: massive monolithic files, poor command discoverability, and difficult testing/maintenance.

**Key Insight**: The current CLI architecture (MVU, message-driven, three interaction modes) is already solid. The problem is **organization**, not architecture.

## Goals

1. **Fix the Real Pain Points**: Break up the 1123-line `command.dark` file into maintainable modules
2. **Improve Command Discoverability**: Better help system with visual grouping
3. **Enable Testing**: Make individual commands testable in isolation
4. **Preserve What Works**: Keep the excellent MVU architecture, input handling, and completion system
5. **Gradual Migration**: No big-bang rewrite, incremental improvement that reduces risk

## Current CLI Analysis

### What's Already Good (Don't Change)

1. **Three Interaction Modes Already Work**:
   - `NonInteractive`: One-shot commands (`dark version`)
   - `Regular`: Interactive REPL (`dark` then commands)  
   - `RefreshScreen`: Rich display mode (already TUI-like)

2. **Solid MVU Architecture**: Message-driven, pure functions, proper state management

3. **Excellent Input System**: Tab completion, history, cursor handling all work well

4. **Working Installation System**: Portable vs installed modes, cross-platform support

### The Real Problems

1. **Monolithic `command.dark`**: 1123 lines, hard to navigate, test, or extend

2. **Poor Command Discoverability**: Commands buried in massive `getAvailableCommands` function

3. **Context Coupling**: Command logic tightly coupled to page contexts (hard to test)

4. **No Testing Strategy**: Individual commands can't be tested in isolation

## Strategy: Parallel Development with CLI2

**Build a new, better-organized CLI alongside the existing one**:
- CLI1 (`cli/`) keeps working throughout development
- CLI2 (`cli2/`) built incrementally with better organization
- Continuous testing: CLI1 vs CLI2 behavior comparison
- Switch over when CLI2 achieves feature parity
- Safe rollback path if issues arise

## Phase 1: CLI2 Foundation (Week 1-2)

### Phase 1 Progress - COMPLETE ✅
- [x] 1.1 Create CLI2 directory structure
- [x] 1.2 Extract System commands (help, version, status, quit, mode, clear)
- [x] 1.3 Create CLI2 entry point with environment variable toggle
- [x] 1.4 Set up continuous testing framework (CLI1 vs CLI2 comparison)

### 1.1 Create CLI2 Structure

**New parallel directory**:

```
packages/darklang/cli/           # Original CLI (keep working)
├── [all existing files...]

packages/darklang/cli2/          # New organized CLI
├── main.dark                    # Entry point
├── state.dark                   # App-specific state types
├── messages.dark               # App-specific message types
├── update.dark                 # Root update function  
├── commands/
│   ├── system.dark             # help, version, status, quit, mode, clear
│   ├── navigation.dark         # cd, ls, back, view  
│   ├── development.dark        # run, eval, scripts
│   └── installation.dark      # install, update, uninstall
├── ui/
│   ├── render.dark            # Enhanced rendering with categories
│   ├── help.dark              # Categorized help system
│   └── colors.dark            # Copy from cli/cliColors.dark
└── tests/
    ├── systemCommands.tests.dark
    ├── navigationCommands.tests.dark
    └── behaviorParity.tests.dark    # CLI1 vs CLI2 comparison
```

### 1.2 Extract Commands Incrementally

**Start with System Commands** (simplest, least coupled):

```dark
// In commands/system.dark
module Darklang =
  module Cli =
    module Commands =
      module System =
        let helpCommand: CommandDetails = {
          name = "help"
          description = "Show help information"
          aliases = []
          arguments = []
          execute = fun state args ->
            // Move existing help logic here
            let commands = getAvailableCommands state
            let helpContent = renderHelpWithCategories commands
            let newState = { state with commandResult = CommandResult.Info helpContent }
            (newState, [])
        }
        
        let versionCommand: CommandDetails = {
          name = "version"
          description = "Show CLI version"
          aliases = ["v"]
          arguments = []
          execute = fun state _ ->
            let versionInfo = getVersionInfo ()
            let newState = { state with commandResult = CommandResult.Info versionInfo }
            (newState, [])
        }
        
        let getCommands (state: State): List<CommandDetails> = [
          helpCommand; versionCommand; statusCommand; quitCommand; modeCommand; clearCommand
        ]
```

### 1.3 CLI2 Entry Point

**Create new main entry point** (in `cli2/main.dark`):

```dark
// Copy and improve the current cli.dark entry point
module Darklang =
  module Cli =
    let main (args: List<String>) : Unit =
      let initialState = { 
        currentPage = Root
        navigationHistory = []
        commandHistory = []
        interactionMode = detectInteractionMode args
        // ... other initial state
      }
      
      let allCommands = [
        Commands.System.getCommands state
        Commands.Navigation.getCommands state  
        Commands.Development.getCommands state
        Commands.Installation.getCommands state
      ] |> List.flatten
      
      // Use improved MVU loop with categorized commands
      runCliLoop initialState allCommands

let detectInteractionMode (args: List<String>) : InteractionMode =
  // Keep current logic but make it cleaner
  match args with
  | [] -> InteractionMode.Regular
  | _ -> InteractionMode.NonInteractive
```

### 1.4 Continuous Testing Framework

**Set up CLI1 vs CLI2 comparison** (in `cli2/tests/behaviorParity.tests.dark`):

```dark
let testCommandParity () =
  let testCommands = [
    "help"; "version"; "status"  
    "cd Darklang.Stdlib"; "ls"; "back"
    "eval 1L + 2L"
    // Add more as we implement
  ]
  
  for command in testCommands do
    let cli1Result = runCommandInCLI1 command
    let cli2Result = runCommandInCLI2 command
    
    assertResultsEquivalent cli1Result cli2Result command

// Helper to run commands in original CLI
let runCommandInCLI1 (command: String) : TestResult = ...

// Helper to run commands in new CLI  
let runCommandInCLI2 (command: String) : TestResult = ...
```

**Benefits of CLI2 Approach**:
- ✅ CLI1 stays working (zero user disruption)
- ✅ Can experiment freely in CLI2
- ✅ Continuous behavior validation  
- ✅ Easy rollback if needed
- ✅ Clean slate for optimal organization

## Phase 2: Navigation & Development Commands (Week 2-3)

### 2.1 Problem: Context-Heavy Commands

Current commands like `view` have massive context logic:

```dark
// Current: 50+ lines of context-specific logic in one place
| "view" ->
  match state.currentPage with
  | Root -> (* 20 lines of logic *)
  | Module(owner, submodules) -> (* 30 lines of logic *)  
  | Type _ -> (* 25 lines of logic *)
  | Fn _ -> (* 15 lines of logic *)
  | Constant _ -> (* 10 lines of logic *)
```

### 2.2 Solution: Extract Context Handlers

```dark
// In commands/navigation.dark
module ViewCommand =
  let viewInRoot (state: State) (args: String) : (State * List<Msg>) = 
    // Extract the "Root" logic here - now testable!
    
  let viewInModule (state: State) (owner: String) (submodules: List<String>) (args: String) : (State * List<Msg>) =
    // Extract the "Module" logic here - now testable!
    
  let viewInType (state: State) (typeName: PackageType.Name) (args: String) : (State * List<Msg>) =
    // Extract the "Type" logic here - now testable!
  
  let execute (state: State) (args: String) : (State * List<Msg>) =
    match state.currentPage with
    | Root -> viewInRoot state args
    | Module(owner, submodules) -> viewInModule state owner submodules args
    | Type(name) -> viewInType state name args
    | Fn(name) -> viewInFn state name args
    | Constant(name) -> viewInConstant state name args

let viewCommand: CommandDetails = {
  name = "view"
  description = "View modules, types, functions, or constants"
  aliases = []
  arguments = ["[<moduleName> | <entityName>]"]
  execute = ViewCommand.execute
}
```

**Benefits**:
- Each context handler is independently testable
- Logic is organized and easier to understand
- Still works exactly the same from user perspective

## Phase 3: Improve Command Discoverability (Week 3-4)

### 3.1 Current Problem

Help output is just a flat list:
```
Available commands:
quit        Exit the CLI
help        Show help information  
version     Show CLI version
cd          Change directory
ls          List items in current directory
run         Run a function from the package manager or execute a script
```

### 3.2 Solution: Categorized Help

Add categories to command details:
```dark
type CommandCategory = 
  | System | Navigation | Development | Installation

type CommandDetails = {
  // ... existing fields
  category: CommandCategory
  examples: List<String>
}
```

Then improve help display:
```dark
let renderHelpWithCategories (commands: List<CommandDetails>) : String =
  let systemCommands = commands |> List.filter (fun cmd -> cmd.category == System)
  let navigationCommands = commands |> List.filter (fun cmd -> cmd.category == Navigation)
  // etc.
  
  let result = [
    "Available commands:"
    ""
    "🔧 System Commands"
    renderCommandGroup systemCommands
    ""
    "📁 Navigation Commands" 
    renderCommandGroup navigationCommands
    ""
    "💻 Development Commands"
    renderCommandGroup developmentCommands
    // etc.
  ] |> String.join "\n"
```

**Result**:
```
/> help

Available commands:

🔧 System Commands
   help        Show this help information
   version     Show CLI version (aliases: v)
   status      Show system status
   quit        Exit the CLI
   
📁 Navigation Commands  
   cd <path>   Change directory (example: cd Darklang.Stdlib)
   ls          List current directory contents
   back        Go back to previous location
   view <name> View entity details (example: view String.append)
   
💻 Development Commands
   run <fn>    Run a function (example: run @String.append "hello" " world")
   eval <expr> Evaluate expression (example: eval 1L + 2L)
   scripts     Manage Dark scripts
   
⚙️ Installation Commands (installed mode only)
   update      Update to latest version
   uninstall   Uninstall the CLI
```

### 3.3 Enhanced Help Commands

Add category-specific help:
```dark
// Enhanced help command
let helpCommand: CommandDetails = {
  name = "help"
  // ...
  execute = fun state args ->
    match String.trim args with
    | "" -> renderAllCategoriesHelp state
    | "system" -> renderCategoryHelp state System
    | "navigation" -> renderCategoryHelp state Navigation
    | "development" -> renderCategoryHelp state Development
    | "installation" -> renderCategoryHelp state Installation
    | commandName -> renderSpecificCommandHelp state commandName
}
```

Usage:
```
/> help navigation
📁 Navigation Commands

cd <path>      Change to module/package directory
               Examples: cd Darklang.Stdlib
                        cd Darklang.Stdlib.String
                        
ls             List contents of current directory
               Shows functions, types, constants in current module
               
view <name>    View details of entity
               Examples: view String.append
                        view List.map
                        view HttpClient.Response
                        
back           Return to previous location
               Navigate back through directory history
```

## Phase 4: CLI2 Testing & Quality (Week 4-5)

### 4.1 Add Command Testing to CLI2

**Comprehensive testing for CLI2 commands** (in `cli2/tests/`):

```dark
// In cli2/tests/system.tests.dark
module Darklang =
  module Cli2 =
    module Commands =
      module System =
        module Tests =
          let testVersionCommand () =
            let state = createTestState ()
            let (newState, msgs) = System.versionCommand.execute state ""
            
            match newState.commandResult with
            | CommandResult.Info versionStr -> 
              assert (String.contains versionStr "Darklang CLI")
              assert (String.contains versionStr "alpha-")
            | _ -> failTest "Expected Info result"
            
            assert (List.isEmpty msgs)
          
          let testHelpCommand () =
            let state = createTestState ()
            let (newState, msgs) = System.helpCommand.execute state ""
            
            match newState.commandResult with
            | CommandResult.Info helpStr ->
              assert (String.contains helpStr "🔧 System Commands")
              assert (String.contains helpStr "📁 Navigation Commands")
            | _ -> failTest "Expected Info result"
```

### 4.2 Context Handler Testing

**Test individual context handlers in CLI2**:
```dark
// In cli2/tests/navigation.tests.dark
let testViewInRoot () =
  let state = createTestState () // currentPage = Root
  let (newState, msgs) = Navigation.ViewCommand.viewInRoot state "Darklang.Stdlib"
  
  match newState.commandResult with
  | CommandResult.Success content ->
    assert (String.contains content "Module: Darklang.Stdlib")
  | _ -> failTest "Expected success"
```

### 4.3 CLI1 vs CLI2 Behavior Parity Testing

**Ensure CLI2 works identically to CLI1**:
```dark
// In cli2/tests/behaviorParity.tests.dark
let testCommandParity () =
  let commands = ["help"; "version"; "status"; "cd Darklang.Stdlib"; "ls"; "back"]
  
  for command in commands do
    let cli1Result = runCommandInCLI1 command
    let cli2Result = runCommandInCLI2 command  
    assertResultsEquivalent cli1Result cli2Result command

// Continuous validation during development
let validateAllImplementedCommands () =
  let implementedCommands = CLI2.getImplementedCommands ()
  
  for command in implementedCommands do
    let cli1Result = runCommandInCLI1 command
    let cli2Result = runCommandInCLI2 command
    
    if cli1Result != cli2Result then
      failTest $"Parity broken for command: {command}"
```

## Phase 5: CLI2 Cutover & Migration (Week 5-6)

### 5.1 Feature Parity Validation

**Ensure CLI2 has complete feature parity**:
- All commands from CLI1 implemented and tested
- All interaction modes working identically
- All edge cases and error handling preserved
- Performance equivalent or better

### 5.2 Gradual Rollout Strategy

**Option 1: Environment Variable Toggle**:
```dark
// In main entry point
let useCLI2 = 
  match Stdlib.Environment.getEnvironmentVariable "DARK_USE_CLI2" with
  | Some "true" -> true
  | _ -> false

if useCLI2 then
  Cli2.main args
else  
  Cli.main args
```

**Option 2: Command-line Flag**:
```
dark --cli2 help    # Use new CLI
dark help           # Use old CLI (default)
```

### 5.3 Migration Steps

1. **Internal Testing**: Use CLI2 for development work
2. **Alpha Testing**: Deploy with environment variable toggle
3. **Beta Testing**: Make CLI2 the default with fallback option
4. **Full Migration**: Remove CLI1, rename CLI2 to CLI
5. **Cleanup**: Remove old files and parallel structure

## Timeline (CLI2 Parallel Development)

- **Week 1-2**: CLI2 Foundation + System Commands  
- **Week 2-3**: Navigation + Development Commands
- **Week 3-4**: Installation Commands + Enhanced Help
- **Week 4-5**: Comprehensive Testing + Parity Validation
- **Week 5-6**: Gradual Rollout + Migration

**Total: 5-6 weeks** with parallel development safety

## Success Metrics

### CLI2 Organization (vs CLI1's 1123-line monolith)
- [ ] Largest CLI2 command file is <300 lines  
- [ ] Each CLI2 command module is <200 lines  
- [ ] Adding new commands requires editing ≤2 files in CLI2
- [ ] Clear separation between system/navigation/development/installation commands

### Enhanced Discoverability
- [ ] CLI2 `help` shows organized command categories with icons
- [ ] CLI2 `help <category>` shows detailed category help
- [ ] Each CLI2 command has usage examples and better descriptions
- [ ] Tab completion works seamlessly with new organization

### Comprehensive Testing
- [ ] Every CLI2 command module has test coverage
- [ ] CLI2 context handlers are individually tested
- [ ] Behavior parity tests ensure CLI1 === CLI2 functionality
- [ ] Continuous validation during development prevents regressions

### Safe Migration
- [ ] CLI1 remains fully functional throughout development
- [ ] CLI2 can be toggled on/off for testing
- [ ] No user disruption during development phase
- [ ] Easy rollback path if issues discovered

### Performance
- [ ] CLI2 command execution speed ≥ CLI1 speed
- [ ] CLI2 help rendering performs well with categorization
- [ ] CLI2 startup time ≤ CLI1 startup time

## What We're NOT Doing

1. **Not creating Stdlib.Cli framework** - Focus on Dark's needs first
2. **Not building full-screen TUI mode** - Current `RefreshScreen` is sufficient  
3. **Not changing MVU architecture** - It already works well
4. **Not breaking existing commands** - All CLI2 behavior identical to CLI1
5. **Not doing big-bang rewrite** - Parallel development ensures safety

## Key Benefits of CLI2 Parallel Development

### Development Safety
1. **Zero User Disruption**: CLI1 keeps working throughout development
2. **Continuous Validation**: Behavior parity testing prevents regressions
3. **Easy Rollback**: Can switch back to CLI1 if issues discovered
4. **Incremental Progress**: Can implement and test features one by one

### Code Quality
1. **Better Organization**: 1123-line monolith → focused, testable modules
2. **Enhanced Discoverability**: Categorized help with examples and icons
3. **Individual Testing**: Each command and context handler testable in isolation
4. **Maintainable Structure**: Easy to add/modify commands in organized modules

### Long-term Value
1. **Clean Architecture**: CLI2 built with optimal organization from start
2. **Testing Foundation**: Comprehensive test coverage for all functionality
3. **Future TUI Support**: Better organized code easier to extend for rich TUI
4. **Development Velocity**: Well-organized code faster to work with

This parallel development approach solves the real problems (monolithic files, poor discoverability, hard to test) while eliminating the risks of big-bang rewrites and preserving everything that currently works well.