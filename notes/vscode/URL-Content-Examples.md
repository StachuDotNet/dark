# URL Content Examples & View Trigger Mechanisms

## How Custom Views Are Triggered

VS Code decides which view to show based on the **URL scheme pattern and context**:

### View Resolution Logic
```typescript
function resolveViewForURL(url: string): ViewType {
  const parsed = parseVirtualURL(url)

  switch (parsed.mode) {
    case 'package':
      return 'readonly-source'     // Read-only text editor
    case 'edit':
      return 'editable-source'     // Full text editor with IntelliSense
    case 'draft':
      return 'editable-template'   // Pre-filled template editor
    case 'history':
      return 'custom-webview'      // Custom HTML timeline view
    case 'patch':
      return 'custom-webview'      // Custom HTML patch overview
    case 'compare':
      return 'diff-editor'         // VS Code's built-in diff view
    case 'session':
      return 'custom-webview'      // Session management interface
    case 'instances':
      return 'custom-webview'      // Instance browser
    case 'search':
      return 'custom-webview'      // Search results interface
    case 'user':
      return 'custom-webview'      // User account settings
  }
}
```

### View Types Explained

1. **Readonly Source View** - Standard VS Code text editor, read-only mode
2. **Editable Source View** - Standard VS Code text editor with full editing
3. **Custom Webview** - HTML/CSS/JS interface for complex interactions
4. **Diff Editor** - VS Code's built-in side-by-side comparison
5. **Template Editor** - Pre-filled editable content for new items

## Session-Based URLs (Updated Design)

**Key Change**: Edit context is now **session-based**, not patch-based. A session can contain multiple patches.

### Updated URL Patterns
```
dark://package/Name.Space.item                   # Browse/read
dark://edit/session-id/editor-id                 # Edit in session context
dark://history/Name.Space.item                   # Version history
dark://session/session-id                        # Session overview
dark://compare/hash1/hash2                       # Version comparison
dark://instances                                 # Instance browser
dark://search?query=term                         # Package search
dark://user                                      # User account/settings
```

## Content Examples for Each URL Type

### 1. Package Browse - `dark://package/MyApp.User.validate`

**View Type**: Readonly Source View
**Content**: Standard Darklang source code

```fsharp
// Current version of MyApp.User.validate
// Hash: hash_validate_v3
// Created: 2024-01-15 by alice@mycompany.com
// Last modified: 2024-01-20 by bob@mycompany.com

let validate (user: User) : Result<Bool, ValidationError> =
  let emailValid =
    user.email
    |> Stdlib.String.contains "@"
    |> fun hasAt ->
        if hasAt
        then
          user.email
          |> Stdlib.String.split "@"
          |> fun parts ->
              match parts with
              | [username; domain] ->
                  if Stdlib.String.length username > 0L && Stdlib.String.length domain > 0L
                  then Ok(true)
                  else Error(ValidationError.InvalidEmail("Email format invalid"))
              | _ -> Error(ValidationError.InvalidEmail("Email format invalid"))
        else Error(ValidationError.InvalidEmail("Email must contain @"))

  let nameValid =
    if Stdlib.String.length user.name >= 2L
    then Ok(true)
    else Error(ValidationError.InvalidName("Name must be at least 2 characters"))

  match (emailValid, nameValid) with
  | (Ok(_), Ok(_)) -> Ok(true)
  | (Error(e), _) -> Error(e)
  | (_, Error(e)) -> Error(e)
```

**Status Bar**: `📦 MyApp.User.validate | Current version | Read-only`
**Available Actions**: Edit, View History, Compare Versions, Copy Reference

### 2. Module Browse - `dark://package/MyApp.User`

**View Type**: Custom Webview
**Content**: Interactive module overview

```html
<!DOCTYPE html>
<html>
<head><title>MyApp.User Module</title></head>
<body>
  <div class="module-header">
    <h1>📁 MyApp.User</h1>
    <p>User management and validation functions</p>
    <div class="stats">
      <span>5 functions</span> • <span>2 types</span> • <span>1 value</span>
      <span>Last updated: 2 hours ago</span>
    </div>
  </div>

  <div class="module-contents">
    <section class="functions">
      <h2>🔧 Functions</h2>
      <div class="item-list">
        <div class="item current">
          <span class="name">validate</span>
          <span class="signature">(User) → Result&lt;Bool, ValidationError&gt;</span>
          <div class="actions">
            <button onclick="openItem('MyApp.User.validate')">View</button>
            <button onclick="editItem('MyApp.User.validate')">Edit</button>
          </div>
        </div>
        <div class="item">
          <span class="name">create</span>
          <span class="signature">(String, String) → Result&lt;User, Error&gt;</span>
          <div class="actions">
            <button onclick="openItem('MyApp.User.create')">View</button>
            <button onclick="editItem('MyApp.User.create')">Edit</button>
          </div>
        </div>
        <!-- More functions... -->
      </div>
    </section>

    <section class="types">
      <h2>📋 Types</h2>
      <div class="item-list">
        <div class="item">
          <span class="name">User</span>
          <span class="signature">Record { email: String, name: String, id: UUID }</span>
          <div class="actions">
            <button onclick="openItem('MyApp.User.User')">View</button>
            <button onclick="editItem('MyApp.User.User')">Edit</button>
          </div>
        </div>
        <div class="item">
          <span class="name">ValidationError</span>
          <span class="signature">Enum | InvalidEmail | InvalidName | ...</span>
          <div class="actions">
            <button onclick="openItem('MyApp.User.ValidationError')">View</button>
            <button onclick="editItem('MyApp.User.ValidationError')">Edit</button>
          </div>
        </div>
      </div>
    </section>

    <section class="dependencies">
      <h2>🔗 Dependencies</h2>
      <div class="dependency-graph">
        <div class="dep">Stdlib.String → 8 usages</div>
        <div class="dep">MyApp.Utils.UUID → 2 usages</div>
        <div class="dep">MyApp.Config.validation → 1 usage</div>
      </div>
    </section>
  </div>
</body>
</html>
```

**Status Bar**: `📁 MyApp.User | 8 items | Module view`

### 3. Edit Session - `dark://edit/session-abc123/editor-1`

**View Type**: Editable Source View
**Content**: Editable Darklang with session context

```fsharp
// Editing in Session: user-improvements (abc123)
// Editor: 1 | Currently editing: MyApp.User.validate
// Patch: validation-enhancements (3 changes)

let validate (user: User) : Result<Bool, ValidationError> =
  // 🟡 MODIFIED: Improved email validation logic
  let emailValid =
    user.email
    |> MyApp.Utils.Email.validateFormat  // ← Changed: Using utility function
    |> Result.map (fun _ -> true)

  let nameValid =
    if Stdlib.String.length user.name >= 2L
    then Ok(true)
    else Error(ValidationError.InvalidName("Name must be at least 2 characters"))

  // 🟢 ADDED: Age validation
  let ageValid =
    if user.age >= 13L && user.age <= 120L
    then Ok(true)
    else Error(ValidationError.InvalidAge("Age must be between 13 and 120"))

  // 🟡 MODIFIED: Updated to include age validation
  match (emailValid, nameValid, ageValid) with
  | (Ok(_), Ok(_), Ok(_)) -> Ok(true)
  | (Error(e), _, _) -> Error(e)
  | (_, Error(e), _) -> Error(e)
  | (_, _, Error(e)) -> Error(e)
```

**Status Bar**: `📝 Session: user-improvements | Editor 1 | MyApp.User.validate | 3 changes`
**Side Panel**: Shows other editors in session, patch summary, conflicts

### 4. Draft Creation - `dark://draft/session-abc123/MyApp.User.createFromEmail`

**View Type**: Template Editor
**Content**: Pre-filled template for new function

```fsharp
// Creating new function: MyApp.User.createFromEmail
// Session: user-improvements (abc123)
// Template: Function returning Result<User, Error>

let createFromEmail (email: String) : Result<User, Error> =
  // TODO: Implement email-based user creation

  // Step 1: Validate email format
  let emailValidation =
    email
    |> MyApp.User.validate  // Uses existing validation
    |> Result.mapError (fun _ -> Error.InvalidInput("Invalid email format"))

  // Step 2: Check if user already exists
  let existingUser =
    MyApp.Database.User.findByEmail email
    |> fun result ->
        match result with
        | Some(user) -> Error(Error.AlreadyExists("User with this email already exists"))
        | None -> Ok(())

  // Step 3: Create new user
  match (emailValidation, existingUser) with
  | (Ok(_), Ok(_)) ->
      let newUser = User {
        email = email
        name = ""  // TODO: Determine name from email or prompt
        id = Stdlib.UUID.generate ()
        age = 0L   // TODO: Get age from user input
        createdAt = Stdlib.DateTime.now ()
      }
      Ok(newUser)
  | (Error(e), _) -> Error(e)
  | (_, Error(e)) -> Error(e)
```

**Status Bar**: `🆕 Creating: MyApp.User.createFromEmail | Session: user-improvements`

### 5. Version History - `dark://history/MyApp.User.validate`

**View Type**: Custom Webview
**Content**: Interactive timeline

```html
<!DOCTYPE html>
<html>
<head><title>History: MyApp.User.validate</title></head>
<body>
  <div class="history-header">
    <h1>📜 History: MyApp.User.validate</h1>
    <p>15 versions spanning 3 months</p>
  </div>

  <div class="timeline">
    <div class="version current">
      <div class="version-info">
        <span class="hash">hash_validate_v3</span>
        <span class="date">2 hours ago</span>
        <span class="author">Bob Smith</span>
        <span class="status current">CURRENT</span>
      </div>
      <div class="changes">
        <div class="change added">+ Age validation logic</div>
        <div class="change modified">~ Improved email validation</div>
        <div class="change modified">~ Better error messages</div>
      </div>
      <div class="actions">
        <button onclick="viewVersion('hash_validate_v3')">View</button>
        <button onclick="compareWith('hash_validate_v3')">Compare</button>
      </div>
    </div>

    <div class="version">
      <div class="version-info">
        <span class="hash">hash_validate_v2</span>
        <span class="date">1 day ago</span>
        <span class="author">Alice Johnson</span>
      </div>
      <div class="changes">
        <div class="change modified">~ Enhanced email regex</div>
        <div class="change added">+ Name length validation</div>
      </div>
      <div class="actions">
        <button onclick="viewVersion('hash_validate_v2')">View</button>
        <button onclick="compareWith('hash_validate_v2')">Compare</button>
        <button onclick="revertTo('hash_validate_v2')">Revert</button>
      </div>
    </div>

    <div class="version deprecated">
      <div class="version-info">
        <span class="hash">hash_validate_v1</span>
        <span class="date">3 days ago</span>
        <span class="author">Bob Smith</span>
        <span class="status deprecated">DEPRECATED</span>
      </div>
      <div class="changes">
        <div class="change removed">- Simple string contains validation</div>
      </div>
      <div class="deprecation-reason">
        Deprecated: Basic validation insufficient for production
      </div>
      <div class="actions">
        <button onclick="viewVersion('hash_validate_v1')">View</button>
        <button onclick="compareWith('hash_validate_v1')">Compare</button>
      </div>
    </div>
  </div>

  <div class="timeline-controls">
    <button onclick="exportHistory()">Export History</button>
    <button onclick="createBranch()">Create Branch from Version</button>
  </div>
</body>
</html>
```

**Status Bar**: `📜 MyApp.User.validate | 15 versions | Timeline view`

### 6. Session Overview - `dark://session/session-abc123`

**View Type**: Custom Webview
**Content**: Multi-patch session management

```html
<!DOCTYPE html>
<html>
<head><title>Session: user-improvements</title></head>
<body>
  <div class="session-header">
    <h1>🔄 Session: user-improvements</h1>
    <div class="session-info">
      <span>Started: 2 hours ago</span>
      <span>3 active patches</span>
      <span>12 files changed</span>
      <span>5 conflicts detected</span>
    </div>
  </div>

  <div class="session-patches">
    <div class="patch active">
      <h3>validation-enhancements</h3>
      <div class="patch-summary">
        <span>3 functions modified</span>
        <span>1 type added</span>
        <span>Ready to merge</span>
      </div>
      <div class="changes-preview">
        <div class="change">📝 MyApp.User.validate - Enhanced validation logic</div>
        <div class="change">🆕 MyApp.User.ValidationResult - New result type</div>
        <div class="change">📝 MyApp.User.create - Uses new validation</div>
      </div>
      <div class="actions">
        <button onclick="openPatch('validation-enhancements')">Review</button>
        <button onclick="mergePatch('validation-enhancements')">Merge</button>
      </div>
    </div>

    <div class="patch">
      <h3>user-profile-features</h3>
      <div class="patch-summary">
        <span>5 functions added</span>
        <span>2 conflicts</span>
        <span>Needs attention</span>
      </div>
      <div class="conflicts">
        <div class="conflict">⚠️ MyApp.User.update conflicts with validation-enhancements</div>
        <div class="conflict">⚠️ MyApp.User.Profile.render conflicts with ui-improvements</div>
      </div>
      <div class="actions">
        <button onclick="resolveConflicts('user-profile-features')">Resolve Conflicts</button>
        <button onclick="openPatch('user-profile-features')">Review</button>
      </div>
    </div>

    <div class="patch draft">
      <h3>performance-optimizations</h3>
      <div class="patch-summary">
        <span>Draft</span>
        <span>2 functions modified</span>
        <span>Experimental</span>
      </div>
      <div class="changes-preview">
        <div class="change">⚡ MyApp.Database.User.findByEmail - Cached lookup</div>
        <div class="change">⚡ MyApp.User.validate - Lazy validation</div>
      </div>
      <div class="actions">
        <button onclick="continueDraft('performance-optimizations')">Continue</button>
        <button onclick="deleteDraft('performance-optimizations')">Delete</button>
      </div>
    </div>
  </div>

  <div class="session-editors">
    <h3>Active Editors</h3>
    <div class="editor-list">
      <div class="editor current">
        <span class="editor-id">Editor 1</span>
        <span class="file">MyApp.User.validate</span>
        <span class="status">Modified</span>
        <button onclick="switchTo('editor-1')">Switch</button>
      </div>
      <div class="editor">
        <span class="editor-id">Editor 2</span>
        <span class="file">MyApp.User.ValidationResult</span>
        <span class="status">New</span>
        <button onclick="switchTo('editor-2')">Switch</button>
      </div>
    </div>
  </div>

  <div class="session-actions">
    <button onclick="saveSession()">Save Session</button>
    <button onclick="mergeAll()">Merge All Ready Patches</button>
    <button onclick="closeSession()">Close Session</button>
  </div>
</body>
</html>
```

**Status Bar**: `🔄 Session: user-improvements | 3 patches | 5 conflicts`

### 7. Version Comparison - `dark://compare/hash_validate_v2/hash_validate_v3`

**View Type**: Diff Editor
**Content**: Side-by-side comparison

```diff
// Left side: hash_validate_v2 (1 day ago)     // Right side: hash_validate_v3 (current)

let validate (user: User) : Result<Bool, ValidationError> =
  let emailValid =
    user.email
-   |> Stdlib.String.contains "@"              +   |> MyApp.Utils.Email.validateFormat
-   |> fun hasAt ->                            +   |> Result.map (fun _ -> true)
-       if hasAt
-       then
-         user.email
-         |> Stdlib.String.split "@"
-         |> fun parts ->
-             match parts with
-             | [username; domain] ->
-                 if Stdlib.String.length username > 0L
-                 then Ok(true)
-                 else Error(ValidationError.InvalidEmail("Email format invalid"))
-             | _ -> Error(ValidationError.InvalidEmail("Email format invalid"))
-       else Error(ValidationError.InvalidEmail("Email must contain @"))

  let nameValid =
    if Stdlib.String.length user.name >= 2L
    then Ok(true)
    else Error(ValidationError.InvalidName("Name must be at least 2 characters"))

+                                               +  let ageValid =
+                                               +    if user.age >= 13L && user.age <= 120L
+                                               +    then Ok(true)
+                                               +    else Error(ValidationError.InvalidAge("Age must be between 13 and 120"))

- match (emailValid, nameValid) with          + match (emailValid, nameValid, ageValid) with
- | (Ok(_), Ok(_)) -> Ok(true)                + | (Ok(_), Ok(_), Ok(_)) -> Ok(true)
  | (Error(e), _) -> Error(e)                   | (Error(e), _, _) -> Error(e)
- | (_, Error(e)) -> Error(e)                 + | (_, Error(e), _) -> Error(e)
+                                             + | (_, _, Error(e)) -> Error(e)
```

**Status Bar**: `🔄 Comparing: hash_validate_v2 ↔ hash_validate_v3 | 15 changes`

### 8. Instance Browser - `dark://instances`

**View Type**: Custom Webview

```html
<!DOCTYPE html>
<html>
<head><title>Instances</title></head>
<body>
  <div class="instances-header">
    <h1>🏢 Instances</h1>
    <p>Manage your Darklang instances and sync settings</p>
  </div>

  <div class="instances-list">
    <div class="instance primary">
      <div class="instance-info">
        <h3>MyCompany Production</h3>
        <span class="url">https://mycompany.darklang.com</span>
        <span class="status connected">Connected</span>
      </div>
      <div class="sync-info">
        <span>Last sync: 5 minutes ago</span>
        <span>Auto-sync: ON</span>
        <span>Public packages: 1,247</span>
      </div>
      <div class="actions">
        <button onclick="browseInstance('prod')">Browse</button>
        <button onclick="syncNow('prod')">Sync Now</button>
        <button onclick="settings('prod')">Settings</button>
      </div>
    </div>

    <div class="instance">
      <div class="instance-info">
        <h3>Personal Development</h3>
        <span class="url">local://dev-instance</span>
        <span class="status local">Local</span>
      </div>
      <div class="sync-info">
        <span>Sync: Manual only</span>
        <span>Private packages: 23</span>
        <span>Experimental features: ON</span>
      </div>
      <div class="actions">
        <button onclick="browseInstance('dev')">Browse</button>
        <button onclick="pushTo('dev')">Push Changes</button>
        <button onclick="settings('dev')">Settings</button>
      </div>
    </div>
  </div>

  <div class="instance-actions">
    <button onclick="addInstance()">+ Add Instance</button>
    <button onclick="importBackup()">Import Backup</button>
    <button onclick="exportData()">Export Data</button>
  </div>
</body>
</html>
```

### 9. Package Search - `dark://search?query=validate&type=function`

**View Type**: Custom Webview

```html
<!DOCTYPE html>
<html>
<head><title>Search Results</title></head>
<body>
  <div class="search-header">
    <h1>🔍 Search: "validate" (functions)</h1>
    <p>47 results across 12 packages</p>
  </div>

  <div class="search-filters">
    <label><input type="checkbox" checked> Functions</label>
    <label><input type="checkbox"> Types</label>
    <label><input type="checkbox"> Values</label>
    <select>
      <option>All instances</option>
      <option>MyCompany Production</option>
      <option>Local Development</option>
    </select>
  </div>

  <div class="search-results">
    <div class="result">
      <div class="result-header">
        <h3>MyApp.User.validate</h3>
        <span class="type">Function</span>
        <span class="signature">(User) → Result&lt;Bool, ValidationError&gt;</span>
      </div>
      <div class="result-description">
        Validates user data including email format, name length, and age range.
      </div>
      <div class="result-meta">
        <span>MyCompany Production</span>
        <span>Updated 2 hours ago</span>
        <span>Used by 15 functions</span>
      </div>
      <div class="actions">
        <button onclick="openResult('MyApp.User.validate')">View</button>
        <button onclick="editResult('MyApp.User.validate')">Edit</button>
        <button onclick="copyReference('MyApp.User.validate')">Copy</button>
      </div>
    </div>

    <div class="result">
      <div class="result-header">
        <h3>Stdlib.Email.validate</h3>
        <span class="type">Function</span>
        <span class="signature">(String) → Bool</span>
      </div>
      <div class="result-description">
        Standard library email validation using RFC 5322 specification.
      </div>
      <div class="result-meta">
        <span>Darklang Standard Library</span>
        <span>Stable</span>
        <span>Used by 342 functions</span>
      </div>
      <div class="actions">
        <button onclick="openResult('Stdlib.Email.validate')">View</button>
        <button onclick="viewDocs('Stdlib.Email.validate')">Docs</button>
        <button onclick="copyReference('Stdlib.Email.validate')">Copy</button>
      </div>
    </div>
  </div>
</body>
</html>
```

## View Triggering Implementation

### FileSystemProvider Implementation

```typescript
class DarklangFileSystemProvider implements vscode.FileSystemProvider {
  readFile(uri: vscode.Uri): Uint8Array {
    const parsed = this.parseURL(uri.toString())

    switch (parsed.mode) {
      case 'package':
        // Return source code as text
        return this.getPackageSource(parsed.target)

      case 'edit':
        // Return editable source with session context
        return this.getEditableSource(parsed.sessionId, parsed.target)

      case 'draft':
        // Return pre-filled template
        return this.getTemplate(parsed.target, parsed.templateType)

      default:
        // For custom views, return placeholder (webview will handle actual content)
        return new TextEncoder().encode('<!-- Custom view -->')
    }
  }

  // Custom views handled separately via WebviewProvider
}

class DarklangWebviewProvider implements vscode.WebviewViewProvider {
  resolveWebviewView(webviewView: vscode.WebviewView) {
    const url = this.getCurrentURL()
    const parsed = this.parseURL(url)

    switch (parsed.mode) {
      case 'history':
        webviewView.webview.html = this.generateHistoryView(parsed.target)
        break
      case 'session':
        webviewView.webview.html = this.generateSessionView(parsed.sessionId)
        break
      case 'search':
        webviewView.webview.html = this.generateSearchView(parsed.query)
        break
      // etc.
    }
  }
}
```

This system provides rich, contextual views while maintaining consistency with VS Code's interface patterns.
