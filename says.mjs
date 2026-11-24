// says.mjs <label...>: assistant text lines that mention a limitation/workaround, tuning tasks only
import { readFileSync, readdirSync, existsSync } from 'node:fs';
const B = '/home/stachu/code/dark/bench-loop.bench';
const measure = new Set(readFileSync(`${B}/lists/measure.txt`, 'utf8').split('\n').filter(Boolean));
const re = /doesn't|does not|no way|can't|cannot|workaround|work around|unfortunately|isn't supported|not supported|missing|instead of|turns out|apparently|quirk|surpris|bug|lacks|no built-?in|isn't available|not available/i;
for (const label of process.argv.slice(2)) {
  if (!existsSync(`${B}/results/${label}`)) continue;
  for (const d of readdirSync(`${B}/results/${label}`).sort()) {
    const task = d.replace(/-\d+$/, ''); if (measure.has(task)) continue;
    const f = `${B}/results/${label}/${d}/agent-0.jsonl`; if (!existsSync(f)) continue;
    for (const line of readFileSync(f, 'utf8').split('\n')) {
      let e; try { e = JSON.parse(line); } catch { continue; }
      if (e.type !== 'assistant') continue;
      for (const c of e.message.content || []) if (c.type === 'text') {
        for (const s of c.text.split(/(?<=[.!?])\s+|\n/)) if (re.test(s) && s.length < 400) console.log(`${label}/${d}: ${s.trim()}`);
      }
    }
  }
}
