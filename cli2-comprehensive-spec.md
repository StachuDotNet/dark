# Darklang CLI2 Comprehensive Specification

## Overview
CLI2 is a modern, organized command-line interface for Darklang with enhanced UX, toplevel support, and MVU/FRP architecture. It provides a hierarchical navigation system and toplevel-aware commands.

## Current Command Architecture

### System Commands
Core system functionality for CLI management.

| Command | Description | Status | Usage |
|---------|-------------|--------|-------|
| `help` | Show available commands and help information | ✅ Complete | `help [command]` |
| `version` | Display CLI version and build info | ✅ Complete | `version` |
| `status` | Show current CLI state and context | ✅ Complete | `status` |
| `quit` | Exit the CLI gracefully | ✅ Complete | `quit` |
| `clear` | Clear the terminal screen | ✅ Complete | `clear` |
| `test` | Run CLI2 internal test suite | ✅ Complete | `test` |

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

## Implementation Roadmap

### Phase 1: Core Infrastructure ✅
- [x] Basic command routing
- [x] MVU/FRP architecture  
- [x] Simple toplevel integration
- [x] Navigation commands
- [x] Help system

### Phase 2: Toplevel Command Spaces
- [ ] HTTP handler management
- [ ] Script management and execution
- [ ] Test runner integration
- [ ] Documentation system
- [ ] Cron/scheduler integration
- [ ] View/UI component management

### Phase 3: Advanced Features
- [ ] Context-aware help and commands
- [ ] Interactive mode with context switching
- [ ] Batch operations
- [ ] Pipeline support
- [ ] Configuration management
- [ ] Plugin system

### Phase 4: Integration & Polish
- [ ] Real package scanning
- [ ] IDE integration
- [ ] Performance optimization
- [ ] Comprehensive testing
- [ ] Documentation and examples

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