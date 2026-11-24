// usage: node rescore.mjs <label>  -- recompute token totals and metrics.mjs fields of results/<label>.jsonl from the saved transcripts
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { metrics, tokens } from './metrics.mjs';
const B = new URL('.', import.meta.url).pathname, label = process.argv[2];
const f = `${B}/results/${label}.jsonl`;
const rows = readFileSync(f, 'utf8').trim().split('\n').map(l => JSON.parse(l)).map(r => {
  const t = `${r.out}/agent-0.jsonl`;
  return existsSync(t) ? { ...r, ...tokens(readFileSync(t, 'utf8')), ...metrics(readFileSync(t, 'utf8')) } : r;
});
writeFileSync(f, rows.map(r => JSON.stringify(r)).join('\n') + '\n');
