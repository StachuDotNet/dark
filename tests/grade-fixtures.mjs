// node tests/grade-fixtures.mjs: the grader's transcript reading, on made-up transcripts.
// One case per false result found in the ab3-r3 analysis (W696), so none comes back.
import assert from 'node:assert/strict';
import { entryFrom, testFilters, pick } from '../grade-lib.mjs';

let id = 0;
const T = (...steps) => steps.map(([cmd, out, isError = false]) => {
  const tid = `t${++id}`;
  return JSON.stringify({ type: 'assistant', message: { id: `m${id}`, content: [{ type: 'tool_use', id: tid, name: 'Bash', input: { command: cmd } }] } }) + '\n' +
    JSON.stringify({ type: 'user', message: { content: [{ type: 'tool_result', tool_use_id: tid, content: out, is_error: isError }] } });
}).join('\n') + '\n' + JSON.stringify({ type: 'result', result: 'done' });

// (a) a module saved with no owner: the test filter is its name, never empty (empty runs the whole store)
{
  const e = entryFrom(T(['$D module /RomanNumerals - < r.dark', '✓ Defined 3 declarations']));
  assert.deepEqual(e.modules, ['RomanNumerals']);
  const owner = 'RomanNumerals'.split('.').slice(0, 2).join('.');
  assert.deepEqual(testFilters(owner.split('.')[1], e), ['RomanNumerals']);
  assert.deepEqual(testFilters(undefined, { modules: [] }), []);
}

// (b) the function is chosen by name before shape; a `_` helper of the same shape never wins
{
  const fns = [
    { short: '__symbolValue', params: 's: String', ret: 'Option<Int64>' },
    { short: 'fromRoman', params: 's: String', ret: 'Result<Int64, String>' },
  ];
  const shape = f => /^\w+\s*:\s*String$/.test(f.params) && /Int64/.test(f.ret);
  assert.equal(pick(fns, /^(from|parse|decode)/i, shape).short, 'fromRoman');
  assert.equal(pick([fns[0]], /^(from|parse|decode)/i, shape), null);
}

// (c) rules come only from clean runs: a run Dark refused (or that exited non-zero) contributes none
{
  const e = entryFrom(T(
    ['$D run --allow "file read /x;env" Me.Backup.main a b', 'invalid permission rule: env', true],
    ['$D run --allow "file read /x" --allow "env read BACKUP_MAX_BYTES" Me.Backup.main a b', 'copied 2'],
  ));
  assert.ok(!e.rules.includes('env'));
  assert.ok(e.rules.includes('env read BACKUP_MAX_BYTES'));
}

// (d) only `module` saves count as modules saved; `search --fn X` and `fn` saves don't
{
  const e = entryFrom(T(
    ['$D search --fn Stdlib.String.toList', 'toList(s: String): List<Char>'],
    ['$D module /Me.Dur - < d.dark', '✓ Defined 4 declarations'],
    ['$D fn /Me.Dur.helper "(x: Int64): Int64 = x"', '✓ Created'],
  ));
  assert.deepEqual(e.moduleSaves, ['Me.Dur']);
  assert.ok(!e.modules.some(m => m.startsWith('Stdlib')));
}
console.log('ok: grade fixtures (a) to (d)');
