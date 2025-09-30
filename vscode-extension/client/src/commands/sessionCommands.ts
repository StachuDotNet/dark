import * as vscode from "vscode";
import { StatusBarManager } from "../ui/statusbar/statusBarManager";
import { SessionsTreeDataProvider } from "../providers/treeviews/sessionsTreeDataProvider";
import { ScenarioManager, DevelopmentScenario } from "../data/scenarioManager";

export class SessionCommands {
  private scenarioManager = ScenarioManager.getInstance();

  constructor(
    private statusBarManager: StatusBarManager,
    private sessionsProvider: SessionsTreeDataProvider
  ) {}

  register(): vscode.Disposable[] {
    return [
      vscode.commands.registerCommand("darklang.session.new", () => {
        vscode.window.showInputBox({ prompt: "Enter session name/intent" }).then(name => {
          if (name) {
            this.statusBarManager.updateSession(name);
            vscode.window.showInformationMessage(`Started new session: ${name}`);
          }
        });
      }),

      vscode.commands.registerCommand("darklang.session.switch", (session) => {
        const sessionLabel = session?.label || "clean-start: New project setup";

        // Map session labels to scenarios
        let scenario = DevelopmentScenario.CleanStart;
        if (sessionLabel.includes("stdlib-dev")) {
          scenario = DevelopmentScenario.ActiveDevelopment;
        } else if (sessionLabel.includes("multi-layer")) {
          scenario = DevelopmentScenario.ReadyForReview;
        } else if (sessionLabel.includes("conflict-fix")) {
          scenario = DevelopmentScenario.ConflictResolution;
        } else if (sessionLabel.includes("team-collab")) {
          scenario = DevelopmentScenario.TeamCollaboration;
        }

        // Switch to the scenario, which will trigger the tree refresh
        this.scenarioManager.setScenario(scenario);
        this.statusBarManager.updateSession(sessionLabel);
        vscode.window.showInformationMessage(`Switched to session: ${sessionLabel}`);
      }),

      vscode.commands.registerCommand("darklang.session.view", (session) => {
        const sessionId = session?.id || "current";
        const sessionLabel = session?.label || "Current Session";

        // Clean URL - the central system handles title, badge, etc.
        const virtualUri = vscode.Uri.parse(`dark:///session/${sessionId}?label=${encodeURIComponent(sessionLabel)}`);
        vscode.workspace.openTextDocument(virtualUri).then(doc => {
          vscode.window.showTextDocument(doc, {
            preview: false,
            preserveFocus: false
          });
          vscode.window.showInformationMessage(`Viewing session: ${sessionLabel}`);
        });
      }),

      vscode.commands.registerCommand("darklang.session.suspend", () => {
        vscode.window.showInformationMessage("Session suspended");
      }),

      vscode.commands.registerCommand("darklang.session.end", () => {
        vscode.window.showInformationMessage("Session ended");
      }),

      vscode.commands.registerCommand("darklang.session.export", () => {
        vscode.window.showInformationMessage("Exporting session...");
      }),

      vscode.commands.registerCommand("darklang.session.import", () => {
        vscode.window.showInformationMessage("Importing session...");
      }),

      vscode.commands.registerCommand("darklang.session.transfer", () => {
        vscode.window.showInformationMessage("Transferring session...");
      })
    ];
  }
}