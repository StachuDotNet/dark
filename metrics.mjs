// Per-attempt counts from a claude stream-json transcript (W402/W477 failed-run rules, any way of calling dark).
const DARK = String.raw`(?:\$\{?D\w*\}?|\bdark|\$\{?DARK\}?)`;
const darkCall = new RegExp(`${DARK}\\s`);
const runCall = new RegExp(String.raw`(?:\brun\b [^|]*\.dark\b|${DARK}\s+run\s+(?:\S+\s+)*Me\.[A-Z]\w*(?:\.\w+)*)`);
const storeCall = new RegExp(`${DARK}\\s+(?:module|fn|val|type)\\b`);
// mistakes an F#-style draft makes, as Dark reports them
const fsharpism = /separates [a-z -]+ with commas|a pair is one argument|a triple is one argument|invalid number literal: \d+L|Invalid name: [A-Z]\w*\b/;
const bad = { lex: /error\[LEX\]/, parse: /error\[PARSE/, typecheck: /Static type check failed|type error/i, notFound: /Error: [\w.]+ not found|Not found: [\w.]+/, perm: /permission denied/i };

export function metrics(text) {
  const tools = new Map(), seen = new Set();
  let fsx = 0, semis = 0, turn = 0, firstWrite = null, runCalls = 0, failedRuns = 0, darkCalls = 0, failedDark = 0, permDenied = 0, docsCmds = 0, storeUse = 0, wroteTest = false, ranTests = false, bashCalls = 0;
  const kinds = {};
  for (const line of text.split('\n')) {
    let e; try { e = JSON.parse(line); } catch { continue; }
    if (e.type === 'assistant') {
      if (e.message?.id && !seen.has(e.message.id)) { seen.add(e.message.id); turn++; }
      for (const c of e.message.content || []) if (c.type === 'tool_use') {
        tools.set(c.id, c);
        if ((c.name === 'Write' || c.name === 'Edit') && firstWrite === null) firstWrite = turn;
        const body = c.input.content || c.input.new_string || c.input.command || '';
        if (/^\s*test\s+\w+\s*=/m.test(body) || /\btest add\b/.test(body)) wroteTest = true;
        if (c.name === 'Bash') {
          bashCalls++;
          const cmd = c.input.command || '';
          if (/\btest run\b/.test(cmd)) ranTests = true;
          if (storeCall.test(cmd)) storeUse++;
          if (new RegExp(`${DARK}\\s+docs\\b`).test(cmd)) docsCmds++;
        }
      }
    }
    if (e.type === 'user' && Array.isArray(e.message?.content)) for (const c of e.message.content) if (c.type === 'tool_result') {
      const tu = tools.get(c.tool_use_id); if (!tu || tu.name !== 'Bash') continue;
      const cmd = tu.input.command || '';
      const t = typeof c.content === 'string' ? c.content : (c.content || []).map(x => x.text || '').join('\n');
      permDenied += (t.match(/permission denied/gi) || []).length;
      // counted per dark call that hit one, since each such call is a turn spent fixing
      if (darkCall.test(cmd)) { if (fsharpism.test(t)) fsx++; if (/separates [a-z -]+ with commas/.test(t)) semis++; }
      if (darkCall.test(cmd)) { darkCalls++; if (Object.values(bad).some(re => re.test(t))) failedDark++; }
      if (runCall.test(cmd)) {
        runCalls++; let failed = false;
        for (const [k, re] of Object.entries(bad)) if (re.test(t)) { kinds[k] = (kinds[k] || 0) + 1; failed = true; }
        if (failed) failedRuns++;
      }
    }
  }
  return { fsx, semis, turns: turn, bashCalls, firstWrite, runCalls, failedRuns, kinds, darkCalls, failedDark, permDenied, docsCmds, storeUse, wroteTest, ranTests };
}

// Token totals for the whole session. A session that answers background-task notifications ends with
// several result events; each one's `usage` covers only its own turn, while `modelUsage` is cumulative.
export function tokens(text) {
  let last = null;
  for (const line of text.split('\n')) { try { const e = JSON.parse(line); if (e.type === 'result') last = e; } catch {} }
  const m = Object.values(last?.modelUsage || {});
  if (!m.length) return { inK: null, outTok: null };
  const sum = k => m.reduce((a, x) => a + (x[k] || 0), 0);
  return { inK: Math.round((sum('inputTokens') + sum('cacheReadInputTokens') + sum('cacheCreationInputTokens')) / 1000), outTok: sum('outputTokens') };
}
