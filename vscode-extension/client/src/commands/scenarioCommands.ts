import * as vscode from "vscode";
import { ScenarioManager, DevelopmentScenario } from "../data/scenarioManager";

export class ScenarioCommands {
  constructor(private scenarioManager: ScenarioManager) {}

  register(): vscode.Disposable[] {
    return [
      vscode.commands.registerCommand("darklang.scenario.switch", async () => {
        const scenarios = this.scenarioManager.getAllScenarios();
        const items = scenarios.map(scenario => ({
          label: scenario.name,
          description: scenario.description,
          scenario: scenario.id
        }));

        const selected = await vscode.window.showQuickPick(items, {
          placeHolder: "Select a development scenario",
          ignoreFocusOut: true
        });

        if (selected) {
          this.scenarioManager.setScenario(selected.scenario);
          vscode.window.showInformationMessage(`Switched to scenario: ${selected.label}`);
        }
      }),

      vscode.commands.registerCommand("darklang.scenario.cleanStart", () => {
        this.scenarioManager.setScenario(DevelopmentScenario.CleanStart);
        vscode.window.showInformationMessage("Scenario: Clean Start");
      }),

      vscode.commands.registerCommand("darklang.scenario.activeDevelopment", () => {
        this.scenarioManager.setScenario(DevelopmentScenario.ActiveDevelopment);
        vscode.window.showInformationMessage("Scenario: Active Development");
      }),

      vscode.commands.registerCommand("darklang.scenario.readyForReview", () => {
        this.scenarioManager.setScenario(DevelopmentScenario.ReadyForReview);
        vscode.window.showInformationMessage("Scenario: Ready for Review");
      }),

      vscode.commands.registerCommand("darklang.scenario.conflictResolution", () => {
        this.scenarioManager.setScenario(DevelopmentScenario.ConflictResolution);
        vscode.window.showInformationMessage("Scenario: Conflict Resolution");
      }),

      vscode.commands.registerCommand("darklang.scenario.teamCollaboration", () => {
        this.scenarioManager.setScenario(DevelopmentScenario.TeamCollaboration);
        vscode.window.showInformationMessage("Scenario: Team Collaboration");
      })
    ];
  }
}