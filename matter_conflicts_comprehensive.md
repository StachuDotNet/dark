# Comprehensive Conflict Taxonomy for Matter

Matter's content-addressable architecture creates new types of conflicts that traditional source control doesn't handle. Here's the complete taxonomy:

---

## 1. Name Pointer Conflicts

### Concurrent Name Updates
**Scenario**: Two sessions update the same name to point to different content hashes
```
Session A: UpdateNamePointer(Stdlib.String.reverse, hash_abc123)
Session B: UpdateNamePointer(Stdlib.String.reverse, hash_def456)
```
**Resolution**: Show both implementations, let developer choose or merge

### Name Existence Conflicts  
**Scenario**: One session creates a name while another deletes it
```
Session A: DeleteName(Utils.helper)
Session B: UpdateNamePointer(Utils.helper, hash_new)
```
**Resolution**: Restore name with new content, or confirm deletion

### Name Movement Conflicts
**Scenario**: One session moves a name while another updates its content
```
Session A: MoveName(Utils.old → Utils.new)  
Session B: UpdateNamePointer(Utils.old, hash_updated)
```
**Resolution**: Apply content update to new location, or keep at old location

---

## 2. Content Dependency Conflicts

### Function Signature Changes
**Scenario**: Function A calls function B, but B's signature changes incompatibly
```
// Session A updates caller
function processUser(user: User): Result = 
  validateEmail(user.email)  // Still expects validateEmail(String)

// Session B changes callee  
function validateEmail(email: String, options: ValidationOptions): Result
```
**Resolution**: Auto-update call sites, or require explicit migration

### Type Definition Conflicts
**Scenario**: Multiple incompatible changes to the same type
```
// Session A adds field
type User = { name: String; email: String; age: Int64 }

// Session B changes field type  
type User = { name: String; email: EmailAddress }
```
**Resolution**: Create new type version, migrate data, or manual merge

### Breaking API Changes
**Scenario**: Public function changes in way that breaks dependents
```
// Many packages depend on this function signature
function parseJSON(input: String): JSON

// Session changes return type
function parseJSON(input: String): Result<JSON, ParseError>
```
**Resolution**: Deprecation workflow, or parallel function versions

---

## 3. Patch Dependency Conflicts

### Conflicting Prerequisites  
**Scenario**: Patch A requires state from Patch B, but B conflicts with current base
```
Patch A: "Add user authentication" (requires database schema from Patch B)
Patch B: "Add user table" (conflicts with existing schema)
Current: Already has incompatible user table
```
**Resolution**: Merge/rebase patches, or find alternative dependency path

### Circular Dependencies
**Scenario**: Patches depend on each other creating a cycle
```
Patch A: "Add user profiles" (imports from Patch B)
Patch B: "Add profile validation" (imports from Patch A)  
```
**Resolution**: Merge patches, or break circular dependency

### Missing Dependencies
**Scenario**: Patch depends on content that doesn't exist in target context
```
Patch: "Update user dashboard" 
Dependency: Requires UserService.getCurrentUser (hash_xyz)
Target: UserService.getCurrentUser doesn't exist or is different version
```
**Resolution**: Pull missing dependencies, or adapt patch to available version

---

## 4. Session State Conflicts

### Overlapping Session Changes
**Scenario**: Two sessions make overlapping changes to related code
```
Session A: Working on user authentication (modifies login.dark, auth.dark)
Session B: Working on user profiles (modifies login.dark, profile.dark)
```
**Resolution**: Merge overlapping changes, coordinate session boundaries

### Session Base Conflicts
**Scenario**: Session based on outdated state that conflicts with current
```
Session: Created from base_patch_abc (2 days ago)
Current: Has moved to base_patch_xyz with breaking changes
Session work: No longer compatible with current state
```
**Resolution**: Rebase session on current state, or maintain parallel version

### Session Merge Timing
**Scenario**: Session A merges while Session B is still working on conflicting changes
```
Session A: Merges changes to core authentication system
Session B: Still developing features that depend on old authentication
```
**Resolution**: Coordinate merge timing, or handle adaptation automatically

---

## 5. Content Hash Conflicts (Hash Collisions)

### True Hash Collisions
**Scenario**: Two different pieces of content produce the same hash (cryptographic collision)
```
Function A: Different implementation
Function B: Different implementation  
Both produce: hash_abc123def
```
**Resolution**: Use stronger hash algorithm, add content verification

### Content Ambiguity
**Scenario**: Same content hash represents different logical meanings
```
Hash abc123: String.reverse function
Hash abc123: Also used for User.reverse method (coincidentally same implementation)
```
**Resolution**: Namespace content hashes, add metadata disambiguation

---

## 6. Data Migration Conflicts

### Competing Migrations
**Scenario**: Multiple sessions try to migrate the same data structure
```
Session A: Migrate User table to add "age" field
Session B: Migrate User table to split "name" into "first_name"/"last_name"
Both: Try to apply migrations to same base data
```
**Resolution**: Compose migrations, or require sequential application

### Non-Commutative Operations
**Scenario**: Migration operations that don't work in different orders
```
Migration A: Rename field "email" → "email_address"
Migration B: Add validation to "email" field
Order matters: B then A vs A then B produce different results
```
**Resolution**: Define migration operation ordering, or make operations commutative

### Data Loss Conflicts
**Scenario**: One migration removes data that another needs
```
Migration A: Drop "temp_field" column from User
Migration B: Use "temp_field" to populate new "permanent_field"
```
**Resolution**: Coordinate migrations, preserve intermediate data, or warn about data dependencies

---

## 7. Visibility & Access Conflicts

### Permission Changes
**Scenario**: One session makes function private while another expects public access
```
Session A: SetVisibility(Utils.helper, Private)
Session B: Imports and uses Utils.helper (expects Public)
```
**Resolution**: Coordinate visibility changes, or maintain multiple visibility levels

### Module Reorganization
**Scenario**: Moving functions between modules while others reference them
```
Session A: MoveName(Old.Module.function → New.Module.function)
Session B: Still importing Old.Module.function
```
**Resolution**: Update all references, or maintain aliases during transition

---

## 8. Execution Environment Conflicts

### Configuration Conflicts
**Scenario**: Sessions assume different configuration values
```
Session A: Develops against staging database config
Session B: Develops against local database config
Merge: Code works in isolation but not together
```
**Resolution**: Environment-aware configuration, or standardize development environments

### Runtime Version Conflicts
**Scenario**: Code developed against different Darklang runtime versions
```
Session A: Uses new Stdlib.List.partition function
Session B: Based on older runtime without that function
```
**Resolution**: Version compatibility checks, or parallel runtime support

---

## Resolution Strategies by Conflict Type

### Automatic Resolution
- **Name pointer conflicts**: Auto-merge if changes don't conflict semantically
- **Missing dependencies**: Auto-pull compatible versions
- **Simple migrations**: Apply if operations are commutative

### Manual Resolution Required
- **Breaking API changes**: Developer must choose migration strategy
- **Data migration conflicts**: Developer must coordinate migration order
- **Complex content dependencies**: Developer must update call sites

### Prevention Strategies
- **Dependency analysis**: Warn before making breaking changes
- **Session coordination**: Show what others are working on
- **Automated testing**: Catch conflicts before they propagate
- **Gradual rollout**: Deploy changes progressively with rollback capability

---

## Sunday Meeting Discussion Points

1. **Which conflicts are most critical to handle first?**
2. **How do we make conflict resolution feel natural, not scary?**
3. **What automatic resolution should we attempt vs always require human decision?**
4. **How do we prevent conflicts rather than just resolve them?**

The goal: Make Matter's conflict handling so good that it becomes a competitive advantage, not a barrier to adoption.