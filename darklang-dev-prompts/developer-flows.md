# Developer Flows Documentation

## Flow 1: Basic Function Addition

### Context
Developer A (Stachu) needs to add a new `List.filterMap` function and share it with Developer B (Ocean).

### Prerequisites
- Both developers have CLI installed
- Both authenticated (even if hardcoded)
- Connected to shared server

### Step-by-Step Flow

#### Developer A: Creating the Function

**Step 1: Start work session**
```bash
$ dark session new --intent "Add List.filterMap to stdlib"
✓ Created session: helpful-owl-42
✓ Switched to patch: draft-89ab4e
```
*System Response*: Creates new session, auto-creates draft patch

**Step 2: Navigate to target location**
```bash
$ dark nav Darklang.Stdlib.List
/Darklang.Stdlib.List (module)
```
*System Response*: Updates CLI context, LSP aware of location

**Step 3: Create function**
```bash
$ dark add function filterMap
Opening editor for new function...
```
*System Response*: 
- Creates function template in VS Code
- LSP provides type hints and completion
- Function saved to draft patch on each edit

**Step 4: Write implementation**
```darklang
let filterMap (list: List<'a>) (f: 'a -> Option<'b>) : List<'b> =
  list
  |> Stdlib.List.fold [] (fun acc item ->
    match f item with
    | Some value -> Stdlib.List.append acc [value]
    | None -> acc)
```
*System Response*: 
- Real-time type checking
- Syntax highlighting
- Auto-save to patch

**Step 5: Test function**
```bash
$ dark eval "List.filterMap [1;2;3] (fn x -> if x > 1 then Some(x*2) else None)"
[4; 6]
```
*System Response*: Executes with draft patch applied

**Step 6: Finalize patch**
```bash
$ dark patch ready --message "Add List.filterMap for filtering and mapping in one pass"
Validating patch...
✓ Type checks passed
✓ No naming conflicts
✓ Tests passed (0 tests)
⚠ No tests defined for new function
Patch marked as ready: patch-89ab4e
```

**Step 7: Share patch**
```bash
$ dark sync push
Pushing patch-89ab4e to server...
✓ Pushed successfully
Patch available at: https://matter.darklang.com/patches/89ab4e
```

#### Developer B: Receiving the Function

**Step 1: Check for updates**
```bash
$ dark sync status
You have 1 new patch available:
  - patch-89ab4e: "Add List.filterMap" by stachu (2 mins ago)
```

**Step 2: Review patch**
```bash
$ dark patch view 89ab4e
Patch: Add List.filterMap for filtering and mapping in one pass
Author: stachu
Created: 2 mins ago

Changes:
+ Function Darklang.Stdlib.List.filterMap
  Signature: (List<'a>, 'a -> Option<'b>) -> List<'b>
  
View full diff? (y/n): y
```

**Step 3: Test before applying**
```bash
$ dark patch test 89ab4e
Creating temporary environment with patch...
$ dark eval --patch 89ab4e "List.filterMap [1;2;3] (fn x -> Some(x*10))"
[10; 20; 30]
```

**Step 4: Apply patch**
```bash
$ dark patch apply 89ab4e
Applying patch-89ab4e...
✓ Added function: Darklang.Stdlib.List.filterMap
✓ Updated local package cache
```

**Step 5: Use new function**
```bash
$ dark eval "List.filterMap [\"a\"; \"\"; \"b\"] (fn s -> if s == \"\" then None else Some(Stdlib.String.toUpper s))"
["A"; "B"]
```

### Success Criteria
- Function available to both developers
- No merge conflicts
- Function works as expected
- Patch history maintained

### Failure Modes

**Validation Failure**
```bash
$ dark patch ready
✗ Validation failed:
  - Type error in filterMap: Expected List<'b> but got List<'a>
  - Fix errors and try again
```

**Network Failure During Push**
```bash
$ dark sync push
✗ Network error: Cannot reach server
  Patch saved locally. Will retry on next sync.
  Run 'dark sync push' when connected.
```

**Conflicting Changes**
```bash
$ dark patch apply 89ab4e
✗ Conflict: Function List.filterMap already exists (added in patch-77cd3f)
Options:
  1. Keep existing version
  2. Replace with this version
  3. View diff
  4. Create new function with different name
Choice: 
```

## Flow 2: Development Setup

### New Developer Onboarding

**Step 1: Install CLI**
```bash
$ curl https://darklang.com/install | bash
Installing Darklang CLI...
✓ Downloaded CLI binary
✓ Added to PATH
✓ Created config directory: ~/.darklang
```

**Step 2: Initial setup**
```bash
$ dark setup
Welcome to Darklang! Let's get you set up.

Username: ocean
Creating local profile...
✓ Profile created

Connect to server? (y/n): y
Server URL [https://dev.darklang.com]: 
✓ Connected to dev server

Install VS Code extension? (y/n): y
✓ Extension installed
✓ LSP configured
```

**Step 3: Verify installation**
```bash
$ dark version
Darklang CLI v0.1.0-dev
Connected to: dev.darklang.com
User: ocean

$ dark sync status
✓ Connected to server
✓ Packages synchronized
0 pending patches
```

## Flow 3: Conflict Resolution

### Scenario: Same Function, Different Implementations

**Ocean's version:**
```darklang
let filterMap list f =
  list |> Stdlib.List.filter (fn x -> Option.isSome (f x))
       |> Stdlib.List.map (fn x -> Option.unwrap (f x))
```

**Stachu's version:**
```darklang
let filterMap list f =
  Stdlib.List.fold list [] (fn acc item ->
    match f item with
    | Some value -> Stdlib.List.append acc [value]
    | None -> acc)
```

### Resolution Process

**Step 1: Conflict Detection**
```bash
$ dark sync pull
✗ Conflict detected in Darklang.Stdlib.List.filterMap
  Local:  patch-89ab4e by stachu
  Remote: patch-92def3 by ocean
```

**Step 2: Review Options**
```bash
$ dark conflict show
Conflicting patches for Darklang.Stdlib.List.filterMap:

[1] patch-89ab4e (stachu) - uses fold approach
    Performance: O(n), single pass
    Style: Functional, explicit

[2] patch-92def3 (ocean) - uses filter+map
    Performance: O(2n), two passes  
    Style: Compositional, clear intent

$ dark conflict resolve
Options:
  1. Keep local (stachu's version)
  2. Keep remote (ocean's version)
  3. Merge both as filterMap and filterMapOptimized
  4. Open both in editor for manual merge
Choice: 1

✓ Kept local version
✓ Created merge patch: patch-a3fc21
```

## Flow 4: AI-Assisted Development

### Parallel Session Management

**Step 1: AI creates multiple sessions**
```bash
$ dark ai start --task "Implement missing List functions"
AI creating work sessions...
✓ Session: add-list-unique (Remove duplicates)
✓ Session: add-list-windows (Sliding windows)
✓ Session: add-list-permutations (All permutations)

3 sessions created. AI working...
```

**Step 2: Human reviews AI progress**
```bash
$ dark session list --ai
Active AI sessions:
  1. add-list-unique      [████████..] 80% - Testing edge cases
  2. add-list-windows     [██████....] 60% - Implementing core logic  
  3. add-list-permutations [██........] 20% - Designing algorithm

$ dark session join add-list-unique
Joining AI session...
Current work: Testing List.unique with empty list
```

**Step 3: Human assists/corrects**
```bash
$ dark patch view --session add-list-unique
AI's implementation needs adjustment for performance...
[Opens in editor for refinement]
```

## Testing & Validation

### Per-Patch Testing
```bash
# Create test environment with specific patch
$ dark test env --patch 89ab4e
Test environment created with patch-89ab4e applied

# Run existing tests against patch
$ dark test run --all
Running all tests with patch-89ab4e...
✓ 342 tests passed
✗ 2 tests failed:
  - List.tests.testFilterBehavior (uses old API)
  - List.tests.testPerformance (timeout)

# Add new tests for patch
$ dark test add List.filterMap
Creating test file for List.filterMap...
[Opens test editor]
```

### Emergency Recovery
```bash
# Something went wrong, revert everything
$ dark emergency revert --last 1h
This will revert all changes in the last hour:
  - 3 patches
  - 2 sessions
  - 15 file edits
Continue? (y/n): y

✓ Reverted to state from 1 hour ago
✓ Problematic patches moved to quarantine
✓ Sessions marked as failed
```

## UI Mockups

### CLI Status Display
```
┌─────────────────────────────────────────┐
│ Darklang CLI - ocean@dev.darklang.com  │
│ Session: helpful-owl-42                │
│ Patch: draft-89ab4e (3 ops)           │
│ Location: /Darklang.Stdlib.List       │
└─────────────────────────────────────────┘
> dark █
```

### VS Code Status Bar
```
[Darklang: Connected] [Patch: draft-89ab4e] [Session: helpful-owl] [↑1 ↓0]
```

### Patch Review Interface (VS Code Webview)
```
┌──────────────────────────────────────────────┐
│ Patch Review: Add List.filterMap            │
├──────────────────────────────────────────────┤
│ Author: stachu                              │
│ Created: 2 mins ago                         │
│ Status: Ready                               │
├──────────────────────────────────────────────┤
│ Changes:                                    │
│ + Darklang.Stdlib.List.filterMap           │
│   Filters and maps in single pass          │
├──────────────────────────────────────────────┤
│ [Test Patch] [Apply] [Request Changes]      │
└──────────────────────────────────────────────┘
```

## Next Steps

These flows provide the foundation for:
1. Basic collaboration between two developers
2. Clear setup process
3. Conflict resolution patterns
4. Future AI integration

Ready for implementation based on priority.