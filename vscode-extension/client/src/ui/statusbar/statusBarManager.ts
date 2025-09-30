import * as vscode from "vscode";

export interface StatusData {
  instance: { name: string };
  session: { name: string };
  patch?: { intent: string };
  sync: {
    hasConflicts: boolean;
    summary: string;
    outgoing: number;
    incoming: number;
  };
  user: string;
}

export class StatusBarManager {
  private statusBarItems: vscode.StatusBarItem[] = [];
  private currentData: StatusData;

  constructor() {
    this.currentData = this.createDemoData();
    this.createStatusBarItems();
    this.updateDisplay();
  }

  private createDemoData(): StatusData {
    return {
      instance: { name: "Local" },
      session: { name: "feature-auth" },
      patch: { intent: "Add user validation" },
      sync: {
        hasConflicts: true,
        summary: "2↑ 1↓",
        outgoing: 2,
        incoming: 1
      },
      user: "stachu"
    };
  }

  private createStatusBarItems(): void {
    this.statusBarItems = [
      vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100),
      vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 99),
      vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 98),
      vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 97),
      vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 96)
    ];
  }

  private updateDisplay(): void {
    const { instance, session, patch, sync, user } = this.currentData;

    this.statusBarItems[0].text = `$(server) ${instance.name}`;
    this.statusBarItems[0].tooltip = "Click to switch Darklang instance";
    this.statusBarItems[0].command = "darklang.instance.switch";

    this.statusBarItems[1].text = `$(target) ${session.name}`;
    this.statusBarItems[1].tooltip = "Current session - click to manage";
    this.statusBarItems[1].command = "darklang.session.switch";

    if (patch) {
      this.statusBarItems[2].text = `$(git-branch) Draft: "${patch.intent}"`;
      this.statusBarItems[2].tooltip = "Current patch - click to manage";
      this.statusBarItems[2].command = "darklang.patch.view";
    } else {
      this.statusBarItems[2].text = `$(git-branch) No active patch`;
      this.statusBarItems[2].tooltip = "Click to create a new patch";
      this.statusBarItems[2].command = "darklang.patch.create";
    }

    if (sync.hasConflicts) {
      this.statusBarItems[3].text = `$(warning) ${sync.summary}`;
      this.statusBarItems[3].tooltip = `Sync: ${sync.outgoing} outgoing, ${sync.incoming} incoming patches. Click to resolve conflicts.`;
      this.statusBarItems[3].command = "darklang.conflicts.list";
      this.statusBarItems[3].backgroundColor = new vscode.ThemeColor("statusBarItem.errorBackground");
    } else {
      this.statusBarItems[3].text = `$(sync) ${sync.summary}`;
      this.statusBarItems[3].tooltip = `Sync: ${sync.outgoing} outgoing, ${sync.incoming} incoming patches`;
      this.statusBarItems[3].command = "darklang.sync.status";
      this.statusBarItems[3].backgroundColor = undefined;
    }

    this.statusBarItems[4].text = `$(account) ${user}`;
    this.statusBarItems[4].tooltip = "Current user";
    this.statusBarItems[4].command = "darklang.user.switch";

    this.statusBarItems.forEach(item => item.show());
  }

  public updateData(newData: Partial<StatusData>): void {
    this.currentData = { ...this.currentData, ...newData };
    this.updateDisplay();
  }

  public updateSession(sessionName: string): void {
    this.updateData({ session: { name: sessionName } });
  }

  public updatePatch(patchIntent?: string): void {
    this.updateData({
      patch: patchIntent ? { intent: patchIntent } : undefined
    });
  }

  public updateSync(sync: Partial<StatusData["sync"]>): void {
    this.updateData({
      sync: { ...this.currentData.sync, ...sync }
    });
  }

  public updateConflicts(hasConflicts: boolean): void {
    this.updateSync({ hasConflicts });
  }

  public dispose(): void {
    this.statusBarItems.forEach(item => item.dispose());
  }

  public getCurrentData(): StatusData {
    return { ...this.currentData };
  }

  public simulateCollaborationScenarios(): void {
    const scenarios = [
      {
        name: "Active Development",
        data: {
          session: { name: "feature-auth" },
          patch: { intent: "Add user validation" },
          sync: { hasConflicts: false, summary: "1↑ 0↓", outgoing: 1, incoming: 0 }
        }
      },
      {
        name: "Conflict Resolution",
        data: {
          session: { name: "user-improvements" },
          patch: { intent: "Fix profile updates" },
          sync: { hasConflicts: true, summary: "2↑ 3↓", outgoing: 2, incoming: 3 }
        }
      },
      {
        name: "Team Sync",
        data: {
          session: { name: "team-session-alpha" },
          patch: undefined,
          sync: { hasConflicts: false, summary: "0↑ 5↓", outgoing: 0, incoming: 5 }
        }
      }
    ];

    let currentScenario = 0;
    setInterval(() => {
      const scenario = scenarios[currentScenario];
      this.updateData(scenario.data);
      console.log(`Status bar demo: ${scenario.name}`);
      currentScenario = (currentScenario + 1) % scenarios.length;
    }, 10000); // Change scenario every 10 seconds for demo
  }
}