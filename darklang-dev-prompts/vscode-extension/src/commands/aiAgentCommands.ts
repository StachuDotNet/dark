import * as vscode from 'vscode';
import { DarkCLI } from '../utils/darkCli';

/**
 * AI Agent Commands for VS Code
 * Provides UI for AI-assisted development workflows
 */

export function registerAiAgentCommands(
    context: vscode.ExtensionContext,
    darkCli: DarkCLI
) {
    // AI code review for current patch
    context.subscriptions.push(
        vscode.commands.registerCommand('darklang.ai.review', async () => {
            try {
                const currentPatch = await darkCli.getCurrentPatch();
                
                if (!currentPatch) {
                    vscode.window.showWarningMessage('No current patch to review');
                    return;
                }
                
                const progressOptions = {
                    location: vscode.ProgressLocation.Notification,
                    title: "AI Code Review",
                    cancellable: false
                };
                
                await vscode.window.withProgress(progressOptions, async (progress) => {
                    progress.report({ message: "Analyzing code..." });
                    
                    const review = await darkCli.performAiCodeReview(currentPatch.id);
                    
                    if (review.success) {
                        await showAiCodeReviewResults(review.result);
                    } else {
                        vscode.window.showErrorMessage(`AI review failed: ${review.error}`);
                    }
                });
                
            } catch (error) {
                vscode.window.showErrorMessage(`AI review failed: ${error}`);
            }
        })
    );
    
    // AI test generation
    context.subscriptions.push(
        vscode.commands.registerCommand('darklang.ai.generateTests', async () => {
            try {
                const editor = vscode.window.activeTextEditor;
                
                if (!editor) {
                    vscode.window.showWarningMessage('No active editor');
                    return;
                }
                
                // Extract function name from cursor position or selection
                const functionName = await getFunctionNameAtCursor(editor);
                
                if (!functionName) {
                    const inputName = await vscode.window.showInputBox({
                        prompt: 'Enter function name to generate tests for',
                        placeHolder: 'functionName'
                    });
                    
                    if (!inputName) return;
                    
                    await generateTestsForFunction(inputName, darkCli);
                } else {
                    await generateTestsForFunction(functionName, darkCli);
                }
                
            } catch (error) {
                vscode.window.showErrorMessage(`Test generation failed: ${error}`);
            }
        })
    );
    
    // AI documentation generation
    context.subscriptions.push(
        vscode.commands.registerCommand('darklang.ai.generateDocs', async () => {
            try {
                const editor = vscode.window.activeTextEditor;
                
                if (!editor) {
                    vscode.window.showWarningMessage('No active editor');
                    return;
                }
                
                const functionName = await getFunctionNameAtCursor(editor);
                
                if (!functionName) {
                    const inputName = await vscode.window.showInputBox({
                        prompt: 'Enter function name to generate documentation for',
                        placeHolder: 'functionName'
                    });
                    
                    if (!inputName) return;
                    
                    await generateDocsForFunction(inputName, darkCli);
                } else {
                    await generateDocsForFunction(functionName, darkCli);
                }
                
            } catch (error) {
                vscode.window.showErrorMessage(`Documentation generation failed: ${error}`);
            }
        })
    );
    
    // AI code explanation
    context.subscriptions.push(
        vscode.commands.registerCommand('darklang.ai.explainCode', async () => {
            try {
                const editor = vscode.window.activeTextEditor;
                
                if (!editor) {
                    vscode.window.showWarningMessage('No active editor');
                    return;
                }
                
                const selection = editor.selection;
                const selectedText = editor.document.getText(selection);
                
                if (!selectedText) {
                    vscode.window.showWarningMessage('Please select code to explain');
                    return;
                }
                
                const progressOptions = {
                    location: vscode.ProgressLocation.Notification,
                    title: "AI Code Explanation",
                    cancellable: false
                };
                
                await vscode.window.withProgress(progressOptions, async (progress) => {
                    progress.report({ message: "Analyzing code..." });
                    
                    const explanation = await darkCli.explainCode(selectedText);
                    
                    if (explanation.success) {
                        await showCodeExplanation(selectedText, explanation.result);
                    } else {
                        vscode.window.showErrorMessage(`Code explanation failed: ${explanation.error}`);
                    }
                });
                
            } catch (error) {
                vscode.window.showErrorMessage(`Code explanation failed: ${error}`);
            }
        })
    );
    
    // AI bug fix suggestions
    context.subscriptions.push(
        vscode.commands.registerCommand('darklang.ai.suggestFix', async () => {
            try {
                const diagnostics = vscode.languages.getDiagnostics();
                const currentFile = vscode.window.activeTextEditor?.document.uri;
                
                if (!currentFile) {
                    vscode.window.showWarningMessage('No active file');
                    return;
                }
                
                const fileDiagnostics = diagnostics.find(([uri, _]) => uri.toString() === currentFile.toString());
                
                if (!fileDiagnostics || fileDiagnostics[1].length === 0) {
                    vscode.window.showInformationMessage('No errors found in current file');
                    return;
                }
                
                // Show error selection if multiple errors
                const errors = fileDiagnostics[1].map(diag => ({
                    label: diag.message,
                    description: `Line ${diag.range.start.line + 1}`,
                    diagnostic: diag
                }));
                
                const selectedError = await vscode.window.showQuickPick(errors, {
                    placeHolder: 'Select error to get AI fix suggestion'
                });
                
                if (!selectedError) return;
                
                const progressOptions = {
                    location: vscode.ProgressLocation.Notification,
                    title: "AI Fix Suggestion",
                    cancellable: false
                };
                
                await vscode.window.withProgress(progressOptions, async (progress) => {
                    progress.report({ message: "Analyzing error..." });
                    
                    const fixSuggestion = await darkCli.suggestFix(
                        selectedError.diagnostic.message,
                        await getCodeContext(currentFile, selectedError.diagnostic.range)
                    );
                    
                    if (fixSuggestion.success) {
                        await showFixSuggestion(selectedError.diagnostic, fixSuggestion.result);
                    } else {
                        vscode.window.showErrorMessage(`Fix suggestion failed: ${fixSuggestion.error}`);
                    }
                });
                
            } catch (error) {
                vscode.window.showErrorMessage(`Fix suggestion failed: ${error}`);
            }
        })
    );
    
    // AI agent configuration
    context.subscriptions.push(
        vscode.commands.registerCommand('darklang.ai.configure', async () => {
            try {
                const agents = await darkCli.getAiAgents();
                
                const actions = [
                    { label: 'Register New Agent', action: 'register' },
                    { label: 'Configure Existing Agent', action: 'configure' },
                    { label: 'View Agent Status', action: 'status' }
                ];
                
                const selectedAction = await vscode.window.showQuickPick(actions, {
                    placeHolder: 'Select AI agent action'
                });
                
                if (!selectedAction) return;
                
                switch (selectedAction.action) {
                    case 'register':
                        await registerNewAgent(darkCli);
                        break;
                    
                    case 'configure':
                        await configureExistingAgent(darkCli, agents);
                        break;
                    
                    case 'status':
                        await showAgentStatus(agents);
                        break;
                }
                
            } catch (error) {
                vscode.window.showErrorMessage(`AI configuration failed: ${error}`);
            }
        })
    );
    
    // Quick AI assist
    context.subscriptions.push(
        vscode.commands.registerCommand('darklang.ai.assist', async () => {
            try {
                const taskTypes = [
                    { label: 'Review Current Patch', description: 'AI code review', taskType: 'review' },
                    { label: 'Generate Tests', description: 'Create unit tests', taskType: 'test' },
                    { label: 'Generate Documentation', description: 'Create function docs', taskType: 'doc' },
                    { label: 'Explain Code', description: 'Explain selected code', taskType: 'explain' },
                    { label: 'Suggest Fix', description: 'Fix error or issue', taskType: 'fix' },
                    { label: 'Custom Request', description: 'Free-form AI assistance', taskType: 'custom' }
                ];
                
                const selectedTask = await vscode.window.showQuickPick(taskTypes, {
                    placeHolder: 'What would you like AI help with?'
                });
                
                if (!selectedTask) return;
                
                let prompt = '';
                
                if (selectedTask.taskType === 'custom') {
                    const customPrompt = await vscode.window.showInputBox({
                        prompt: 'Enter your request for AI assistance',
                        placeHolder: 'e.g., How can I optimize this function?'
                    });
                    
                    if (!customPrompt) return;
                    prompt = customPrompt;
                }
                
                const progressOptions = {
                    location: vscode.ProgressLocation.Notification,
                    title: "AI Assistant",
                    cancellable: false
                };
                
                await vscode.window.withProgress(progressOptions, async (progress) => {
                    progress.report({ message: "Processing request..." });
                    
                    const result = await darkCli.requestAiAssistance(selectedTask.taskType, prompt);
                    
                    if (result.success) {
                        await showAiAssistanceResult(selectedTask.label, result.result);
                    } else {
                        vscode.window.showErrorMessage(`AI assistance failed: ${result.error}`);
                    }
                });
                
            } catch (error) {
                vscode.window.showErrorMessage(`AI assistance failed: ${error}`);
            }
        })
    );
}

async function getFunctionNameAtCursor(editor: vscode.TextEditor): Promise<string | undefined> {
    const position = editor.selection.active;
    const document = editor.document;
    const lineText = document.lineAt(position).text;
    
    // Simple regex to extract function name (would be more sophisticated in real implementation)
    const functionMatch = lineText.match(/let\s+(\w+)\s*[=\(]/);
    return functionMatch ? functionMatch[1] : undefined;
}

async function generateTestsForFunction(functionName: string, darkCli: DarkCLI) {
    const progressOptions = {
        location: vscode.ProgressLocation.Notification,
        title: "AI Test Generation",
        cancellable: false
    };
    
    await vscode.window.withProgress(progressOptions, async (progress) => {
        progress.report({ message: `Generating tests for ${functionName}...` });
        
        const result = await darkCli.generateAiTests(functionName);
        
        if (result.success) {
            await showGeneratedTests(functionName, result.result);
        } else {
            vscode.window.showErrorMessage(`Test generation failed: ${result.error}`);
        }
    });
}

async function generateDocsForFunction(functionName: string, darkCli: DarkCLI) {
    const progressOptions = {
        location: vscode.ProgressLocation.Notification,
        title: "AI Documentation Generation",
        cancellable: false
    };
    
    await vscode.window.withProgress(progressOptions, async (progress) => {
        progress.report({ message: `Generating documentation for ${functionName}...` });
        
        const result = await darkCli.generateAiDocumentation(functionName);
        
        if (result.success) {
            await showGeneratedDocumentation(functionName, result.result);
        } else {
            vscode.window.showErrorMessage(`Documentation generation failed: ${result.error}`);
        }
    });
}

async function getCodeContext(uri: vscode.Uri, range: vscode.Range): Promise<string> {
    const document = await vscode.workspace.openTextDocument(uri);
    
    // Get context around the error (5 lines before and after)
    const startLine = Math.max(0, range.start.line - 5);
    const endLine = Math.min(document.lineCount - 1, range.end.line + 5);
    
    const contextRange = new vscode.Range(startLine, 0, endLine, document.lineAt(endLine).text.length);
    return document.getText(contextRange);
}

async function showAiCodeReviewResults(review: any) {
    const panel = vscode.window.createWebviewPanel(
        'aiCodeReview',
        'AI Code Review Results',
        vscode.ViewColumn.Beside,
        { enableScripts: true }
    );
    
    panel.webview.html = getCodeReviewHtml(review);
}

async function showCodeExplanation(code: string, explanation: any) {
    const panel = vscode.window.createWebviewPanel(
        'aiCodeExplanation',
        'AI Code Explanation',
        vscode.ViewColumn.Beside,
        { enableScripts: true }
    );
    
    panel.webview.html = getCodeExplanationHtml(code, explanation);
}

async function showFixSuggestion(diagnostic: vscode.Diagnostic, suggestion: any) {
    const action = await vscode.window.showInformationMessage(
        `AI suggests: ${suggestion.description}`,
        'Apply Fix',
        'View Details'
    );
    
    if (action === 'Apply Fix') {
        // Apply the suggested fix
        await applyFixSuggestion(diagnostic, suggestion);
    } else if (action === 'View Details') {
        // Show detailed fix explanation
        await showFixDetails(diagnostic, suggestion);
    }
}

async function showGeneratedTests(functionName: string, tests: any) {
    const panel = vscode.window.createWebviewPanel(
        'aiGeneratedTests',
        `Generated Tests - ${functionName}`,
        vscode.ViewColumn.Beside,
        { enableScripts: true }
    );
    
    panel.webview.html = getGeneratedTestsHtml(functionName, tests);
    
    // Handle messages from webview
    panel.webview.onDidReceiveMessage(async (message) => {
        switch (message.command) {
            case 'createPatch':
                // Create patch with generated tests
                break;
            case 'copyTests':
                await vscode.env.clipboard.writeText(message.testCode);
                vscode.window.showInformationMessage('Tests copied to clipboard');
                break;
        }
    });
}

async function showGeneratedDocumentation(functionName: string, docs: any) {
    const panel = vscode.window.createWebviewPanel(
        'aiGeneratedDocs',
        `Generated Documentation - ${functionName}`,
        vscode.ViewColumn.Beside,
        { enableScripts: true }
    );
    
    panel.webview.html = getGeneratedDocsHtml(functionName, docs);
}

async function registerNewAgent(darkCli: DarkCLI) {
    const name = await vscode.window.showInputBox({
        prompt: 'Enter agent name',
        placeHolder: 'My AI Assistant'
    });
    
    if (!name) return;
    
    const providers = [
        { label: 'Claude (Anthropic)', value: 'claude' },
        { label: 'GPT (OpenAI)', value: 'gpt' },
        { label: 'Gemini (Google)', value: 'gemini' },
        { label: 'Local Model', value: 'local' }
    ];
    
    const selectedProvider = await vscode.window.showQuickPick(providers, {
        placeHolder: 'Select AI provider'
    });
    
    if (!selectedProvider) return;
    
    if (selectedProvider.value === 'local') {
        const modelPath = await vscode.window.showInputBox({
            prompt: 'Enter path to local model',
            placeHolder: '/path/to/model'
        });
        
        if (!modelPath) return;
        
        const result = await darkCli.registerAiAgent(name, 'local', modelPath, '');
        
        if (result.success) {
            vscode.window.showInformationMessage(`Local AI agent '${name}' registered successfully`);
        } else {
            vscode.window.showErrorMessage(`Failed to register agent: ${result.error}`);
        }
    } else {
        const model = await vscode.window.showInputBox({
            prompt: 'Enter model name',
            placeHolder: 'claude-3-sonnet, gpt-4, gemini-pro'
        });
        
        if (!model) return;
        
        const apiKey = await vscode.window.showInputBox({
            prompt: 'Enter API key',
            placeHolder: 'sk-...',
            password: true
        });
        
        if (!apiKey) return;
        
        const result = await darkCli.registerAiAgent(name, selectedProvider.value, model, apiKey);
        
        if (result.success) {
            vscode.window.showInformationMessage(`AI agent '${name}' registered successfully`);
        } else {
            vscode.window.showErrorMessage(`Failed to register agent: ${result.error}`);
        }
    }
}

async function configureExistingAgent(darkCli: DarkCLI, agents: any[]) {
    if (agents.length === 0) {
        vscode.window.showInformationMessage('No AI agents registered');
        return;
    }
    
    const agentItems = agents.map(agent => ({
        label: agent.name,
        description: agent.provider,
        detail: `Status: ${agent.isActive ? 'Active' : 'Inactive'}`,
        agent: agent
    }));
    
    const selectedAgent = await vscode.window.showQuickPick(agentItems, {
        placeHolder: 'Select agent to configure'
    });
    
    if (!selectedAgent) return;
    
    const configOptions = [
        { label: 'Toggle Active Status', action: 'toggle' },
        { label: 'Update Capabilities', action: 'capabilities' },
        { label: 'Remove Agent', action: 'remove' }
    ];
    
    const selectedOption = await vscode.window.showQuickPick(configOptions, {
        placeHolder: 'Select configuration option'
    });
    
    if (!selectedOption) return;
    
    switch (selectedOption.action) {
        case 'toggle':
            const newStatus = !selectedAgent.agent.isActive;
            await darkCli.setAgentStatus(selectedAgent.agent.id, newStatus);
            vscode.window.showInformationMessage(
                `Agent ${selectedAgent.agent.name} ${newStatus ? 'activated' : 'deactivated'}`
            );
            break;
        
        case 'capabilities':
            // Show capability configuration UI
            break;
        
        case 'remove':
            const confirmRemove = await vscode.window.showWarningMessage(
                `Remove agent '${selectedAgent.agent.name}'?`,
                'Remove',
                'Cancel'
            );
            
            if (confirmRemove === 'Remove') {
                await darkCli.removeAiAgent(selectedAgent.agent.id);
                vscode.window.showInformationMessage(`Agent '${selectedAgent.agent.name}' removed`);
            }
            break;
    }
}

async function showAgentStatus(agents: any[]) {
    const panel = vscode.window.createWebviewPanel(
        'aiAgentStatus',
        'AI Agent Status',
        vscode.ViewColumn.Beside,
        { enableScripts: true }
    );
    
    panel.webview.html = getAgentStatusHtml(agents);
}

async function showAiAssistanceResult(taskLabel: string, result: any) {
    const panel = vscode.window.createWebviewPanel(
        'aiAssistanceResult',
        `AI Assistant - ${taskLabel}`,
        vscode.ViewColumn.Beside,
        { enableScripts: true }
    );
    
    panel.webview.html = getAiAssistanceHtml(taskLabel, result);
}

async function applyFixSuggestion(diagnostic: vscode.Diagnostic, suggestion: any) {
    // Implementation for applying fix suggestion
}

async function showFixDetails(diagnostic: vscode.Diagnostic, suggestion: any) {
    // Implementation for showing detailed fix explanation
}

// HTML generation functions (simplified)
function getCodeReviewHtml(review: any): string {
    return `<html><body><h1>AI Code Review</h1><p>Score: ${review.score}</p></body></html>`;
}

function getCodeExplanationHtml(code: string, explanation: any): string {
    return `<html><body><h1>Code Explanation</h1><pre>${code}</pre><p>${explanation.text}</p></body></html>`;
}

function getGeneratedTestsHtml(functionName: string, tests: any): string {
    return `<html><body><h1>Generated Tests for ${functionName}</h1><pre>${tests.code}</pre></body></html>`;
}

function getGeneratedDocsHtml(functionName: string, docs: any): string {
    return `<html><body><h1>Documentation for ${functionName}</h1><div>${docs.content}</div></body></html>`;
}

function getAgentStatusHtml(agents: any[]): string {
    return `<html><body><h1>AI Agent Status</h1><p>${agents.length} agents registered</p></body></html>`;
}

function getAiAssistanceHtml(taskLabel: string, result: any): string {
    return `<html><body><h1>${taskLabel}</h1><div>${result.response}</div></body></html>`;
}