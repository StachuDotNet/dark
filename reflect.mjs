// usage: node reflect.mjs <label>... [--md out.md] [--json out.json] [--timeline label/task-n] [--examples N]
// Turns bench transcripts into: where turns go, failure classes, the first wrong turn, what each
// failure costs to recover from, the tokens tool output adds to every later turn, and (with several
// labels) how all of that moves between runs.
// Measure-set tasks are counted, never quoted: no examples, commands or timelines from them.
import { readFileSync, readdirSync, existsSync, writeFileSync } from 'node:fs';

const B = new URL('.', import.meta.url).pathname;
const args = process.argv.slice(2);
const opt = (k, d) => { const i = args.indexOf(k); if (i < 0) return d; const v = args[i + 1]; args.splice(i, 2); return v; };
const mdOut = opt('--md'), jsonOut = opt('--json'), timelineOf = opt('--timeline'), nEx = +opt('--examples', 3);
const labels = args;
const kindOf = {};
for (const set of readdirSync(`${B}/tasks`)) { const d = `${B}/tasks/${set}`; try { for (const f of readdirSync(d)) if (f.endsWith('.md')) {
  const t = readFileSync(`${d}/${f}`, 'utf8'); const k = (t.match(/\*\*Kind:\*\*\s*([^\n]*)/)?.[1] || '').toLowerCase();
  kindOf[f.slice(0, -3)] = /http server|web service|server/.test(k + (t.match(/\*\*Areas:\*\*([^\n]*)/)?.[1] || '').toLowerCase().replace(/http client.*/, '')) && /server/.test(k) ? 'server' : /existing code|seed/.test(k) ? 'existing code' : /network|http client|api client/.test(k) ? 'network client' : 'new CLI/library'; } } catch {} }
const measure = new Set(readdirSync(`${B}/lists`).filter(f => /^measure/.test(f)).flatMap(f => readFileSync(`${B}/lists/${f}`, 'utf8').split('\n').map(s => s.trim()).filter(s => s && !s.startsWith('#'))));
const seqOut = args.includes('--seq') ? (args.splice(args.indexOf('--seq'), 1), true) : false;

// ---- classifying one tool call ----
const DARK = String.raw`(?:"?\$\{?D\w*\}?"?|\S*/dark|(?<![\w/-])dark)`;
const darkSub = new RegExp(`${DARK}\\s+((?:-[-\\w]+(?:=\\S+)?\\s+)*)([a-z][\\w-]*)(?:\\s+([a-z][\\w-]*))?`, 'g');
const SAVE = new Set(['module', 'fn', 'val', 'type', 'edit', 'patch', 'add', 'rename', 'move', 'mv', 'delete', 'rm', 'typecheck', 'check', 'deprecate', 'trait', 'impl']);
const PHASE_OF = sub => SAVE.has(sub) ? 'author'
  : sub === 'test' ? 'test'
  : ['run', 'serve', 'http-server', 'run-script'].includes(sub) ? 'run'
  : sub === 'eval' ? 'probe'
  : ['permissions', 'grant', 'allow'].includes(sub) ? 'perms'
  : ['status', 'discard', 'commit', 'log', 'diff', 'constraints', 'branch', 'undo', 'history', 'sync', 'init'].includes(sub) ? 'store'
  : ['search', 'view', 'ls', 'find', 'tree', 'deps', 'refs', 'usages', 'callers', 'show', 'signatures', 'trace', 'traces'].includes(sub) ? 'lookup'
  : ['docs', 'help', '--help', 'version', '--version'].includes(sub) ? 'docs' : 'other-dark';
const KNOWN = new Set([...SAVE, 'test', 'run', 'serve', 'http-server', 'run-script', 'eval', 'permissions', 'grant', 'allow', 'status', 'discard', 'commit', 'log', 'diff', 'constraints', 'branch', 'undo', 'history', 'sync', 'init',
  'search', 'view', 'ls', 'find', 'tree', 'deps', 'refs', 'usages', 'callers', 'show', 'signatures', 'trace', 'traces', 'docs', 'help', 'version', 'format', 'fmt', 'config', 'install', 'update', 'scripts']);
const RANK = ['author', 'test', 'run', 'probe', 'perms', 'store', 'lookup', 'docs', 'other-dark', 'shell', 'orient', 'meta'];

function classify(tu) {
  const n = tu.name, i = tu.input || {};
  if (n === 'Write' || n === 'Edit' || n === 'MultiEdit' || n === 'NotebookEdit') return { phase: 'author', subs: [], what: `${n} ${short(i.file_path)}`, writeChars: (i.content || i.new_string || '').length, file: i.file_path };
  if (n === 'Read' || n === 'Glob' || n === 'Grep' || n === 'LS') return { phase: 'orient', subs: [], what: `${n} ${short(i.file_path || i.pattern || i.path)}` };
  if (n !== 'Bash' && n !== 'BashOutput') return { phase: 'meta', subs: [], what: n };
  const cmd = i.command || '';
  const subs = [];
  for (const m of cmd.matchAll(darkSub)) {
    let s = m[2];
    if (!KNOWN.has(s)) continue;
    if (s === 'docs' && m[3] === 'signatures') s = 'signatures';
    if (s === 'test' && m[3] === 'add') s = 'add';
    subs.push(s);
  }
  const phases = subs.map(PHASE_OF);
  if (/\bcurl\b|\bwget\b|\bnc\b /.test(cmd)) phases.push('run');
  if (/<<\s*'?EOF'?[\s\S]*\btest\s+\w+\s*=/.test(cmd)) phases.push('test');
  if (!phases.length) phases.push(/^\s*(ls|cat|head|tail|find|tree|pwd|wc|file)\b/.test(cmd) ? 'orient' : 'shell');
  const phase = RANK.find(r => phases.includes(r));
  return { phase, subs, what: cmd };
}
const short = s => String(s || '').replace(/.*\//, '');

// ---- classifying what came back ----
// Only Dark's own diagnostics count, not the program's own error output, and never docs text.
const ERR = [
  ['foreign-syntax', /separates [a-z -]+ with commas|a pair is one argument|a triple is one argument|invalid number literal: \d+\w*L\b|write `Ok\(x\)`|take no type annotation|`let` at the top level|there is no `(?:::|@|\+\+)`|must use 'val'; 'let' is reserved/],
  ['parse', /error\[(?:LEX|PARSE)|Parse error at line|Parse error:/],
  ['stdlib-name', /Not found: Stdlib\.|Error: Stdlib\.[\w.]+ not found|'Stdlib\.[\w.]+' is unresolved|Not in the standard library, so nothing was saved/],
  ['own-name', /'Me\.[\w.]+' is unresolved|Not found: Me\.|Error: Me\.[\w.]+ not found|Unresolved(?:Value|Fn|Type)Name\] '(?!Stdlib)/],
  ['store-model', /\bMe\.(\w+)\.Me\.\1\b|Expressions are not allowed in package files|only allowed in package files|is not a module in the store|nothing to commit|has no such item/],
  ['type', /\[TypeMismatch\]|Type check failed|Static type check failed|\[ArgumentCount|\[WrongArity|expected \d+ arguments?, got/],
  ['permission', /permission denied(?: by instance policy)?:|Explicitly denied:/],
  ['sandbox-net', /connect to [\d.]+ port \d+[^\n]*failed|Couldn't connect to server|Failed to connect to/],
  ['cli-usage', /^Usage: (?:dark |run-script|module|serve|commit|test|fn|eval|search|view|docs|permissions|edit|delete|typecheck)\b/m],
  ['runtime', /Encountered a Runtime Error|Uncaught exception(?!: permission)|Script error:(?! permission)|\[HttpServer\] internal error/],
  ['test-fail', /\d+ tests?[^\n]*: \d+ passed, [1-9]\d* failed|failed [1-9]\d*, incomplete|^\s*FAIL Me\./m],
  ['harness', /bwrap: |Exit code 143|Command timed out|timed out after/],
  ['shell', /command not found|: No such file or directory$/m],
];
const DARK_ERR = new Set(['foreign-syntax', 'parse', 'stdlib-name', 'own-name', 'store-model', 'type', 'permission', 'cli-usage', 'runtime', 'test-fail']);
function outcome(phase, text, isErr, cmd) {
  if (phase === 'docs' || phase === 'lookup' || phase === 'orient') return { errs: [], line: '' };
  const errs = ERR.filter(([k, re]) => re.test(text) && !(k === 'cli-usage' && /\bhelp\b|--help/.test(cmd))).map(([k]) => k);
  if (errs.includes('foreign-syntax') && !errs.includes('parse')) errs.push('parse');
  if (errs.includes('store-model')) { const k = errs.indexOf('own-name'); if (k >= 0) errs.splice(k, 1); }
  let line = '';
  const PREF = { 'stdlib-name': /Not found: Stdlib|Stdlib\.[\w.]+ not found|'Stdlib\./, type: /\[TypeMismatch\]|\[\w+\]/, permission: /is not allowed|not allowed by|needs? (?:read|write|an? )/ };
  if (errs.length) { const re = ERR.find(([k]) => k === errs[0])[1]; line = (PREF[errs[0]] && text.split('\n').find(l => PREF[errs[0]].test(l) && !/permission denied/.test(l))) || text.split('\n').find(l => re.test(l)) || text.split('\n').find(l => /error|denied|not found|failed/i.test(l)) || ''; }
  return { errs, line: line.trim().slice(0, 180), isErr };
}

// ---- one session ----
const toks = chars => Math.round(chars / 4);
function session(label, dir) {
  const task = dir.replace(/-\d+$/, ''), n = +dir.match(/-(\d+)$/)[1];
  const f = `${B}/results/${label}/${dir}/agent-0.jsonl`;
  if (!existsSync(f)) return null;
  const tools = new Map(), msgs = new Map(), turnsOrder = [];
  const calls = [];
  let finalText = '', wall = 0, t0 = null, t1 = null;
  for (const line of readFileSync(f, 'utf8').split('\n')) {
    let e; try { e = JSON.parse(line); } catch { continue; }
    if (e.type === 'assistant' && !e.parent_tool_use_id) {
      const id = e.message?.id; if (!id) continue;
      if (!msgs.has(id)) { msgs.set(id, { turn: msgs.size + 1, ctx: 0, out: 0, text: '' }); turnsOrder.push(id); }
      const m = msgs.get(id), u = e.message.usage || {};
      m.ctx = (u.input_tokens || 0) + (u.cache_read_input_tokens || 0) + (u.cache_creation_input_tokens || 0);
      m.out = Math.max(m.out, u.output_tokens || 0);
      for (const c of e.message.content || []) {
        if (c.type === 'text') m.text += c.text;
        if (c.type === 'tool_use') { const k = classify(c); const call = { turn: m.turn, tool: c.name, ...k, resChars: 0, errs: [], line: '', t0: Date.parse(e.timestamp) }; tools.set(c.id, call); calls.push(call); }
      }
    }
    if (e.type === 'user' && Array.isArray(e.message?.content)) for (const c of e.message.content) if (c.type === 'tool_result') {
      const call = tools.get(c.tool_use_id); if (!call) continue;
      const t = typeof c.content === 'string' ? c.content : (c.content || []).map(x => x.text || '').join('\n');
      call.resChars = t.length; call.ms = Date.parse(e.timestamp) - call.t0;
      if (c.is_error && /background with ID/.test(t)) call.ms = 0;
      Object.assign(call, outcome(call.phase, t, c.is_error, call.what));
    }
    if (e.type === 'result') { finalText = String(e.result || ''); if (!wall) wall = e.duration_ms; }
    if (e.timestamp) { const ts = Date.parse(e.timestamp); t0 ??= ts; t1 = ts; }
  }
  const T = msgs.size;
  const turns = turnsOrder.map(id => ({ ...msgs.get(id), calls: calls.filter(c => c.turn === msgs.get(id).turn) }));
  // a turn's phase is its strongest call's; a turn with no call is talk
  for (const t of turns) t.phase = t.calls.length ? RANK.find(r => t.calls.some(c => c.phase === r)) : 'talk';
  // fixing: turns after a Dark error until a call of the same phase comes back clean
  let open = null; const episodes = [];
  for (const t of turns) {
    const darkErrs = t.calls.flatMap(c => c.errs.filter(x => DARK_ERR.has(x) || x === 'sandbox-net'));
    // closed by a call of the same kind that no longer shows the error, or by any clean save, run or test
    const cleanSame = open && t.calls.some(c => (c.phase === open.phase && !c.errs.includes(open.cls) && (c.subs.length || c.phase === 'run'))
      || (['author', 'run', 'test'].includes(c.phase) && c.subs.length && !c.errs.length));
    if (open && cleanSame && !darkErrs.length) { open.end = t.turn; open = null; t.fixing = false; continue; }
    if (open) { t.fixing = true; open.turns++; }
    if (darkErrs.length && !open) {
      const c = t.calls.find(c => c.errs.some(x => DARK_ERR.has(x) || x === 'sandbox-net'));
      open = { start: t.turn, phase: c.phase, cls: c.errs.find(x => DARK_ERR.has(x) || x === 'sandbox-net'), all: [...new Set(darkErrs)], line: c.line, cmd: c.what, turns: 0, end: null };
      episodes.push(open);
    }
  }
  // tokens: each tool result rides along in every later turn's context
  let carried = 0; const big = [];
  for (const c of calls) { const tk = toks(c.resChars), later = T - c.turn; c.carry = tk * later; carried += c.carry; if (tk >= 3000) big.push({ turn: c.turn, phase: c.phase, tokens: tk, carry: c.carry, what: c.what }); }
  const ctxSum = turns.reduce((a, t) => a + t.ctx, 0);
  const firstCtx = turns[0]?.ctx || 0;
  const carryBy = {}; for (const c of calls) carryBy[c.phase] = (carryBy[c.phase] || 0) + c.carry;
  // writing whole files again
  const writes = {}; for (const c of calls) if (c.tool === 'Write' && c.file) writes[c.file] = (writes[c.file] || 0) + 1;
  const rewrites = Object.values(writes).reduce((a, k) => a + Math.max(0, k - 1), 0);
  const writeChars = calls.reduce((a, c) => a + (c.writeChars || 0), 0);
  const phaseTurns = {}; for (const t of turns) { const k = t.fixing ? 'fix' : t.phase; phaseTurns[k] = (phaseTurns[k] || 0) + 1; }
  const errSessions = new Set(calls.flatMap(c => c.errs));
  const subs = {}; for (const c of calls) for (const s of c.subs) subs[s] = (subs[s] || 0) + 1;
  const toolMs = calls.reduce((a, c) => a + (c.ms > 0 && c.ms < 3.6e6 ? c.ms : 0), 0), spanMs = (t1 - t0) || 0;
  const darkMs = calls.filter(c => c.subs.length === 1 && c.what.length < 400).map(c => c.ms).filter(x => x > 0);
  const looked = [], docParts = [];
  for (const c of calls) if (c.tool === 'Bash') {
    for (const m of c.what.matchAll(/\b(?:signatures|view|search(?: --batch)?)\s+((?:"[^"]*"|'[^']*'|[A-Za-z][\w.]*)(?:\s+(?:"[^"]*"|'[^']*'|[A-Z][\w.]*))*)/g)) for (const w of m[1].split(/\s+/)) if (w && !w.startsWith('-')) looked.push(w.replace(/["']/g, '').replace(/^Darklang\./, '').split('.').slice(0, 2).join('.'));
    for (const m of c.what.matchAll(/\bdocs\s+(for-ai(?:\s+[a-z][\w-]*)*|[a-z][\w-]*)/g)) docParts.push(m[1].replace(/\s+\d.*/, ''));
  }
  const seq = turns.map(t => t.fixing ? 'F' : ({ orient: 'o', docs: 'D', lookup: 'L', probe: 'p', author: 'A', run: 'R', test: 'T', store: 's', perms: 'P', shell: 'h', 'other-dark': 'x', meta: 'm', talk: '.' })[t.phase] || '?').join('');
  const firstSave = calls.find(c => c.phase === 'author' && c.subs.some(x => SAVE.has(x)));
  return { firstSaveErrs: firstSave ? firstSave.errs.filter(x => DARK_ERR.has(x)) : null, looked, docParts, seq, kind: kindOf[task] || '?', toolMs, spanMs, darkMs, label, task, n, dir, measure: measure.has(task), T, turns, calls, episodes, carried, ctxSum, firstCtx, carryBy, big, rewrites, writeChars, phaseTurns, errSessions, subs, finalText,
    outTok: turns.reduce((a, t) => a + t.out, 0), firstWrong: episodes[0] || null,
    firstAuthor: turns.find(t => t.phase === 'author')?.turn ?? null, firstGreen: turns.find(t => t.calls.some(c => (c.phase === 'run' || c.phase === 'test') && !c.errs.length))?.turn ?? null };
}

function loadLabel(label) {
  const rows = existsSync(`${B}/results/${label}.jsonl`) ? readFileSync(`${B}/results/${label}.jsonl`, 'utf8').trim().split('\n').filter(Boolean).map(l => JSON.parse(l)) : [];
  const byDir = Object.fromEntries(rows.map(r => [`${r.task}-${r.n}`, r]));
  const dirs = existsSync(`${B}/results/${label}`) ? readdirSync(`${B}/results/${label}`).filter(d => /-\d+$/.test(d)).sort() : [];
  return dirs.map(d => { const s = session(label, d); if (s) Object.assign(s, { row: byDir[d] || {} }); return s; }).filter(Boolean);
}

// ---- aggregation ----
const med = xs => { const s = xs.filter(x => x != null).sort((a, b) => a - b); if (!s.length) return null; const m = s.length >> 1; return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2; };
const pct = (a, b) => b ? Math.round(100 * a / b) : 0;
const PH = ['orient', 'docs', 'lookup', 'probe', 'author', 'fix', 'run', 'test', 'store', 'perms', 'shell', 'other-dark', 'meta', 'talk'];
function aggregate(S) {
  const turns = S.reduce((a, s) => a + s.T, 0);
  const phase = Object.fromEntries(PH.map(p => [p, S.reduce((a, s) => a + (s.phaseTurns[p] || 0), 0)]));
  const ctx = S.reduce((a, s) => a + s.ctxSum, 0), carried = S.reduce((a, s) => a + s.carried, 0);
  const carryBy = {}; for (const s of S) for (const [k, v] of Object.entries(s.carryBy)) carryBy[k] = (carryBy[k] || 0) + v;
  const base = S.reduce((a, s) => a + s.firstCtx * s.T, 0);
  const cls = {}; for (const s of S) for (const e of s.episodes) { const c = (cls[e.cls] ||= { episodes: 0, sessions: new Set(), turns: 0, ex: [] }); c.episodes++; c.sessions.add(s.dir + s.label); c.turns += e.turns + 1; if (!s.measure && c.ex.length < nEx && e.line) c.ex.push(`${s.task}#${s.n} t${e.start}: ${e.line}`); }
  const first = {}; for (const s of S) if (s.firstWrong) first[s.firstWrong.cls] = (first[s.firstWrong.cls] || 0) + 1;
  const subs = {}; for (const s of S) for (const [k, v] of Object.entries(s.subs)) subs[k] = (subs[k] || 0) + v;
  return { sessions: S.length, turns, medTurns: med(S.map(s => s.T)), cost: +S.reduce((a, s) => a + (s.row.cost || 0), 0).toFixed(2), medCost: med(S.map(s => s.row.cost)),
    done: S.filter(s => /^completed/.test(s.row.status || '')).length, phase, ctx, carried, carryBy, base, cls, first,
    clean: S.filter(s => !s.episodes.length).length, medFirstAuthor: med(S.map(s => s.firstAuthor)), medFirstGreen: med(S.map(s => s.firstGreen)),
    rewrites: S.reduce((a, s) => a + s.rewrites, 0), outTok: S.reduce((a, s) => a + s.outTok, 0), subs,
    toolMs: S.reduce((a, s) => a + s.toolMs, 0), spanMs: S.reduce((a, s) => a + s.spanMs, 0), medDarkMs: med(S.flatMap(s => s.darkMs)),
    firstSaves: S.filter(s => s.firstSaveErrs).length, firstSaveBad: S.filter(s => s.firstSaveErrs?.length).length,
    afterGreen: med(S.map(s => s.firstGreen == null ? null : s.T - s.firstGreen)),
    looked: S.flatMap(s => s.looked), docParts: S.flatMap(s => s.docParts),
    overhead: med(S.map(s => (s.phaseTurns.docs || 0) + (s.phaseTurns.lookup || 0) + (s.phaseTurns.probe || 0) + (s.phaseTurns.fix || 0) + (s.phaseTurns.perms || 0) + (s.phaseTurns.store || 0))),
    wroteTest: S.filter(s => s.subs.add || s.calls.some(c => c.phase === 'test')).length };
}

// ---- report ----
const L = []; const p = s => L.push(s);
// a label may pool several runs: base-tune+final-tune
const all = labels.map(l => [l, l.split('+').flatMap(loadLabel)]);
for (const [label, S] of all) {
  const A = aggregate(S), tune = S.filter(s => !s.measure);
  p(`## ${label}: ${A.sessions} sessions (${S.length - tune.length} measure-set, counted only)\n`);
  p(`| | value |\n|---|---:|`);
  p(`| turns, total / median | ${A.turns} / ${A.medTurns} |`);
  p(`| cost, total / median | $${A.cost} / $${A.medCost?.toFixed(2)} |`);
  p(`| completed | ${A.done} of ${A.sessions} |`);
  p(`| sessions with no Dark error | ${A.clean} (${pct(A.clean, A.sessions)}%) |`);
  p(`| median turn of first write / first green run | ${A.medFirstAuthor} / ${A.medFirstGreen} |`);
  p(`| median turns after the first green run | ${A.afterGreen} |`);
  p(`| first store save with a Dark error | ${A.firstSaveBad} of ${A.firstSaves} |`);
  p(`| input tokens (all turns) | ${(A.ctx / 1e6).toFixed(1)}M |`);
  p(`| of which the opening context (prompt, tools), repeated | ${pct(A.base, A.ctx)}% |`);
  p(`| of which tool output carried into later turns | ${pct(A.carried, A.ctx)}% |`);
  p(`| whole-file rewrites | ${A.rewrites} |`);
  p(`| wall time in tools (Bash, files) / in the model | ${pct(A.toolMs, A.spanMs)}% / ${100 - pct(A.toolMs, A.spanMs)}% |`);
  p(`| median single Dark call | ${A.medDarkMs} ms |\n`);
  p(`**Where turns go** (a turn counts once, under its strongest call; "fix" is any turn between a Dark error and the next clean call of that kind)\n`);
  p(`| phase | turns | share |\n|---|---:|---:|`);
  for (const ph of PH) if (A.phase[ph]) p(`| ${ph} | ${A.phase[ph]} | ${pct(A.phase[ph], A.turns)}% |`);
  p(`\n**Tool output carried forward, by phase** (estimated tokens × later turns, share of all input tokens)\n`);
  p(`| phase | share |\n|---|---:|`);
  for (const [k, v] of Object.entries(A.carryBy).sort((a, b) => b[1] - a[1]).slice(0, 8)) p(`| ${k} | ${pct(v, A.ctx)}% |`);
  p(`\n**Failure classes** (an episode starts at a Dark error and ends at the next clean call of that kind)\n`);
  p(`| class | sessions | episodes | turns in episodes | first wrong turn in |\n|---|---:|---:|---:|---:|`);
  for (const [k, c] of Object.entries(A.cls).sort((a, b) => b[1].sessions.size - a[1].sessions.size)) p(`| ${k} | ${c.sessions.size} | ${c.episodes} | ${c.turns} | ${A.first[k] || 0} |`);
  p('');
  for (const [k, c] of Object.entries(A.cls)) if (c.ex.length) { p(`- **${k}**:`); for (const x of c.ex) p(`  - \`${x.replace(/`/g, "'")}\``); }
  const bigs = tune.flatMap(s => s.big.map(b => ({ ...b, s }))).sort((a, b) => b.carry - a.carry).slice(0, 8);
  if (bigs.length) { p(`\n**Largest outputs read** (tuning tasks; tokens, then tokens × later turns)\n`); p(`| session | turn | phase | tokens | carried | command |\n|---|---:|---|---:|---:|---|`);
    for (const b of bigs) p(`| ${b.s.task}#${b.s.n} | ${b.turn} | ${b.phase} | ${b.tokens} | ${b.carry} | \`${b.what.replace(/.*?dark"?\s/, 'dark ').replace(/\s+/g, ' ').replace(/[|`]/g, ' ').slice(0, 70)}\` |`); }
  p(`\n**By task kind** (medians per session)\n`);
  p(`| kind | sessions | turns | docs+lookup+probe | fix | run | cost |\n|---|---:|---:|---:|---:|---:|---:|`);
  const kinds = {}; for (const s of S) (kinds[s.kind] ||= []).push(s);
  for (const [k, ss] of Object.entries(kinds).sort()) p(`| ${k} | ${ss.length} | ${med(ss.map(s => s.T))} | ${med(ss.map(s => (s.phaseTurns.docs || 0) + (s.phaseTurns.lookup || 0) + (s.phaseTurns.probe || 0)))} | ${med(ss.map(s => s.phaseTurns.fix || 0))} | ${med(ss.map(s => s.phaseTurns.run || 0))} | $${med(ss.map(s => s.row.cost))?.toFixed(2)} |`);
  p(`\nMedian turns spent on docs, lookup, probe, fix, permissions and store per session: ${A.overhead} of ${A.medTurns}.`);
  const top = xs => Object.entries(xs.reduce((a, x) => (a[x] = (a[x] || 0) + 1, a), {})).sort((a, b) => b[1] - a[1]);
  p(`\n**Docs read** (all sessions): ${top(A.docParts).slice(0, 14).map(([k, v]) => `${k} ${v}`).join(', ')}`);
  p(`\n**Looked up** (signatures/view/search targets, first two name parts): ${top(A.looked).slice(0, 20).map(([k, v]) => `${k} ${v}`).join(', ')}`);
  if (seqOut) { p(`\n**Turn sequences** (tuning tasks; o orient, D docs, L lookup, p probe, A author, F fix, R run, T test, s store, P perms, h shell, . talk)\n`); p('```'); for (const s of tune) p(`${(s.task + '#' + s.n).padEnd(24)} ${s.seq}`); p('```'); }
  p(`\n**Dark subcommands called** (all sessions)\n`);
  p(Object.entries(A.subs).sort((a, b) => b[1] - a[1]).slice(0, 25).map(([k, v]) => `${k} ${v}`).join(', '));
  p('');
}
if (all.length > 1) {
  p(`## Across runs\n`);
  const As = all.map(([l, S]) => [l, aggregate(S)]);
  const row = (name, f) => p(`| ${name} | ${As.map(([, A]) => f(A)).join(' | ')} |`);
  p(`| | ${As.map(([l]) => l).join(' | ')} |\n|---|${As.map(() => '---:').join('|')}|`);
  row('sessions', A => A.sessions); row('median turns', A => A.medTurns); row('median cost', A => `$${A.medCost?.toFixed(2)}`); row('total cost', A => `$${A.cost}`);
  row('no Dark error', A => `${pct(A.clean, A.sessions)}%`); row('first save has an error', A => `${pct(A.firstSaveBad, A.firstSaves)}%`); row('turns after first green', A => A.afterGreen); row('first green run (median turn)', A => A.medFirstGreen);
  for (const ph of ['docs', 'lookup', 'probe', 'author', 'fix', 'run', 'test', 'store', 'perms']) row(`turns: ${ph}`, A => `${pct(A.phase[ph] || 0, A.turns)}%`);
  row('wall time in tools', A => `${pct(A.toolMs, A.spanMs)}%`); row('median Dark call ms', A => A.medDarkMs);
  row('input: carried tool output', A => `${pct(A.carried, A.ctx)}%`); row('input: docs output', A => `${pct(A.carryBy.docs || 0, A.ctx)}%`);
  const classes = [...new Set(As.flatMap(([, A]) => Object.keys(A.cls)))];
  for (const k of classes) row(`sessions with ${k}`, A => `${pct(A.cls[k]?.sessions.size || 0, A.sessions)}%`);
  p('');
}
if (timelineOf) {
  const [lab, dir] = timelineOf.split('/');
  if (measure.has(dir.replace(/-\d+$/, ''))) { console.error('measure-set task: no timeline'); process.exit(2); }
  const s = session(lab, dir);
  p(`## Timeline ${lab}/${dir}: ${s.T} turns\n`);
  p(`| turn | phase | context k | calls (result tokens, seconds) | outcome |\n|---:|---|---:|---|---|`);
  for (const t of s.turns) p(`| ${t.turn} | ${t.fixing ? 'fix' : t.phase} | ${Math.round(t.ctx / 1000)} | ${t.calls.map(c => `${c.tool}${c.subs.length ? ' ' + c.subs.join(',') : ''} (${toks(c.resChars)}t, ${((c.ms || 0) / 1000).toFixed(1)}s)`).join('; ').replace(/\|/g, '/')} | ${[...new Set(t.calls.flatMap(c => c.errs))].join(',') || 'ok'} |`);
  p('');
}
const md = L.join('\n');
if (mdOut) writeFileSync(mdOut, md); else console.log(md);
if (jsonOut) writeFileSync(jsonOut, JSON.stringify(all.map(([l, S]) => ({ label: l, ...aggregate(S), cls: Object.fromEntries(Object.entries(aggregate(S).cls).map(([k, c]) => [k, { ...c, sessions: c.sessions.size }])), perSession: S.map(s => ({ task: s.task, n: s.n, measure: s.measure, T: s.T, cost: s.row.cost, phaseTurns: s.phaseTurns, episodes: s.episodes.map(e => ({ cls: e.cls, start: e.start, turns: e.turns })), carried: s.carried, ctx: s.ctxSum })) })), null, 1));
