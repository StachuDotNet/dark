import * as vscode from "vscode";
import * as os from "os";

export class ScriptCommands {
  constructor(private isDebugMode: () => boolean) {}

  register(): vscode.Disposable[] {
    return [
      vscode.commands.registerCommand("darklang.runScript", async () => {
        const editor = vscode.window.activeTextEditor;
        if (!editor) {
          vscode.window.showErrorMessage("No active editor");
          return;
        }

        const uri = editor.document.uri;
        const scriptPath = uri.scheme === "file" ? uri.fsPath : "/tmp/darklang_script.dark";

        // For non-file schemes, write content to temp file
        if (uri.scheme !== "file") {
          const content = editor.document.getText();
          require("fs").writeFileSync(scriptPath, content);
        }

        let terminal = vscode.window.terminals.find(t => t.name === "darklang-terminal");
        if (!terminal) {
          terminal = vscode.window.createTerminal(`darklang-terminal`);
        }

        terminal.show(true);

        if (this.isDebugMode()) {
          let scriptCommand = `cd /home/dark/app && { TIMEFORMAT=$'Script executed in: %3lR'; time ./scripts/run-cli "${scriptPath}" --skip-self-update; }`;
          terminal.sendText(scriptCommand, true);
        } else {
          let scriptCommand = `cd ${os.homedir()} && { TIMEFORMAT=$'Script executed in: %3lR'; time darklang "${scriptPath}" --skip-self-update; }`;
          terminal.sendText(scriptCommand, true);
        }
      }),

      vscode.commands.registerCommand("darklang.init", async () => {
        try {
          const virtualUri = vscode.Uri.parse("dark:///package/MyApp.Sample");
          const document = await vscode.workspace.openTextDocument(virtualUri);
          await vscode.window.showTextDocument(document);
          vscode.window.showInformationMessage("Darklang workspace initialized!");
        } catch (error) {
          vscode.window.showErrorMessage(`Failed to initialize workspace: ${error}`);
        }
      })
    ];
  }
}