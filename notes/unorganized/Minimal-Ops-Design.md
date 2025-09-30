# Minimal Ops Design

## The Three Essential Operations

Based on your guidance, here are the **minimal, representative** Ops needed:

### 1. **AddContent** - Adding immutable content structures
```fsharp
| AddContent of hash: string * content: PackageContent
```
- Creates new function, type, or value in content-addressable storage
- Content never changes once created (immutable)
- Hash uniquely identifies the content

### 2. **RepointName** - Repointing fully qualified names to package items
```fsharp
| RepointName of name: string * fromHash: string option * toHash: string
```
- Changes what a fully qualified name points to: `MyApp.User.validate` → hash_abc123
- `fromHash` can be None for new names
- This is how "editing" works - point name to new content

### 3. **DeprecateName** - Deprecating things
```fsharp
| DeprecateName of name: string * reason: string option
```
- Marks a name as deprecated (still points to content, but marked)
- Optional reason for deprecation
- Name still exists but shouldn't be used in new code

## Why These Three Are Sufficient

### **AddContent** handles all creation:
- New functions: `AddContent(hash_abc, FunctionContent(...))`
- New types: `AddContent(hash_def, TypeContent(...))`
- New values: `AddContent(hash_ghi, ValueContent(...))`

### **RepointName** handles all updates/renames:
- Edit function: `RepointName("MyApp.User.validate", Some(old_hash), new_hash)`
- Rename: `RepointName("MyApp.User.newName", Some(old_hash), same_hash)` + `DeprecateName("MyApp.User.oldName")`
- New name: `RepointName("MyApp.User.brand_new", None, existing_hash)`

### **DeprecateName** handles cleanup:
- Remove from public API: `DeprecateName("MyApp.Internal.helper", Some("moved to MyApp.Utils.helper"))`
- Mark as obsolete: `DeprecateName("MyApp.Legacy.oldWay", Some("use MyApp.User.newWay instead"))`

## Patch Example

```fsharp
Patch "user-validation-improvements" [
  // Add new content
  AddContent("hash_new_validate", FunctionContent(...))
  // Point name to new content
  RepointName("MyApp.User.validate", Some("hash_old_validate"), "hash_new_validate")
  // Create entirely new function
  AddContent("hash_create_account", FunctionContent(...))
  RepointName("MyApp.User.createAccount", None, "hash_create_account")
  // Deprecate old function
  DeprecateName("MyApp.Legacy.oldFunction", Some("replaced by MyApp.User.createAccount"))
]
```