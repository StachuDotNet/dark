# Darklang CLI2 Comprehensive Specification

## Overview
CLI2 is a modern, organized command-line interface for Darklang with enhanced UX, toplevel support, and MVU/FRP architecture. It provides a hierarchical navigation system and toplevel-aware commands.

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

## Real vs Fake Command Analysis

### ✅ REAL Commands (Fully Implemented)
- **help**: Logo display, contextual and organized help using CLI1's system
- **version**: Build hash, GitHub update checking using `Builtin.getBuildHash()`
- **status**: Real CLI status formatting
- **quit/exit/q**: Proper state management for CLI exit
- **clear/cls**: Real ANSI screen clearing
- **cli-test**: Actual test runner execution
- **back**: Real navigation using page history
- **cd**: Real directory navigation with state management
- **ls/list/dir/pwd**: Real directory listing from current page
- **tree**: Real package structure display
- **view**: Real item viewing with context awareness
- **toplevels**: Real toplevel category display
- **scripts list**: Integration with CLI1's Scripts.list() for real database scripts

### 🔴 FAKE Commands (Need Real Implementation)
- **http routes**: Hardcoded fake route tree
- **http list**: Hardcoded fake HTTP handlers
- **run**: Completely fake script outputs
- **eval/e**: Placeholder text only
- **install**: Calls non-existent functions (would error)
- **update**: Calls non-existent functions (would error) 
- **uninstall**: Calls non-existent functions (would error)
- **test list**: Returns fake sample data
- **docs list**: Returns fake markdown names
- **cron list**: Returns fake cron jobs
- **views list**: Returns fake UI components

### 🟡 PARTIALLY REAL Commands (Mixed Implementation)
- **Navigation commands**: Real logic but some hardcoded directory contents
- **Tree command**: Real structure but some fake data mixed in
- **Scripts scanning**: Real CLI1 integration for scripts but fake data for other toplevels

## Implementation Priority Roadmap

### Phase 1: Core Infrastructure ✅ COMPLETE
- [x] Basic command routing with organized dispatch
- [x] MVU/FRP architecture using CLI1's state system
- [x] Beautiful help system with CLI1's logo integration
- [x] Real version command with build hash and update checking
- [x] Navigation commands with real state management
- [x] Parser compatibility fixes (parentheses for pipes, etc.)

### Phase 2: Make Fake Commands Real (CURRENT FOCUS)

#### Priority 1: HTTP Handler Integration (Easiest)
**Based on ProgramTypes.fs Handler.Spec patterns:**
- [ ] Integrate with real Handler.Spec types (HTTP, Worker, Cron, REPL)
- [ ] Use real tlid-based lookups
- [ ] Display actual route/method combinations from Handler.Spec.HTTP
- [ ] Show real handler counts and listings

#### Priority 2: Script Execution (Medium)
**Already has CLI1 integration, needs real execution:**
- [ ] Remove fake script outputs ("hello-world", "test-math", etc.)
- [ ] Use CLI1's real script execution system
- [ ] Integrate with Builtin.cliExecuteFunction for real script running
- [ ] Show real script results and error handling

#### Priority 3: Expression Evaluation (Medium)
**Integrate with real Darklang interpreter:**
- [ ] Remove "not yet implemented" placeholder
- [ ] Use CLI1's parser and execution system
- [ ] Integrate with LanguageTools.Parser.parseToSimplifiedTree
- [ ] Real expression parsing and evaluation

#### Priority 4: Package Management (Harder)
**Requires integration with real package system:**
- [ ] Remove non-existent function calls
- [ ] Research current package management system
- [ ] Implement real install/update/uninstall functionality

### Phase 3: Toplevel System Enhancement
**Based on real ProgramTypes.fs Toplevel.T patterns:**
- [ ] Integrate with real TLDB and TLHandler types
- [ ] Use real tlid-based toplevel identification
- [ ] Implement real package scanning with proper user access control
- [ ] Real-time toplevel discovery and caching

### Phase 4: Advanced Features & Polish
- [ ] Context-aware help and commands
- [ ] Interactive mode with context switching
- [ ] Performance optimizations
- [ ] Comprehensive integration testing

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