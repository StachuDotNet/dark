// Pure helpers for grade.mjs: reading what an attempt did from its transcript. tests/grade-fixtures.mjs covers them.

export const DARK_ERR = /invalid permission rule|error\[(?:LEX|PARSE)|Parse error|Not found: |not found\. Did you mean|is unresolved|Type check failed|permission denied|No script file or function named|Uncaught exception|Encountered a Runtime Error/;
export const NAME = String.raw`([A-Z]\w*(?:\.\w+)+)`;

// `dark run [flags] Fn args`: the first dotted name after `run` whose last part is lower case, however
// the flags were written (quoted, or a shell array), and the words after it up to `;`, `|`, `&` or `>`
export function runsIn(text) {
  const out = [];
  for (const line of text.split('\n')) for (const m of line.matchAll(/(?<!test )\brun\b(.*)/g)) {
    // quoted flag values (`--allow "a; b"`) can hold `;`, so they go first
    const words = m[1].replace(/"[^"]*"|'[^']*'/g, (q) => /^["'][^"' ]*["']$/.test(q) ? q : 'Q').split(/[;|&>]/)[0].trim().split(/\s+/);
    const i = words.findIndex(w => /^[A-Z]\w*(?:\.\w+)*\.[a-z]\w*$/.test(w));
    const rest = words.slice(i + 1); const f = rest.findIndex(w => /^--allow/.test(w));
    if (i >= 0 && !/^(Stdlib|Darklang)\./.test(words[i])) out.push({ fn: words[i], rest: f >= 0 ? rest.slice(0, f) : rest });
  }
  return out;
}

export function entryFrom(transcript) {
  const tools = new Map(); const runs = [], serves = [], modules = new Set(), moduleSaves = new Set(); let final = '', argsOf = {}; const ports = [], rules = new Set();
  for (const line of transcript.split('\n')) {
    let e; try { e = JSON.parse(line); } catch { continue; }
    if (e.type === 'assistant') for (const c of e.message?.content || []) if (c.type === 'tool_use') tools.set(c.id, c);
    if (e.type === 'user' && Array.isArray(e.message?.content)) for (const c of e.message.content) if (c.type === 'tool_result') {
      const tu = tools.get(c.tool_use_id);
      // structured save tools: { module: "Me.X", source } or { name: "Me.X.f", source }
      if (typeof tu?.input?.module === 'string') { const m = tu.input.module.replace(/^\//, '').replace(/\.Tests$/, ''); modules.add(m); moduleSaves.add(m); }
      else if (typeof tu?.input?.name === 'string' && typeof tu.input.source === 'string') modules.add(tu.input.name.replace(/^\//, '').split('.').slice(0, -1).join('.').replace(/\.Tests$/, ''));
      const cmd = tu?.input?.command; if (typeof cmd !== 'string') continue;
      const t = typeof c.content === 'string' ? c.content : (c.content || []).map(x => x.text || '').join('\n');
      // a clean run: no Dark diagnostic (a refused rule is one); arguments and rules come only from those.
      // Not the exit code: a server run in the background ends killed, and its arguments are the ones to keep.
      const ok = !DARK_ERR.test(t);
      // `module X` and `fn X` as commands, not as flags (`search --fn Stdlib.String.toList`)
      for (const m of cmd.matchAll(/(?<![-\w])(module|fn)\s+\/?([A-Z][\w.]*)/g)) if (!/^(Stdlib|Darklang)\b/.test(m[2])) {
        const name = m[2].replace(/\.Tests$/, ''); modules.add(name); if (m[1] === 'module') moduleSaves.add(name);
      }
      if (!ok) continue;
      for (const { fn, rest } of runsIn(cmd)) { runs.push(fn); argsOf[fn] = rest; }
      for (const m of cmd.matchAll(/(?:127\.0\.0\.1|localhost):(\d{4,5})/g)) ports.push(+m[1]);
      for (const m of cmd.matchAll(/--allow[= ]+(?:"([^"]*)"|'([^']*)'|([a-z][\w-]*)(?=\s|$))/g)) for (const r of (m[1] ?? m[2] ?? m[3]).split(';')) rules.add(r.trim());
      for (const m of cmd.matchAll(new RegExp(String.raw`\b(?:serve|http-server)\s+` + NAME, 'g'))) serves.push(m[1]);
    }
    if (e.type === 'result' && !final) final = String(e.result || '');
  }
  for (const { fn, rest } of runsIn(final.replace(/`/g, ' '))) { runs.push(fn); argsOf[fn] ??= rest; }
  for (const m of final.matchAll(new RegExp(String.raw`\bserve\s+` + NAME, 'g'))) serves.push(m[1]);
  const count = xs => { const c = {}; xs.forEach((x, i) => c[x] = (c[x] || 0) + 1 + i / 1000); return Object.entries(c).sort((a, b) => b[1] - a[1]).map(x => x[0]); };
  // the program is the one the final answer names, else the one run most often, else the last one run
  const inFinal = [...final.matchAll(/\b([A-Z]\w*(?:\.[A-Z]\w*)*\.main)\b/g)].map(m => m[1]);
  const named = [...runs].reverse().find(f => inFinal.includes(f)) || inFinal[0] || [...runs].reverse().find(f => final.includes(f));
  const main = named || count(runs)[0] || null;
  return { main, mainArgs: argsOf[main] || [], mains: count(runs), router: serves.at(-1) || null, modules: [...modules], moduleSaves: [...moduleSaves], final, ports: [...new Set(ports.reverse())], rules: [...rules].filter(Boolean) };
}

// test filters to try, in order: the program's name, then each saved module; never an empty filter,
// which would run every test in the store
export function testFilters(filter, entry) {
  return [...new Set([filter, ...entry.modules].filter(f => typeof f === 'string' && f.length > 0))];
}

// the user's function for a job: by name first, then by shape; helpers named with a leading `_` never
export function pick(fns, nameRe, shapeOk) {
  const pub = fns.filter(f => !f.short.startsWith('_'));
  return pub.find(f => nameRe.test(f.short) && shapeOk(f)) || pub.find(shapeOk) || null;
}
