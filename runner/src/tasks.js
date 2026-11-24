import { readFile } from 'node:fs/promises';

// Keep the short task descriptions in their own files.
const taskNames = ['countdown-timer', 'even-or-odd-cli', 'fizzbuzz', 'github-stats', 'hangman', 'password-gen', 'todo-list-cli', 'unit-convert', 'url-shortener-cli'];
export const defaultTasks = await Promise.all(taskNames.map(async name => ({
  templateId: name, name, enabled: false,
  prompt: (await readFile(new URL(`../benchmark-tasks/${name}.md`, import.meta.url), 'utf8')).trim(),
  source: { file: `benchmark-tasks/${name}.md` }
})));
// Sites a task needs to reach. Each agent's sandbox allows only these; everything else stays offline.
defaultTasks.find(task => task.name === 'github-stats').networkSites = ['api.github.com', 'github.com'];
// Show all nine choices, but don't silently turn a small configuration into a large batch.
defaultTasks.find(task => task.name === 'even-or-odd-cli').enabled = true;
