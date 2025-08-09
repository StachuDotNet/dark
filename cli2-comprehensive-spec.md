# Darklang CLI2 Specification

## Overview
CLI2 achieves complete feature parity with CLI1 while providing enhanced UX and maintainability. The primary goal is to match CLI1's functionality exactly, then extend beyond it.

## Current Command Architecture

### System Commands
Core system functionality for CLI management.

| Command | Description | Status | Usage |
|---------|-------------|--------|-------|
| `help` | Show available commands with logo and formatting | ✅ Complete | `help [command]` |
| `version` | Display CLI version, build hash, and update status | ✅ Complete | `version` |
| `status` | Show current CLI state and context | ✅ Complete | `status` |
| `quit` | Exit the CLI gracefully | ✅ Complete | `quit` |
| `clear` | Clear the terminal screen | ✅ Complete | `clear` |
| `cli-test` | Run CLI2 internal test suite | ✅ Complete | `test` |

### Navigation Commands
Hierarchical navigation through package structure.

| Command | Description | Status | Usage |
|---------|-------------|--------|-------|
| `ls` | List contents of current location | ✅ Complete | `ls [path]` |
| `cd` | Change current directory/context | 🟡 Partial | `cd <target>` |
| `tree` | Show hierarchical view of packages | ✅ Complete | `tree [depth]` |
| `back` | Go back to previous location | 🟡 Basic | `back` |
| `view` | View detailed information about item | 🟡 Basic | `view <item>` |

### Toplevel Commands
Commands for working with Darklang toplevel types.

| Command | Description | Status | Toplevel Type |
|---------|-------------|--------|---------------|
| `toplevels` | Show all toplevel categories | ✅ Complete | All |
| `http` | Manage HTTP handlers | ✅ Complete | HttpHandler |
| `scripts-tl` | Manage script toplevels | ✅ Complete | Script |
| `tests-tl` | Manage test toplevels | ✅ Complete | Test |
| `docs` | Manage documentation | 🟡 Basic | Markdown |
| `cron` | Manage scheduled tasks | 🔴 Missing | Cron |
| `views` | Manage UI components | 🔴 Missing | View |

### Development Commands
Commands for running and developing code.

| Command | Description | Status | Usage |
|---------|-------------|--------|-------|
| `run` | Execute a script or toplevel | ✅ Complete | `run <name> [args]` |
| `scripts` | List available executable scripts | ✅ Complete | `scripts` |
| `eval` | Evaluate Darklang expression | 🟡 Placeholder | `eval "<expression>"` |

### Installation Commands  
Package and dependency management.

| Command | Description | Status | Usage |
|---------|-------------|--------|-------|
| `install` | Install packages or dependencies | 🔴 Incomplete | `install <package>` |
| `update` | Update packages to latest versions | 🔴 Incomplete | `update [package]` |
| `uninstall` | Remove packages or dependencies | 🔴 Incomplete | `uninstall <package>` |

## Proposed Toplevel Command Spaces

Each toplevel type should have its own command space with subcommands for comprehensive management.

### HTTP Handler Space (`http`)

```bash
http                    # List all HTTP handlers
http list              # Same as above  
http create <path>     # Create new HTTP handler
http edit <name>       # Edit existing handler
http test <name>       # Test handler with sample requests
http delete <name>     # Remove handler
http routes            # Show all routes in a tree
http middleware        # Manage middleware
```

### Script Space (`script` or `scripts`)

```bash
scripts                # List all scripts
scripts run <name>     # Execute a script
scripts create <name>  # Create new script
scripts edit <name>    # Edit existing script
scripts delete <name>  # Remove script  
scripts schedule       # Show scheduled scripts
scripts deps <name>    # Show script dependencies
scripts log <name>     # View execution logs
```

### Test Space (`test`)

```bash
test                   # Run all tests
test list             # List available tests  
test run <pattern>    # Run specific tests
test create <name>    # Create new test
test watch           # Run tests in watch mode
test coverage        # Show test coverage
test results         # Show last test results
test failed          # Show only failed tests
```

### Cron/Scheduler Space (`cron`)

```bash
cron                  # List all scheduled tasks
cron list            # Same as above
cron create          # Create new scheduled task
cron edit <name>     # Edit existing task
cron delete <name>   # Remove scheduled task
cron logs <name>     # View task execution logs
cron enable <name>   # Enable task
cron disable <name>  # Disable task
cron next <name>     # Show next execution time
```

### View/UI Space (`views`)

```bash
views                # List all UI components/views
views list          # Same as above
views create <name> # Create new view component
views edit <name>   # Edit existing view
views preview <name> # Preview component in browser
views delete <name> # Remove view component
views templates     # List available templates
views build         # Build all views for production
```

### Documentation Space (`docs`)

```bash
docs                 # List all documentation
docs list           # Same as above  
docs create <name>  # Create new document
docs edit <name>    # Edit existing document
docs build          # Generate static documentation
docs serve          # Start documentation server
docs publish        # Publish docs to hosting
docs search <term>  # Search documentation content
```

## Advanced Features Specification

### Context Awareness
CLI should be context-aware and show different options based on current location:

- **Root Context**: Show all toplevel categories
- **Package Context**: Show package-specific commands
- **Toplevel Context**: Show toplevel-specific operations
- **Development Context**: Show development and testing commands

### Interactive Mode
```bash
darklang cli         # Enter interactive mode
> help              # Context-sensitive help
> cd http           # Enter HTTP handler context
http> list          # List HTTP handlers in current context
http> create api/users  # Create new handler
http> edit api/users    # Edit the handler
http> back          # Go back to root context
>
```

### Batch Operations
```bash
# Batch operations on multiple items
http delete api/users api/posts api/comments
test run unit/ integration/ e2e/
scripts schedule deploy backup cleanup --cron="0 2 * * *"
```

### Pipeline Support
```bash
# Pipe operations
scripts list --status=failed | scripts rerun
test list --pattern="*integration*" | test run
http list --method=GET | http middleware add cors
```

### Configuration Management
```bash
# CLI configuration
config set editor vim
config set test.reporter junit
config set http.default-cors true
config list
config reset
```

## CLI1 Parity Analysis

### ✅ CLI1 Commands Implemented in CLI2
- **help**: ✅ Complete with logo (matches CLI1)
- **version**: ✅ Complete with build hash and update checking (matches CLI1)
- **status**: ✅ Complete (matches CLI1) 
- **quit**: ✅ Complete (matches CLI1)
- **clear**: ✅ Complete (matches CLI1)
- **back**: ✅ Complete with page history (matches CLI1)
- **cd**: ✅ Complete with state management (matches CLI1)
- **ls**: ✅ Complete with directory listing (matches CLI1)
- **view**: ✅ Complete with item viewing (matches CLI1)

### 🔴 CLI1 Commands Missing/Broken in CLI2
- **mode**: ❌ Missing - CLI1 has mode switching functionality
- **run**: 🔴 Fake implementation - CLI2 has placeholder outputs, CLI1 has real script execution
- **scripts**: 🟡 Partially real - CLI2 shows real script list but fake execution 
- **eval**: 🔴 Fake implementation - CLI2 placeholder, CLI1 has real expression evaluation
- **install**: 🔴 Broken - CLI2 calls non-existent functions, CLI1 works
- **update**: 🔴 Broken - CLI2 calls non-existent functions, CLI1 works  
- **uninstall**: 🔴 Broken - CLI2 calls non-existent functions, CLI1 works

### ➕ CLI2 Extra Commands (Should be removed for parity)
- **http, toplevels, cron, views, docs, test, tree**: These are new features, not CLI1 parity

## CLI1 Parity Roadmap

### Phase 1: Infrastructure ✅ COMPLETE
- [x] Basic command routing matching CLI1's structure
- [x] Clean MVU/FRP architecture using CLI1's state system (foundation for TUI)
- [x] Help system with CLI1's logo and formatting
- [x] Version command with real build hash and update checking
- [x] Navigation commands (cd, ls, back, view) matching CLI1
- [x] TUI-ready state management and rendering pipeline

### Phase 2: Fix Broken CLI1 Commands (CURRENT FOCUS)

#### Priority 1: Add Missing CLI1 Commands
- [ ] **mode**: Implement CLI1's mode switching (Installed vs Portable)

#### Priority 2: Fix Broken Implementation Commands
- [ ] **run**: Replace fake outputs with CLI1's real script execution system
- [ ] **eval**: Replace placeholder with CLI1's real expression evaluation
- [ ] **install/update/uninstall**: Fix broken function calls, use CLI1's installation system

#### Priority 3: Clean Up Extra Commands
- [ ] Remove all non-CLI1 commands (http, toplevels, cron, views, docs, test, tree)
- [ ] Update help to show only CLI1 commands
- [ ] Simplify command routing to match CLI1 exactly

### Phase 3: Testing & Validation
- [ ] Write tests comparing CLI1 vs CLI2 output exactly
- [ ] Validate every CLI1 command works identically in CLI2
- [ ] Performance testing to ensure CLI2 isn't slower than CLI1
- [ ] Integration testing with real scripts and expressions

### Phase 4: Transition Planning
- [ ] Document exact behavioral differences (if any)
- [ ] Create migration plan to switch from CLI1 to CLI2
- [ ] Ensure CLI2 can be drop-in replacement for CLI1

### Future: Extensions (After Parity)
- [ ] Then and only then: Add new organized command structure
- [ ] Then and only then: Add toplevel-aware features
- [ ] Then and only then: Add enhanced navigation

## Technical Architecture

### Command Resolution
1. Parse command line arguments
2. Determine command category (system, navigation, toplevel, etc.)
3. Route to appropriate handler
4. Execute with context awareness
5. Return formatted result

### State Management
- Current location/context
- Recently accessed items
- User preferences
- Command history
- Active toplevels

### Extension Points
- Custom toplevel types
- Plugin commands
- Custom formatters
- External tool integrations

## Quality Standards

- **Performance**: Commands should respond in <100ms
- **Reliability**: Zero crashes, graceful error handling
- **Usability**: Intuitive command structure, good help
- **Consistency**: Uniform command patterns across all spaces
- **Extensibility**: Easy to add new toplevel types and commands