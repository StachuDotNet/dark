# Extend Existing VS Code Extension with Collaboration Demo Components

## Context

Read the comprehensive design documentation in `notes/` directory which contains:

- **Core Design**: `notes/vscode/VS Code Design*.md` - Architecture and component design
- **Virtual URLs**: `notes/vscode/Virtual-File-URL-Design.md` - URL patterns and content
- **Tree Views**: `notes/vscode/tree-views/*.md` - Detailed tree view specifications
- **Pages**: `notes/vscode/pages/**/*.md` - All page content and layouts
- **Commands**: `notes/vscode/commands/*.md` - Command specifications
- **WebViews**: Multiple files detailing WebView panel designs
- **User Flows**: `notes/user flows/*.md` - Complete workflow documentation
- **Implementation Details**: `notes/unorganized/*.md` and `notes/prompts/*.md`

## Current State Analysis

**Existing Extension Structure** (`vscode-extension/`):
- ✅ Basic package.json with language support and one tree view
- ✅ LSP client integration with Darklang server
- ✅ DarkFS virtual file system provider (darkfs:// scheme) - **REMOVE THIS**
- ✅ ServerBackedTreeDataProvider for package browsing
- ✅ Commands for package lookup and script execution

**Current Views:**
- Single `darklangTreeView` in `darklangViewContainer` showing packages (Darklang, Stachu, Scripts)

## GOAL: Transform into Rich Collaboration Demo

Extend the existing thin extension with **static fake components** implementing the designs specified in the `notes/` directory. Focus on visual impact and workflow demonstration, not functional implementation.

## Priority Tasks (Generate These in Order)

### 1. Extend package.json
- Add all collaboration views as specified in `notes/vscode/tree-views/*.md`
- Add all commands as detailed in `notes/vscode/commands/*.md`
- Use exact view IDs, names, icons, and when conditions from the design docs
- Update LSP client document selector to use "dark" scheme instead of "darkfs"

### 2. Status Bar Manager
- Implement status bar design from `notes/vscode/status-bar.md`
- Show session, patch count, sync status, user, conflicts as specified
- Use static demo data matching the collaboration scenarios

### 3. Tree Data Providers
- **Patches**: Implement design from `notes/vscode/tree-views/patches.md` with expandable operations
- **Sessions**: Implement design from `notes/vscode/tree-views/sessions.md` with transfer capabilities
- **Enhanced Packages**: Update existing provider per `notes/vscode/tree-views/packages.md` to show session modifications
- **Conflicts**: Implement design from `notes/vscode/tree-views/source-control-tree-view.md`
- Use static demo data reflecting the scenarios described in `notes/user flows/*.md`

### 4. WebView Panels
- **Patch Review**: Implement design from `notes/vscode/pages/patches/review-patch.md` and related patch pages
- **Conflict Resolution**: Implement design from `notes/vscode/pages/patches/resolve-conflicts.md`
- **Session Transfer**: Implement session export/import UI as described in session documentation
- Use rich HTML/CSS interfaces with VS Code theme integration
- All content should match the layouts and flows described in the page documentation

### 5. Dark URL Content Provider
- **Remove DarkFS**: Replace the existing file system provider with TextDocumentContentProvider
- **URL Patterns**: Implement all patterns from `notes/vscode/Virtual-File-URL-Design.md`:
  - `dark:/package/Name.Space.item` - Browse/read content
  - `dark:/edit/current-patch/Name.Space.item` - Edit with patch context
  - `dark:/history/Name.Space.item` - Version history
  - `dark:/patch/patchId` - Patch overview (fallback to text)
- **Content Generation**: Use examples from URL design doc and page content specifications
- **Integration**: Update LSP client to use "dark" scheme instead of "darkfs"

### 6. Update Extension Activation
- **Remove DarkFS**: Delete file system provider registration and imports
- **Add Content Provider**: Register TextDocumentContentProvider for "dark" scheme
- **Register Tree Views**: Create all new tree view providers for patches, sessions, conflicts
- **Add Commands**: Register all collaboration commands per `notes/vscode/commands/*.md`
- **Initialize Status Bar**: Set up status bar with demo data
- **Integrate Components**: Wire up all the new components with proper event handling

### 7. Demo Data and Scenarios
Create realistic static data that demonstrates:
- **Multi-user collaboration** as described in `notes/user flows/developer-flows.md`
- **Session workflows** from `notes/user flows/03-developer-flows.md`
- **Conflict scenarios** matching the designs in conflict resolution pages
- **Package modifications** showing the before/after states described in documentation
- **All scenarios** should reflect the comprehensive user flows documented in the notes

## Expected Demo Result

Transform the existing thin VS Code extension into a comprehensive demonstration of Darklang's collaborative development vision. The extension should implement all the designs specified in the `notes/` directory with static demo data.

### Key Deliverable:
A visually impressive VS Code extension that showcases the complete collaboration workflow described in the documentation, ready for demonstration without requiring backend functionality.

### Success Criteria:
- **Visual Fidelity**: All UI components match the designs in the notes
- **Workflow Completeness**: Demonstrates the full developer collaboration cycle
- **Professional Quality**: Looks and feels like a production-ready extension
- **Demo Ready**: Can be used for an effective meeting presentation

### Reference Implementation:
All implementations should closely follow the specifications, layouts, content examples, and user flows documented throughout the `notes/` directory. When in doubt about design details, refer to the comprehensive documentation provided.