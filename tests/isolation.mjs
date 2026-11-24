// node tests/isolation.mjs: a bench workspace sees none of the user's agent setup.
// From Nov 18 to Nov 24 workspaces lived under /home/stachu, and sessions loaded ~/.claude/CLAUDE.md
// and ~/.claude/skills through the ancestor walk (W696).
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { setupAbove } from '../isolation.mjs';

const B = new URL('..', import.meta.url).pathname;
const ws = readFileSync(`${B}/attempt.mjs`, 'utf8').match(/process\.env\.BENCH_WS \|\| '([^']+)'/)[1];
assert.deepEqual(setupAbove(path.join(ws, 'label', 'task-1')), [], `the default workspace ${ws} has agent setup above it`);
assert.ok(!ws.startsWith(os.homedir()), 'the default workspace is under the home folder');

// the check itself finds a .claude folder or a CLAUDE.md anywhere above
const t = mkdtempSync(path.join(os.tmpdir(), 'iso-'));
try {
  mkdirSync(path.join(t, 'a', '.claude'), { recursive: true }); mkdirSync(path.join(t, 'a', 'b', 'c'), { recursive: true });
  writeFileSync(path.join(t, 'a', 'b', 'CLAUDE.md'), 'x');
  const found = setupAbove(path.join(t, 'a', 'b', 'c', 'ws'));
  assert.ok(found.includes(path.join(t, 'a', '.claude')) && found.includes(path.join(t, 'a', 'b', 'CLAUDE.md')));
} finally { rmSync(t, { recursive: true, force: true }); }

// the old workspace root really did sit under the user's setup on this machine
if (os.homedir() === '/home/stachu') assert.ok(setupAbove('/home/stachu/code/bench-loop-ws/x/y').length > 0);
console.log(`ok: ${ws} has no agent setup above it`);
