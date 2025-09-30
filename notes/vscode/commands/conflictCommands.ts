import * as vscode from 'vscode';
import { DarkCLI, ConflictInfo } from '../utils/darkCli';
import { ConflictProvider } from '../providers/conflictProvider';
import { ConflictResolutionPanel } from '../webviews/conflictResolution';

export function registerConflictCommands(
    context: vscode.ExtensionContext,
    darkCli: DarkCLI,
    conflictProvider: ConflictProvider
) {
    // Resolve Conflicts Command (opens interactive panel)
    const resolveConflictsCommand = vscode.commands.registerCommand('darklang.conflicts.resolve', async () => {
        try {
            const conflicts = await darkCli.getConflicts();
            
            if (conflicts.length === 0) {
                vscode.window.showInformationMessage('🎉 No conflicts to resolve!');
                return;
            }

            // Open the conflict resolution webview panel
            ConflictResolutionPanel.createOrShow(context.extensionUri, darkCli, conflicts);
            
        } catch (error) {
            vscode.window.showErrorMessage(`Error loading conflicts: ${error}`);
        }
    });

    // Auto-Resolve Simple Conflicts Command
    const autoResolveCommand = vscode.commands.registerCommand('darklang.conflicts.auto', async () => {
        try {
            const conflicts = await darkCli.getConflicts();
            const autoResolvableConflicts = conflicts.filter(c => c.canAutoResolve);
            
            if (autoResolvableConflicts.length === 0) {
                vscode.window.showInformationMessage('No conflicts can be auto-resolved at this time.');
                return;
            }

            const confirmation = await vscode.window.showWarningMessage(
                `Auto-resolve ${autoResolvableConflicts.length} simple conflict${autoResolvableConflicts.length > 1 ? 's' : ''}?\n\n` +
                'This will automatically apply resolution strategies for:\n' +
                autoResolvableConflicts.map(c => `• ${c.type}: ${c.description}`).join('\n'),
                { modal: true },
                'Auto Resolve',
                'Cancel'
            );

            if (confirmation === 'Auto Resolve') {
                await vscode.window.withProgress({
                    location: vscode.ProgressLocation.Notification,
                    title: 'Auto-resolving conflicts',
                    cancellable: false
                }, async (progress) => {
                    progress.report({ message: 'Analyzing conflicts...' });
                    
                    await darkCli.autoResolveConflicts();
                    
                    progress.report({ message: 'Complete!' });
                });

                const remainingConflicts = await darkCli.getConflicts();
                const resolvedCount = conflicts.length - remainingConflicts.length;
                
                if (remainingConflicts.length === 0) {
                    vscode.window.showInformationMessage(
                        `🎉 Successfully auto-resolved all ${resolvedCount} conflicts!`
                    );
                } else {
                    vscode.window.showInformationMessage(
                        `✅ Auto-resolved ${resolvedCount} conflicts. ${remainingConflicts.length} require manual review.`,
                        'Review Remaining'
                    ).then(selection => {
                        if (selection === 'Review Remaining') {
                            vscode.commands.executeCommand('darklang.conflicts.resolve');
                        }
                    });
                }
                
                // Refresh conflict provider
                conflictProvider.refresh();
            }

        } catch (error) {
            vscode.window.showErrorMessage(`Error auto-resolving conflicts: ${error}`);
        }
    });

    // List All Conflicts Command (shows in quick pick)
    const listConflictsCommand = vscode.commands.registerCommand('darklang.conflicts.list', async () => {
        try {
            const conflicts = await darkCli.getConflicts();
            
            if (conflicts.length === 0) {
                vscode.window.showInformationMessage('🎉 No conflicts detected!');
                return;
            }

            const items = conflicts.map(conflict => ({
                label: `${getSeverityIcon(conflict.severity)} ${conflict.type}`,
                description: conflict.description,
                detail: `Patches: ${conflict.patches.join(', ')} • ${conflict.canAutoResolve ? 'Auto-resolvable' : 'Manual review required'}`,
                conflict: conflict
            }));

            const selected = await vscode.window.showQuickPick(items, {
                placeHolder: 'Select a conflict to resolve',
                matchOnDescription: true,
                matchOnDetail: true
            });

            if (selected) {
                // Open detailed resolution for specific conflict
                await showConflictResolutionOptions(selected.conflict, darkCli, conflictProvider);
            }

        } catch (error) {
            vscode.window.showErrorMessage(`Error listing conflicts: ${error}`);
        }
    });

    // Show Conflict Plan Command
    const showPlanCommand = vscode.commands.registerCommand('darklang.conflicts.plan', async () => {
        try {
            const conflicts = await darkCli.getConflicts();
            
            if (conflicts.length === 0) {
                vscode.window.showInformationMessage('🎉 No conflicts to plan for!');
                return;
            }

            // Generate resolution plan
            const plan = generateResolutionPlan(conflicts);
            
            // Show plan in a new document
            const doc = await vscode.workspace.openTextDocument({
                content: plan,
                language: 'markdown'
            });
            
            await vscode.window.showTextDocument(doc);

        } catch (error) {
            vscode.window.showErrorMessage(`Error generating conflict plan: ${error}`);
        }
    });

    // Resolve Single Conflict Command (used by tree view)
    const resolveSingleConflictCommand = vscode.commands.registerCommand('darklang.conflicts.resolveSingle', async (conflictId: string) => {
        try {
            const conflicts = await darkCli.getConflicts();
            const conflict = conflicts.find(c => c.id === conflictId);
            
            if (!conflict) {
                vscode.window.showErrorMessage(`Conflict ${conflictId} not found`);
                return;
            }

            await showConflictResolutionOptions(conflict, darkCli, conflictProvider);

        } catch (error) {
            vscode.window.showErrorMessage(`Error resolving conflict: ${error}`);
        }
    });

    // Register all commands
    context.subscriptions.push(
        resolveConflictsCommand,
        autoResolveCommand,
        listConflictsCommand,
        showPlanCommand,
        resolveSingleConflictCommand
    );
}

async function showConflictResolutionOptions(
    conflict: ConflictInfo,
    darkCli: DarkCLI,
    conflictProvider: ConflictProvider
) {
    const strategies = getResolutionStrategies(conflict);
    
    const items = strategies.map(strategy => ({
        label: strategy.label,
        description: strategy.description,
        detail: strategy.detail,
        strategy: strategy.value,
        isDestructive: strategy.isDestructive
    }));

    const selected = await vscode.window.showQuickPick(items, {
        placeHolder: `Choose resolution strategy for: ${conflict.description}`,
        matchOnDescription: true
    });

    if (!selected) {
        return;
    }

    // Show warning for destructive actions
    if (selected.isDestructive) {
        const confirmation = await vscode.window.showWarningMessage(
            `This action will ${selected.detail}. This cannot be undone.`,
            { modal: true },
            'Continue',
            'Cancel'
        );

        if (confirmation !== 'Continue') {
            return;
        }
    }

    try {
        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: `Resolving conflict: ${selected.label}`,
            cancellable: false
        }, async (progress) => {
            progress.report({ message: 'Applying resolution...' });
            
            await darkCli.resolveConflict(conflict.id, selected.strategy);
            
            progress.report({ message: 'Complete!' });
        });

        vscode.window.showInformationMessage(
            `✅ Conflict resolved using: ${selected.label}`,
            'View Results'
        ).then(selection => {
            if (selection === 'View Results') {
                // Could open diff view or show affected files
                vscode.commands.executeCommand('darklang.conflicts.list');
            }
        });

        // Refresh conflict provider
        conflictProvider.refresh();

    } catch (error) {
        vscode.window.showErrorMessage(`Failed to resolve conflict: ${error}`);
    }
}

function getResolutionStrategies(conflict: ConflictInfo) {
    const strategies = [];

    if (conflict.type === 'Same Function Different Implementation') {
        strategies.push(
            {
                label: '🔄 Three-Way Merge',
                description: 'Attempt intelligent merge',
                detail: 'Try to merge both changes automatically',
                value: 'three-way',
                isDestructive: false
            },
            {
                label: '📥 Keep Local Changes',
                description: 'Use your local implementation',
                detail: 'discard the remote changes',
                value: 'keep-local',
                isDestructive: true
            },
            {
                label: '☁️ Keep Remote Changes',
                description: 'Use the incoming implementation',
                detail: 'discard your local changes',
                value: 'keep-remote',
                isDestructive: true
            },
            {
                label: '✏️ Manual Resolution',
                description: 'Resolve manually in editor',
                detail: 'open merge editor for manual resolution',
                value: 'manual',
                isDestructive: false
            }
        );
    } else if (conflict.type === 'Name Collision') {
        strategies.push(
            {
                label: '🏷️ Rename Both',
                description: 'Keep both with different names (recommended)',
                detail: 'rename both entities to avoid collision',
                value: 'rename-both',
                isDestructive: false
            },
            {
                label: '📥 Keep Local Only',
                description: 'Keep your version',
                detail: 'discard the remote version',
                value: 'keep-local',
                isDestructive: true
            },
            {
                label: '☁️ Keep Remote Only',
                description: 'Keep the incoming version',
                detail: 'discard your local version',
                value: 'keep-remote',
                isDestructive: true
            }
        );
    } else if (conflict.type === 'Deleted Dependency') {
        strategies.push(
            {
                label: '🔍 Manual Review',
                description: 'Review dependency usage',
                detail: 'analyze the impact of dependency deletion',
                value: 'manual',
                isDestructive: false
            },
            {
                label: '📥 Keep Local (Restore)',
                description: 'Restore the deleted dependency',
                detail: 'undo the deletion and keep the dependency',
                value: 'keep-local',
                isDestructive: true
            },
            {
                label: '☁️ Keep Remote (Delete)',
                description: 'Proceed with deletion',
                detail: 'delete the dependency and update dependents',
                value: 'keep-remote',
                isDestructive: true
            }
        );
    } else {
        // Generic strategies
        strategies.push(
            {
                label: '📥 Keep Local Changes',
                description: 'Use your changes',
                detail: 'keep your local changes',
                value: 'keep-local',
                isDestructive: true
            },
            {
                label: '☁️ Keep Remote Changes',
                description: 'Use incoming changes',
                detail: 'use the remote changes',
                value: 'keep-remote',
                isDestructive: true
            },
            {
                label: '✏️ Manual Resolution',
                description: 'Resolve manually',
                detail: 'require manual intervention',
                value: 'manual',
                isDestructive: false
            }
        );
    }

    return strategies;
}

function getSeverityIcon(severity: string): string {
    switch (severity) {
        case 'high':
            return '🔴';
        case 'medium':
            return '🟡';
        case 'low':
            return '🟢';
        default:
            return '❓';
    }
}

function generateResolutionPlan(conflicts: ConflictInfo[]): string {
    const highPriority = conflicts.filter(c => c.severity === 'high');
    const mediumPriority = conflicts.filter(c => c.severity === 'medium');
    const lowPriority = conflicts.filter(c => c.severity === 'low');
    const autoResolvable = conflicts.filter(c => c.canAutoResolve);

    let plan = `# Conflict Resolution Plan\n\n`;
    plan += `**Total conflicts:** ${conflicts.length}\n`;
    plan += `**Auto-resolvable:** ${autoResolvable.length}\n`;
    plan += `**Require manual review:** ${conflicts.length - autoResolvable.length}\n\n`;

    plan += `## Recommended Resolution Order\n\n`;

    plan += `### 1. Auto-Resolve Simple Conflicts\n`;
    plan += `Run \`darklang.conflicts.auto\` to automatically resolve ${autoResolvable.length} simple conflicts:\n`;
    autoResolvable.forEach(c => {
        plan += `- ${getSeverityIcon(c.severity)} ${c.type}: ${c.description}\n`;
    });
    plan += `\n**Estimated time:** 1-2 minutes\n\n`;

    if (highPriority.length > 0) {
        plan += `### 2. High Priority Conflicts (${highPriority.length})\n`;
        plan += `These require immediate attention:\n`;
        highPriority.forEach(c => {
            plan += `- **${c.type}**: ${c.description}\n`;
            plan += `  - Patches: ${c.patches.join(', ')}\n`;
            plan += `  - Recommended: Manual review\n`;
        });
        plan += `\n**Estimated time:** 15-30 minutes per conflict\n\n`;
    }

    if (mediumPriority.length > 0) {
        plan += `### 3. Medium Priority Conflicts (${mediumPriority.length})\n`;
        mediumPriority.forEach(c => {
            plan += `- **${c.type}**: ${c.description}\n`;
            plan += `  - ${c.canAutoResolve ? 'Auto-resolvable' : 'Manual review needed'}\n`;
        });
        plan += `\n**Estimated time:** 5-15 minutes per conflict\n\n`;
    }

    if (lowPriority.length > 0) {
        plan += `### 4. Low Priority Conflicts (${lowPriority.length})\n`;
        lowPriority.forEach(c => {
            plan += `- **${c.type}**: ${c.description}\n`;
        });
        plan += `\n**Estimated time:** 2-5 minutes per conflict\n\n`;
    }

    const totalTime = (highPriority.length * 22.5) + (mediumPriority.length * 10) + (lowPriority.length * 3.5) + 1.5;
    plan += `## Summary\n\n`;
    plan += `**Total estimated time:** ${Math.ceil(totalTime)} minutes\n\n`;
    plan += `### Next Steps\n`;
    plan += `1. Run auto-resolution for simple conflicts\n`;
    plan += `2. Address high-priority conflicts first\n`;
    plan += `3. Review and resolve remaining conflicts\n`;
    plan += `4. Test changes after resolution\n`;
    plan += `5. Push resolved changes to team\n`;

    return plan;
}