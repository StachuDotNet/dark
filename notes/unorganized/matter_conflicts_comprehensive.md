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

