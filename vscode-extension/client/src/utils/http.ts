import * as http from "http";
import * as vscode from "vscode";

// Get configuration values from VS Code settings
function getEditorHost(): string {
  return vscode.workspace
    .getConfiguration("darklang")
    .get<string>("editorHost", "dark-editor.dlio.localhost");
}

function getEditorPort(): number {
  return vscode.workspace
    .getConfiguration("darklang")
    .get<number>("editorPort", 11001);
}

export const DARK_EDITOR_HOST = getEditorHost();
export const DARK_EDITOR_PORT = getEditorPort();

// Helper function to make HTTP requests to the dark-editor server
export function httpRequest(
  options: http.RequestOptions,
  body?: string,
): Promise<{ statusCode: number; data: string }> {
  return new Promise((resolve, reject) => {
    // If there's a body, ensure Content-Length is set to avoid chunked encoding
    if (body) {
      options.headers = {
        ...options.headers,
        "Content-Length": Buffer.byteLength(body).toString(),
      };
    }

    const req = http.request(options, res => {
      let data = "";
      res.on("data", chunk => {
        data += chunk;
      });
      res.on("end", () => {
        resolve({ statusCode: res.statusCode || 500, data });
      });
    });
    req.on("error", reject);
    if (body) {
      req.write(body);
    }
    req.end();
  });
}
