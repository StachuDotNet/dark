# Comprehensive Ops Design

## Type-Specific Operations for Package Management

Based on feedback, we need **type-specific operations** rather than generic ones, plus support for **mutability** and **move operations**. Here's the comprehensive set:

## Core F# Type Definitions

```fsharp
module PackageOps =


  type Op =
    // Content Creation
    | AddFunction of hash: PackageHash * name: FullyQualifiedName * func: PackageFunction
    | AddType of hash: PackageHash * name: FullyQualifiedName * typ: PackageType
    | AddValue of hash: PackageHash * name: FullyQualifiedName * value: PackageValue

    // Mutability (for configs, feature flags, etc.)
    | UpdateValue of name: FullyQualifiedName * newValue: Dval * preserveHash: bool
    | UpdateFunction of name: FullyQualifiedName * newFunc: PackageFunction * preserveHash: bool

    // Name Management
    | RepointName of name: FullyQualifiedName * fromHash: PackageHash option * toHash: PackageHash

    // Deprecation (deprecate specific items, not names)
    | DeprecateItem of hash: PackageHash * reason: string option

    // Moves (real refactoring)
    | MoveItem of fromName: FullyQualifiedName * target: MoveTarget * updateReferences: bool

  type Patch =
    { id: string
      author: UserId
      timestamp: Timestamp
      ops: List<Op>
      description: string }
```

## Operation Details

### 1. **AddFunction** - Create new functions
```fsharp
AddFunction(
  "hash_validate_user",
  "MyApp.User.validate",
  { parameters = [{ name = "user"; typ = TypeReference.Known("User") }]
    returnType = TypeReference.Known("Bool")
    body = ... })
```
- Creates immutable function content
- Associates with name immediately
- Hash-based content addressing



## Name-Item Relationship Model

### Bidirectional Association
```fsharp
// Names table - mutable pointers to current content
type NameRecord =
  { name: FullyQualifiedName
    currentHash: PackageHash
    location: string
    createdBy: UserId
    createdAt: Timestamp }

// Items table - immutable content with name memory
type ItemRecord =
  { hash: PackageHash
    content: PackageItem
    originalName: FullyQualifiedName  // Remembers original name
    createdBy: UserId
    createdAt: Timestamp
    deprecatedAt: Timestamp option
    deprecationReason: string option }

// History table - tracks all name-item associations
type NameHistoryRecord =
  { name: FullyQualifiedName
    hash: PackageHash
    activeFrom: Timestamp
    activeTo: Timestamp option  // None = still current
    setBy: UserId }
```