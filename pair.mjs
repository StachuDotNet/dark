// usage: node pair.mjs <darkLabel(s)> <pythonLabel(s)> [--md out.md]
// Pairs each task's Dark and Python sessions (labels may pool with +) and attributes every extra Dark
// call to a category (reflect.mjs `cats`): docs, lookup, save, fix, permission, server, other. Per task
// it takes the mean over attempts. Reads reflect.mjs --json output, so it quotes no transcript.
import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
const B = new URL('.', import.meta.url).pathname;
const args = process.argv.slice(2); const mi = args.indexOf('--md'); const md = mi >= 0 ? args.splice(mi, 2)[1] : null;
const [dark, py] = args;
const load = (l) => { const f = `/tmp/pair-${process.pid}-${l.replace(/\W/g, '_')}.json`; execFileSync('node', [`${B}/reflect.mjs`, l, '--json', f, '--md', '/dev/null']); return JSON.parse(readFileSync(f, 'utf8'))[0].perSession; };
const CATS = ['docs', 'lookup', 'save', 'fix', 'permission', 'server', 'other'];
const byTask = (ss) => { const g = {}; for (const s of ss) (g[s.task] ||= []).push(s); return Object.fromEntries(Object.entries(g).map(([t, xs]) => [t, Object.fromEntries([['T', xs.reduce((a, x) => a + x.T, 0) / xs.length], ...CATS.map(c => [c, xs.reduce((a, x) => a + (x.cats?.[c] || 0), 0) / xs.length])])])); };
const D = byTask(load(dark)), P = byTask(load(py));
const tasks = Object.keys(D).filter(t => P[t]).sort();
const sum = (m, k) => tasks.reduce((a, t) => a + m[t][k], 0);
const f1 = x => (Math.round(x * 10) / 10).toFixed(1);
const L = [];
L.push(`${dark} vs ${py}: ${tasks.length} tasks paired; Dark ${f1(sum(D, 'T'))} calls, Python ${f1(sum(P, 'T'))}, extra ${f1(sum(D, 'T') - sum(P, 'T'))}`);
L.push(`| category | Dark | Python | extra | share of extra |`); L.push('|---|---:|---:|---:|---:|');
const extra = sum(D, 'T') - sum(P, 'T');
for (const c of CATS) { const e = sum(D, c) - sum(P, c); L.push(`| ${c} | ${f1(sum(D, c))} | ${f1(sum(P, c))} | ${f1(e)} | ${Math.round(100 * e / extra)}% |`); }
L.push('', '| task | Dark | Python | extra | biggest extra |', '|---|---:|---:|---:|---|');
for (const t of tasks) { const top = CATS.map(c => [c, D[t][c] - P[t][c]]).sort((a, b) => b[1] - a[1])[0]; L.push(`| ${t} | ${f1(D[t].T)} | ${f1(P[t].T)} | ${f1(D[t].T - P[t].T)} | ${top[0]} ${f1(top[1])} |`); }
const out = L.join('\n'); if (md) writeFileSync(md, out); console.log(out);
