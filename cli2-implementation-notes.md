# CLI2 Implementation Notes

## Architecture Overview

CLI2 follows a modular architecture with clear separation of concerns:

```
CLI2 Architecture:
├── main.dark              - Entry point and CLI2 orchestration
├── commands.dark          - Command routing and dispatch
├── state.dark             - State management and types
├── ui.dark                - User interface and formatting
├── systemCommands.dark    - Core system commands (help, version, etc.)
├── navigationCommands.dark - Navigation (cd, ls, tree, view, back)
├── developmentCommands.dark - Development tools (run, scripts, eval)
├── installationCommands.dark - Package management (install, update, uninstall)
├── toplevels.dark         - Toplevel type definitions and scanning
├── tests.dark             - Unit tests and testing infrastructure
└── messages.dark          - Message types and communication
```

## Key Design Decisions

### 1. Complete Independence from CLI1
- **Zero delegation**: CLI2 has no dependencies on CLI1 code
- **Separate command implementations**: All commands reimplemented in CLI2
- **Independent state management**: CLI2 manages its own state structure
- **Benefit**: Can evolve independently, easier to maintain, clearer architecture

### 2. Contextual vs Organized Help
- **Default help**: Shows flat list of all commands (like CLI1)
- **Organized help**: Available via `menu` command or `help --full`
- **Rationale**: Matches CLI1 UX while providing enhanced organization when needed

### 3. Parser-Compatible Syntax
- **Constraint**: Darklang parser has strict limitations
- **Adaptations**: 
  - No `||` operator (use nested if-else)
  - Semicolon list separators instead of commas
  - Explicit type constructors for records
  - Avoid arithmetic in string interpolation
- **Benefit**: Code compiles and runs reliably

### 4. Safety-First Approach
- **Package loading checks**: CLI won't run if packages fail to load
- **Error handling**: Graceful degradation for unknown commands
- **State validation**: Comprehensive state management with safety checks

## Command Categories

### System Commands
Core CLI functionality that's always available:
- `help` - Contextual help (default) or organized help (`--full`, `menu`)  
- `version` - CLI2 version information
- `status` - Current CLI status and configuration
- `quit` - Exit CLI
- `clear` - Clear screen
- `test` - Run CLI2 unit tests

### Navigation Commands  
Moving through the package/toplevel structure:
- `cd` - Change directory/context
- `ls` - List contents of current location
- `tree` - Show hierarchical structure
- `view` - View detailed item information
- `back` - Return to previous location

### Development Commands
Tools for code development and execution:
- `run` - Execute scripts
- `scripts` - List available scripts  
- `eval` - Evaluate Darklang expressions
- Future: Integration with real Darklang interpreter

### Installation Commands
Package and dependency management:
- `install` - Install packages
- `update` - Update packages
- `uninstall` - Uninstall packages
- Future: Integration with real package manager

## State Management

CLI2 uses a functional state management approach:

```darklang
type State =
  { currentPage: Page              // Current navigation location
    pageHistory: List<Page>        // Navigation history stack
    mainPrompt: String             // Current prompt text
    cursorPosition: Int64          // Cursor position in prompt
    commandHistory: List<String>   // Command history
    historyPosition: Int64         // Position in command history
    draftPrompt: String            // Draft prompt text
    commandResult: CommandResult   // Result of last command
    interactionMode: InteractionMode // Current interaction mode
    needsFullRedraw: Bool          // Whether full redraw is needed
    isExiting: Bool                // Whether CLI is exiting
    completionState: Option<CompletionState> } // Auto-completion state
```

## Testing Strategy

### Current Tests
- **Basic unit tests**: Command dispatch and core functionality
- **Parser compatibility**: All tests written to work with Darklang parser
- **Mock state creation**: Utilities for creating test state

### Test Categories
1. **Unit Tests**: Individual function testing
2. **Integration Tests**: Command execution workflows  
3. **CLI Behavior Tests**: End-to-end CLI interaction
4. **Toplevel System Tests**: Toplevel discovery and navigation

### Testing Limitations
- **Parser constraints**: Can't use complex test patterns
- **Async limitations**: Limited async testing capabilities
- **Mocking**: Simple mock implementations for package scanning

## Performance Considerations

### Package Scanning
- **Current**: Mock data for development
- **Future**: Cached scanning results for performance
- **Challenge**: Real-time updates vs performance trade-offs

### Memory Management
- **State size**: Keep state minimal and focused
- **History limits**: Bound command/navigation history
- **Caching**: Strategic caching of frequently accessed data

## Error Handling

### Graceful Degradation
- **Unknown commands**: Clear error messages with suggestions
- **Package failures**: Safe mode operation when packages fail
- **State corruption**: Recovery mechanisms for invalid state

### User Experience
- **Clear error messages**: Actionable error information
- **Help suggestions**: Context-aware help recommendations
- **Progressive disclosure**: Show complexity only when needed

## Future Enhancements

### Near Term (Phase 4)
- Real package scanning integration
- Advanced toplevel navigation features
- Enhanced search and filtering
- Performance optimizations

### Medium Term (Phase 5)
- IDE integration support
- Visual toplevel browsers  
- Real-time collaboration features
- Advanced scripting capabilities

### Long Term (Phase 6)
- AI-assisted navigation and discovery
- Custom toplevel type creation tools
- Cross-project toplevel sharing
- Plugin architecture for extensions