// usage: node errors.mjs <label>  -- every dark call in tuning attempts whose output looks like an error: task, turn, command head, first error lines
import { readFileSync, readdirSync, existsSync } from 'node:fs';
const B = new URL('.', import.meta.url).pathname;
const label = process.argv[2];
const measure = new Set((readFileSync(`${B}/lists/measure.txt`, 'utf8') + '\n' + readFileSync(`${B}/lists/measure2.txt`, 'utf8')).split('\n').filter(Boolean));
const errRe = /error\[|Error:|error:|not found|permission denied|Static type check failed|Uncaught|nothing was saved|Unknown|Usage:|expected|Did you mean/i;
for (const d of readdirSync(`${B}/results/${label}`).sort()) {
  const task = d.replace(/-\d+$/, ''); if (measure.has(task)) continue;
  const f = `${B}/results/${label}/${d}/agent-0.jsonl`; if (!existsSync(f)) continue;
  const tools = new Map(), seen = new Set(); let turn = 0;
  for (const line of readFileSync(f, 'utf8').split('\n')) {
    let e; try { e = JSON.parse(line); } catch { continue; }
    if (e.type === 'assistant') { if (e.message?.id && !seen.has(e.message.id)) { seen.add(e.message.id); turn++; }
      for (const c of e.message.content || []) if (c.type === 'tool_use') tools.set(c.id, { c, turn }); }
    if (e.type === 'user' && Array.isArray(e.message?.content)) for (const c of e.message.content) if (c.type === 'tool_result') {
      const tu = tools.get(c.tool_use_id); if (!tu || tu.c.name !== 'Bash') continue;
      const t = typeof c.content === 'string' ? c.content : (c.content || []).map(x => x.text || '').join('\n');
      if (!errRe.test(t) && !c.is_error) continue;
      const lines = t.split('\n').filter(l => errRe.test(l)).slice(0, 3).map(l => l.trim().slice(0, 200));
      console.log(`${d} [${tu.turn}] $ ${(tu.c.input.command || '').replace(/\s+/g, ' ').slice(0, 160)}\n    ${lines.join('\n    ')}`);
    }
  }
}
