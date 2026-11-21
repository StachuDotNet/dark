import * as vscode from "vscode";
import { LanguageClient, LanguageClientOptions, ServerOptions, TransportKind } from "vscode-languageclient/node";

let client: LanguageClient | undefined;

// Package items open as `darkfs:/Owner/Module/name.fn.dark`. The server reads them from the store,
// so the editor always shows what the store has now.
class DarkFileSystem implements vscode.FileSystemProvider {
  private changed = new vscode.EventEmitter<vscode.FileChangeEvent[]>();
  readonly onDidChangeFile = this.changed.event;

  // The store moved: every open item may read differently now.
  storeChanged() {
    const events = vscode.workspace.textDocuments
      .filter((d) => d.uri.scheme === "darkfs")
      .map((d) => ({ type: vscode.FileChangeType.Changed, uri: d.uri }));
    if (events.length > 0) this.changed.fire(events);
  }

  stat(_uri: vscode.Uri): vscode.FileStat {
    return { type: vscode.FileType.File, ctime: 0, mtime: Date.now(), size: 0, permissions: vscode.FilePermission.Readonly };
  }

  async readFile(uri: vscode.Uri): Promise<Uint8Array> {
    if (!client) throw vscode.FileSystemError.Unavailable("the Darklang language server is not running");
    const text: string | null = await client.sendRequest("dark/fs/read", { uri: uri.toString() });
    if (text === null) throw vscode.FileSystemError.FileNotFound(uri);
    return new TextEncoder().encode(text);
  }

  watch(): vscode.Disposable {
    return new vscode.Disposable(() => {});
  }
  readDirectory(): [string, vscode.FileType][] {
    return [];
  }
  createDirectory(): void {
    throw vscode.FileSystemError.NoPermissions("the store has no directories to make");
  }
  writeFile(uri: vscode.Uri): void {
    throw vscode.FileSystemError.NoPermissions(uri);
  }
  delete(uri: vscode.Uri): void {
    throw vscode.FileSystemError.NoPermissions(uri);
  }
  rename(uri: vscode.Uri): void {
    throw vscode.FileSystemError.NoPermissions(uri);
  }
}

function startClient(fs: DarkFileSystem): LanguageClient {
  const command = vscode.workspace.getConfiguration("darklang").get<string>("path") || "dark";
  const run = { command, args: ["lsp"], transport: TransportKind.stdio };
  const serverOptions: ServerOptions = { run, debug: run };
  const clientOptions: LanguageClientOptions = {
    documentSelector: [
      { scheme: "file", language: "darklang" },
      { scheme: "untitled", language: "darklang" },
      { scheme: "darkfs", language: "darklang" },
    ],
  };
  const c = new LanguageClient("darklang", "Darklang", serverOptions, clientOptions);
  c.onNotification("dark/storeChanged", () => fs.storeChanged());
  c.start();
  return c;
}

export function activate(context: vscode.ExtensionContext) {
  const fs = new DarkFileSystem();
  context.subscriptions.push(
    vscode.workspace.registerFileSystemProvider("darkfs", fs, { isCaseSensitive: true, isReadonly: true }),
    vscode.commands.registerCommand("darklang.restartServer", async () => {
      await client?.stop();
      client = startClient(fs);
    }),
  );
  client = startClient(fs);
}

export function deactivate(): Thenable<void> | undefined {
  return client?.stop();
}
