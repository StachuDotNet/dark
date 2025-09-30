# Darklang SCM+PM Implementation - Concrete Artifacts


## 🎯 Key Architectural Breakthrough

**The Insight**: Separate "what code is" from "where code lives"

**Before**: `UpdateFunction(uuid, newDefinition)` - mutates in place
**After**: `AddFunctionContent(hash, definition)` + `UpdateNamePointer(location, newHash)`

**Benefits**:
- True immutability: every version preserved forever
- Easy moves/renames: just update name pointers  
- Perfect deduplication: same content = same hash
- Natural aliasing: multiple names → same content
- Version history via Op sequence, not DB structure
