import { ParsedUrl } from "../urlPatternRouter";

/**
 * Content provider for edit URLs
 * Handles: dark://edit/context/target
 */
export class EditContentProvider {
  static getContent(parsedUrl: ParsedUrl): string {
    const { context, target } = parsedUrl;

    if (!context || !target) {
      return this.getEditHelpContent();
    }

    if (context === 'current-patch') {
      return this.getCurrentPatchEditContent(target);
    } else if (context.startsWith('patch-')) {
      return this.getSpecificPatchEditContent(context, target);
    } else {
      return this.getGenericEditContent(context, target);
    }
  }

  private static getCurrentPatchEditContent(target: string): string {
    if (target === 'MyApp.User.validate') {
      return this.getEditUserValidateContent();
    } else if (target === 'MyApp.User.create') {
      return this.getEditUserCreateContent();
    } else {
      return this.getGenericCurrentPatchEdit(target);
    }
  }

  private static getEditUserValidateContent(): string {
    return `# Editing: MyApp.User.validate [CURRENT PATCH]

**Status:** New function in current patch
**Patch:** abc123 - Add user validation
**Mode:** CREATE

## Function Implementation

\`\`\`fsharp
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
\`\`\`

## Edit Context

### Patch Information
- **Intent:** Add comprehensive user validation
- **Author:** stachu
- **Status:** Draft
- **Created:** 2024-01-15 14:30:00

### Operation Type
- **Type:** CREATE
- **Target:** MyApp.User.validate
- **Dependencies:** MyApp.User.ValidationError (also being created)

### Validation Status
- ✅ Syntax valid
- ✅ Type checking passed
- ✅ Tests passing (12/12)
- ✅ Style compliant

## Dependencies

### Required Types
\`\`\`fsharp
// Also being created in this patch
type ValidationError =
  | InvalidEmail
  | WeakPassword
  | MissingField of String
\`\`\`

### Used Functions
- \`Stdlib.String.isEmpty\` - Check for empty strings
- \`Stdlib.String.contains\` - Email format validation
- \`Stdlib.String.length\` - Password length check
- \`Stdlib.List.append\` - Combine error lists
- \`Stdlib.Uuid.generate\` - Generate user ID
- \`MyApp.Auth.hashPassword\` - Secure password hashing
- \`Stdlib.DateTime.now\` - Timestamp creation

## Tests

### Test Coverage: 95%

\`\`\`fsharp
// Email validation tests
Stdlib.Test.expect (validate { email = ""; password = "valid123" })
  (Error [MissingField "email"])

Stdlib.Test.expect (validate { email = "invalid"; password = "valid123" })
  (Error [InvalidEmail])

// Password validation tests
Stdlib.Test.expect (validate { email = "test@example.com"; password = "" })
  (Error [MissingField "password"])

Stdlib.Test.expect (validate { email = "test@example.com"; password = "weak" })
  (Error [WeakPassword])

// Success case
Stdlib.Test.expect_ok (validate { email = "test@example.com"; password = "strong123" })

// Multiple errors
Stdlib.Test.expect (validate { email = ""; password = "weak" })
  (Error [MissingField "email"; WeakPassword])
\`\`\`

## Edit Actions

- [💾 Save Changes](command:darklang.edit.save)
- [🔍 Run Tests](command:darklang.edit.runTests)
- [📝 Update Intent](command:darklang.patch.editIntent)
- [👀 Preview Patch](dark://patch/abc123)
- [🔄 Compare with Base](dark://compare/current/patch-abc123?target=MyApp.User.validate)

## Live Validation

Real-time feedback as you edit:
- ✅ Syntax: Valid
- ✅ Types: All correct
- ✅ Style: Compliant
- ✅ Performance: Acceptable
- ⚠️ TODO: Add integration test

## Related Edits

Other items being edited in this patch:
- [ValidationError type](dark://edit/current-patch/MyApp.User.ValidationError)
- [Updated create function](dark://edit/current-patch/MyApp.User.create)

*You are editing in PATCH MODE. Changes are tracked and will be part of patch abc123.*`;
  }

  private static getEditUserCreateContent(): string {
    return `# Editing: MyApp.User.create [CURRENT PATCH]

**Status:** Modified in current patch
**Patch:** abc123 - Add user validation
**Mode:** MODIFY

## Current Implementation

\`\`\`fsharp
let create (input: UserInput): Result<User, String> =
  match MyApp.User.validate input with
  | Ok user -> Ok user
  | Error errors ->
    let errorMsg =
      errors
      |> Stdlib.List.map (fun err ->
        match err with
        | InvalidEmail -> "Invalid email format"
        | WeakPassword -> "Password too weak"
        | MissingField field -> field ++ " is required"
      )
      |> Stdlib.String.join ", "
    Error errorMsg
\`\`\`

## Changes Made

### Before (Original)
\`\`\`fsharp
let create (input: UserInput): Result<User, String> =
  // TODO: Add validation
  Ok {
    id = Stdlib.Uuid.generate ()
    email = input.email
    hashedPassword = MyApp.Auth.hashPassword input.password
    createdAt = Stdlib.DateTime.now ()
  }
\`\`\`

### After (Current)
\`\`\`fsharp
let create (input: UserInput): Result<User, String> =
  match MyApp.User.validate input with
  | Ok user -> Ok user
  | Error errors ->
    let errorMsg =
      errors
      |> Stdlib.List.map (fun err ->
        match err with
        | InvalidEmail -> "Invalid email format"
        | WeakPassword -> "Password too weak"
        | MissingField field -> field ++ " is required"
      )
      |> Stdlib.String.join ", "
    Error errorMsg
\`\`\`

## Impact Analysis

### Breaking Changes
- ⚠️ **Return type semantics changed**
  - Still returns Result<User, String>
  - But now actually validates input (was TODO before)
  - Error messages are more structured

### Dependencies Added
- **MyApp.User.validate** - New validation function
- **MyApp.User.ValidationError** - New error type

### Callers Affected
Functions that call MyApp.User.create:
- MyApp.Routes.Auth.register ✅ (already handles Result)
- MyApp.Services.UserService.createUser ✅ (already handles Result)
- MyApp.Tests.UserTests.* ⚠️ (tests need updates for new validation)

## Edit History

### Changes in This Session
1. Replaced TODO comment with actual validation call
2. Added structured error message formatting
3. Integrated with new ValidationError type

### Validation Status
- ✅ Syntax valid
- ✅ Type checking passed
- ⚠️ Tests need updates (6 failing, need to handle new validation)
- ✅ Style compliant

## Tests to Update

### Failing Tests (Need Updates)
\`\`\`fsharp
// These tests need to account for new validation
test_create_with_invalid_email // Now returns validation error
test_create_with_weak_password // Now returns validation error
test_create_error_messages     // Error format changed
\`\`\`

### Suggested Test Updates
\`\`\`fsharp
// Update to use valid input
Stdlib.Test.expect_ok (create {
  email = "test@example.com";
  password = "strong123"
})

// Test validation integration
Stdlib.Test.expect_error (create {
  email = "";
  password = "weak"
}) "email is required, Password too weak"
\`\`\`

## Edit Actions

- [💾 Save Changes](command:darklang.edit.save)
- [🔧 Fix Tests](command:darklang.edit.fixTests)
- [📊 Impact Analysis](dark://patch/abc123/impact)
- [🔄 Compare Changes](dark://compare/original/current?target=MyApp.User.create)
- [↩️ Revert Changes](command:darklang.edit.revert)

*You are editing in PATCH MODE. Changes are tracked and will be part of patch abc123.*`;
  }

  private static getGenericCurrentPatchEdit(target: string): string {
    return `# Editing: ${target} [CURRENT PATCH]

**Status:** Being edited in current patch
**Mode:** EDIT

## Edit Context

You are editing ${target} in patch mode. Changes will be tracked and included in your current patch.

### Current Patch Info
- **Intent:** Current patch intent
- **Author:** Current user
- **Status:** Draft

## Edit Mode Features

- 🔄 **Live Validation**: Real-time syntax and type checking
- 📝 **Auto-save**: Changes saved automatically
- 🧪 **Test Integration**: Run tests as you edit
- 📊 **Impact Analysis**: See what your changes affect

## Actions

- [💾 Save Changes](command:darklang.edit.save)
- [🔍 Run Tests](command:darklang.edit.runTests)
- [👀 Preview Patch](command:darklang.patch.preview)
- [📊 Impact Analysis](command:darklang.edit.analyze)

*Edit ${target} here. Changes are automatically tracked in patch mode.*`;
  }

  private static getSpecificPatchEditContent(patchId: string, target: string): string {
    return `# Editing: ${target} [${patchId.toUpperCase()}]

**Status:** Editing in specific patch context
**Patch:** ${patchId}
**Target:** ${target}

## Patch Context

You are editing ${target} in the context of patch ${patchId}.

### Patch Information
- **ID:** ${patchId}
- **Status:** Specific patch editing mode
- **Target:** ${target}

## Edit Features

All changes made here will be associated with patch ${patchId}.

## Actions

- [💾 Save to Patch](command:darklang.edit.saveToPatch?${patchId})
- [📝 View Patch](dark://patch/${patchId.replace('patch-', '')})
- [🔄 Switch to Current](dark://edit/current-patch/${target})

*Editing ${target} in patch ${patchId} context.*`;
  }

  private static getGenericEditContent(context: string, target: string): string {
    return `# Editing: ${target}

**Context:** ${context}
**Target:** ${target}
**Mode:** EDIT

## Edit Context

You are editing ${target} in ${context} context.

## Features Available

- Real-time validation
- Syntax highlighting
- Type checking
- Auto-completion
- Error detection

## Actions

- [💾 Save Changes](command:darklang.edit.save)
- [📝 View Source](dark://package/${target})
- [📊 View History](dark://history/${target})

*Edit ${target} here.*`;
  }

  private static getEditHelpContent(): string {
    return `# Edit Mode Help

## Edit URL Patterns

### Current Patch Editing
\`dark://edit/current-patch/Name.Space.item\`
Edit items in your current active patch.

### Specific Patch Editing
\`dark://edit/patch-abc123/Name.Space.item\`
Edit items in a specific patch context.

## Edit Features

### Live Validation
- **Syntax**: Real-time syntax checking
- **Types**: Type validation as you edit
- **Style**: Code style compliance
- **Tests**: Automatic test running

### Auto-save
Changes are automatically saved and tracked.

### Patch Integration
All edits are tracked in patch context for collaboration.

## Getting Started

1. [Browse packages](dark://package/) to find items to edit
2. Click "Edit" button or use context menu
3. Make your changes in the editor
4. Changes are automatically saved to your current patch

## Actions

- [🆕 Create New Item](command:darklang.edit.create)
- [📦 Browse Packages](dark://package/)
- [📝 View Current Patch](command:darklang.patch.current)`;
  }
}