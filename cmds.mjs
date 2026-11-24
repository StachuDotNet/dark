// usage: node cmds.mjs <label> <regex>  -- tuning attempts' Bash commands matching regex (measure set skipped)
import { readFileSync, readdirSync, existsSync } from 'node:fs';
const B = new URL('.', import.meta.url).pathname;
const [label, re] = process.argv.slice(2);
const measure = new Set((readFileSync(`${B}/lists/measure.txt`, 'utf8') + '\n' + readFileSync(`${B}/lists/measure2.txt`, 'utf8')).split('\n').filter(Boolean));
const rx = new RegExp(re);
for (const d of readdirSync(`${B}/results/${label}`).sort()) {
  if (measure.has(d.replace(/-\d+$/, ''))) continue;
  const f = `${B}/results/${label}/${d}/agent-0.jsonl`; if (!existsSync(f)) continue;
  for (const line of readFileSync(f, 'utf8').split('\n')) {
    let e; try { e = JSON.parse(line); } catch { continue; }
    if (e.type === 'assistant') for (const c of e.message.content || []) if (c.type === 'tool_use' && c.name === 'Bash') {
      for (const m of (c.input.command || '').matchAll(new RegExp(re, 'g'))) console.log(`${d}\t${m[0]}`);
    }
  }
}
