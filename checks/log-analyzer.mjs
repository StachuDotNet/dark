import { execSync } from 'node:child_process';
const nums = (v, out = []) => { if (typeof v === 'number') out.push(v); else if (v && typeof v === 'object') Object.values(v).forEach(x => nums(x, out)); return out; };
export default async (c) => {
  execSync(`python3 /home/stachu/code/dark/bench-loop.bench/tasks/seeds/log-analyzer-check/gen.py ${c.tmp}`);
  const exp = JSON.parse(execSync(`cat ${c.tmp}/expected.json`).toString())['access.log'];
  const flat = nums(exp);
  const text = c.run(['access.log'], { timeout: 120000 });
  const js = c.run(['--json', 'access.log'], { timeout: 120000 });
  let j = null; try { j = JSON.parse(js.out.slice(js.out.indexOf('{'))); } catch {}
  const got = j ? new Set(nums(j)) : new Set();
  const textNums = new Set((text.out.match(/\d+(?:\.\d+)?/g) || []).map(Number));
  const pick = (k) => { const f = (v) => v && typeof v === 'object' ? (k in v ? v[k] : Object.values(v).map(f).find(x => x !== undefined)) : undefined; return f(exp); };
  const want = ['requests', 'malformed', 'median_ms', 'p95_ms'].map(pick).filter(x => typeof x === 'number');
  return [
    { name: 'runs on 10,000 lines', ok: text.code === 0 && !text.timedOut, got: `${text.code} ${text.err.slice(0, 150)}` },
    { name: '--json parses', ok: !!j, got: js.out.slice(0, 150) + js.err.slice(0, 100) },
    { name: 'headline numbers in the JSON', ok: want.length > 0 && want.every(x => got.has(x)), got: `want ${want} ` },
    { name: 'headline numbers in the text', ok: want.length > 0 && want.every(x => textNums.has(x)), got: `want ${want}` },
    { name: 'most expected numbers present', ok: flat.filter(x => got.has(x)).length >= 0.8 * flat.length, got: `${flat.filter(x => got.has(x)).length}/${flat.length}` },
  ];
};
