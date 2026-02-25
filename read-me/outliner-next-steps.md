# Outliner: Next Steps

## Separate parent/child collection from Node entity

Currently a `Node` owns its children directly:

```
type Node =
  { id: Int64
    text: String
    children: List<Node>
    collapsed: Bool }
```

This is simple but makes some operations awkward — moving a node means removing it from one parent's `children` list and inserting it into another, recursively searching the tree each time. It also tangles the *content* of a node (text) with the *structure* (children, collapsed).

### Alternative: flat node map + separate structure

```
type NodeContent =
  { id: Int64
    text: String }

type TreeStructure =
  { id: Int64
    children: List<Int64>
    collapsed: Bool }

type Outline =
  { nodes: Dict<Int64, NodeContent>
    structure: Dict<Int64, TreeStructure>
    rootChildren: List<Int64>
    nextId: Int64 }
```

Benefits:
- Node content is independent of position in the tree
- Moving a node is just updating two `children` lists (remove from old parent, insert in new)
- Easier to add metadata to nodes later (notes, tags, timestamps) without touching structure
- Closer to how Dynalist/Workflowy actually model things internally
- Makes persistence more natural (nodes are rows, structure is edges)

Trade-offs:
- More indirection — rendering requires lookups instead of direct recursion
- `flattenVisible` becomes a lookup-based traversal instead of simple recursion
- Slightly more complex code for a small tree

This refactor could happen before or after multi-doc — either way works.


## Multi-document support

### Data model

```
type Document =
  { id: Int64
    title: String
    outline: Outline       // or List<Node> if we keep current model
    nextNodeId: Int64 }

type State =
  { documents: List<Document>
    activeDocId: Int64
    cursor: Int64           // into flattened visible list of active doc
    mode: Mode
    nextDocId: Int64 }
```

`cursor` and `mode` stay as session-level UI state — they apply to whichever doc is active.

### Document picker/switcher

Add a new mode to the outliner (not a separate CLI page):

```
type Mode =
  | Navigate
  | Editing of editText: String * editCursor: Int64
  | DocPicker
```

When in `DocPicker` mode:
- Display a list of documents with their titles
- Up/Down to select, Enter to open, `n` to create new
- Escape to go back to the active document
- Maybe `d` to delete (with confirmation)

Key binding to enter picker: maybe `Ctrl+D` or `/` from navigate mode.

The render function would check `mode` and either render the tree (Navigate/Editing) or render the doc list (DocPicker).

### Persistence

Multi-doc only makes sense with persistence. Options:

1. **JSON file on disk** — simplest. Serialize the full `List<Document>` to `~/.dark-outliner.json` or similar. Save on every mutation (or on exit).

2. **Darklang package DB** — more integrated but heavier. Nodes could be stored as package items. Probably overkill for now.

3. **One file per document** — `~/.dark-outliner/<title>.json`. Nice for version control / sharing but more filesystem management.

Option 1 is the obvious starting point. Auto-save after each keystroke that mutates state.

### Implementation order

1. Add persistence (save/load single doc to a JSON file)
2. Refactor Node → flat model (optional, but cleaner for multi-doc)
3. Add `Document` wrapper with title
4. Add `DocPicker` mode with list rendering and key handling
5. Wire up create/delete/switch operations
6. Update persistence to handle the document list
