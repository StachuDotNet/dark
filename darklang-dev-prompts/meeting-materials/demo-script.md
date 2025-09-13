# Demo Script: Darklang Developer Collaboration

## Setup (Before Demo)
- Two terminal windows (Stachu and Ocean)
- VS Code open with Darklang extension
- Local SQLite databases initialized
- Simple HTTP server running for sync

## Demo Flow

### Part 1: Current State (1 minute)
```bash
# Show current CLI capabilities
$ dark --version
Darklang CLI v0.1.0-dev

$ dark nav Darklang.Stdlib.List
/Darklang.Stdlib.List (module)

$ dark ls
Functions: map, filter, fold, length...
```

**Narration**: "This is what we have today - navigation and viewing, but no way to share changes."

### Part 2: Authentication & Sessions (2 minutes)

**Terminal 1 (Stachu):**
```bash
$ dark auth login stachu
✓ Logged in as stachu

$ dark session new --intent "Add List.filterMap to stdlib"
✓ Created session: helpful-owl-42
✓ Working on patch: draft-89ab4e
```

**Terminal 2 (Ocean):**
```bash
$ dark auth login ocean  
✓ Logged in as ocean

$ dark sync status
No new patches available
```

**Narration**: "Simple auth system and session management. Each developer has their own context."

### Part 3: Creating a Function (3 minutes)

**Terminal 1 (Stachu):**
```bash
$ dark add function filterMap
Opening editor...
```

**VS Code:**
```darklang
let filterMap (list: List<'a>) (f: 'a -> Option<'b>) : List<'b> =
  list
  |> Stdlib.List.fold [] (fun acc item ->
    match f item with
    | Some value -> Stdlib.List.append acc [value]
    | None -> acc)
```

```bash
$ dark eval "List.filterMap [1;2;3] (fn x -> if x > 1 then Some(x*2) else None)"
[4; 6]

$ dark patch ready --message "Add List.filterMap for filtering and mapping"
✓ Patch validated and marked ready
```

**Narration**: "Create function, test it locally, mark patch as ready."

### Part 4: Sharing the Patch (2 minutes)

**Terminal 1 (Stachu):**
```bash
$ dark sync push
Pushing patch-89ab4e...
✓ Pushed to server

$ dark patch list
patch-89ab4e: "Add List.filterMap" (ready) - just now
```

**Terminal 2 (Ocean):**
```bash
$ dark sync status
1 new patch available:
  patch-89ab4e: "Add List.filterMap" by stachu

$ dark patch view 89ab4e
Patch: Add List.filterMap for filtering and mapping
Author: stachu
Changes:
  + Function Darklang.Stdlib.List.filterMap

$ dark patch apply 89ab4e
✓ Applied patch-89ab4e
✓ Added function: Darklang.Stdlib.List.filterMap
```

**Narration**: "Simple sync protocol. Ocean can review and apply the patch."

### Part 5: Using Shared Code (1 minute)

**Terminal 2 (Ocean):**
```bash
$ dark eval "List.filterMap [\"hello\", \"\", \"world\"] (fn s -> if s == \"\" then None else Some(Stdlib.String.toUpper s))"
["HELLO"; "WORLD"]
```

**Both Terminals:**
```bash
$ dark ls Darklang.Stdlib.List | grep filterMap
  filterMap - Filter and map in one pass
```

**Narration**: "Both developers now have the function. Collaboration achieved!"

### Part 6: Handling Conflicts (Bonus - 2 minutes)

**If time allows, show conflict scenario:**

**Terminal 2 (Ocean creates different implementation):**
```bash
$ dark session new --intent "Optimize List.filterMap"
$ dark edit function filterMap
# Makes different implementation

$ dark sync push
✗ Conflict detected: List.filterMap modified in patch-89ab4e
Options:
  1. Keep your version
  2. Keep stachu's version  
  3. View diff
Choice: 3

# Shows diff between versions
```

**Narration**: "Basic conflict detection prevents accidental overwrites."

## Fallback Options

### If Live Demo Fails

**Option 1: Pre-recorded Terminal**
- Use asciinema recording of the flow
- Explain what's happening at each step

**Option 2: Static Mockups**
- Show screenshots of each step
- Walk through the conceptual flow

**Option 3: Pseudo-Demo**
```bash
# Explain what would happen
$ dark patch create  # Would create a new patch
$ dark sync push     # Would send to server
# etc.
```

## Key Points to Emphasize

1. **Simplicity**: No complex git commands, just patches
2. **Integration**: Works with existing CLI and package system
3. **Extensibility**: Foundation for future features
4. **Developer Focus**: Solves real collaboration problem

## Q&A Preparation

**Q: Why not just use git?**
A: Darklang's function-level packages and live deployment model don't map well to file-based version control. Our approach is package-native.

**Q: How does this scale?**
A: Start with 2 developers, expand to teams. The patch model scales better than branches for our use case.

**Q: What about conflicts?**
A: Basic detection now, smart resolution later. Most patches won't conflict (different functions).

**Q: Timeline to production?**
A: 2 weeks for basic system, 1 month for polish, 2 months for community readiness.

## Closing
"This is the foundation that enables Darklang developers to actually collaborate. From here we can build the full vision - AI integration, instant deployment, trace-driven development - but first we need developers to be able to share code."