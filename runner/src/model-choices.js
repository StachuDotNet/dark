import { readFile, readdir } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

// Claude's CLI has no command that lists models, and its aliases ('opus', 'sonnet') move to newer
// models over time. Pinned IDs keep repeated batches on the same model; update this list for new releases.
const CLAUDE_MODELS = [
  ['claude-fable-5-1', 'Fable 5.1'],
  ['claude-opus-5-5', 'Opus 5.5'],
  ['claude-opus-5-5[1m]', 'Opus 5.5 (1M context)'],
  ['claude-sonnet-5-5', 'Sonnet 5.5'],
  ['claude-haiku-5-5', 'Haiku 5.5'],
].map(([value, name]) => ({ value, label: `${name} · ${value}` }));
const CLAUDE_ALIASES = { fable: 'claude-fable-5-1', opus: 'claude-opus-5-5', sonnet: 'claude-sonnet-5-5', haiku: 'claude-haiku-5-5' };
const codexHome = () => process.env.CODEX_HOME || path.join(os.homedir(), '.codex');
const readJson = async file => { try { return JSON.parse(await readFile(file, 'utf8')); } catch { return null; } };

// The model each CLI uses when none is given. Claude's comes from its settings. Codex's model cache
// changes as models roll out, so without a config.toml model we report what its last default attempt used.
async function defaultModels(batches) {
  let codex = null;
  try { codex = (await readFile(path.join(codexHome(), 'config.toml'), 'utf8')).match(/^model\s*=\s*"([^"]+)"/m)?.[1] ?? null; } catch {}
  codex ??= batches.flatMap(b => b.runs).filter(r => r.agent === 'codex' && r.model === '(CLI default)' && r.modelUsed && r.startedAt)
    .sort((a, b) => b.startedAt.localeCompare(a.startedAt))[0]?.modelUsed ?? null;
  const setting = process.env.ANTHROPIC_MODEL || (await readJson(path.join(os.homedir(), '.claude', 'settings.json')))?.model || null;
  const alias = setting?.match(/^(\w+)(\[1m\])?$/);
  const claude = alias && CLAUDE_ALIASES[alias[1]] ? CLAUDE_ALIASES[alias[1]] + (alias[2] || '') : setting;
  return { codex, claude };
}

// Codex's JSON output names no model, but its own session log does.
export async function codexSessionModel(sessionId) {
  if (!sessionId) return null;
  try {
    const root = path.join(codexHome(), 'sessions');
    const file = (await readdir(root, { recursive: true })).find(f => f.endsWith(`${sessionId}.jsonl`));
    return file ? (await readFile(path.join(root, file), 'utf8')).match(/"model":"([^"]+)"/)?.[1] ?? null : null;
  } catch { return null; }
}

// The model an attempt actually used, as reported by the CLI.
export async function modelUsed(run) {
  const segment = run.segments?.[0];
  if (!segment) return null;
  return run.agent === 'codex' ? codexSessionModel(segment.sessionId) : segment.reportedModels?.[0] ?? null;
}

// Read CLI metadata only. No provider requests, authentication files or paid sessions.
export async function modelChoices(batches = []) {
  const cache = await readJson(path.join(codexHome(), 'models_cache.json'));
  const codex = (Array.isArray(cache?.models) ? cache.models : []).filter(m => m.visibility === 'list' && typeof m.slug === 'string')
    .sort((a, b) => (a.priority ?? Infinity) - (b.priority ?? Infinity)).map(m => ({ value:m.slug, label:m.display_name || m.slug }));
  return { codex, claude: CLAUDE_MODELS, defaults: await defaultModels(batches) };
}
