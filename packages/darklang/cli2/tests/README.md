# CLI2 Testing Framework

This directory contains the testing framework for CLI1 vs CLI2 behavior parity testing.

## Overview

As part of the CLI refactoring to CLI2, we need to ensure that CLI2 maintains behavioral compatibility with CLI1. This testing framework provides the infrastructure to verify this compatibility.

## Files

- `behaviorParity.dark` - Core testing framework for CLI1 vs CLI2 comparison
- `runParityTests.dark` - Test runner entry point
- `README.md` - This documentation file

## Current Implementation

The current implementation provides:

1. **Test Structure Framework**: Types and functions to define and run parity tests
2. **Structural Tests**: Tests that verify CLI2 modules are accessible and properly structured
3. **Command Tests**: Framework for testing individual commands for parity
4. **Results Reporting**: Clear reporting of test results with pass/fail status

## Future Development

As CLI2 development progresses, this framework can be extended to:

1. **Execute Real CLI Commands**: Currently uses placeholder implementations
2. **Environment Variable Testing**: Test CLI1 vs CLI2 using DARK_USE_CLI2 toggle
3. **Output Comparison**: Compare actual command outputs between versions
4. **Performance Comparison**: Track performance differences between CLI1 and CLI2
5. **Integration with CI**: Automated testing in continuous integration

## Running Tests

When the CLI2 implementation is more complete, tests can be run using:

```
# Run CLI2 parity tests
./scripts/run-cli @Darklang.Cli2.Tests.runParityTests
```

## Test Categories

1. **Structural Tests**
   - CLI2 accessibility
   - System commands availability
   - Module organization

2. **Command Tests**
   - version command parity
   - help command parity  
   - status command parity
   - error handling parity

3. **Integration Tests**
   - Interactive mode behavior
   - Environment variable handling
   - File system interactions

## Phase 1.4 Completion

This framework establishes the foundation for continuous testing of CLI1 vs CLI2 behavior parity, completing Phase 1.4 of the CLI refactoring plan.