import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, readFile, access, stat, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { prepareDarkRuntime, dotnetLocks } from '../src/dark-runtime.js';
import { previewInstructions } from '../src/instructions.js';
import { preflight, defaults } from '../src/config.js';

test('each batch gets independent binary bytes, empty store and extraction directory', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'dark-runtime-test-'));
  const source = path.join(root, 'source'); await mkdir(source);
  const darkPath = path.join(source, 'dark');
  await writeFile(darkPath, 'binary bytes'); await writeFile(darkPath + '.mode', 'r2r');
  await mkdir(path.join(source, '.darklang')); await writeFile(path.join(source, '.darklang', 'data.db'), 'Shared dirty state');
  const config = { darkPath, workspaceRoot: path.join(root, 'projects'), languages: ['Dark'] };
  const first = await prepareDarkRuntime(config, 'first');
  await writeFile(path.join(first.store, 'data.db'), 'First batch changes');
  await writeFile(path.join(first.extraction, 'extracted'), 'Cache');
  const second = await prepareDarkRuntime(config, 'second');
  assert.equal(await readFile(second.binaryPath, 'utf8'), 'binary bytes');
  assert.equal(await readFile(second.binaryPath + '.mode', 'utf8'), 'r2r');
  assert.equal(first.binarySha256, second.binarySha256);
  assert.notEqual(first.root, second.root);
  assert.notEqual((await stat(first.binaryPath)).ino, (await stat(second.binaryPath)).ino);
  assert.equal((await stat(second.root)).mode & 0o777, 0o700);
  await assert.rejects(access(path.join(second.store, 'data.db')));
  await assert.rejects(access(path.join(second.extraction, 'extracted')));
  assert.equal(await readFile(path.join(source, '.darklang', 'data.db'), 'utf8'), 'Shared dirty state');
  assert.deepEqual(second.writableDirs, [second.store, dotnetLocks]);
  assert.equal(second.env.DARK_CONFIG_RUNDIR, second.store);
  assert.equal(second.env.DOTNET_BUNDLE_EXTRACT_BASE_DIR, second.extraction);
  await assert.rejects(prepareDarkRuntime(config, 'second'), /EEXIST/);
  await assert.rejects(prepareDarkRuntime(config, '../escape'), /Invalid Dark runtime/);
  await rm(root, { recursive: true, force: true });
});

test('environment checks use disposable Dark copies and never the source package store', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'dark-preflight-test-'));
  const darkPath = path.join(root, 'dark');
  const script = `#!${process.execPath}\nconst fs=require('node:fs'),path=require('node:path');fs.writeFileSync(path.join(process.env.DARK_CONFIG_RUNDIR,'data.db'),'fresh');console.log(process.argv.includes('--help')?'--json --sandbox --skip-git-repo-check --add-dir':'v1');`;
  await writeFile(darkPath, script, { mode: 0o700 });
  const config = { ...defaults, agents: [{ agent:'codex', path:darkPath, model:'' }], languages:['Dark'], darkPath, workspaceRoot:path.join(root,'projects'), outputFolder:path.join(root,'results') };
  const environment = await preflight(config);
  assert.notEqual(environment.languages.Dark.executable, darkPath);
  await assert.rejects(access(environment.runtime.root));
  await assert.rejects(access(path.join(root,'.darklang')));
  await rm(root, { recursive: true, force: true });
});

test('tasks and repetitions in two Dark batches never see earlier package mutations', async () => {
  const { Runner } = await import('../src/runner.js');
  const root = await mkdtemp(path.join(os.tmpdir(), 'dark-batches-test-'));
  const darkPath = path.join(root, 'dark');
  await writeFile(darkPath, `#!${process.execPath}
const fs=require('node:fs'),p=require('node:path'),a=process.argv.slice(2);
if(a.includes('--help')) { console.log('--json --sandbox --skip-git-repo-check --add-dir'); process.exit(0); }
if(a.includes('--version')) { console.log('v1'); process.exit(0); }
process.stdin.resume();process.stdin.on('end',()=>{
const store=process.env.DARK_CONFIG_RUNDIR;
if(fs.existsSync(p.join(store,'previous-batch'))) process.exit(1);
fs.writeFileSync(p.join(store,'previous-batch'),'changed');
console.log(JSON.stringify({type:'thread.started',thread_id:'fresh'}));
console.log(JSON.stringify({type:'turn.completed',usage:{input_tokens:1,cached_input_tokens:0,output_tokens:1}}));
});`, { mode: 0o700 });
  const config = { ...defaults, agents: [{agent:'codex',path:darkPath,model:''}], languages:['Dark'], darkPath, tasks:[{name:'even-odd',prompt:'Build a program and test it.'},{name:'fizzbuzz',prompt:'Build another program and test it.'}], repetitions:2, workspaceRoot:path.join(root,'projects'),outputFolder:path.join(root,'results') };
  const runner = new Runner(); runner.historyFile = path.join(root,'history.json');
  const reviewed = { ...config, runtimeId:'review-first' };
  reviewed.reviewedInstructions = previewInstructions(reviewed, await preflight(config));
  const first = await runner.start(reviewed);
  while (runner.controller) await new Promise(resolve => setTimeout(resolve,20));
  const second = await runner.start(config);
  while (runner.controller) await new Promise(resolve => setTimeout(resolve,20));
  assert.ok(first.runs.concat(second.runs).every(r => r.status === 'completed'));
  assert.equal(new Set(first.runs.concat(second.runs).map(r => r.runtime.store)).size, 8);
  assert.notEqual(first.environment.runtime.root, second.environment.runtime.root);
  for (const batch of [first, second]) {
    const runtime = batch.environment.runtime;
    assert.equal(batch.environment.languages.Dark.executable, runtime.binaryPath);
    for (const run of batch.runs) {
      assert.notEqual(run.runtime.store, runtime.store);
      assert.ok(run.instructions.includes(run.runtime.binaryPath));
      assert.ok(run.segments[0].args.includes(run.runtime.store));
      assert.equal(await readFile(path.join(run.runtime.store,'previous-batch'),'utf8'), 'changed');
      assert.equal(run.runtime.binarySha256, runtime.binarySha256);
      const attemptEnvironment = JSON.parse(await readFile(path.join(run.artifactFolder,'environment.json'),'utf8'));
      assert.equal(attemptEnvironment.languages.Dark.executable, run.runtime.binaryPath);
      if (batch === first) assert.equal(run.instructions, reviewed.reviewedInstructions.find(p => p.task === run.task && p.repetition === run.repetition).prompt);
    }
    const saved = JSON.parse(await readFile(path.join(batch.folder,'environment.json'),'utf8'));
    assert.equal(saved.runtime.sourcePath, darkPath);
    assert.equal(saved.runtime.env.DARK_CONFIG_RUNDIR, runtime.store);
  }
  await assert.rejects(access(path.join(root,'.darklang')));
  await runner.deleteBatch(first.key);
  await assert.rejects(access(first.environment.runtime.root));
  await access(second.environment.runtime.binaryPath);
  await rm(root, {recursive:true,force:true});
});
