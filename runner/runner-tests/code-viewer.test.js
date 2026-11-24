import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, symlink } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { projectFiles } from '../src/project-files.js';

test('code viewer reads source without executing it and explains omitted files', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'code-viewer-test-'));
  const workspace = path.join(root, 'project');
  await mkdir(path.join(workspace, 'src'), { recursive: true });
  await mkdir(path.join(workspace, 'node_modules'));
  const source = 'console.log("hello");\n';
  await writeFile(path.join(workspace, 'src/main.ts'), source);
  await writeFile(path.join(workspace, 'README.md'), '<script>not executed</script>');
  await writeFile(path.join(workspace, '.env'), 'SECRET=private');
  await writeFile(path.join(workspace, 'binary'), Buffer.from([0, 1, 2]));
  await writeFile(path.join(workspace, 'big.txt'), 'a'.repeat(512001));
  await writeFile(path.join(root, 'outside.txt'), 'outside secret');
  await symlink(path.join(root, 'outside.txt'), path.join(workspace, 'linked.txt'));
  const result = await projectFiles(workspace);
  assert.deepEqual(result.files.map(f => f.name), ['README.md', 'src/main.ts']);
  assert.equal(result.files.find(f => f.name === 'src/main.ts').content, source);
  assert.equal(result.skipped.find(f => f.name === 'linked.txt').reason, 'Symbolic link');
  assert.ok(['.env', 'binary', 'big.txt', 'node_modules'].every(name => result.skipped.some(f => f.name === name)));
  await assert.rejects(projectFiles(path.join(root, 'missing')), /no longer available/);
});
