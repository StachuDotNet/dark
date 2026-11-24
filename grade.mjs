// usage: node grade.mjs <label> [--only task-n,...] [--force]
// Hidden checks: runs what an attempt saved against inputs and expected outputs no agent sees
// (checks/<task>.mjs), using the attempt's own store. Rows go to results/<label>.grades.jsonl, one per
// attempt: { task, n, graded, pass, passed, total, checks: [{ name, ok, got }] }.
// The entry point comes from the transcript: the function the agent last ran without a Dark error
// (or served), and the modules it saved. Any agent that writes stream-json and uses `dark run`/`serve`
// or saves with `module` is graded the same way.
import { readFileSync, existsSync, appendFileSync, mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { spawnSync, spawn } from 'node:child_process';
import path from 'node:path';
import { entryFrom, testFilters } from './grade-lib.mjs';

const B = new URL('.', import.meta.url).pathname;
const args = process.argv.slice(2);
const opt = (k, d) => { const i = args.indexOf(k); if (i < 0) return d; const v = args[i + 1]; args.splice(i, 2); return v; };
const only = opt('--only', null)?.split(',');
const force = args.includes('--force') ? (args.splice(args.indexOf('--force'), 1), true) : false;
const label = args[0];
// --force regrades; readers (summary.mjs) take the last row per attempt
const outFile = `${B}/results/${label}.grades.jsonl`;
const done = new Set(!force && existsSync(outFile) ? readFileSync(outFile, 'utf8').trim().split('\n').filter(Boolean).map(l => { const r = JSON.parse(l); return `${r.task}-${r.n}`; }) : []);
const rows = readFileSync(`${B}/results/${label}.jsonl`, 'utf8').trim().split('\n').filter(Boolean).map(l => JSON.parse(l));

function ctxFor(row, runRoot, entry) {
  // the attempt's own copy of the binary, else the label's wrapper
  const D = existsSync(path.join(runRoot, 'dark')) ? path.join(runRoot, 'dark') : `${B}/bins/${row.label}/dark`;
  const env = { ...process.env, DARK_CONFIG_RUNDIR: path.join(runRoot, '.darklang'), DOTNET_BUNDLE_EXTRACT_BASE_DIR: path.join(runRoot, 'dotnet'), NO_PROXY: '127.0.0.1,localhost' };
  const tmp = mkdtempSync('/home/stachu/code/bench-loop-ws/grade-');
  env.TMPDIR = tmp; // a program's temporary files land in the grading folder, as the agent's landed in its sandbox
  const dark = (argv, o = {}) => {
    const r = spawnSync(D, argv, { cwd: o.cwd || tmp, input: o.stdin ?? '', env: { ...env, ...(o.env || {}) }, timeout: o.timeout || 60000, maxBuffer: 64 << 20 });
    return { code: r.status, out: String(r.stdout || ''), err: String(r.stderr || ''), timedOut: !!r.error };
  };
  // what any program in these tasks may need, scoped to the grading folder
  // plus the non-file, non-server rules the agent itself passed to `run` (`concurrency`, `env read X`, ...)
  // with `$D` as this attempt's dark and any other `$VAR...` path as the grading folder
  const theirs = entry.rules.filter(r => /^[a-z][\w-]*(\s|$)/.test(r) && !/^(file|http-server)\b/.test(r))
    .map(r => r.replace(/["']?\$\{?(?:D|DARK)\}?["']?(?=\s|$)/g, D).replace(/\$\{?\w+\}?(\/[^\s'"]*)?/g, tmp));
  const allow = (extra = []) => ['--allow', [...new Set([`file read ${tmp}`, `file write ${tmp}`, 'env read *', ...theirs, ...extra])].join('; ')];
  // an agent's path argument becomes one inside the grading folder; one without an extension is a folder
  const here = (x) => { if (/^"?\$\{?(D|DARK)\}?"?$/.test(x)) return D; const q = path.join(tmp, path.basename(x)); if (!path.extname(q)) mkdirSync(q, { recursive: true }); return q; };
  const ctx = {
    tmp, entry, dark, final: entry.final,
    write: (name, content) => { writeFileSync(path.join(tmp, name), content); return path.join(tmp, name); },
    // run the agent's main (or another function) with args, as `dark run` would
    run: (argv, o = {}) => dark(['run', ...allow(o.allow), o.fn || entry.main, ...argv], o),
    eval: (expr, o = {}) => dark(['eval', expr], o),
    // counts from the summary line, which every release prints (`--json` is newer)
    // tests named after the program, else after any saved module
    test: (filter) => {
      let last = { passed: 0, failed: 0, code: null, raw: 'no test filter: nothing was saved by name' };
      for (const f of testFilters(filter, entry)) {
        const r = dark(['test', 'run', ...(f ? [f] : [])], { timeout: 300000 });
        const m = (r.out + r.err).match(/(\d+) tests?[^\n]*?: (\d+) passed, (\d+) failed/);
        last = m ? { tests: +m[1], passed: +m[2], failed: +m[3], code: r.code, filter: f } : { passed: 0, failed: 0, code: r.code, raw: (r.out + r.err).slice(-200) };
        if (m) return last;
      }
      return last;
    },
    source: (name) => dark(['view', name, '--raw']).out,
    // the user's declarations `let f(...): T` in the saved modules
    fns: () => entry.modules.flatMap(m => [...ctx.source(m).matchAll(/^\s*let (\w+)\(([^)]*)\)\s*:\s*([^=]+)=/gm)].map(x => ({ module: m, name: `${m}.${x[1]}`, short: x[1], params: x[2].trim(), ret: x[3].trim() }))),
    owner: () => (entry.main || entry.router || entry.modules[0] || 'Me.X').split('.').slice(0, 2).join('.'),
    // start the server on our port; if it never answers there, start it as the agent did and try the
    // ports its own requests used. Returns { stop, port } or null.
    async serve(port, extraAllow = []) {
      const up = async (p, proc) => { for (let i = 0; i < 40; i++) { if (proc.exitCode != null) return false; try { await fetch(`http://127.0.0.1:${p}/`); return true; } catch { await new Promise(r => setTimeout(r, 500)); } } return false; };
      const start = (argv, allowPorts) => spawn(D, argv[0] === 'serve' ? argv : ['run', ...allow([...allowPorts.map(q => `http-server 127.0.0.1 ${q}`), ...extraAllow]), entry.main, ...argv], { cwd: tmp, env, stdio: 'ignore' });
      if (entry.router) {
        dark(['permissions', 'allow', 'http-server', '127.0.0.1', String(port)]);
        const p = start(['serve', entry.router, '--port', String(port)], []);
        if (await up(port, p)) return { stop: () => p.kill('SIGKILL'), port };
        p.kill('SIGKILL'); return null;
      }
      // the agent's own last arguments, with its port swapped for ours and its absolute paths moved here
      const a = entry.mainArgs.map(x => x.replace(/^["']|["']$/g, '')).map(x => /^\d{4,5}$/.test(x) ? String(port) : x.startsWith('/') || x.startsWith('$') ? here(x) : x.replace(/:\d{4,5}$/, `:${port}`));
      let p = start(a.some(x => x.includes(String(port))) ? a : [...a, String(port)], [port]);
      if (await up(port, p)) return { stop: () => p.kill('SIGKILL'), port };
      p.kill('SIGKILL');
      const plain = entry.mainArgs.map(x => x.replace(/^["']|["']$/g, '')).map(x => x.startsWith('/') || x.startsWith('$') ? here(x) : x);
      for (const q of [...entry.ports, 8080].slice(0, 3)) {
        p = start(plain, [q]);
        if (await up(q, p)) return { stop: () => p.kill('SIGKILL'), port: q };
        p.kill('SIGKILL');
      }
      return null;
    },
    async http(port, method, p, body, headers = {}) {
      try { const r = await fetch(`http://127.0.0.1:${port}${p}`, { method, body, headers }); return { status: r.status, text: await r.text() }; } catch (e) { return { status: 0, text: String(e) }; }
    },
    cleanup: () => rmSync(tmp, { recursive: true, force: true }),
  };
  return ctx;
}

const results = [];
for (const row of rows) {
  const key = `${row.task}-${row.n}`;
  if ((only && !only.includes(key) && !only.includes(row.task)) || done.has(key)) continue;
  const g = { label, task: row.task, n: row.n, graded: false, pass: null, passed: 0, total: 0, checks: [] };
  const checkFile = `${B}/checks/${row.task}.mjs`;
  const runJson = `${B}/results/${label}/${key}/run.json`;
  const transcript = `${B}/results/${label}/${key}/agent-0.jsonl`;
  if (!existsSync(checkFile)) g.why = 'no hidden check for this task';
  else if ((row.language || 'Dark') !== 'Dark') g.why = 'not a Dark attempt';
  else if (!existsSync(runJson) || !existsSync(transcript)) g.why = 'no transcript';
  else {
    const root = JSON.parse(readFileSync(runJson, 'utf8')).runtime?.root;
    if (!root || !existsSync(path.join(root, '.darklang'))) g.why = 'store not kept';
    else {
      const entry = entryFrom(readFileSync(transcript, 'utf8'));
      const ctx = ctxFor(row, root, entry);
      // nothing was run by name: a saved module's `main`, preferring one the final answer names
      if (!entry.main) { const ms = entry.modules.filter(m => /^\s*let main\(/m.test(ctx.source(m))); entry.main = ms.find(m => entry.final.includes(m)) ? `${ms.find(m => entry.final.includes(m))}.main` : ms.length ? `${ms.at(-1)}.main` : null; }
      try {
        const check = (await import(checkFile)).default;
        g.checks = (await check(ctx)).map(c => ({ name: c.name, ok: !!c.ok, got: c.ok ? undefined : String(c.got ?? '').slice(0, 200) }));
        g.graded = true; g.entry = { main: entry.main, router: entry.router };
        g.passed = g.checks.filter(c => c.ok).length; g.total = g.checks.length; g.pass = g.passed === g.total && g.total > 0;
      } catch (e) { g.why = `check crashed: ${String(e.message).slice(0, 200)}`; }
      ctx.cleanup();
    }
  }
  appendFileSync(outFile, JSON.stringify(g) + '\n');
  results.push(g);
  console.log(`${key.padEnd(22)} ${g.graded ? (g.pass ? 'PASS' : 'FAIL') : '----'} ${g.graded ? `${g.passed}/${g.total}` : g.why}${g.graded && !g.pass ? '  ' + g.checks.filter(c => !c.ok).map(c => c.name).join(', ') : ''}`);
}
