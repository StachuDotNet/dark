import { workspace, ExtensionContext, commands, window, Uri } from "vscode";
import * as os from "os";
import * as vscode from "vscode";
import { SemanticTokensFeature } from "vscode-languageclient/lib/common/semanticTokens";
import {
  LanguageClient,
  LanguageClientOptions,
  ServerOptions,
  Trace,
  TransportKind,
} from "vscode-languageclient/node";

import { ServerBackedTreeDataProvider } from "./providers/treeviews/ServerBackedTreeDataProvider";
import { ComprehensiveDarkContentProvider } from "./providers/comprehensiveDarkContentProvider";
import { StatusBarManager } from "./ui/statusbar/statusBarManager";
import { SessionsTreeDataProvider } from "./providers/treeviews/sessionsTreeDataProvider";
import { InstancesTreeDataProvider } from "./providers/treeviews/instancesTreeDataProvider";
import { EnhancedPackagesTreeDataProvider } from "./providers/treeviews/enhancedPackagesTreeDataProvider";
import { WelcomeViewProvider } from "./providers/treeviews/welcomeViewProvider";
import { CurrentViewProvider } from "./providers/treeviews/currentViewProvider";
import { SessionTreeDataProvider } from "./providers/treeviews/sessionTreeDataProvider";
import { PatchReviewPanel, ConflictResolutionPanel } from "./panels";
import {
  PatchCommands,
  SessionCommands,
  ConflictCommands,
  PackageCommands,
  SyncCommands,
  ScriptCommands,
  RefreshCommands,
  ScenarioCommands
} from "./commands";
import { InstanceCommands } from "./commands/instanceCommands";
import { ScenarioManager } from "./data/scenarioManager";
import { DarklangFileDecorationProvider } from "./providers/fileDecorationProvider";
import { DarklangHomePanel } from "./panels/darklangHomePanel";

let client: LanguageClient;
let statusBarManager: StatusBarManager;
let sessionsProvider: SessionsTreeDataProvider;
let instancesProvider: InstancesTreeDataProvider;
let packagesProvider: EnhancedPackagesTreeDataProvider;
let contentProvider: ComprehensiveDarkContentProvider;
let fileDecorationProvider: DarklangFileDecorationProvider;

interface LSPFileResponse {
  content: string;
}

export function activate(context: ExtensionContext) {
  console.log('🚀 Darklang VS Code extension is activating...');

  const isDebugMode = () => process.env.VSCODE_DEBUG_MODE === "true";

  const sharedServerOptions = {
    options: { cwd: "/home/dark/app" },
    command: "bash",
    args: [
      isDebugMode ? "./scripts/run-cli" : "darklang",
      "run",
      "@Darklang.LanguageTools.LspServer.runServerCli",
      "()", // 'parses' to () - TODO clean this up once we switch over to new parser
    ],
    transport: TransportKind.stdio,
  };
  const serverOptions: ServerOptions = {
    run: sharedServerOptions,
    debug: sharedServerOptions,
  };

  const clientOptions: LanguageClientOptions = {
    documentSelector: [
      { scheme: "file", language: "darklang" },
      { scheme: "dark", language: "darklang" },
    ],
    synchronize: {
      fileEvents: workspace.createFileSystemWatcher("**/*.dark"),
    },

    // in the window that has the extension loaded, go to the Output tab,
    // and select this option in the dropdown to find corresponding logs
    traceOutputChannel: vscode.window.createOutputChannel(
      "Darklang LSP - Client",
    ),

    // without this, VS Code will try to restart our extension 5 times,
    // which can be really annoying while debugging
    connectionOptions: {
      cancellationStrategy: null,
      maxRestartCount: 0,
    },
  };

  // DISABLED: LSP client to avoid crashes during UI development
  // client = new LanguageClient(
  //   "darklangLsp",
  //   "Darklang LSP - Server",
  //   serverOptions,
  //   clientOptions,
  // );
  // client.registerFeature(new SemanticTokensFeature(client));
  // client.trace = Trace.Verbose;

  // Initialize status bar manager
  statusBarManager = new StatusBarManager();
  context.subscriptions.push(statusBarManager);

  // Initialize content provider for dark:// URLs
  contentProvider = new ComprehensiveDarkContentProvider();
  const contentProviderRegistration = workspace.registerTextDocumentContentProvider("dark", contentProvider);
  context.subscriptions.push(contentProviderRegistration);

  // Initialize file decoration provider for badges
  fileDecorationProvider = new DarklangFileDecorationProvider();
  const decorationProviderRegistration = vscode.window.registerFileDecorationProvider(fileDecorationProvider);
  context.subscriptions.push(decorationProviderRegistration);

  // Register home page command and open it automatically
  context.subscriptions.push(
    vscode.commands.registerCommand('darklang.openHome', () => {
      DarklangHomePanel.createOrShow(context.extensionUri);
    })
  );

  // Auto-open home page when extension activates
  setTimeout(() => {
    vscode.commands.executeCommand('darklang.openHome');
  }, 1000); // Small delay to ensure everything is initialized

  // Register webview panel serializer for home page
  if (vscode.window.registerWebviewPanelSerializer) {
    vscode.window.registerWebviewPanelSerializer(DarklangHomePanel.viewType, {
      async deserializeWebviewPanel(webviewPanel: vscode.WebviewPanel, state: any) {
        DarklangHomePanel.revive(webviewPanel, context.extensionUri);
      }
    });
  }

  // Initialize tree data providers
  packagesProvider = new EnhancedPackagesTreeDataProvider();
  sessionsProvider = new SessionsTreeDataProvider();
  instancesProvider = new InstancesTreeDataProvider();

  // Create tree views
  console.log('📊 Creating tree views...');

  const sessionsView = vscode.window.createTreeView("darklangSessions", {
    treeDataProvider: sessionsProvider,
    showCollapseAll: true,
  });

  const instancesView = vscode.window.createTreeView("darklangInstances", {
    treeDataProvider: instancesProvider,
    showCollapseAll: true,
  });

  const packagesView = vscode.window.createTreeView("darklangPackages", {
    treeDataProvider: packagesProvider,
    showCollapseAll: true,
  });

  // Register welcome view provider
  const welcomeProvider = new WelcomeViewProvider(context.extensionUri);
  context.subscriptions.push(
    vscode.window.registerWebviewViewProvider(WelcomeViewProvider.viewType, welcomeProvider)
  );

  console.log('✅ Tree views created successfully');

  // DISABLED: Legacy tree view that depends on LSP client
  // const legacyTreeDataProvider = new ServerBackedTreeDataProvider(client);
  // const legacyView = vscode.window.createTreeView(`darklangTreeView`, {
  //   treeDataProvider: legacyTreeDataProvider,
  //   showCollapseAll: true,
  // });

  context.subscriptions.push(sessionsView, instancesView, packagesView);

  // DISABLED: LSP client start
  // client.start();

  // Initialize scenario manager and command handlers
  const scenarioManager = ScenarioManager.getInstance();
  const scriptCommands = new ScriptCommands(isDebugMode);
  const packageCommands = new PackageCommands();
  const patchCommands = new PatchCommands(context, statusBarManager);
  const sessionCommands = new SessionCommands(statusBarManager, sessionsProvider);
  const instanceCommands = new InstanceCommands(statusBarManager, instancesProvider);
  const conflictCommands = new ConflictCommands(context, statusBarManager);
  const syncCommands = new SyncCommands(statusBarManager);
  const refreshCommands = new RefreshCommands(null, packagesProvider);
  const scenarioCommands = new ScenarioCommands(scenarioManager);

  // Register all commands
  const allPatchCommands = patchCommands.register();
  const allSessionCommands = sessionCommands.register();
  const allInstanceCommands = instanceCommands.register();
  const allConflictCommands = conflictCommands.register();
  const allSyncCommands = syncCommands.register();
  const allScriptCommands = scriptCommands.register();
  const allPackageCommands = packageCommands.register();
  const allRefreshCommands = refreshCommands.register();
  const allScenarioCommands = scenarioCommands.register();

  // Register all commands with context
  context.subscriptions.push(
    ...allScriptCommands,
    ...allPackageCommands,
    ...allPatchCommands,
    ...allSessionCommands,
    ...allInstanceCommands,
    ...allConflictCommands,
    ...allSyncCommands,
    ...allRefreshCommands,
    ...allScenarioCommands
  );

  // Initialize with clean start scenario
  console.log('🎭 Initializing with scenario-based demo data...');
  scenarioManager.setScenario(scenarioManager.currentScenario); // Trigger initial data load

  console.log('🎉 Darklang VS Code extension fully activated!');
}

export function deactivate(): Thenable<void> | undefined {
  // DISABLED: LSP client deactivation
  // if (!client) {
  //   return undefined;
  // }
  // return client.stop();
  return undefined;
}
