# Demo: Darklang Developer Collaboration

- `auth` - Authentication for collaboration
- `patch` - Manage code patches for collaboration  
- `session` - Manage work sessions
- `sync` - Sync patches with other developers

**Terminal 1 (Stachu):**
```bash
# Authenticate
$ dark auth login stachu
🔑 Logging in as stachu...
✅ Successfully logged in as stachu
Connected to: dev.darklang.com

# Start a work session
$ dark session new --intent "Add List.filterMap to stdlib"
✓ Created session: brave-cat-91
Name: new-work
Intent: Add List.filterMap to stdlib
✓ Switched to session: brave-cat-91

# Create a patch
$ dark patch create "Add List.filterMap for filtering and mapping"
✓ Created patch: abc123
Use 'patch ready' when you're done making changes

# [Simulate: write the function in VS Code]
# [Simulate: test the function]

# Mark patch as ready
$ dark patch ready
Marking current patch as ready...
Validating patch...
✓ Type checks passed
✓ No naming conflicts
✓ Patch marked as ready: patch-abc123
Use 'sync push' to share with other developers

# Share with team
$ dark sync push
Pushing patches to server...
Checking local patches...
  - ready-def456: ready ✅
  - draft-abc123: skipped (not ready)
Uploading to dev.darklang.com...
✅ Pushed 1 patch successfully
```

### Ocean Receives and Uses the Function

**Terminal 2 (Ocean):**
```bash
# Authenticate
$ dark auth login ocean
🔑 Logging in as ocean...
✅ Successfully logged in as ocean
Connected to: dev.darklang.com

# Check for updates
$ dark sync status
Sync Status:
  Server: dev.darklang.com
  User: ocean
  Connection: ✅ Connected
Local patches: 0
Remote patches: 1 new available
  - patch-89ab4e: "Add List.filterMap" by stachu

# Pull the patch
$ dark sync pull
Pulling patches from server...
Found 1 new patch:
  📦 patch-89ab4e: "Add List.filterMap" by stachu
     Created: 5 mins ago
     Changes: +1 function
✓ Patch downloaded for review

# Review the patch
$ dark patch view 89ab4e
Patch: 89ab4e
Author: stachu
Intent: Add List.filterMap for filtering and mapping
Status: ready
Operations: 1
Created: 5 mins ago

Changes:
  + Function Darklang.Stdlib.List.filterMap
    Signature: (List<'a>, 'a -> Option<'b>) -> List<'b>

# Apply the patch
$ dark patch apply 89ab4e
Applying patch 89ab4e...
Validating changes...
✓ Type checks passed
✓ No naming conflicts
✓ Applied: Add List.filterMap for filtering and mapping
✓ Added function: Darklang.Stdlib.List.filterMap

# Use the new function
$ dark eval "List.filterMap [1;2;3] (fn x -> if x > 1 then Some(x*2) else None)"
[4; 6]
```

## Part 3: Session Management (1 minute)

**"Sessions keep track of your work context:"**

```bash
# Show current session
$ dark session current
Current session: brave-cat-91
Name: new-work
Intent: Add List.filterMap to stdlib
Status: active
Started: 2 hours ago
Patches: 1

# List all sessions
$ dark session list
Sessions:
  🟢 brave-cat-91: "Add List.filterMap to stdlib" (now)
  ⏸️ clever-fox-17: "Fix String module edge cases" (1 hour ago)

# Suspend work
$ dark session suspend
Suspending current session: brave-cat-91
✓ Session state saved
Use 'session continue brave-cat' to resume later
```
