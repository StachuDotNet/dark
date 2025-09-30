import * as vscode from "vscode";
import { UrlPatternRouter, ParsedUrl } from "./urlPatternRouter";
import { PackageContentProvider } from "./content/packageContentProvider";
import { PatchContentProvider } from "./content/patchContentProvider";
import { HistoryContentProvider } from "./content/historyContentProvider";
import { CompareContentProvider } from "./content/compareContentProvider";
import { EditContentProvider } from "./content/editContentProvider";
import { HomeContentProvider } from "./content/homeContentProvider";

/**
 * Comprehensive content provider for all dark:// URLs
 *
 * Implements all documented URL patterns from notes/vscode/Virtual-File-URL-Design.md
 * and notes/vscode/pages/ specifications.
 *
 * URL Pattern Support:
 * - dark://package/Name.Space.item[?view=type] - Package browsing
 * - dark://edit/context/Name.Space.item - Editing in patch context
 * - dark://draft/Name.Space.newItem - Creating new items
 * - dark://patch/patchId[/subview] - Patch management
 * - dark://history/Name.Space.item[?view=type] - Version history
 * - dark://compare/v1/v2[?target=item] - Version comparison
 * - dark://session/sessionId[/action] - Session management
 * - dark://config[/section] - Configuration
 * - dark://instance/type - Instance management
 */
export class ComprehensiveDarkContentProvider implements vscode.TextDocumentContentProvider {
  private _onDidChange = new vscode.EventEmitter<vscode.Uri>();
  readonly onDidChange = this._onDidChange.event;

  constructor() {
    console.log('ComprehensiveDarkContentProvider initialized with support for all URL patterns');
  }

  /**
   * Provide content for any dark:// URL
   */
  provideTextDocumentContent(uri: vscode.Uri): string {
    try {
      const url = uri.toString();
      console.log(`Providing content for: ${url}`);

      // Handle special home page
      if (uri.path === '/home' || uri.path === '/' || uri.path === '') {
        return HomeContentProvider.getContent();
      }

      const parsedUrl = UrlPatternRouter.parseUrl(url);

      if (!parsedUrl) {
        return this.getErrorContent(url, "Invalid URL format");
      }

      return this.getContentForParsedUrl(parsedUrl);

    } catch (error) {
      console.error('Error providing content:', error);
      return this.getErrorContent(uri.toString(), `Error: ${error}`);
    }
  }

  /**
   * Route parsed URL to appropriate content provider
   */
  private getContentForParsedUrl(parsedUrl: ParsedUrl): string {
    switch (parsedUrl.mode) {
      case 'package':
        return PackageContentProvider.getContent(parsedUrl);

      case 'edit':
        return EditContentProvider.getContent(parsedUrl);

      case 'draft':
        return this.getDraftContent(parsedUrl);

      case 'patch':
        return PatchContentProvider.getContent(parsedUrl);

      case 'history':
        return HistoryContentProvider.getContent(parsedUrl);

      case 'compare':
        return CompareContentProvider.getContent(parsedUrl);

      case 'session':
        return this.getSessionContent(parsedUrl);

      case 'config':
        return this.getConfigContent(parsedUrl);

      case 'instance':
        return this.getInstanceContent(parsedUrl);

      default:
        return this.getErrorContent(
          `dark:///${parsedUrl.mode}/${parsedUrl.context || ''}/${parsedUrl.target || ''}`,
          `Unsupported URL mode: ${parsedUrl.mode}`
        );
    }
  }

  /**
   * Content for draft URLs: dark://draft/Name.Space.newItem
   */
  private getDraftContent(parsedUrl: ParsedUrl): string {
    const { target } = parsedUrl;

    return `# Draft: ${target || 'New Item'}

## Create New Item

You are creating a new item: **${target || 'Unnamed'}**

### Template Selection

Choose a template for your new item:

#### Function Template
\`\`\`fsharp
let ${target?.split('.').pop() || 'newFunction'} (param: Type): ReturnType =
  // TODO: Implement function
  failwith "Not implemented"
\`\`\`

#### Type Template
\`\`\`fsharp
type ${target?.split('.').pop() || 'NewType'} = {
  // TODO: Define fields
  placeholder: String
}
\`\`\`

#### Constant Template
\`\`\`fsharp
let ${target?.split('.').pop() || 'newConstant'}: Type =
  // TODO: Define value
  defaultValue
\`\`\`

## Draft Actions

- [📝 Start with Function](command:darklang.draft.function?${target})
- [🏗️ Start with Type](command:darklang.draft.type?${target})
- [📊 Start with Constant](command:darklang.draft.constant?${target})
- [🚀 Create from Scratch](command:darklang.draft.blank?${target})

## Context

Creating ${target || 'new item'} will:
1. Add the item to your current patch
2. Open in edit mode for implementation
3. Generate basic tests and documentation

[🔙 Cancel](command:darklang.draft.cancel)
[▶️ Continue to Edit](dark://edit/current-patch/${target})

*Draft mode - Item will be created when you start editing.*`;
  }

  /**
   * Content for session URLs: dark://session/sessionId[/action]
   */
  private getSessionContent(parsedUrl: ParsedUrl): string {
    const { context: sessionId, view: action } = parsedUrl;

    if (!sessionId) {
      return this.getSessionListContent();
    }

    switch (action) {
      case 'export':
        return this.getSessionExportContent(sessionId);
      case 'import':
        return this.getSessionImportContent(sessionId);
      case 'transfer':
        return this.getSessionTransferContent(sessionId);
      default:
        return this.getSessionOverviewContent(sessionId);
    }
  }

  private getSessionOverviewContent(sessionId: string): string {
    return `# Session: ${sessionId}

## Session Information

| Field | Value |
|-------|-------|
| **ID** | ${sessionId} |
| **Name** | ${sessionId === 'feature-auth' ? 'Authentication Features' : sessionId} |
| **Status** | ${sessionId === 'feature-auth' ? 'Active' : 'Available'} |
| **Created** | 2024-01-15 10:00:00 |
| **Last Activity** | 2024-01-15 16:45:00 |

## Session Contents

### Active Patches
- [abc123](dark://patch/abc123) - Add user validation (Draft)
- [def456](dark://patch/def456) - Fix email validation (Ready)

### Session State
- **Modified Items:** 5
- **New Items:** 2
- **Pending Changes:** 3 operations
- **Test Status:** 95% passing

### Collaboration
- **Owner:** stachu
- **Shared With:** alice, bob
- **Access Level:** Read/Write

## Session Actions

- [🎯 Switch to Session](command:darklang.session.switch?${sessionId})
- [📤 Export Session](dark://session/${sessionId}/export)
- [🔄 Transfer Session](dark://session/${sessionId}/transfer)
- [⏸️ Suspend Session](command:darklang.session.suspend?${sessionId})
- [🛑 End Session](command:darklang.session.end?${sessionId})

## Recent Activity

- 16:45 - Modified MyApp.User.validate
- 16:30 - Created ValidationError type
- 16:15 - Updated MyApp.User.create
- 15:45 - Added validation tests

[📊 View Session Statistics](dark://session/${sessionId}/stats)
[🗂️ Browse Session Files](dark://session/${sessionId}/files)`;
  }

  private getSessionExportContent(sessionId: string): string {
    return `# Export Session: ${sessionId}

## Export Options

### Quick Export
Export the entire session state for backup or sharing:

- [📦 Export as Bundle](command:darklang.session.export.bundle?${sessionId})
- [📝 Export as Patch Set](command:darklang.session.export.patches?${sessionId})
- [🗃️ Export State Only](command:darklang.session.export.state?${sessionId})

### Selective Export
Choose specific items to export:

#### Patches
- ☑️ abc123 - Add user validation
- ☑️ def456 - Fix email validation
- ☐ ghi789 - Performance improvements

#### Modified Items
- ☑️ MyApp.User.validate (new)
- ☑️ MyApp.User.create (modified)
- ☐ MyApp.Auth.hashPassword (modified)

### Export Formats
- **Darklang Bundle** (.dlb) - Complete session with all context
- **Patch Archive** (.dpa) - Just the patches and changes
- **JSON Export** (.json) - Metadata and references only

## Export Settings

- **Include Tests:** ☑️ Yes
- **Include History:** ☐ No
- **Compress:** ☑️ Yes
- **Encrypt:** ☐ No

[📥 Start Export](command:darklang.session.export.start?${sessionId})
[🔙 Back to Session](dark://session/${sessionId})`;
  }

  private getSessionImportContent(sessionId: string): string {
    return `# Import to Session: ${sessionId}

## Import Source

### File Import
- [📁 Browse File](command:darklang.session.import.file)
- [📋 Paste Content](command:darklang.session.import.paste)
- [🌐 Import from URL](command:darklang.session.import.url)

### Remote Import
- [👥 From Team Member](command:darklang.session.import.team)
- [☁️ From Cloud Storage](command:darklang.session.import.cloud)
- [🔄 Sync from Remote](command:darklang.session.import.sync)

## Import Options

### Conflict Resolution
- **Merge Strategy:** Smart merge
- **On Conflicts:** Prompt for resolution
- **Backup Current:** ☑️ Yes

### Import Scope
- **Import Patches:** ☑️ Yes
- **Import State:** ☑️ Yes
- **Import Tests:** ☑️ Yes
- **Import History:** ☐ No

## Recent Imports

- 2024-01-14 - team-session-alpha (alice)
- 2024-01-13 - backup-session-20240113 (stachu)
- 2024-01-12 - feature-branch-merge (bob)

[📤 Start Import](command:darklang.session.import.start)
[🔙 Back to Session](dark://session/${sessionId})`;
  }

  private getSessionTransferContent(sessionId: string): string {
    return `# Transfer Session: ${sessionId}

## Transfer Options

### Device Transfer
Move this session to another development environment:

- [💻 Transfer to Local Machine](command:darklang.session.transfer.local)
- [☁️ Transfer to Cloud Instance](command:darklang.session.transfer.cloud)
- [🖥️ Transfer to Remote Server](command:darklang.session.transfer.remote)

### Team Transfer
Share session ownership with team members:

- [👤 Transfer to alice](command:darklang.session.transfer.user?alice)
- [👤 Transfer to bob](command:darklang.session.transfer.user?bob)
- [👥 Transfer to Team](command:darklang.session.transfer.team)

## Transfer Settings

### Session State
- **Include Working Changes:** ☑️ Yes
- **Include Draft Patches:** ☑️ Yes
- **Include Test Results:** ☐ No
- **Include History:** ☐ No

### Security
- **Encrypt Transfer:** ☑️ Yes
- **Require Confirmation:** ☑️ Yes
- **Backup Before Transfer:** ☑️ Yes

## Transfer Status

Current session can be transferred:
- ✅ No pending operations
- ✅ All changes saved
- ✅ No active conflicts
- ✅ Ready for transfer

[🚀 Start Transfer](command:darklang.session.transfer.start)
[🔙 Back to Session](dark://session/${sessionId})`;
  }

  private getSessionListContent(): string {
    return `# Sessions Overview

## Active Sessions

### Current Session
- **feature-auth** (Active) - Authentication features development

### Available Sessions
- **team-session-alpha** (Shared) - Team collaboration session
- **performance-optimizations** (Suspended) - Performance improvements
- **ui-redesign** (Draft) - User interface updates

## Quick Actions

- [🆕 Create New Session](command:darklang.session.new)
- [📥 Import Session](command:darklang.session.import)
- [🔄 Sync Sessions](command:darklang.session.sync)

## Session Management

### Recent Activity
- 16:45 - Switched to feature-auth
- 15:30 - Created performance-optimizations
- 14:20 - Shared team-session-alpha

### Session Templates
- [🔐 Authentication Template](command:darklang.session.template.auth)
- [🎨 UI Development Template](command:darklang.session.template.ui)
- [⚡ Performance Template](command:darklang.session.template.perf)

[📊 Session Statistics](dark://session/stats)
[⚙️ Session Settings](dark://config/sessions)`;
  }

  /**
   * Content for config URLs: dark://config[/section]
   */
  private getConfigContent(parsedUrl: ParsedUrl): string {
    const { context: section } = parsedUrl;

    switch (section) {
      case 'user':
        return this.getUserConfigContent();
      case 'sync':
        return this.getSyncConfigContent();
      case 'sessions':
        return this.getSessionsConfigContent();
      default:
        return this.getGeneralConfigContent();
    }
  }

  private getGeneralConfigContent(): string {
    return `# Darklang Configuration

## Configuration Sections

### User Settings
- [👤 User Profile](dark://config/user) - Personal settings and preferences
- [🔐 Authentication](dark://config/auth) - Login and security settings
- [🎨 Appearance](dark://config/appearance) - UI themes and layout

### Development Settings
- [🎯 Sessions](dark://config/sessions) - Session management preferences
- [🔄 Sync](dark://config/sync) - Synchronization settings
- [🧪 Testing](dark://config/testing) - Test execution preferences

### Advanced Settings
- [⚡ Performance](dark://config/performance) - Performance tuning
- [🔧 Advanced](dark://config/advanced) - Expert configuration
- [📊 Diagnostics](dark://config/diagnostics) - Debugging and logging

## Quick Settings

### Most Common
- **Auto-save patches:** Enabled
- **Sync frequency:** Every 5 minutes
- **Conflict resolution:** Prompt
- **Test execution:** On save

### Recent Changes
- 16:30 - Updated sync frequency
- 15:45 - Changed conflict resolution strategy
- 14:20 - Enabled auto-save

[💾 Export Settings](command:darklang.config.export)
[📥 Import Settings](command:darklang.config.import)
[🔄 Reset to Defaults](command:darklang.config.reset)`;
  }

  private getUserConfigContent(): string {
    return `# User Configuration

## Profile Settings

| Setting | Value |
|---------|-------|
| **Username** | stachu |
| **Email** | stachu@darklang.com |
| **Display Name** | Stachu |
| **Timezone** | UTC-8 |

## Preferences

### Editor Settings
- **Theme:** Dark mode
- **Font Size:** 14px
- **Tab Size:** 2 spaces
- **Word Wrap:** Enabled

### Collaboration Settings
- **Show presence:** Enabled
- **Auto-share sessions:** Disabled
- **Notification level:** Important only

### Development Preferences
- **Auto-save:** Every 30 seconds
- **Auto-format:** On save
- **Show diagnostics:** Real-time
- **Test runner:** Background

## Actions

- [✏️ Edit Profile](command:darklang.config.user.edit)
- [🔑 Change Password](command:darklang.config.user.password)
- [🔄 Sync Preferences](command:darklang.config.user.sync)

[🔙 Back to Config](dark://config)`;
  }

  private getSyncConfigContent(): string {
    return `# Sync Configuration

## Synchronization Settings

### Auto-Sync
- **Enabled:** ☑️ Yes
- **Frequency:** Every 5 minutes
- **Sync on save:** ☑️ Yes
- **Background sync:** ☑️ Yes

### Conflict Resolution
- **Strategy:** Prompt for resolution
- **Auto-resolve simple:** ☑️ Yes
- **Backup before merge:** ☑️ Yes

### Remote Settings
- **Server URL:** https://api.darklang.com
- **Connection timeout:** 30 seconds
- **Retry attempts:** 3

## Sync Status

### Current Status
- **Last sync:** 2 minutes ago
- **Status:** ✅ Connected
- **Pending:** 0 changes
- **Conflicts:** 0

### Sync History
- 16:45 - Pushed 2 patches
- 16:30 - Pulled 1 patch
- 16:15 - Auto-resolved conflict

## Actions

- [🔄 Sync Now](command:darklang.sync.now)
- [📊 Sync History](command:darklang.sync.history)
- [🧪 Test Connection](command:darklang.sync.test)

[🔙 Back to Config](dark://config)`;
  }

  private getSessionsConfigContent(): string {
    return `# Sessions Configuration

## Session Management

### Default Settings
- **Auto-create session:** On new work
- **Session naming:** Auto-generate
- **Session lifetime:** 30 days
- **Max sessions:** 10

### Session Features
- **Auto-export:** Weekly
- **Backup sessions:** ☑️ Yes
- **Share by default:** ☐ No
- **Track activity:** ☑️ Yes

### Collaboration
- **Default sharing:** Team only
- **Permission level:** Read/Write
- **Notification on share:** ☑️ Yes

## Session Templates

Available templates for quick session creation:
- **Feature Development** - Standard feature work
- **Bug Fixes** - Issue resolution workflow
- **Performance Work** - Optimization focused
- **Experiments** - Prototype and research

## Actions

- [🆕 Create Template](command:darklang.sessions.createTemplate)
- [📋 Manage Templates](command:darklang.sessions.manageTemplates)
- [🧹 Cleanup Old Sessions](command:darklang.sessions.cleanup)

[🔙 Back to Config](dark://config)`;
  }

  /**
   * Content for instance URLs: dark://instance/type
   */
  private getInstanceContent(parsedUrl: ParsedUrl): string {
    const { context: instanceType, queryParams } = parsedUrl;

    // Handle query-based instance URLs
    if (instanceType === 'packages' && queryParams?.instance) {
      return this.getInstancePackagesContent(queryParams.instance);
    }
    if (instanceType === 'sessions' && queryParams?.instance) {
      return this.getInstanceSessionsContent(queryParams.instance);
    }
    if (instanceType === 'patches' && queryParams?.instance) {
      return this.getInstancePatchesContent(queryParams.instance);
    }
    if (instanceType === 'namespace' && queryParams?.name && queryParams?.instance) {
      return this.getInstanceNamespaceContent(queryParams.name, queryParams.instance);
    }
    if (instanceType === 'session' && queryParams?.name && queryParams?.instance) {
      return this.getInstanceSessionContent(queryParams.name, queryParams.instance);
    }
    if (instanceType === 'patch-category' && queryParams?.name && queryParams?.instance) {
      return this.getInstancePatchCategoryContent(queryParams.name, queryParams.instance);
    }
    if (instanceType === 'registry') {
      return this.getInstanceRegistryContent();
    }

    switch (instanceType) {
      case 'local':
        return this.getLocalInstanceContent();
      case 'remote':
        return this.getRemoteInstanceContent();
      default:
        return this.getInstanceListContent();
    }
  }

  private getInstancePackagesContent(instanceId: string): string {
    const instanceName = instanceId === 'matter-instance' ? 'matter.darklang.com' : 'Local Instance';
    return `# Packages in ${instanceName}

## Package Overview

| Namespace | Count | Status |
|-----------|-------|--------|
| **Darklang.Stdlib** | 45 functions | ✅ Stable |
| **Darklang.Http** | 12 functions | ✅ Stable |
| **MyApp.Auth** | 8 functions | 🚧 Development |
| **MyApp.User** | 15 functions | ✅ Ready |
| **MyApp.Utils** | 6 functions | ✅ Ready |

## Available Packages

### Core Libraries
- [📦 Darklang.Stdlib.List](dark:///package/Darklang.Stdlib.List) - List manipulation functions
- [📦 Darklang.Stdlib.String](dark:///package/Darklang.Stdlib.String) - String processing functions
- [📦 Darklang.Stdlib.Int64](dark:///package/Darklang.Stdlib.Int64) - Integer operations
- [📦 Darklang.Http](dark:///package/Darklang.Http) - HTTP client functions

### Application Packages
- [📦 MyApp.Auth](dark:///package/MyApp.Auth) - Authentication and authorization
- [📦 MyApp.User](dark:///package/MyApp.User) - User management functions
- [📦 MyApp.Utils](dark:///package/MyApp.Utils) - Utility functions

## Package Statistics

- **Total Functions:** 86
- **Total Types:** 23
- **Total Constants:** 12
- **Last Update:** 2 hours ago

## Package Actions

- [🔄 Refresh Packages](command:darklang.instance.sync?${instanceId})
- [📥 Import Package](command:darklang.package.import)
- [🔍 Search Packages](command:darklang.package.search)

[🔙 Back to Instance](dark:///instance/remote)`;
  }

  private getInstanceSessionsContent(instanceId: string): string {
    const instanceName = instanceId === 'matter-instance' ? 'matter.darklang.com' : 'Local Instance';
    return `# Sessions in ${instanceName}

## Active Sessions

### Team Sessions
- **feature-auth** (stachu) - Authentication implementation
  - Status: Active
  - Members: stachu, alice
  - Last activity: 5 minutes ago
  - [View Session](dark:///session/feature-auth)

- **ui-redesign** (alice) - Interface improvements
  - Status: In Review
  - Members: alice, bob
  - Last activity: 2 hours ago
  - [View Session](dark:///session/ui-redesign)

### Personal Sessions
- **performance-test** (bob) - Performance optimization
  - Status: Draft
  - Members: bob
  - Last activity: 1 day ago
  - [View Session](dark:///session/performance-test)

## Session Statistics

| Metric | Value |
|--------|-------|
| **Active Sessions** | 3 |
| **Total Users** | 5 |
| **Avg Session Duration** | 4.2 hours |
| **Sessions Today** | 8 |

## Recent Activity

- 16:45 - stachu modified MyApp.User.validate
- 16:30 - alice created new UI component
- 15:20 - bob started performance session
- 14:45 - team merged feature-login session

## Session Actions

- [🆕 Create Session](command:darklang.session.new)
- [👥 Join Session](command:darklang.session.join)
- [🔄 Refresh Sessions](command:darklang.instance.sync?${instanceId})

[🔙 Back to Instance](dark:///instance/remote)`;
  }

  private getInstancePatchesContent(instanceId: string): string {
    const instanceName = instanceId === 'matter-instance' ? 'matter.darklang.com' : 'Local Instance';
    return `# Patches in ${instanceName}

## Active Patches

### Ready for Review
- **abc123** - Add user validation (stachu)
  - Session: feature-auth
  - Files: 3 modified, 1 new
  - [View Patch](dark:///patch/abc123)
  - [Review](command:darklang.patch.review?abc123)

- **def456** - Fix email validation (alice)
  - Session: feature-auth
  - Files: 2 modified
  - [View Patch](dark:///patch/def456)
  - [Review](command:darklang.patch.review?def456)

### Draft Patches
- **ghi789** - Performance improvements (bob)
  - Session: performance-test
  - Files: 5 modified
  - [View Patch](dark:///patch/ghi789)

### Applied Patches
- **jkl012** - Database schema update (team)
  - Applied: 2 hours ago
  - Session: feature-auth
  - [View History](dark:///history/MyApp.Database)

## Patch Statistics

| Status | Count |
|--------|-------|
| **Draft** | 5 |
| **Ready** | 3 |
| **In Review** | 2 |
| **Applied** | 12 |
| **Conflicted** | 1 |

## Patch Categories

### By Feature
- **Authentication** (4 patches) - Login and security features
- **User Interface** (3 patches) - UI improvements and fixes
- **Performance** (2 patches) - Optimization work
- **Database** (2 patches) - Schema and queries

### By Priority
- **Critical** (1 patch) - Security fixes
- **High** (4 patches) - Important features
- **Normal** (6 patches) - Standard improvements
- **Low** (2 patches) - Nice-to-have features

## Patch Actions

- [🆕 Create Patch](command:darklang.patch.create)
- [📥 Pull Patches](command:darklang.patch.pull)
- [🔄 Sync Patches](command:darklang.patch.sync)

[🔙 Back to Instance](dark:///instance/remote)`;
  }

  private getInstanceNamespaceContent(namespaceName: string, instanceId: string): string {
    const instanceName = instanceId === 'matter-instance' ? 'matter.darklang.com' : 'Local Instance';
    return `# Namespace: ${namespaceName}
## Instance: ${instanceName}

## Namespace Overview

| Property | Value |
|----------|-------|
| **Full Name** | ${namespaceName} |
| **Instance** | ${instanceName} |
| **Type** | ${namespaceName.includes('Stdlib') ? 'Core Library' : 'Application Package'} |
| **Functions** | ${namespaceName.includes('Stdlib') ? '45' : '12'} |
| **Types** | ${namespaceName.includes('Stdlib') ? '8' : '4'} |
| **Constants** | ${namespaceName.includes('Stdlib') ? '5' : '2'} |

## Contents

### Functions
${namespaceName.includes('Stdlib') ? `
- **map** - Transform list elements with function
- **filter** - Select elements matching predicate
- **fold** - Reduce list to single value
- **length** - Get list length
- **append** - Combine two lists
` : `
- **create** - Create new user account
- **validate** - Validate user data
- **authenticate** - User login verification
- **updateProfile** - Update user information
`}

### Types
${namespaceName.includes('Stdlib') ? `
- **List<'a>** - Generic list type
- **Option<'a>** - Optional value type
- **Result<'a,'b>** - Success/error type
` : `
- **User** - User account record
- **Profile** - User profile information
- **Credentials** - Login credentials
`}

## Recent Changes

- 2 hours ago - Updated validation function
- 1 day ago - Added new utility functions
- 3 days ago - Fixed performance issue

## Namespace Actions

- [📦 Browse Full Package](dark:///package/${namespaceName})
- [✏️ Edit in Current Patch](dark:///edit/current-patch/${namespaceName})
- [📜 View History](dark:///history/${namespaceName})

[🔙 Back to Instance Packages](dark:///instance/packages?instance=${instanceId})`;
  }

  private getInstanceSessionContent(sessionName: string, instanceId: string): string {
    const instanceName = instanceId === 'matter-instance' ? 'matter.darklang.com' : 'Local Instance';
    return `# Remote Session: ${sessionName}
## Instance: ${instanceName}

## Session Details

| Property | Value |
|----------|-------|
| **Session Name** | ${sessionName} |
| **Instance** | ${instanceName} |
| **Owner** | ${sessionName.includes('auth') ? 'stachu' : 'alice'} |
| **Status** | ${sessionName.includes('auth') ? 'Active' : 'In Review'} |
| **Created** | ${sessionName.includes('auth') ? '3 days ago' : '1 week ago'} |
| **Last Activity** | ${sessionName.includes('auth') ? '5 minutes ago' : '2 hours ago'} |

## Session Members

### Collaborators
- **stachu** (Owner) - Full access
- **alice** (Collaborator) - Read/Write
- **bob** (Reviewer) - Read only

### Permissions
- Create patches: ✅ Yes
- Review patches: ✅ Yes
- Apply patches: ✅ Yes (Owner only)
- Manage session: ✅ Yes (Owner only)

## Session Activity

### Recent Actions
- 16:45 - stachu modified MyApp.User.validate
- 16:30 - alice created new test cases
- 16:15 - bob reviewed patch abc123
- 15:45 - stachu created patch def456

### Session Stats
- **Total Patches:** ${sessionName.includes('auth') ? '8' : '3'}
- **Active Patches:** ${sessionName.includes('auth') ? '2' : '1'}
- **Functions Modified:** ${sessionName.includes('auth') ? '12' : '5'}
- **Tests Added:** ${sessionName.includes('auth') ? '25' : '8'}

## Session Content

### Modified Packages
${sessionName.includes('auth') ? `
- **MyApp.Auth** - 5 functions modified
- **MyApp.User** - 3 functions modified
- **MyApp.Database** - 2 functions modified
` : `
- **MyApp.UI** - 4 functions modified
- **MyApp.Components** - 3 functions modified
`}

### Active Patches
${sessionName.includes('auth') ? `
- [abc123](dark:///patch/abc123) - Add user validation (Ready)
- [def456](dark:///patch/def456) - Fix email validation (Draft)
` : `
- [ghi789](dark:///patch/ghi789) - Update button styles (Ready)
`}

## Session Actions

- [🎯 Switch to Session](command:darklang.session.switch?${sessionName})
- [👥 Join Session](command:darklang.session.join?${sessionName})
- [📤 Export Session](dark:///session/${sessionName}/export)
- [📥 Clone Session](command:darklang.session.clone?${sessionName})

[🔙 Back to Instance Sessions](dark:///instance/sessions?instance=${instanceId})`;
  }

  private getInstancePatchCategoryContent(categoryName: string, instanceId: string): string {
    const instanceName = instanceId === 'matter-instance' ? 'matter.darklang.com' : 'Local Instance';
    return `# Patch Category: ${categoryName}
## Instance: ${instanceName}

## Category Overview

| Property | Value |
|----------|-------|
| **Category** | ${categoryName} |
| **Instance** | ${instanceName} |
| **Total Patches** | ${categoryName.includes('Feature') ? '12' : '8'} |
| **Active Patches** | ${categoryName.includes('Feature') ? '3' : '2'} |
| **Contributors** | ${categoryName.includes('Feature') ? '4' : '2'} |

## Patches in Category

### Ready for Review
${categoryName.includes('Feature') ? `
- **abc123** - Add user validation (stachu)
  - Files: 3 modified, 1 new
  - Tests: 8 added
  - [Review Patch](command:darklang.patch.review?abc123)

- **def456** - Fix email validation (alice)
  - Files: 2 modified
  - Tests: 4 added
  - [Review Patch](command:darklang.patch.review?def456)
` : `
- **ghi789** - Update button styles (alice)
  - Files: 4 modified
  - Tests: 2 added
  - [Review Patch](command:darklang.patch.review?ghi789)
`}

### Draft Patches
${categoryName.includes('Feature') ? `
- **jkl012** - Performance improvements (bob)
  - Files: 5 modified
  - Tests: 12 added
  - [View Patch](dark:///patch/jkl012)
` : `
- **mno345** - Color scheme updates (bob)
  - Files: 3 modified
  - Tests: 1 added
  - [View Patch](dark:///patch/mno345)
`}

### Applied Patches (Recent)
${categoryName.includes('Feature') ? `
- **pqr678** - Database migration (team)
  - Applied: 2 days ago
  - Files: 8 modified
  - [View History](dark:///history/MyApp.Database)
` : `
- **stu901** - Typography improvements (alice)
  - Applied: 1 week ago
  - Files: 5 modified
  - [View History](dark:///history/MyApp.UI)
`}

## Category Statistics

### Patch Status Distribution
- **Draft:** ${categoryName.includes('Feature') ? '5' : '3'} patches
- **Ready:** ${categoryName.includes('Feature') ? '3' : '2'} patches
- **In Review:** ${categoryName.includes('Feature') ? '2' : '1'} patches
- **Applied:** ${categoryName.includes('Feature') ? '7' : '4'} patches

### Impact Analysis
- **High Impact:** ${categoryName.includes('Feature') ? '4' : '2'} patches
- **Medium Impact:** ${categoryName.includes('Feature') ? '6' : '4'} patches
- **Low Impact:** ${categoryName.includes('Feature') ? '2' : '2'} patches

## Category Actions

- [🆕 Create Patch in Category](command:darklang.patch.create?category=${categoryName})
- [📊 Category Analytics](command:darklang.patch.analytics?category=${categoryName})
- [🔄 Sync Category](command:darklang.patch.sync?category=${categoryName})

[🔙 Back to Instance Patches](dark:///instance/patches?instance=${instanceId})`;
  }

  private getInstanceRegistryContent(): string {
    return `# Public Registry Browser

## Available Instances

### Official Instances
- **darklang.com** - Main production instance
  - Status: ✅ Online
  - Version: 2024.1.15
  - Packages: 250+
  - [Connect](command:darklang.instance.connect?darklang.com)

- **staging.darklang.com** - Latest development builds
  - Status: ✅ Online
  - Version: 2024.1.16-beta
  - Packages: 280+
  - [Connect](command:darklang.instance.connect?staging.darklang.com)

### Community Instances
- **community.darklang.com** - Community packages
  - Status: ✅ Online
  - Maintainer: Community
  - Packages: 150+
  - [Connect](command:darklang.instance.connect?community.darklang.com)

- **examples.darklang.com** - Example applications
  - Status: ✅ Online
  - Maintainer: Darklang Team
  - Packages: 45+
  - [Connect](command:darklang.instance.connect?examples.darklang.com)

### Regional Instances
- **eu.darklang.com** - European instance
  - Status: ✅ Online
  - Region: Europe
  - Packages: 230+
  - [Connect](command:darklang.instance.connect?eu.darklang.com)

- **asia.darklang.com** - Asian instance
  - Status: ✅ Online
  - Region: Asia-Pacific
  - Packages: 220+
  - [Connect](command:darklang.instance.connect?asia.darklang.com)

## Registry Statistics

### Overall Stats
- **Total Instances:** 12
- **Online Instances:** 11
- **Total Packages:** 1,200+
- **Active Users:** 2,500+

### Popular Packages
- **Darklang.Stdlib** - Core standard library
- **Darklang.Http** - HTTP client and server
- **Darklang.Database** - Database operations
- **Darklang.Json** - JSON processing
- **Darklang.Testing** - Test utilities

## Registry Actions

- [🔄 Refresh Registry](command:darklang.registry.refresh)
- [🔍 Search Packages](command:darklang.registry.search)
- [📊 Registry Stats](command:darklang.registry.stats)
- [➕ Submit Instance](command:darklang.registry.submit)

[🔙 Back to Instances](command:darklang.instances.refresh)`;
  }

  private getLocalInstanceContent(): string {
    return `# Local Darklang Instance

## Instance Status

| Property | Value |
|----------|-------|
| **Type** | Local Development |
| **Status** | ✅ Running |
| **Version** | 2024.1.15 |
| **Uptime** | 4h 23m |
| **Port** | 8080 |

## Performance Metrics

### Resource Usage
- **CPU:** 15% (2 cores)
- **Memory:** 512MB / 2GB (25%)
- **Disk:** 1.2GB used
- **Network:** Minimal

### Request Statistics
- **Requests/min:** 45
- **Average response:** 25ms
- **Error rate:** 0.1%

## Local Services

### Running Services
- ✅ **Language Server** (Port 8081)
- ✅ **Package Server** (Port 8082)
- ✅ **Test Runner** (Background)
- ✅ **File Watcher** (Active)

### Configuration
- **Data Directory:** ~/.darklang/
- **Logs Directory:** ~/.darklang/logs/
- **Cache Size:** 256MB
- **Debug Mode:** Enabled

## Development Features

### Local-Only Features
- Real-time compilation
- Instant feedback
- Local package cache
- Development tools

### Sync Status
- **Last sync:** 5 minutes ago
- **Pending uploads:** 2 patches
- **Remote conflicts:** 0

## Actions

- [🔄 Restart Instance](command:darklang.instance.restart)
- [📊 View Logs](command:darklang.instance.logs)
- [⚙️ Configure](command:darklang.instance.configure)
- [🌐 Switch to Remote](dark://instance/remote)

[📈 Performance Dashboard](dark://instance/local/performance)
[🔧 Advanced Settings](dark://instance/local/settings)`;
  }

  private getRemoteInstanceContent(): string {
    return `# Remote Darklang Instance

## Instance Status

| Property | Value |
|----------|-------|
| **Type** | Shared Remote |
| **Status** | ✅ Connected |
| **Region** | US-West-2 |
| **Latency** | 45ms |
| **URL** | https://api.darklang.com |

## Connection Info

### Authentication
- **User:** stachu
- **Token:** Valid (expires in 6h)
- **Permissions:** Read/Write
- **Rate Limit:** 1000/hour (45 used)

### Sync Status
- **Last sync:** 1 minute ago
- **Real-time updates:** ✅ Enabled
- **Offline changes:** 0
- **Conflicts:** 0

## Remote Features

### Collaboration
- **Active users:** 12
- **Your sessions:** 3
- **Shared sessions:** 8
- **Team patches:** 15

### Backup & Recovery
- **Auto-backup:** Every hour
- **Point-in-time recovery:** 30 days
- **Geographic replication:** 3 regions

## Performance

### Response Times
- **API calls:** 25ms avg
- **Package loads:** 150ms avg
- **Sync operations:** 500ms avg

### Reliability
- **Uptime:** 99.9% (30 days)
- **Last incident:** None
- **Maintenance window:** Sundays 2-4 AM UTC

## Actions

- [🔌 Disconnect](command:darklang.instance.disconnect)
- [🔄 Force Sync](command:darklang.instance.forceSync)
- [📊 Usage Stats](command:darklang.instance.usage)
- [🏠 Switch to Local](dark://instance/local)

[👥 Team Dashboard](dark://instance/remote/team)
[📈 Usage Analytics](dark://instance/remote/analytics)`;
  }

  private getInstanceListContent(): string {
    return `# Darklang Instances

## Available Instances

### Local Development
- [🏠 Local Instance](dark://instance/local) - Your development environment
  - Status: ✅ Running
  - Performance: Good
  - Last sync: 5 minutes ago

### Remote Instances
- [🌐 Production](dark://instance/remote) - Shared team environment
  - Status: ✅ Connected
  - Latency: 45ms
  - Active users: 12

### Offline Mode
- [📱 Offline Instance](dark://instance/offline) - Work without connection
  - Status: Available
  - Local changes: 0
  - Last offline: Never

## Instance Management

### Quick Actions
- [🔄 Sync All](command:darklang.instance.syncAll)
- [⚙️ Configure Instances](command:darklang.instance.configure)
- [📊 Instance Health](command:darklang.instance.health)

### Connection Status
All instances are healthy and synchronized.

[➕ Add Instance](command:darklang.instance.add)
[🔧 Instance Settings](dark://config/instances)`;
  }

  /**
   * Refresh content for a specific URI
   */
  public refresh(uri?: vscode.Uri): void {
    if (uri) {
      this._onDidChange.fire(uri);
    } else {
      // Refresh all dark:// documents
      vscode.workspace.textDocuments
        .filter(doc => doc.uri.scheme === "dark")
        .forEach(doc => this._onDidChange.fire(doc.uri));
    }
  }

  /**
   * Error content for invalid URLs
   */
  private getErrorContent(url: string, error: string): string {
    const supportedPatterns = UrlPatternRouter.getSupportedPatterns();

    return `# Error Loading Darklang Content

**URL:** ${url}
**Error:** ${error}

## Supported URL Patterns

The Darklang extension supports the following URL patterns:

### Package Browsing
\`\`\`
${supportedPatterns.filter(p => p.includes('package')).slice(0, 4).join('\n')}
\`\`\`

### Editing
\`\`\`
${supportedPatterns.filter(p => p.includes('edit')).slice(0, 2).join('\n')}
\`\`\`

### Collaboration
\`\`\`
${supportedPatterns.filter(p => p.includes('patch')).slice(0, 3).join('\n')}
\`\`\`

### Version Control
\`\`\`
${supportedPatterns.filter(p => p.includes('history') || p.includes('compare')).slice(0, 3).join('\n')}
\`\`\`

### Management
\`\`\`
${supportedPatterns.filter(p => p.includes('session') || p.includes('config')).slice(0, 3).join('\n')}
\`\`\`

## Getting Started

Try these working examples:
- [Browse Darklang.Stdlib.List.map](dark://package/Darklang.Stdlib.List.map)
- [Edit MyApp.User.validate](dark://edit/current-patch/MyApp.User.validate)
- [View patch abc123](dark://patch/abc123)
- [See version history](dark://history/Darklang.Stdlib.List.map)

## Help

- [📚 Documentation](dark://package/Darklang.Docs)
- [🆘 Support](command:darklang.help.support)
- [🐛 Report Issue](command:darklang.help.reportIssue)

Please check your URL format and try again.`;
  }
}