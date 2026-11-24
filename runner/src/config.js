import path from 'node:path';
import os from 'node:os';
import { existsSync } from 'node:fs';
import { access, readFile, realpath, stat, rm } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { command } from './process.js';
import { prepareDarkRuntime } from './dark-runtime.js';
import { defaultTasks } from './tasks.js';

const executablePath = name => (process.env.PATH || '').split(path.delimiter).map(folder => path.join(folder, name)).find(existsSync) || name;

export const defaults = {
  agents: [{ agent: 'codex', path: executablePath('codex'), model: '' }, { agent: 'claude', path: executablePath('claude'), model: '' }],
  languages: ['Dark'], darkPath: process.env.DARK_PATH || '', darkBuild: '',
  pythonPath: executablePath('python3'), nodePath: process.execPath,
  repetitions: 1, minutes: 45, outputFolder: path.resolve('results'),
  workspaceRoot: path.join(os.tmpdir(), 'language-benchmark-workspaces'),
  tasks: defaultTasks,
  documentation: { Dark: '' },
  setupTrack: 'ready-environment'
};
export function selectedTasks(config) {
  // Older saved configurations had one task. They still work when copied or queued.
  const tasks = config.tasks ?? (config.task ? [config.task] : []);
  if (!Array.isArray(tasks)) throw Error('Tasks must be a list.');
  // A page or saved configuration from before network sites existed leaves them out; use the template's.
  return tasks.filter(task => task?.enabled !== false).map(task => {
    if (task?.networkSites !== undefined) return task;
    const template = defaultTasks.find(t => t.templateId === task?.templateId || t.name === task?.name);
    return template?.networkSites ? { ...task, networkSites: template.networkSites } : task;
  });
}
export function validate(config) {
  if (!Array.isArray(config.agents) || !config.agents.length) throw Error('Select at least one agent.');
  for (const a of config.agents) if (!(['codex', 'claude'].includes(a.agent) || a.agent.startsWith('dark-agent')) || !a.path || typeof a.model !== 'string') throw Error('Provide an executable and model setting for each agent.');
  if (!config.languages?.length || config.languages.some(l => !['Dark', 'Python', 'TypeScript'].includes(l))) throw Error('Select supported languages.');
  for (const [key, min, max] of [['repetitions', 1, 100], ['minutes', 0.01, 1440]]) {
    if (!Number.isFinite(config[key]) || config[key] < min || config[key] > max || (key !== 'minutes' && !Number.isInteger(config[key]))) throw Error(`Invalid ${key}.`);
  }
  if (config.setupTrack !== 'ready-environment') throw Error('First-time installation measurements are not implemented yet. Use a ready environment.');
  const tasks = selectedTasks(config);
  if (!tasks.length) throw Error('Include at least one task.');
  const names = new Set();
  for (const task of tasks) {
    if (typeof task?.name !== 'string' || !task.name.trim() || typeof task.prompt !== 'string' || !task.prompt.trim()) throw Error('Add a name and prompt for each included task.');
    if (names.has(task.name)) throw Error('Give each included task a different name.');
    if (task.networkSites !== undefined && (!Array.isArray(task.networkSites) || task.networkSites.some(site => typeof site !== 'string' || !/^(\*\.)?[a-z0-9-]+(\.[a-z0-9-]+)+$/i.test(site)))) throw Error(`Network sites for ${task.name} must be host names such as api.github.com.`);
    names.add(task.name);
  }
  if (config.languages.includes('Dark') && !config.darkPath) throw Error('Provide the Dark executable path before running Dark.');
  if (!path.isAbsolute(config.workspaceRoot) || !path.isAbsolute(config.outputFolder)) throw Error('Use absolute paths for workspaces and output.');
}
const hash = content => createHash('sha256').update(content).digest('hex');
async function identity(file) {
  const resolved = await realpath(file); const info = await stat(resolved);
  return { path: file, resolved, bytes: info.size, sha256: hash(await readFile(resolved)) };
}
export async function preflight(config, signal, batchRuntime = null) {
  validate(config);
  // Use temporary Dark copies for previews so checks do not open the original store.
  const temporary = config.languages.includes('Dark') && !batchRuntime ? await prepareDarkRuntime(config) : null;
  try { return await inspect(config, signal, batchRuntime || temporary); }
  finally { if (temporary) await rm(temporary.root, { recursive: true, force: true }); }
}
async function inspect(config, signal, runtime) {
  validate(config);
  // Resolve parent symlinks before checking whether the project would be inside a checkout.
  let parent = config.workspaceRoot;
  while (true) { try { parent = await realpath(parent); break; } catch { const next = path.dirname(parent); if (next === parent) throw Error('Cannot resolve workspace root.'); parent = next; } }
  for (let current = parent; ; current = path.dirname(current)) {
    try { await access(path.join(current, '.git')); throw Error('Workspaces must be outside a Git checkout.'); } catch (e) { if (e.message.startsWith('Workspaces')) throw e; }
    for (const file of ['AGENTS.md', 'CLAUDE.md']) { try { await access(path.join(current, file)); throw Error(`Workspace ancestor contains ${file}: ${current}`); } catch (e) { if (e.message.startsWith('Workspace ancestor')) throw e; } }
    if (path.dirname(current) === current) break;
  }
  const probes = [];
  const probe = async (name, executable, args, required = []) => {
    const r = await command(executable, args, { signal, env: runtime?.env, timeoutMs: 180000 });
    probes.push({ name, executable, args, ...r });
    if (r.code !== 0 || r.error || r.timedOut) throw Error(`${name} is not ready: ${r.error || r.stderr || r.stdout}`);
    if (required.some(flag => !r.stdout.includes(flag))) throw Error(`${name} lacks a required CLI option. Review its saved help output.`);
    return r.stdout.trim();
  };
  const agents = [];
  for (const a of config.agents) {
    const version = await probe(`${a.agent} version`, a.path, ['--version']);
    // dark-agent arms are the Dark binary: check it has the agent command, not Claude's flags
    if (a.agent.startsWith('dark-agent')) await probe(`${a.agent} help`, a.path, ['help', 'agent'], ['--output-format']);
    else await probe(`${a.agent} help`, a.path, a.agent === 'codex' ? ['exec', '--help'] : ['--help'], a.agent === 'codex' ? ['--json', '--sandbox', '--skip-git-repo-check', '--add-dir'] : ['--output-format', '--permission-prompts', 'acceptEdits', '--settings', '--allowedTools', '--add-dir']);
    agents.push({ ...a, version, binary: await identity(a.path) });
  }
  const languages = {};
  for (const l of config.languages) {
    const executable = l === 'Dark' ? runtime.binaryPath : l === 'Python' ? defaults.pythonPath : defaults.nodePath;
    languages[l] = { executable, version: await probe(`${l} version`, executable, ['--version']), binary: await identity(executable), buildType: l === 'Dark' ? config.darkBuild || null : null };
    if (l === 'Dark') await probe('Dark help', executable, ['--help']);
    if (l === 'TypeScript') {
      // Record available tools; let the agent choose how to run TypeScript.
      languages[l].tooling = [];
      for (const name of ['tsc', 'tsx', 'ts-node', 'deno', 'bun']) {
        const toolPath = executablePath(name);
        if (!path.isAbsolute(toolPath)) continue;
        const result = await command(toolPath, ['--version'], { signal });
        probes.push({ name: `${name} version`, executable: toolPath, args: ['--version'], ...result });
        languages[l].tooling.push({ executable: toolPath, version: result.code === 0 && !result.error ? result.stdout.trim() : null });
      }
    }
  }
  // Save instruction file paths and hashes. Do not save login files or full user settings.
  const instructions = [];
  for (const file of [path.join(os.homedir(), '.codex/AGENTS.md'), path.join(os.homedir(), '.claude/CLAUDE.md')]) {
    try { const content = await readFile(file); instructions.push({ path: file, sha256: hash(content) }); } catch {}
  }
  return { agents, languages, probes, instructions, runtime, inheritedConfiguration: true,
    caveat: 'User settings, memory, hooks and plugins remain active. This inventory is not exhaustive.' };
}
