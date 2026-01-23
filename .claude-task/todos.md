# Bring Back WASM - Implementation Plan

## Goal
Restore tree-sitter WASM compilation to enable browser-based Darklang parsing in the VS Code extension.

## Context
- The full Blazor/F# WASM project was intentionally removed during the CLI-focused cleanup (commit 700bd71f6)
- However, the tree-sitter parser WASM build was also disabled (commented out in scripts/build/build-parser:107-108)
- The VS Code extension could benefit from having tree-sitter WASM for client-side parsing and syntax highlighting
- The `web-tree-sitter` package is already listed as a devDependency in tree-sitter-darklang/package.json

## Tasks

### Phase 1: Enable tree-sitter WASM Build
- [ ] Uncomment WASM build steps in scripts/build/build-parser (lines 107-108)
- [ ] Add tree-sitter WASM compilation command to build-parser script
- [ ] Verify tree-sitter-darklang.wasm is generated successfully
- [ ] Test that the WASM file is copied to backend/src/LibTreeSitter/lib correctly

### Phase 2: Integrate WASM into VS Code Extension
- [ ] Copy tree-sitter WASM files to vscode-extension directory during build
- [ ] Add web-tree-sitter loading code to VS Code extension
- [ ] Create TypeScript wrapper for tree-sitter WASM parser
- [ ] Update vscode-extension/package.json if needed to include WASM files

### Phase 3: Update Build Scripts
- [ ] Update scripts/build/compile to copy WASM files to extension (if needed)
- [ ] Ensure WASM files are included in VS Code extension packaging (.vscodeignore)
- [ ] Update any gitignore rules if needed for WASM artifacts

### Phase 4: Documentation
- [ ] Update tree-sitter-darklang/README.md to document WASM build
- [ ] Add notes about WASM usage in VS Code extension README
- [ ] Document any new npm scripts or build commands

### Phase 5: Testing
- [ ] Test tree-sitter WASM build in isolation
- [ ] Test VS Code extension can load and use WASM parser
- [ ] Verify syntax highlighting works with WASM parser
- [ ] Test on different platforms (if cross-platform WASM needed)

### Phase 6: Cleanup
- [ ] Remove any temporary debugging code
- [ ] Ensure builds are clean and reproducible
- [ ] Run any existing tests to ensure nothing broke

## Success Criteria
- [ ] `tree-sitter-darklang.wasm` file is generated during build
- [ ] WASM parser can be loaded in VS Code extension
- [ ] Darklang code can be parsed client-side in the extension
- [ ] No regression in existing functionality
- [ ] Build process is documented and repeatable

## Notes
- This does NOT restore the full Blazor/F# WASM project (that was intentionally removed)
- This only restores the tree-sitter WASM parser for client-side parsing
- The tree-sitter WASM is much smaller and simpler than the full F# WASM runtime
