// usage: node summary.mjs <a.jsonl> [b.jsonl] [--tasks t1,t2]
// One file: per task medians and totals. Two files: before (a) and after (b) side by side.
// Only counts, never transcript text, so it is safe on the measure set.
import { readFileSync } from 'node:fs';
const args = process.argv.slice(2);
const ti = args.indexOf('--tasks');
const only = ti >= 0 ? new Set(args.splice(ti, 2)[1].split(',')) : null;
const load = f => readFileSync(f, 'utf8').trim().split('\n').filter(Boolean).map(l => JSON.parse(l)).filter(r => !only || only.has(r.task));
const med = xs => { const s = xs.filter(x => x != null).sort((a, b) => a - b); if (!s.length) return null; const m = s.length >> 1; return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2; };
const done = r => /^completed/.test(r.status);
function byTask(rows) {
  const g = {};
  for (const r of rows) (g[r.task] ||= []).push(r);
  return Object.fromEntries(Object.entries(g).sort().map(([t, rs]) => [t, {
    n: rs.length, done: rs.filter(done).length, calls: med(rs.map(r => r.calls)), s: med(rs.map(r => r.s)), cost: med(rs.map(r => r.cost)),
    failedRuns: rs.reduce((a, r) => a + (r.failedRuns || 0), 0), fsx: rs.reduce((a, r) => a + (r.fsx || 0), 0), store: rs.filter(r => r.storeUse > 0).length, tests: rs.filter(r => r.wroteTest).length,
    statuses: [...new Set(rs.map(r => r.status))].join('/') }]));
}
function totals(rows) {
  const t = byTask(rows), v = Object.values(t);
  const sum = k => v.reduce((a, x) => a + (x[k] || 0), 0);
  return { tasks: v.length, attempts: rows.length, done: rows.filter(done).length, calls: sum('calls'), s: sum('s'), cost: +sum('cost').toFixed(2),
    failedRuns: rows.reduce((a, r) => a + (r.failedRuns || 0), 0), fsx: rows.reduce((a, r) => a + (r.fsx || 0), 0), spend: +rows.reduce((a, r) => a + (r.cost || 0), 0).toFixed(2),
    store: rows.filter(r => r.storeUse > 0).length, tests: rows.filter(r => r.wroteTest).length };
}
const pad = (x, w) => String(x ?? '-').padStart(w);
const [a, b] = args.map(load);
if (!b) {
  console.log(`${'task'.padEnd(22)}${pad('n', 3)}${pad('done', 5)}${pad('calls', 7)}${pad('time', 7)}${pad('cost', 7)}${pad('fail', 5)}${pad('fsx', 5)}${pad('store', 6)}${pad('tests', 6)}  status`);
  for (const [t, x] of Object.entries(byTask(a))) console.log(`${t.padEnd(22)}${pad(x.n, 3)}${pad(x.done, 5)}${pad(x.calls, 7)}${pad(x.s, 7)}${pad(x.cost?.toFixed(2), 7)}${pad(x.failedRuns, 5)}${pad(x.fsx, 5)}${pad(x.store, 6)}${pad(x.tests, 6)}  ${x.statuses}`);
  const T = totals(a);
  console.log(`TOTAL (sum of per-task medians): ${T.tasks} tasks, ${T.attempts} attempts, ${T.done} done, ${T.calls} calls, ${T.s} s, $${T.cost}; failed runs ${T.failedRuns}; F#-style mistakes ${T.fsx}; store ${T.store}, tests ${T.tests}; spend $${T.spend}`);
} else {
  const A = byTask(a), Bt = byTask(b);
  console.log(`${'task'.padEnd(22)}${pad('calls', 12)}${pad('time', 12)}${pad('cost', 14)}${pad('fail', 9)}${pad('fsx', 9)}${pad('done', 9)}`);
  for (const t of [...new Set([...Object.keys(A), ...Object.keys(Bt)])].sort()) {
    const x = A[t] || {}, y = Bt[t] || {};
    console.log(`${t.padEnd(22)}${pad(`${x.calls ?? '-'}>${y.calls ?? '-'}`, 12)}${pad(`${x.s ?? '-'}>${y.s ?? '-'}`, 12)}${pad(`${x.cost?.toFixed(2) ?? '-'}>${y.cost?.toFixed(2) ?? '-'}`, 14)}${pad(`${x.failedRuns ?? '-'}>${y.failedRuns ?? '-'}`, 9)}${pad(`${x.fsx ?? '-'}>${y.fsx ?? '-'}`, 9)}${pad(`${x.done ?? '-'}/${x.n ?? '-'}>${y.done ?? '-'}/${y.n ?? '-'}`, 9)}`);
  }
  const X = totals(a), Y = totals(b);
  for (const k of ['attempts', 'done', 'calls', 's', 'cost', 'failedRuns', 'fsx', 'store', 'tests', 'spend']) console.log(`${k.padEnd(12)}${pad(X[k], 10)}${pad(Y[k], 10)}`);
}
