/**
 * URL Pattern Router for Darklang VS Code Extension
 *
 * Handles all documented URL patterns from notes/vscode/Virtual-File-URL-Design.md
 * and notes/vscode/pages/ specifications.
 */

export interface ParsedUrl {
  scheme: string;
  mode: UrlMode;
  context?: string;
  target?: string;
  view?: string;
  queryParams?: Record<string, string>;
}

export type UrlMode =
  | 'package'     // Browse/read package items
  | 'edit'        // Edit in patch context
  | 'draft'       // Create new items
  | 'patch'       // Patch overview/management
  | 'history'     // Version history
  | 'compare'     // Version comparison
  | 'session'     // Session management
  | 'config'      // Configuration
  | 'instance';   // Instance management

export class UrlPatternRouter {
  /**
   * Parse a dark:// URL according to documented patterns
   */
  static parseUrl(url: string): ParsedUrl | null {
    try {
      console.log('UrlPatternRouter: Parsing URL:', url);
      const urlObj = new URL(url);

      if (urlObj.protocol !== 'dark:') {
        console.log('UrlPatternRouter: Invalid protocol:', urlObj.protocol);
        return null;
      }

      const pathParts = urlObj.pathname.split('/').filter(p => p);
      const queryParams = this.parseQueryParams(urlObj.search);
      console.log('UrlPatternRouter: Path parts:', pathParts);
      console.log('UrlPatternRouter: Query params:', queryParams);

      if (pathParts.length === 0) {
        console.log('UrlPatternRouter: No path parts found');
        return null;
      }

      const mode = pathParts[0] as UrlMode;
      console.log('UrlPatternRouter: Mode:', mode);

      let result = null;
      switch (mode) {
        case 'package':
          result = this.parsePackageUrl(pathParts, queryParams);
          break;
        case 'edit':
          result = this.parseEditUrl(pathParts, queryParams);
          break;
        case 'draft':
          result = this.parseDraftUrl(pathParts, queryParams);
          break;
        case 'patch':
          result = this.parsePatchUrl(pathParts, queryParams);
          break;
        case 'history':
          result = this.parseHistoryUrl(pathParts, queryParams);
          break;
        case 'compare':
          result = this.parseCompareUrl(pathParts, queryParams);
          break;
        case 'session':
          result = this.parseSessionUrl(pathParts, queryParams);
          break;
        case 'config':
          result = this.parseConfigUrl(pathParts, queryParams);
          break;
        case 'instance':
          result = this.parseInstanceUrl(pathParts, queryParams);
          break;
        default:
          console.log('UrlPatternRouter: Unknown mode:', mode);
          return null;
      }

      console.log('UrlPatternRouter: Parsed result:', result);
      return result;
    } catch (error) {
      console.error('UrlPatternRouter: Failed to parse URL:', url, error);
      return null;
    }
  }

  /**
   * Parse package URLs: dark://package/Name.Space.item[?view=type] or dark://package/Name/Space/item/name.darklang
   */
  private static parsePackageUrl(pathParts: string[], queryParams: Record<string, string>): ParsedUrl {
    // Handle new format: dark://package/Darklang/Stdlib/List/map/map.darklang
    // vs old format: dark://package/Darklang.Stdlib.List.map

    let target: string;
    if (pathParts.length > 2 && pathParts[pathParts.length - 1].includes('.darklang')) {
      // New format with .darklang extension - reconstruct the dotted path
      // Remove 'package' and the filename, join the rest with dots
      const pathWithoutExtension = pathParts.slice(1, -1);
      target = pathWithoutExtension.join('.');
    } else {
      // Old format - join remaining parts with '/' to preserve the dotted target
      target = pathParts.slice(1).join('/');
    }

    return {
      scheme: 'dark',
      mode: 'package',
      target: target,
      view: queryParams.view || 'source',
      queryParams
    };
  }

  /**
   * Parse edit URLs: dark://edit/current-patch/Name.Space.item or dark://edit/patch-abc123/Name.Space.item
   */
  private static parseEditUrl(pathParts: string[], queryParams: Record<string, string>): ParsedUrl {
    return {
      scheme: 'dark',
      mode: 'edit',
      context: pathParts[1], // current-patch or patch-abc123
      target: pathParts.slice(2).join('.'),
      view: queryParams.view || 'edit',
      queryParams
    };
  }

  /**
   * Parse draft URLs: dark://draft/Name.Space.newItem
   */
  private static parseDraftUrl(pathParts: string[], queryParams: Record<string, string>): ParsedUrl {
    return {
      scheme: 'dark',
      mode: 'draft',
      target: pathParts.slice(1).join('.'),
      view: queryParams.view || 'template',
      queryParams
    };
  }

  /**
   * Parse patch URLs: dark://patch/abc123[/edit|/meta|/check|/conflicts] or dark://patch/abc123/operations.darklang-ops
   */
  private static parsePatchUrl(pathParts: string[], queryParams: Record<string, string>): ParsedUrl {
    const patchId = pathParts[1];
    let subView = pathParts[2]; // edit, meta, check, conflicts, operations.darklang-ops, etc.

    // Handle new format with extensions
    if (subView && subView.includes('.darklang-')) {
      if (subView.includes('.darklang-ops')) {
        subView = 'operations';
      } else if (subView.includes('.darklang-conflicts')) {
        subView = 'conflicts';
      } else if (subView.includes('.darklang-tests')) {
        subView = 'tests';
      } else if (subView.includes('.darklang-test')) {
        subView = 'test';
        // For individual tests, the test name is in the query parameter or path
        if (pathParts[3] && pathParts[3].includes('.darklang-test')) {
          const testName = pathParts[3].replace('.darklang-test', '');
          queryParams.name = testName;
        }
      }
    }

    // Handle test URLs with path structure: dark://patch/abc123/test/testname.darklang-test
    if (pathParts[2] === 'test' && pathParts[3] && pathParts[3].includes('.darklang-test')) {
      subView = 'test';
      const testName = pathParts[3].replace('.darklang-test', '');
      queryParams.name = testName;
    }

    return {
      scheme: 'dark',
      mode: 'patch',
      context: patchId,
      view: subView || queryParams.view || 'overview',
      queryParams
    };
  }

  /**
   * Parse history URLs: dark://history/Name.Space.item
   */
  private static parseHistoryUrl(pathParts: string[], queryParams: Record<string, string>): ParsedUrl {
    return {
      scheme: 'dark',
      mode: 'history',
      target: pathParts.slice(1).join('.'),
      view: queryParams.view || 'timeline',
      queryParams
    };
  }

  /**
   * Parse compare URLs: dark://compare/hash1/hash2 or dark://compare/current/patch-abc123
   */
  private static parseCompareUrl(pathParts: string[], queryParams: Record<string, string>): ParsedUrl {
    const version1 = pathParts[1];
    const version2 = pathParts[2];

    return {
      scheme: 'dark',
      mode: 'compare',
      context: `${version1}/${version2}`,
      target: queryParams.target, // Optional target for specific item comparison
      view: queryParams.view || 'side-by-side',
      queryParams
    };
  }

  /**
   * Parse session URLs: dark://session/session-id[/action]
   */
  private static parseSessionUrl(pathParts: string[], queryParams: Record<string, string>): ParsedUrl {
    return {
      scheme: 'dark',
      mode: 'session',
      context: pathParts[1], // session-id
      view: pathParts[2] || queryParams.view || 'overview',
      queryParams
    };
  }

  /**
   * Parse config URLs: dark://config[/section]
   */
  private static parseConfigUrl(pathParts: string[], queryParams: Record<string, string>): ParsedUrl {
    return {
      scheme: 'dark',
      mode: 'config',
      context: pathParts[1], // config section
      view: queryParams.view || 'general',
      queryParams
    };
  }

  /**
   * Parse instance URLs:
   * - dark://instance/local/details.darklang-instance
   * - dark://instance/remote/details.darklang-instance
   * - dark://instance/instanceId/packages.darklang-packages
   * - dark://instance/instanceId/sessions.darklang-sessions
   * - dark://instance/instanceId/namespace/name.darklang-namespace
   * - dark://instance/instanceId/session/name.darklang-remote-session
   * - dark://instance/instanceId/patch-category/name.darklang-patch-category
   * - dark://instance/registry/browse.darklang-registry
   */
  private static parseInstanceUrl(pathParts: string[], queryParams: Record<string, string>): ParsedUrl {
    const context = pathParts[1]; // local, remote, instanceId, registry
    let view = 'status';
    let target = undefined;

    // Handle various instance URL patterns
    if (pathParts.length >= 3) {
      const lastPart = pathParts[pathParts.length - 1];

      if (lastPart.includes('.darklang-instance')) {
        view = 'details';
      } else if (lastPart.includes('.darklang-packages')) {
        view = 'packages';
      } else if (lastPart.includes('.darklang-sessions')) {
        view = 'sessions';
      } else if (lastPart.includes('.darklang-namespace')) {
        view = 'namespace';
        target = lastPart.replace('.darklang-namespace', '');
      } else if (lastPart.includes('.darklang-remote-session')) {
        view = 'remote-session';
        target = lastPart.replace('.darklang-remote-session', '');
      } else if (lastPart.includes('.darklang-patch-category')) {
        view = 'patch-category';
        target = lastPart.replace('.darklang-patch-category', '');
      } else if (lastPart.includes('.darklang-registry')) {
        view = 'registry';
      } else {
        // For URLs like dark://instance/patches?instance=id
        view = pathParts[2] || queryParams.view || 'status';
      }
    }

    return {
      scheme: 'dark',
      mode: 'instance',
      context: context,
      target: target,
      view: view,
      queryParams
    };
  }

  private static parseQueryParams(search: string): Record<string, string> {
    const params: Record<string, string> = {};
    if (search) {
      const urlParams = new URLSearchParams(search);
      for (const [key, value] of urlParams) {
        params[key] = value;
      }
    }
    return params;
  }

  /**
   * Generate URLs for common patterns
   */
  static createPackageUrl(packagePath: string, view?: string): string {
    const url = `dark:///package/${packagePath.replace(/\./g, '/')}`;
    return view ? `${url}?view=${view}` : url;
  }

  static createEditUrl(packagePath: string, context: string = 'current-patch'): string {
    return `dark:///edit/${context}/${packagePath.replace(/\./g, '/')}`;
  }

  static createPatchUrl(patchId: string, subView?: string): string {
    const url = `dark:///patch/${patchId}`;
    return subView ? `${url}/${subView}` : url;
  }

  static createHistoryUrl(packagePath: string): string {
    return `dark:///history/${packagePath.replace(/\./g, '/')}`;
  }

  static createCompareUrl(version1: string, version2: string, target?: string): string {
    const url = `dark:///compare/${version1}/${version2}`;
    return target ? `${url}?target=${target}` : url;
  }

  /**
   * Get all supported URL patterns for documentation/testing
   */
  static getSupportedPatterns(): string[] {
    return [
      // Package browsing
      'dark:///package/Darklang.Stdlib.List.map',
      'dark:///package/MyApp.User',
      'dark:///package/MyApp.User.Profile?view=ast',
      'dark:///package/MyApp.User?view=graph',

      // Editing
      'dark:///edit/current-patch/MyApp.User.validate',
      'dark:///edit/patch-abc123/MyApp.User.validate',

      // Draft creation
      'dark:///draft/MyApp.User.newFunction',

      // Patch management
      'dark:///patch/abc123',
      'dark:///patch/abc123/edit',
      'dark:///patch/abc123/meta',
      'dark:///patch/abc123/check',
      'dark:///patch/abc123/conflicts',
      'dark:///patch/abc123/operations.darklang-ops',
      'dark:///patch/abc123/conflicts.darklang-conflicts',
      'dark:///patch/abc123/tests.darklang-tests',
      'dark:///patch/abc123/test/validation_test.darklang-test',

      // Version history
      'dark:///history/MyApp.User.validate',
      'dark:///history/MyApp.User',

      // Version comparison
      'dark:///compare/hash1/hash2',
      'dark:///compare/current/patch-abc123',
      'dark:///compare/v1.2.0/v1.2.1?target=MyApp.User.validate',

      // Session management
      'dark:///session/feature-auth.darklang-session',
      'dark:///session/team-session-alpha.darklang-session',
      'dark:///session/team-session-alpha/export',

      // Configuration
      'dark:///config',
      'dark:///config/user',
      'dark:///config/sync',

      // Instance management
      'dark:///instance/local/details.darklang-instance',
      'dark:///instance/remote/details.darklang-instance',
      'dark:///instance/matter-prod/packages.darklang-packages',
      'dark:///instance/matter-prod/sessions.darklang-sessions',
      'dark:///instance/matter-prod/namespace/MyApp.darklang-namespace',
      'dark:///instance/matter-prod/session/feature-auth.darklang-remote-session',
      'dark:///instance/matter-prod/patch-category/ready-for-review.darklang-patch-category',
      'dark:///instance/registry/browse.darklang-registry'
    ];
  }
}