# Virtual File System Test Plan

## Overview
Test the Virtual File System Provider for VS Code that allows editing package items as virtual .dark files.

## Test Cases

### 1. Virtual URI Parsing
- ✅ Test `createVirtualUri("Darklang.Stdlib.List", "map")` returns `"dark://package/Darklang.Stdlib.List/map.dark"`
- ✅ Test `parseVirtualUri("dark://package/Darklang.Stdlib.List/map.dark")` returns `Some ("Darklang.Stdlib.List", "map")`
- ✅ Test invalid URIs return `None`

### 2. LSP Message Routing
- ✅ VFS methods (`darklang/vfs/*`) are routed to VFS handlers
- ✅ Package methods (`darklang/packages/*`) are routed to package handlers
- ✅ Standard LSP methods still work normally

### 3. File System Operations
- **Stat**: Get file/directory info for virtual URIs
- **ReadFile**: Get content of package items as .dark files
- **WriteFile**: Update package items through virtual file writes
- **ReadDirectory**: List packages and package items
- **Watch/Unwatch**: Monitor changes to virtual files

### 4. VS Code Integration
- **File System Provider**: Registered for `dark://` scheme
- **Package Tree View**: Shows packages/modules/items hierarchy  
- **Open Files**: Virtual .dark files open in editor
- **Edit/Save**: Changes sync back to package manager

## Manual Testing Steps

1. **Start LSP Server**
   ```bash
   dark lsp-server
   ```

2. **Open VS Code Extension**
   - Install the Darklang Collaboration extension
   - Open a workspace with .dark files
   - LSP client should connect and register VFS provider

3. **Test Package Tree**
   - Package tree view should show under "Darklang Collaboration"
   - Should list standard library packages
   - Expanding packages should show functions/types

4. **Test Virtual File Editing**
   - Click on a function in package tree
   - Should open as virtual .dark file in editor
   - Edit the file content
   - Save should trigger LSP write request

5. **Test File Operations**
   - Try creating new functions through VS Code
   - Test file watching/notifications
   - Verify changes propagate properly

## Architecture Verification

- ✅ Server-first: 90% logic in F#/Darklang LSP server
- ✅ Minimal JavaScript: Only thin VS Code FileSystemProvider
- ✅ Editor-agnostic: Uses standard LSP protocol extensions
- ✅ Package integration: Virtual files map to package items

## Next Steps

1. Complete missing function implementations in VFS handlers
2. Test end-to-end workflow with real package data
3. Add proper error handling and validation
4. Integrate with existing package manager operations