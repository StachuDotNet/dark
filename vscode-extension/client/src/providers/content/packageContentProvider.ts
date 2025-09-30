import { ParsedUrl } from "../urlPatternRouter";

/**
 * Content provider for package browsing URLs
 * Handles: dark://package/Name.Space.item[?view=type]
 */
export class PackageContentProvider {
  static getContent(parsedUrl: ParsedUrl): string {
    const { target, view, queryParams } = parsedUrl;

    // Handle namespace view with query parameter
    if (target === 'namespace' && queryParams?.path) {
      return this.getNamespaceOverviewContent(queryParams.path);
    }

    if (!target) {
      return this.getPackageListContent();
    }

    switch (view) {
      case 'ast':
        return this.getAstView(target);
      case 'types':
        return this.getTypesView(target);
      case 'docs':
        return this.getDocsView(target);
      case 'graph':
        return this.getGraphView(target);
      case 'exports':
        return this.getExportsView(target);
      case 'tests':
        return this.getTestsView(target);
      default:
        return this.getSourceView(target);
    }
  }

  private static getSourceView(target: string): string {
    // Handle different types of targets
    if (target === 'Darklang.Stdlib.List.map') {
      return this.getListMapContent();
    } else if (target === 'Darklang.Stdlib.List.filterMap') {
      return this.getListFilterMapContent();
    } else if (target === 'MyApp.User.validate') {
      return this.getUserValidateContent();
    } else if (target === 'MyApp.User') {
      return this.getUserModuleContent();
    } else if (target.includes('Darklang.Stdlib')) {
      return this.getStdlibItemContent(target);
    } else {
      return this.getGenericPackageContent(target);
    }
  }

  private static getAstView(target: string): string {
    return `# AST View: ${target}

## Abstract Syntax Tree

\`\`\`
FunctionDef {
  name: "${target.split('.').pop()}"
  parameters: [
    Parameter { name: "fn", type: "'a -> 'b" },
    Parameter { name: "list", type: "List<'a>" }
  ]
  returnType: "List<'b>"
  body: MatchExpression {
    expr: Variable("list")
    cases: [
      Case {
        pattern: EmptyList
        body: EmptyList
      },
      Case {
        pattern: Cons(Variable("head"), Variable("tail"))
        body: Cons(
          FunctionCall(Variable("fn"), [Variable("head")]),
          FunctionCall(Variable("map"), [Variable("fn"), Variable("tail")])
        )
      }
    ]
  }
}
\`\`\`

## Type Inference

- **Input Types**: \`'a -> 'b\`, \`List<'a>\`
- **Output Type**: \`List<'b>\`
- **Type Variables**: \`'a\`, \`'b\` (polymorphic)
- **Constraints**: None

## Compilation Target

\`\`\`javascript
function map(fn, list) {
  if (list.length === 0) return [];
  return [fn(list[0])].concat(map(fn, list.slice(1)));
}
\`\`\``;
  }

  private static getTypesView(target: string): string {
    return `# Type Information: ${target}

## Function Signature

\`\`\`fsharp
val map : ('a -> 'b) -> List<'a> -> List<'b>
\`\`\`

## Type Parameters

- **'a**: Input element type (polymorphic)
- **'b**: Output element type (polymorphic)

## Type Constraints

- No constraints - fully polymorphic

## Usage Examples with Types

\`\`\`fsharp
// Int64 -> Int64
Stdlib.List.map (fun x -> x * 2L) [1L; 2L; 3L] : List<Int64>

// String -> Int64
Stdlib.List.map Stdlib.String.length ["hello"; "world"] : List<Int64>

// User -> String
Stdlib.List.map (fun user -> user.name) users : List<String>
\`\`\`

## Type Evolution

| Version | Signature | Changes |
|---------|-----------|---------|
| v1.0.0  | \`('a -> 'b) -> List<'a> -> List<'b>\` | Initial |
| v1.1.0  | \`('a -> 'b) -> List<'a> -> List<'b>\` | No change |
| v1.2.0  | \`('a -> 'b) -> List<'a> -> List<'b>\` | No change |

## Related Types

- \`List<'a>\` - Input/output container type
- \`Option<'a>\` - Used in filterMap variant
- \`Result<'a, 'e>\` - Used in error-handling variants`;
  }

  private static getDocsView(target: string): string {
    return `# Documentation: ${target}

## Overview

Maps a function over each element of a list, returning a new list with the transformed elements.

## Purpose

The \`map\` function is a fundamental list operation that applies a transformation function to every element in a list, producing a new list with the results. It preserves the structure and order of the original list while transforming the content.

## Parameters

### fn : 'a -> 'b
The transformation function to apply to each element. This function:
- Takes an element of type 'a as input
- Returns a transformed element of type 'b
- Is applied to each element in order

### list : List<'a>
The input list to transform. Can be:
- Empty list \`[]\` - returns empty list
- Single element list \`[x]\` - returns \`[fn(x)]\`
- Multiple elements \`[x; y; z]\` - returns \`[fn(x); fn(y); fn(z)]\`

## Return Value

Returns a new \`List<'b>\` containing the results of applying \`fn\` to each element of the input list. The original list is unchanged (immutable).

## Examples

### Basic Transformations

\`\`\`fsharp
// Double each number
Stdlib.List.map (fun x -> x * 2L) [1L; 2L; 3L; 4L]
// Result: [2L; 4L; 6L; 8L]

// Convert to strings
Stdlib.List.map Stdlib.Int64.toString [1L; 2L; 3L]
// Result: ["1"; "2"; "3"]

// Extract property
Stdlib.List.map (fun user -> user.name) users
// Result: ["Alice"; "Bob"; "Charlie"]
\`\`\`

### Advanced Usage

\`\`\`fsharp
// Chain transformations
[1L; 2L; 3L]
|> Stdlib.List.map (fun x -> x * 2L)
|> Stdlib.List.map (fun x -> x + 1L)
// Result: [3L; 5L; 7L]

// Complex transformations
Stdlib.List.map (fun user -> {
  displayName = user.firstName ++ " " ++ user.lastName
  isActive = user.lastLogin > thirtyDaysAgo
}) users
\`\`\`

## Performance

- **Time Complexity**: O(n) where n is the length of the list
- **Space Complexity**: O(n) for the new list
- **Stack Usage**: O(n) recursive calls (tail-call optimized in v1.2.1+)

## Related Functions

- \`Stdlib.List.filter\` - Select elements based on a predicate
- \`Stdlib.List.filterMap\` - Transform and filter in one pass
- \`Stdlib.List.fold\` - Reduce list to a single value
- \`Stdlib.List.iter\` - Apply function for side effects only

## See Also

- [List module documentation](dark://package/Darklang.Stdlib.List)
- [Functional programming guide](dark://docs/functional-programming)
- [Performance best practices](dark://docs/performance)`;
  }

  private static getGraphView(target: string): string {
    if (target === 'MyApp.User') {
      return `# Dependency Graph: ${target}

## Module Dependencies

\`\`\`
MyApp.User
├── Dependencies (imports)
│   ├── Darklang.Stdlib.String (email validation)
│   ├── Darklang.Stdlib.Uuid (ID generation)
│   ├── Darklang.Stdlib.DateTime (timestamps)
│   ├── MyApp.Auth (password hashing)
│   └── MyApp.Database (persistence)
│
├── Dependents (used by)
│   ├── MyApp.Routes.Auth (login/register)
│   ├── MyApp.Services.UserService (user management)
│   ├── MyApp.Controllers.Profile (profile updates)
│   └── MyApp.Tests.UserTests (test suite)
│
└── Internal Structure
    ├── Types
    │   ├── User (main entity)
    │   ├── UserInput (creation input)
    │   ├── UserUpdates (update input)
    │   └── ValidationError (error types)
    │
    └── Functions
        ├── validate (input validation)
        ├── create (user creation)
        ├── update (user updates)
        ├── findById (lookup)
        └── hashPassword (security)
\`\`\`

## Impact Analysis

### If MyApp.User changes:
- 🔴 **High Impact**: MyApp.Routes.Auth, MyApp.Services.UserService
- 🟡 **Medium Impact**: MyApp.Controllers.Profile
- 🟢 **Low Impact**: MyApp.Tests.UserTests

### Risk Assessment:
- **Breaking Changes**: 4 modules affected
- **Test Coverage**: 95% (good safety net)
- **API Stability**: Public interface unchanged in last 6 months

## Circular Dependencies

✅ **No circular dependencies detected**

## Performance Hotspots

- **MyApp.User.validate**: Called in 85% of user operations
- **MyApp.User.findById**: Database bottleneck (consider caching)`;
    }

    return `# Dependency Graph: ${target}

Module dependency visualization would be shown here for ${target}.`;
  }

  private static getExportsView(target: string): string {
    if (target === 'MyApp.User') {
      return `# Public Interface: ${target}

## Exported Types

### User
\`\`\`fsharp
type User = {
  id: Uuid
  email: String
  hashedPassword: String
  profile: Profile
  preferences: UserPreferences
  createdAt: DateTime
  updatedAt: DateTime
}
\`\`\`

### UserInput
\`\`\`fsharp
type UserInput = {
  email: String
  password: String
  profile: Profile
}
\`\`\`

### ValidationError
\`\`\`fsharp
type ValidationError =
  | InvalidEmail
  | WeakPassword
  | MissingField of String
  | UserNotFound of Uuid
\`\`\`

## Exported Functions

### validate
\`\`\`fsharp
val validate : UserInput -> Result<User, List<ValidationError>>
\`\`\`
Validates user input and returns a validated User or list of errors.

### create
\`\`\`fsharp
val create : UserInput -> Result<User, String>
\`\`\`
Creates a new user with validation and persistence.

### update
\`\`\`fsharp
val update : Uuid -> UserUpdates -> Result<User, ValidationError>
\`\`\`
Updates an existing user with validation.

### findById
\`\`\`fsharp
val findById : Uuid -> Option<User>
\`\`\`
Finds a user by ID.

## Private Implementation

The following are internal and not exported:
- \`hashPassword\` - Password hashing utility
- \`validateEmail\` - Email format validation
- \`validatePassword\` - Password strength validation

## API Stability

| Export | Stability | Last Changed |
|--------|-----------|-------------|
| User type | Stable | v1.2.0 |
| validate | Stable | Current |
| create | Stable | v1.1.0 |
| update | ⚠️ Modified | Current patch |
| findById | Stable | v1.0.0 |

## Breaking Changes

None in current patch. Next major version may:
- Add required profile fields
- Change validation error format`;
    }

    return `# Public Interface: ${target}

Exported types and functions would be listed here for ${target}.`;
  }

  private static getTestsView(target: string): string {
    return `# Test Coverage: ${target}

## Test Summary

- **Total Tests**: 24
- **Passing**: 24 ✅
- **Failing**: 0 ❌
- **Coverage**: 95%
- **Last Run**: 2 minutes ago

## Test Categories

### Unit Tests (18)
- ✅ validate_empty_email_returns_error
- ✅ validate_invalid_email_returns_error
- ✅ validate_weak_password_returns_error
- ✅ validate_missing_fields_returns_errors
- ✅ validate_valid_user_returns_ok
- ✅ create_with_valid_input_succeeds
- ✅ create_with_invalid_input_fails
- ✅ update_existing_user_succeeds
- ✅ update_nonexistent_user_fails
- ✅ findById_existing_user_returns_some
- ✅ findById_nonexistent_user_returns_none
- ✅ password_hashing_is_secure
- ✅ email_validation_edge_cases
- ✅ profile_updates_preserve_other_fields
- ✅ concurrent_updates_handle_conflicts
- ✅ validation_error_messages_are_clear
- ✅ multiple_validation_errors_combined
- ✅ database_errors_handled_gracefully

### Integration Tests (4)
- ✅ user_creation_end_to_end
- ✅ user_login_flow_integration
- ✅ profile_update_with_validation
- ✅ user_deletion_cascade_effects

### Performance Tests (2)
- ✅ validate_performance_under_load
- ✅ bulk_user_operations_performance

## Coverage Report

| Function | Coverage | Missing Lines |
|----------|----------|---------------|
| validate | 100% | - |
| create | 98% | Error handling edge case |
| update | 95% | Conflict resolution path |
| findById | 100% | - |
| hashPassword | 100% | - |

## Test Data

Tests use realistic data sets:
- 50 valid user examples
- 100+ invalid input combinations
- Performance tests with 10,000 users
- Edge cases for all validation rules

## Continuous Integration

- Tests run on every patch
- Performance benchmarks tracked
- Coverage requirements: 90% minimum
- Mutation testing score: 85%`;
  }

  private static getListMapContent(): string {
    return `// Darklang.Stdlib.List.map - Maps a function over a list

let map (fn: 'a -> 'b) (list: List<'a>): List<'b> =
  match list with
  | [] -> []
  | head :: tail -> (fn head) :: (map fn tail)

// Examples:
// map (fun x -> x * 2) [1L; 2L; 3L] returns [2L; 4L; 6L]
// map String.length ["hello"; "world"] returns [5L; 5L]

// Tests:
Stdlib.Test.expect (map (fun x -> x * 2L) []) []
Stdlib.Test.expect (map (fun x -> x * 2L) [1L; 2L; 3L]) [2L; 4L; 6L]
Stdlib.Test.expect (map String.length ["hi"]) [2L]`;
  }

  private static getListFilterMapContent(): string {
    return `// Darklang.Stdlib.List.filterMap [MODIFIED IN CURRENT PATCH]
// Applies function and keeps only Some results

let filterMap (fn: 'a -> Option<'b>) (list: List<'a>): List<'b> =
  let rec helper acc remaining =
    match remaining with
    | [] -> Stdlib.List.reverse acc
    | head :: tail ->
      match fn head with
      | Some result -> helper (result :: acc) tail
      | None -> helper acc tail
  helper [] list

// PATCH CHANGES:
// + Tail-recursive implementation for better performance
// + Comprehensive test coverage added
// + Documentation improvements

// Example:
let isEven x = if x % 2L == 0L then Some (x * 10L) else None
filterMap isEven [1L; 2L; 3L; 4L] // Returns [20L; 40L]

// Tests:
Stdlib.Test.expect (filterMap isEven []) []
Stdlib.Test.expect (filterMap isEven [1L; 3L]) []
Stdlib.Test.expect (filterMap isEven [2L; 4L]) [20L; 40L]`;
  }

  private static getUserValidateContent(): string {
    return `// MyApp.User.validate [NEW IN CURRENT PATCH]
// Comprehensive user input validation

type ValidationError =
  | InvalidEmail
  | WeakPassword
  | MissingField of String

let validate (user: MyApp.User.UserInput): Result<MyApp.User.User, List<ValidationError>> =
  let errors = []

  // Email validation
  let emailErrors =
    if Stdlib.String.isEmpty user.email then
      [MissingField "email"]
    elif not (Stdlib.String.contains user.email "@") then
      [InvalidEmail]
    else
      []

  // Password validation
  let passwordErrors =
    if Stdlib.String.isEmpty user.password then
      [MissingField "password"]
    elif Stdlib.String.length user.password < 8L then
      [WeakPassword]
    else
      []

  let allErrors = Stdlib.List.append emailErrors passwordErrors

  if Stdlib.List.isEmpty allErrors then
    Ok {
      id = Stdlib.Uuid.generate ()
      email = user.email
      hashedPassword = MyApp.Auth.hashPassword user.password
      createdAt = Stdlib.DateTime.now ()
    }
  else
    Error allErrors

// STATUS: Ready for review
// TESTS: 12 passing, 95% coverage
// CONFLICTS: None`;
  }

  private static getUserModuleContent(): string {
    return `# MyApp.User Module

## Overview
User management module providing authentication, validation, and profile management.

## Types

### User
\`\`\`fsharp
type User = {
  id: Uuid
  email: String
  hashedPassword: String
  profile: Profile
  createdAt: DateTime
  updatedAt: DateTime
}
\`\`\`

### ValidationError
\`\`\`fsharp
type ValidationError =
  | InvalidEmail
  | WeakPassword
  | MissingField of String
\`\`\`

## Functions

### validate [NEW]
Validates user input with comprehensive checks.
[View Source](dark://package/MyApp.User.validate)

### create
Creates new user with validation.
[View Source](dark://package/MyApp.User.create)

### update [CONFLICTS]
Updates existing user (has merge conflicts).
[View Source](dark://package/MyApp.User.update)

### findById
Finds user by ID.
[View Source](dark://package/MyApp.User.findById)

## Recent Changes
- ✅ Added comprehensive validation function
- ⚠️ Conflicts in update function (needs resolution)
- 📈 Test coverage improved to 95%

## Dependencies
- Darklang.Stdlib.String (email validation)
- Darklang.Stdlib.Uuid (ID generation)
- MyApp.Auth (password hashing)`;
  }

  private static getStdlibItemContent(target: string): string {
    const itemName = target.split('.').pop();
    return `// ${target}
// Standard library function

let ${itemName} = (* Implementation would be shown here *)

// This is a core Darklang standard library function.
// [View Documentation](dark://package/${target}?view=docs)
// [View Type Information](dark://package/${target}?view=types)
// [View AST](dark://package/${target}?view=ast)

// Usage examples and tests would be shown here.`;
  }

  private static getGenericPackageContent(target: string): string {
    return `# ${target}

This is a placeholder for package content.

## Available Views
- [Source Code](dark://package/${target})
- [Documentation](dark://package/${target}?view=docs)
- [Type Information](dark://package/${target}?view=types)
- [AST View](dark://package/${target}?view=ast)

## Actions
- [Edit in Current Patch](dark://edit/current-patch/${target})
- [View History](dark://history/${target})
- [Create Draft](dark://draft/${target}.newItem)

## Module Contents
Functions and types in this package would be listed here.

*This is demo content for the Darklang VS Code extension.*`;
  }

  private static getNamespaceOverviewContent(namespacePath: string): string {
    const namespaceSegments = namespacePath.split('.');
    const namespaceName = namespaceSegments.join('.');

    return `# Namespace Overview: ${namespaceName}

## Namespace Information

| Property | Value |
|----------|-------|
| **Full Path** | ${namespacePath} |
| **Type** | ${namespacePath.includes('Stdlib') ? 'Core Library' : 'Application Package'} |
| **Visibility** | Public |
| **Stability** | ${namespacePath.includes('Stdlib') ? 'Stable' : 'Development'} |

## Package Structure

### ${namespacePath.includes('Stdlib') ? 'Standard Library' : 'Application'} Overview
${namespacePath.includes('Stdlib') ? `
This namespace contains core Darklang standard library functions that provide fundamental operations for ${namespacePath.includes('List') ? 'list manipulation' : namespacePath.includes('String') ? 'string processing' : 'data operations'}.

#### Key Features
- **High Performance**: Optimized implementations
- **Type Safety**: Comprehensive type checking
- **Immutability**: Functional programming principles
- **Documentation**: Complete API documentation
` : `
This namespace contains application-specific functionality for ${namespacePath.includes('User') ? 'user management and authentication' : namespacePath.includes('Auth') ? 'authentication and security' : 'business logic'}.

#### Key Features
- **Business Logic**: Domain-specific operations
- **Validation**: Input validation and error handling
- **Integration**: Seamless module integration
- **Testing**: Comprehensive test coverage
`}

## Contents

### Functions (${namespacePath.includes('Stdlib') ? '15' : '8'})
${namespacePath.includes('Stdlib.List') ? `
- **map** - Transform each element with a function
- **filter** - Select elements matching predicate
- **fold** - Reduce list to single value
- **length** - Get list length
- **append** - Combine two lists
- **head** - Get first element
- **tail** - Get all but first element
- **reverse** - Reverse list order
- **sort** - Sort elements
- **unique** - Remove duplicates
- **zip** - Combine two lists pairwise
- **range** - Generate number sequences
- **isEmpty** - Check if list is empty
- **contains** - Check if element exists
- **find** - Find first matching element
` : namespacePath.includes('User') ? `
- **validate** - Comprehensive input validation
- **create** - Create new user account
- **update** - Update existing user
- **findById** - Lookup user by ID
- **authenticate** - Verify user credentials
- **hashPassword** - Secure password hashing
- **generateToken** - Create auth tokens
- **revokeToken** - Invalidate tokens
` : `
- **Core functions for ${namespaceName}**
- **Domain-specific operations**
- **Utility functions**
- **Helper methods**
`}

### Types (${namespacePath.includes('Stdlib') ? '5' : '4'})
${namespacePath.includes('Stdlib.List') ? `
- **List<'a>** - Generic list container
- **ListError** - List operation errors
- **SortOrder** - Sorting direction
- **CompareResult** - Comparison outcomes
- **ListStats** - List statistics
` : namespacePath.includes('User') ? `
- **User** - User account entity
- **UserInput** - User creation input
- **ValidationError** - Validation errors
- **UserProfile** - User profile data
` : `
- **Core types for ${namespaceName}**
- **Error types**
- **Configuration types**
- **Result types**
`}

### Constants (${namespacePath.includes('Stdlib') ? '3' : '2'})
${namespacePath.includes('Stdlib') ? `
- **maxListSize** - Maximum list length
- **defaultComparer** - Default comparison function
- **emptyList** - Empty list constant
` : `
- **Configuration constants**
- **Default values**
`}

## Usage Examples

### Common Patterns
\`\`\`fsharp
${namespacePath.includes('Stdlib.List') ? `
// Transform and filter data
[1L; 2L; 3L; 4L; 5L]
|> Darklang.Stdlib.List.filter (fun x -> x > 2L)
|> Darklang.Stdlib.List.map (fun x -> x * 2L)
// Result: [6L; 8L; 10L]

// Aggregate operations
[1L; 2L; 3L; 4L; 5L]
|> Darklang.Stdlib.List.fold (fun acc x -> acc + x) 0L
// Result: 15L
` : namespacePath.includes('User') ? `
// Create and validate user
let userInput = { email = "user@example.com"; password = "securepass123" }
match MyApp.User.validate userInput with
| Ok validUser ->
    match MyApp.User.create validUser with
    | Ok user -> Printf.printf "User created: %s" user.email
    | Error msg -> Printf.printf "Creation failed: %s" msg
| Error errors ->
    errors |> List.iter (Printf.printf "Validation error: %A")
` : `
// Usage examples for ${namespaceName} would be shown here
let example = ${namespaceName}.someFunction input
`}
\`\`\`

## Documentation Links

### API Documentation
- [📖 Complete API Reference](dark://package/${namespacePath}?view=docs)
- [🏗️ Type Definitions](dark://package/${namespacePath}?view=types)
- [🌳 Dependency Graph](dark://package/${namespacePath}?view=graph)
- [📤 Public Interface](dark://package/${namespacePath}?view=exports)

### Development Resources
- [🧪 Test Coverage](dark://package/${namespacePath}?view=tests)
- [📊 Performance Metrics](dark://package/${namespacePath}?view=performance)
- [🔍 Source Code](dark://package/${namespacePath})
- [📝 Change History](dark://history/${namespacePath})

## Recent Activity

### Recent Changes
${namespacePath.includes('User') ? `
- **2 hours ago** - Added comprehensive validation function
- **1 day ago** - Fixed authentication edge cases
- **3 days ago** - Performance improvements
- **1 week ago** - Updated documentation
` : `
- **1 week ago** - Performance optimizations
- **2 weeks ago** - Bug fixes and improvements
- **1 month ago** - API stabilization
- **2 months ago** - Initial release
`}

### Usage Statistics
- **Daily Calls**: ${namespacePath.includes('Stdlib') ? '50,000+' : '2,500+'}
- **Active Projects**: ${namespacePath.includes('Stdlib') ? '250+' : '12'}
- **Error Rate**: ${namespacePath.includes('Stdlib') ? '0.01%' : '0.05%'}
- **Performance**: ${namespacePath.includes('Stdlib') ? 'Excellent' : 'Good'}

## Development

### Contributing
${namespacePath.includes('Stdlib') ? `
This is a core standard library namespace maintained by the Darklang team. Contributions are welcome through the standard RFC process.

- **RFC Process**: Propose changes via GitHub issues
- **Testing**: All changes require comprehensive tests
- **Documentation**: Complete API documentation required
- **Review**: Requires core team approval
` : `
This application namespace is actively developed by the team. Contributions follow standard development practices.

- **Development**: Feature branches and pull requests
- **Testing**: Unit and integration tests required
- **Review**: Team review process
- **Deployment**: Automated deployment pipeline
`}

### Quality Metrics
- **Test Coverage**: ${namespacePath.includes('Stdlib') ? '98%' : '95%'}
- **Documentation**: ${namespacePath.includes('Stdlib') ? '100%' : '90%'}
- **Performance**: ${namespacePath.includes('Stdlib') ? 'Optimized' : 'Good'}
- **Stability**: ${namespacePath.includes('Stdlib') ? 'Production Ready' : 'Active Development'}

## Related Namespaces

### Dependencies
${namespacePath.includes('User') ? `
- **MyApp.Auth** - Authentication and security
- **MyApp.Database** - Data persistence
- **Darklang.Stdlib.String** - String operations
- **Darklang.Stdlib.Uuid** - ID generation
` : namespacePath.includes('Stdlib') ? `
- **Core runtime** - Basic operations
- **Platform APIs** - System integration
` : `
- **Related application modules**
- **Core standard library**
`}

### Dependents
${namespacePath.includes('Stdlib') ? `
- **All application code** - Core dependency
- **Other stdlib modules** - Cross-references
- **Third-party packages** - External usage
` : namespacePath.includes('User') ? `
- **MyApp.Routes.Auth** - Authentication routes
- **MyApp.Services.UserService** - User management
- **MyApp.Controllers.Profile** - Profile management
` : `
- **Application modules using this namespace**
- **Integration points**
`}

## Quick Actions

- [📦 Browse Full Package](dark://package/${namespacePath})
- [✏️ Edit in Current Patch](dark://edit/current-patch/${namespacePath})
- [📜 View Change History](dark://history/${namespacePath})
- [🔍 Search Within Namespace](command:darklang.search.namespace?${namespacePath})
- [📋 Copy Import Statement](command:darklang.copy.import?${namespacePath})

[🏠 Back to All Packages](dark://package/)`;
  }

  private static getPackageListContent(): string {
    return `# Darklang Packages

## Available Packages

### Darklang.Stdlib
Core standard library functions
- [List](dark://package/Darklang.Stdlib.List) - List operations
- [String](dark://package/Darklang.Stdlib.String) - String manipulation
- [Option](dark://package/Darklang.Stdlib.Option) - Optional values
- [Result](dark://package/Darklang.Stdlib.Result) - Error handling
- [DateTime](dark://package/Darklang.Stdlib.DateTime) - Date/time utilities

### MyApp
Application-specific packages
- [User](dark://package/MyApp.User) - User management [MODIFIED]
- [Auth](dark://package/MyApp.Auth) - Authentication
- [Database](dark://package/MyApp.Database) - Data access

## Recent Activity
- 🆕 MyApp.User.validate added
- ⚠️ MyApp.User.update has conflicts
- ✅ Darklang.Stdlib.List.filterMap optimized

[Browse all packages](dark://package/)`;
  }
}