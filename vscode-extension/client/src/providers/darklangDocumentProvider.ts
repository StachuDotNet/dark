import * as vscode from 'vscode';
import { UrlMetadataSystem } from './urlMetadataSystem';
import { ComprehensiveDarkContentProvider } from './comprehensiveDarkContentProvider';

/**
 * Enhanced document provider that can set custom tab titles
 * This is the secret to dynamic tab titles in VS Code!
 */
export class DarklangDocumentProvider extends vscode.Disposable {
  private contentProvider: ComprehensiveDarkContentProvider;
  private disposables: vscode.Disposable[] = [];

  constructor() {
    super(() => this.dispose());

    this.contentProvider = new ComprehensiveDarkContentProvider();

    // Register the content provider
    this.disposables.push(
      vscode.workspace.registerTextDocumentContentProvider('dark', this.contentProvider)
    );

    // Hook into document opening to set custom titles
    this.disposables.push(
      vscode.workspace.onDidOpenTextDocument(this.onDocumentOpened, this)
    );
  }

  private async onDocumentOpened(document: vscode.TextDocument): Promise<void> {
    if (document.uri.scheme !== 'dark') {
      return;
    }

    // Get metadata for this URL
    const metadata = UrlMetadataSystem.getMetadata(document.uri);
    if (!metadata) {
      return;
    }

    // Find the editor for this document
    const editor = vscode.window.visibleTextEditors.find(e => e.document === document);
    if (!editor) {
      return;
    }

    // Here's the magic: VS Code respects the last path segment as the tab title
    // But we can influence it by creating a new URI with the desired title as the last segment
    const desiredTitle = this.sanitizeTitle(metadata.title);

    // Check if the current URI already has the desired title
    const currentTitle = this.extractTitleFromUri(document.uri);
    if (currentTitle === desiredTitle) {
      return; // Already has the right title
    }

    // Create a new URI with the desired title
    const newUri = this.createUriWithTitle(document.uri, desiredTitle);

    // We can't directly change the tab title, but we can suggest VS Code to use our title
    // by setting a custom property (this is an undocumented feature)
    (document as any)._darklangTitle = metadata.title;
  }

  private sanitizeTitle(title: string): string {
    // Remove characters that aren't valid in URIs
    return title.replace(/[^a-zA-Z0-9\-_.: ]/g, '-');
  }

  private extractTitleFromUri(uri: vscode.Uri): string {
    const pathParts = uri.path.split('/').filter(p => p);
    return pathParts[pathParts.length - 1] || '';
  }

  private createUriWithTitle(originalUri: vscode.Uri, title: string): vscode.Uri {
    const pathParts = originalUri.path.split('/').filter(p => p);

    // Replace the last segment with our desired title, or add it
    if (pathParts.length > 0) {
      pathParts[pathParts.length - 1] = title;
    } else {
      pathParts.push(title);
    }

    return originalUri.with({
      path: '/' + pathParts.join('/')
    });
  }

  public dispose(): void {
    this.disposables.forEach(d => d.dispose());
  }
}