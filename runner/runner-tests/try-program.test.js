import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, readFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { tryProgram, savedTries, suggestedCommand, sandboxProfile } from '../src/try-program.js';

test('manual runs use a throwaway copy, keep the project unchanged and save each result', { skip: process.platform !== 'darwin' }, async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'try-program-test-'));
  const workspace = path.join(root, 'project'), folder = path.join(root, 'batch');
  await mkdir(workspace, { recursive: true });
  await writeFile(path.join(workspace, 'main.py'), 'print("hi")\n');
  const batch = { folder }, run = { id: 'run-1', language: 'Python', workspace };
  const first = await tryProgram(batch, run, { commandLine: 'cat; echo made > new.txt; echo oops >&2; exit 3', input: 'typed' });
  assert.equal(first.entry.stdout, 'typed');
  assert.equal(first.entry.stderr, 'oops\n');
  assert.equal(first.entry.exitCode, 3);
  await assert.rejects(readFile(path.join(workspace, 'new.txt')));
  const slow = await tryProgram(batch, run, { commandLine: 'sleep 5', timeoutMs: 100 });
  assert.equal(slow.entry.timedOut, true);
  assert.equal((await savedTries(batch, run)).length, 2);
  await assert.rejects(tryProgram(batch, run, { commandLine: ' ' }), /Enter a command/);
  await assert.rejects(tryProgram(batch, { ...run, workspace: path.join(root, 'missing') }, { commandLine: 'true' }), /no longer available/);
});

test('manual runs cannot read your home folder, write outside their copy or see inherited secrets', { skip: process.platform !== 'darwin' }, async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'try-sandbox-test-'));
  const workspace = path.join(root, 'project');
  await mkdir(workspace, { recursive: true });
  process.env.TRY_TEST_TOKEN = 'very-secret-value';
  const batch = { folder: path.join(root, 'batch') }, run = { id: 'run-2', language: 'Python', workspace };
  const attempt = async commandLine => (await tryProgram(batch, run, { commandLine })).entry;
  try {
    assert.notEqual((await attempt(`ls ${JSON.stringify(os.homedir())}`)).exitCode, 0);
    assert.notEqual((await attempt(`echo x > ${JSON.stringify(path.join(workspace, 'escaped.txt'))}`)).exitCode, 0);
    await assert.rejects(readFile(path.join(workspace, 'escaped.txt')));
    assert.equal((await attempt('echo "${TRY_TEST_TOKEN:-none}"')).stdout, 'none\n');
    const own = await attempt('echo kept > note.txt && cat note.txt && echo x > "$HOME/h" && echo y > "$TMPDIR/t" && python3 -c "print(2+2)"');
    assert.equal(own.stdout, 'kept\n4\n');
  } finally { delete process.env.TRY_TEST_TOKEN; }
});

test('sandbox profile escapes paths', () => {
  assert.match(sandboxProfile({ home: '/Users/a"b', writable: ['/tmp/x\\y'] }), /"\/Users\/a\\"b"[\s\S]*"\/tmp\/x\\\\y"/);
});

test('suggested commands point at the main program, not its tests', () => {
  assert.equal(suggestedCommand('Python', ['test_parity.py', 'parity.py']), 'python3 parity.py ');
  assert.equal(suggestedCommand('Dark', ['even_odd.dark']), '"$DARK" run even_odd.dark ');
  assert.equal(suggestedCommand('TypeScript', ['package.json', 'src/index.ts']), 'npm start -- ');
  assert.equal(suggestedCommand('TypeScript', ['src/main.test.ts', 'src/main.ts']), 'node src/main.ts ');
});
