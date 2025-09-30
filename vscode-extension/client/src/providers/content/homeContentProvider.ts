import * as vscode from 'vscode';
import { UrlMetadataSystem } from '../urlMetadataSystem';

/**
 * Content provider for the Darklang home page
 * Shows all available URLs as clickable links organized by category
 */
export class HomeContentProvider {
  static getContent(): string {
    return `# 🌑 Darklang VS Code Extension

Welcome to the Darklang development environment! Click any link below to explore different parts of the system.

## 🏢 Sessions
- [Session: feature-auth](dark:///session/feature-auth) - Authentication feature development
- [Session: stdlib-dev](dark:///session/stdlib-dev) - Standard library development
- [Session: clean-start](dark:///session/clean-start) - New project setup
- [Session: conflict-fix](dark:///session/conflict-fix) - Conflict resolution
- [Session: team-collab](dark:///session/team-collab) - Team collaboration

## 🔧 Patches
- [Patch abc123 Overview](dark:///patch/abc123) - Main patch view
- [⚡ abc123 Operations](dark:///patch/abc123/operations) - Patch operations
- [⚠️ abc123 Conflicts](dark:///patch/abc123/conflicts) - Conflict resolution
- [🧪 abc123 Tests](dark:///patch/abc123/tests) - All test results
- [🔬 Individual Test](dark:///patch/abc123/test?name=validation_test) - Specific test details
- [ℹ️ abc123 Metadata](dark:///patch/abc123/meta) - Patch metadata
- [✏️ Edit abc123](dark:///patch/abc123/edit) - Edit patch

### More Patches
- [Patch def456 Overview](dark:///patch/def456) - Another patch
- [⚡ def456 Operations](dark:///patch/def456/operations) - Operations view
- [🧪 def456 Tests](dark:///patch/def456/tests) - Test results

## 📦 Packages
- [Package: Stdlib.List.map](dark:///package/Darklang.Stdlib.List.map) - List mapping function
- [Package: Stdlib.Option](dark:///package/Darklang.Stdlib.Option) - Option type
- [Package: MyApp.User](dark:///package/MyApp.User) - User module
- [Package: MyApp.Auth](dark:///package/MyApp.Auth) - Authentication module
- [Package Browser](dark:///package/Darklang.Stdlib?view=graph) - Visual package browser

## 🖥️ Instances
- [📁 Local Instance](dark:///instance/local) - Local development instance
- [🌐 Remote Instance](dark:///instance/remote) - Remote production instance
- [📦 matter-prod Packages](dark:///instance/matter-prod/packages) - Production packages
- [🏢 matter-prod Sessions](dark:///instance/matter-prod/sessions) - Production sessions
- [📂 Namespace Browser](dark:///instance/matter-prod/namespace?target=MyApp) - Namespace view
- [🔗 Remote Session](dark:///instance/matter-prod/session?target=team-alpha) - Remote session details
- [🏪 Package Registry](dark:///instance/registry) - Public package registry

## ✏️ Editing & Drafts
- [Edit User Module](dark:///edit/current-patch/MyApp.User.validate) - Edit in current patch
- [Edit in Specific Patch](dark:///edit/patch-abc123/MyApp.User.validate) - Edit in patch context
- [📝 Draft: New Function](dark:///draft/MyApp.User.newFunction) - Create new function
- [📝 Draft: New Module](dark:///draft/MyApp.NewModule) - Create new module

## 📜 History & Comparison
- [📜 User Module History](dark:///history/MyApp.User.validate) - Version history
- [📜 Auth Module History](dark:///history/MyApp.Auth) - Module history
- [🔍 Compare Versions](dark:///compare/hash1/hash2) - Version comparison
- [🔍 Compare Current vs Patch](dark:///compare/current/patch-abc123) - Compare with patch
- [🔍 Compare Releases](dark:///compare/v1.2.0/v1.2.1?target=MyApp.User.validate) - Release comparison

## ⚙️ Configuration
- [⚙️ General Config](dark:///config) - General settings
- [⚙️ User Config](dark:///config/user) - User preferences
- [⚙️ Sync Config](dark:///config/sync) - Synchronization settings
- [⚙️ Instance Config](dark:///config/instances) - Instance management

---

## 🎯 Quick Actions

### Development Workflow
1. **Start**: [Create New Session](dark:///session/new-feature) → [Create Patch](dark:///patch/new) → [Edit Code](dark:///edit/current-patch/MyApp.NewFeature)
2. **Review**: [View Operations](dark:///patch/current/operations) → [Run Tests](dark:///patch/current/tests) → [Check Conflicts](dark:///patch/current/conflicts)
3. **Deploy**: [Compare Changes](dark:///compare/current/main) → [View History](dark:///history/MyApp.NewFeature)

### Collaboration
- [Team Session](dark:///session/team-collab) - Join team session
- [Sync Status](dark:///config/sync) - Check synchronization
- [Remote Packages](dark:///instance/matter-prod/packages) - Browse shared packages

### Package Development
- [Browse Stdlib](dark:///package/Darklang.Stdlib?view=graph) - Explore standard library
- [Create Package](dark:///draft/MyPackage.NewModule) - Start new package
- [Package Registry](dark:///instance/registry) - Publish packages

---

*💡 Tip: Use Ctrl+Click (Cmd+Click on Mac) to open links in new tabs. Each URL type has its own badge and color theme for easy identification.*

*🔧 This home page is available at: \`dark:///home\`*
`;
  }

  /**
   * Get all available demo URLs for testing
   */
  static getAllDemoUrls(): Array<{category: string, name: string, url: string, description: string}> {
    return [
      // Sessions
      { category: 'Sessions', name: 'feature-auth', url: 'dark:///session/feature-auth', description: 'Authentication feature development' },
      { category: 'Sessions', name: 'stdlib-dev', url: 'dark:///session/stdlib-dev', description: 'Standard library development' },
      { category: 'Sessions', name: 'clean-start', url: 'dark:///session/clean-start', description: 'New project setup' },

      // Patches
      { category: 'Patches', name: 'abc123 Overview', url: 'dark:///patch/abc123', description: 'Main patch view' },
      { category: 'Patches', name: 'abc123 Operations', url: 'dark:///patch/abc123/operations', description: 'Patch operations' },
      { category: 'Patches', name: 'abc123 Conflicts', url: 'dark:///patch/abc123/conflicts', description: 'Conflict resolution' },
      { category: 'Patches', name: 'abc123 Tests', url: 'dark:///patch/abc123/tests', description: 'All test results' },
      { category: 'Patches', name: 'Validation Test', url: 'dark:///patch/abc123/test?name=validation_test', description: 'Specific test details' },

      // Packages
      { category: 'Packages', name: 'Stdlib.List.map', url: 'dark:///package/Darklang.Stdlib.List.map', description: 'List mapping function' },
      { category: 'Packages', name: 'MyApp.User', url: 'dark:///package/MyApp.User', description: 'User module' },
      { category: 'Packages', name: 'Package Browser', url: 'dark:///package/Darklang.Stdlib?view=graph', description: 'Visual package browser' },

      // Instances
      { category: 'Instances', name: 'Local Instance', url: 'dark:///instance/local', description: 'Local development instance' },
      { category: 'Instances', name: 'matter-prod Packages', url: 'dark:///instance/matter-prod/packages', description: 'Production packages' },
      { category: 'Instances', name: 'Package Registry', url: 'dark:///instance/registry', description: 'Public package registry' },

      // Editing
      { category: 'Editing', name: 'Edit User Module', url: 'dark:///edit/current-patch/MyApp.User.validate', description: 'Edit in current patch' },
      { category: 'Editing', name: 'New Function Draft', url: 'dark:///draft/MyApp.User.newFunction', description: 'Create new function' },

      // History
      { category: 'History', name: 'User Module History', url: 'dark:///history/MyApp.User.validate', description: 'Version history' },
      { category: 'History', name: 'Compare Versions', url: 'dark:///compare/hash1/hash2', description: 'Version comparison' },

      // Config
      { category: 'Config', name: 'General Config', url: 'dark:///config', description: 'General settings' },
      { category: 'Config', name: 'Sync Config', url: 'dark:///config/sync', description: 'Synchronization settings' }
    ];
  }
}