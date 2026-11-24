import { writeFile, mkdir, rename, readFile, readdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';

// Remove known secrets and common credential formats before saving.
export function redact(text) {
  let value = String(text);
  for (const [key, secret] of Object.entries(process.env)) {
    if (/(TOKEN|SECRET|PASSWORD|API_KEY|CREDENTIAL)/i.test(key) && secret?.length >= 6) value = value.split(secret).join('[REDACTED]');
  }
  return value.replace(/\b(?:sk-[A-Za-z0-9_-]{12,}|Bearer\s+[A-Za-z0-9._~-]+)/g, '[REDACTED]')
    .replace(/((?:api[_-]?key|access[_-]?token|refresh[_-]?token|password|authorization)\s*["']?\s*[:=]\s*["']?)[^\s,"'}]+/gi, '$1[REDACTED]');
}
export async function save(file, value) {
  await mkdir(path.dirname(file), { recursive: true });
  const text = typeof value === 'string' ? value : JSON.stringify(value, null, 2);
  await writeFile(`${file}.tmp`, redact(text));
  await rename(`${file}.tmp`, file);
}
export function combineUsage(items) {
  const keys = ['inputTokens', 'cachedInputTokens', 'cacheCreationTokens', 'outputTokens', 'reasoningTokens', 'modelCalls', 'costUsd'];
  return Object.fromEntries(keys.map(k => [k, items.length && items.every(u => typeof u[k] === 'number') ? items.reduce((s, u) => s + u[k], 0) : null]));
}
const median = values => { const sorted = values.sort((a, b) => a - b); const i = Math.floor(sorted.length / 2); return sorted.length ? (sorted.length % 2 ? sorted[i] : (sorted[i - 1] + sorted[i]) / 2) : null; };
export function summaries(runs) {
  const groups = new Map();
  for (const r of runs.filter(r => r.status !== 'queued' && r.status !== 'running')) {
    const key = JSON.stringify([r.task, r.agent, r.model, r.language, r.setupTrack, r.status, r.verification || 'acceptance-checks']);
    if (!groups.has(key)) groups.set(key, []); groups.get(key).push(r);
  }
  const result = [...groups.values()].map(rows => ({ task: rows[0].task, agent: rows[0].agent, model: rows[0].model, language: rows[0].language, setupTrack: rows[0].setupTrack, status: rows[0].status, verification: rows[0].verification || 'acceptance-checks',
    attempts: rows.length, completed: rows.filter(r => ['completed', 'passed'].includes(r.status)).length,
    totals: combineUsage(rows.map(r => r.usage)),
    medians: Object.fromEntries(['inputTokens', 'outputTokens', 'costUsd', 'modelCalls', 'durationMs'].map(k => [k, rows.every(r => typeof (k === 'durationMs' ? r[k] : r.usage[k]) === 'number') ? median(rows.map(r => k === 'durationMs' ? r[k] : r.usage[k])) : null])) }));

  result.sort((a, b) => (a.medians.durationMs ?? Infinity) - (b.medians.durationMs ?? Infinity));
  for (const dark of result.filter(s => s.language === 'Dark')) {
    const peers = result.filter(s => s.language !== 'Dark' && s.status === dark.status && s.task === dark.task && s.agent === dark.agent && s.model === dark.model && s.setupTrack === dark.setupTrack && s.verification === dark.verification);
    // Missing costs stay blank; a free or failed run cannot provide a cost ratio.
    dark.costComparisons = ['completed', 'passed'].includes(dark.status) && typeof dark.medians.costUsd === 'number'
      ? peers.filter(s => s.medians.costUsd > 0).map(other => ({ language: other.language, ratio: dark.medians.costUsd / other.medians.costUsd }))
      : [];
    // Failed runs still show their time, but they cannot establish a speed advantage.
    dark.speedComparisons = ['completed', 'passed'].includes(dark.status) && dark.medians.durationMs > 0
      ? peers.filter(s => s.medians.durationMs > 0)
        .map(other => ({ language: other.language, ratio: dark.medians.durationMs / other.medians.durationMs }))
      : [];
  }
  return result;
}
export async function exportsFor(batch) {
  const frozen = { schemaVersion: 1, sessionType: 'CLI sessions', id: batch.id, config: batch.config, environment: batch.environment, startedAt: batch.startedAt, finishedAt: batch.finishedAt,
    runs: batch.runs, summaries: summaries(batch.runs), analysisExcluded: true, logPolicy: 'Original CLI output with credential redaction; no synthetic usage.' };
  await save(path.join(batch.folder, 'results.json'), frozen);
  const columns = ['id', 'task', 'agent', 'model', 'language', 'repetition', 'setupTrack', 'status', 'verification', 'inputTokens', 'cachedInputTokens', 'outputTokens', 'reasoningTokens', 'modelCalls', 'userPrompts', 'costUsd', 'durationMs', 'manualInterventions'];
  const escape = v => `"${String(v ?? '').replaceAll('"', '""')}"`;
  const csv = [columns, ...batch.runs.map(r => columns.map(k => k === 'manualInterventions' ? r.interventions.length : r.usage?.[k] ?? r[k]))].map(row => row.map(escape).join(',')).join('\n');
  await save(path.join(batch.folder, 'results.csv'), csv + '\n');
  const bytes = await readFile(path.join(batch.folder, 'results.json'));
  const artifacts = {};
  async function inventory(folder) {
    for (const entry of await readdir(folder, { withFileTypes: true })) {
      if (['analysis', 'frozen.json'].includes(entry.name)) continue;
      const file = path.join(folder, entry.name);
      if (entry.isDirectory()) await inventory(file);
      else if (entry.isFile()) artifacts[path.relative(batch.folder, file)] = createHash('sha256').update(await readFile(file)).digest('hex');
    }
  }
  await inventory(batch.folder);
  await save(path.join(batch.folder, 'frozen.json'), { frozenAt: new Date().toISOString(), sha256: createHash('sha256').update(bytes).digest('hex'), artifacts });
  return frozen;
}
