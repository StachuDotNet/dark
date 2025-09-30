import * as vscode from 'vscode';
import { UrlPatternRouter, ParsedUrl } from './urlPatternRouter';

export interface UrlMetadata {
  title: string;
  badge: string;
  tooltip: string;
  themeColor: vscode.ThemeColor;
  contentProvider: string; // Which provider handles this URL type
}

/**
 * Central system that determines all URL behavior based on the URL pattern itself
 * No file extensions needed - the URL structure tells us everything
 */
export class UrlMetadataSystem {
  /**
   * Get complete metadata for any dark:// URL
   */
  static getMetadata(uri: vscode.Uri): UrlMetadata | null {
    // Handle special home page
    if (uri.path === '/home' || uri.path === '/' || uri.path === '') {
      return {
        title: 'Darklang Home',
        badge: '🏠',
        tooltip: 'Darklang VS Code Extension Home Page',
        themeColor: new vscode.ThemeColor('charts.foreground'),
        contentProvider: 'home'
      };
    }

    const parsed = UrlPatternRouter.parseUrl(uri.toString());
    if (!parsed) return null;

    return this.createMetadata(parsed, uri);
  }

  private static createMetadata(parsed: ParsedUrl, uri: vscode.Uri): UrlMetadata {
    switch (parsed.mode) {
      case 'session':
        return {
          title: this.getSessionTitle(parsed),
          badge: '🏢',
          tooltip: `Session: ${parsed.context || 'Unknown'}`,
          themeColor: new vscode.ThemeColor('charts.blue'),
          contentProvider: 'session'
        };

      case 'patch':
        return {
          title: this.getPatchTitle(parsed),
          badge: this.getPatchBadge(parsed.view),
          tooltip: `Patch ${parsed.context}: ${this.getPatchTooltip(parsed.view)}`,
          themeColor: new vscode.ThemeColor('charts.green'),
          contentProvider: 'patch'
        };

      case 'package':
        return {
          title: this.getPackageTitle(parsed),
          badge: '📦',
          tooltip: `Package: ${parsed.target || 'Unknown'}`,
          themeColor: new vscode.ThemeColor('charts.purple'),
          contentProvider: 'package'
        };

      case 'instance':
        return {
          title: this.getInstanceTitle(parsed),
          badge: this.getInstanceBadge(parsed),
          tooltip: `Instance: ${parsed.context} (${parsed.view})`,
          themeColor: new vscode.ThemeColor('charts.orange'),
          contentProvider: 'instance'
        };

      case 'edit':
        return {
          title: this.getEditTitle(parsed),
          badge: '✏️',
          tooltip: `Editing: ${parsed.target} in ${parsed.context}`,
          themeColor: new vscode.ThemeColor('charts.yellow'),
          contentProvider: 'edit'
        };

      case 'draft':
        return {
          title: this.getDraftTitle(parsed),
          badge: '📝',
          tooltip: `Draft: ${parsed.target}`,
          themeColor: new vscode.ThemeColor('charts.gray'),
          contentProvider: 'draft'
        };

      case 'history':
        return {
          title: this.getHistoryTitle(parsed),
          badge: '📜',
          tooltip: `History: ${parsed.target}`,
          themeColor: new vscode.ThemeColor('charts.purple'),
          contentProvider: 'history'
        };

      case 'compare':
        return {
          title: this.getCompareTitle(parsed),
          badge: '🔍',
          tooltip: `Compare: ${parsed.context}`,
          themeColor: new vscode.ThemeColor('charts.red'),
          contentProvider: 'compare'
        };

      case 'config':
        return {
          title: this.getConfigTitle(parsed),
          badge: '⚙️',
          tooltip: `Configuration: ${parsed.context || 'General'}`,
          themeColor: new vscode.ThemeColor('charts.gray'),
          contentProvider: 'config'
        };

      default:
        return {
          title: 'Darklang Document',
          badge: '🌑',
          tooltip: 'Darklang Document',
          themeColor: new vscode.ThemeColor('charts.foreground'),
          contentProvider: 'default'
        };
    }
  }

  // Title generation methods
  private static getSessionTitle(parsed: ParsedUrl): string {
    const sessionName = parsed.context || 'unknown';
    return `Session: ${sessionName}`;
  }

  private static getPatchTitle(parsed: ParsedUrl): string {
    const patchId = parsed.context || 'unknown';
    const view = parsed.view || 'overview';

    switch (view) {
      case 'operations': return `${patchId} Operations`;
      case 'conflicts': return `${patchId} Conflicts`;
      case 'tests': return `${patchId} Tests`;
      case 'test': return `Test: ${parsed.queryParams?.name || 'unknown'}`;
      case 'meta': return `${patchId} Metadata`;
      case 'edit': return `Edit ${patchId}`;
      default: return `Patch ${patchId}`;
    }
  }

  private static getPackageTitle(parsed: ParsedUrl): string {
    const target = parsed.target || 'unknown';
    const lastPart = target.split('.').pop() || target;
    return `Package: ${lastPart}`;
  }

  private static getInstanceTitle(parsed: ParsedUrl): string {
    const context = parsed.context || 'unknown';
    const view = parsed.view || 'status';

    if (view === 'packages') return `${context} Packages`;
    if (view === 'sessions') return `${context} Sessions`;
    if (view === 'namespace') return `Namespace: ${parsed.target}`;
    if (view === 'remote-session') return `Remote Session: ${parsed.target}`;
    if (view === 'patch-category') return `Patches: ${parsed.target}`;
    if (view === 'registry') return 'Package Registry';
    if (view === 'details') return `Instance: ${context}`;

    return `Instance: ${context}`;
  }

  private static getEditTitle(parsed: ParsedUrl): string {
    const target = parsed.target || 'unknown';
    const lastPart = target.split('.').pop() || target;
    return `Edit: ${lastPart}`;
  }

  private static getDraftTitle(parsed: ParsedUrl): string {
    const target = parsed.target || 'unknown';
    const lastPart = target.split('.').pop() || target;
    return `Draft: ${lastPart}`;
  }

  private static getHistoryTitle(parsed: ParsedUrl): string {
    const target = parsed.target || 'unknown';
    const lastPart = target.split('.').pop() || target;
    return `History: ${lastPart}`;
  }

  private static getCompareTitle(parsed: ParsedUrl): string {
    const context = parsed.context || 'unknown';
    return `Compare: ${context.replace('/', ' vs ')}`;
  }

  private static getConfigTitle(parsed: ParsedUrl): string {
    const section = parsed.context || 'general';
    return `Config: ${section}`;
  }

  // Badge methods
  private static getPatchBadge(view?: string): string {
    switch (view) {
      case 'operations': return '⚡';
      case 'conflicts': return '⚠️';
      case 'tests': return '🧪';
      case 'test': return '🔬';
      case 'meta': return 'ℹ️';
      case 'edit': return '✏️';
      default: return '🔧';
    }
  }

  private static getInstanceBadge(parsed: ParsedUrl): string {
    if (parsed.context === 'local') return '📁';
    if (parsed.context === 'remote') return '🌐';
    if (parsed.context === 'registry') return '🏪';

    switch (parsed.view) {
      case 'packages': return '📦';
      case 'sessions': return '🏢';
      case 'namespace': return '📂';
      case 'remote-session': return '🔗';
      case 'patch-category': return '📋';
      case 'registry': return '🏪';
      default: return '🖥️';
    }
  }

  private static getPatchTooltip(view?: string): string {
    switch (view) {
      case 'operations': return 'Operations';
      case 'conflicts': return 'Conflicts';
      case 'tests': return 'All Tests';
      case 'test': return 'Individual Test';
      case 'meta': return 'Metadata';
      case 'edit': return 'Edit Mode';
      default: return 'Overview';
    }
  }

  /**
   * Create a clean URL without redundant extensions
   */
  static createUrl(mode: string, context?: string, view?: string, target?: string, queryParams?: Record<string, string>): string {
    let url = `dark:///${mode}`;

    if (context) url += `/${context}`;
    if (view && view !== 'overview') url += `/${view}`;
    if (target) url += `/${target}`;

    if (queryParams && Object.keys(queryParams).length > 0) {
      const params = new URLSearchParams(queryParams);
      url += `?${params.toString()}`;
    }

    return url;
  }
}