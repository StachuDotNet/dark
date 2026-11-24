// usage: node dump.mjs <label> <task> [n] [width]  -- a tuning attempt's tool calls and results, compact
// Refuses measure-set tasks: those are read only through summary.mjs.
import { readFileSync } from 'node:fs';
const B = new URL('.', import.meta.url).pathname;
const [label, task, n = '1', w = '400'] = process.argv.slice(2);
if ((readFileSync(`${B}/lists/measure.txt`, 'utf8') + '\n' + readFileSync(`${B}/lists/measure2.txt`, 'utf8')).split('\n').includes(task)) { console.error('measure-set task: not shown'); process.exit(2); }
const text = readFileSync(`${B}/results/${label}/${task}-${n}/agent-0.jsonl`, 'utf8');
const seen = new Set(); let turn = 0;
for (const line of text.split('\n')) {
  let e; try { e = JSON.parse(line); } catch { continue; }
  if (e.type === 'assistant') {
    if (e.message?.id && !seen.has(e.message.id)) { seen.add(e.message.id); turn++; }
    for (const c of e.message.content || []) {
      if (c.type === 'text' && c.text.trim()) console.log(`[${turn}] SAY: ${c.text.replace(/\s+/g, ' ').slice(0, +w)}`);
      if (c.type === 'tool_use') console.log(`[${turn}] ${c.name}: ${(c.input.command || c.input.file_path || JSON.stringify(c.input)).slice(0, +w * 2)}${c.input.content ? `\n   <content ${c.input.content.length} chars>` : ''}`);
    }
  }
  if (e.type === 'user' && Array.isArray(e.message?.content)) for (const c of e.message.content) if (c.type === 'tool_result') {
    const t = typeof c.content === 'string' ? c.content : (c.content || []).map(x => x.text || '').join('\n');
    console.log(`   -> ${t.slice(0, +w).replace(/\n/g, '\n      ')}`);
  }
  if (e.type === 'result') console.log(`RESULT: ${String(e.result).replace(/\s+/g, ' ').slice(0, +w * 2)}`);
}
